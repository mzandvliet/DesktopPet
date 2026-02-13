using UnityEngine;
using System;
using System.Text;
using Unity.Mathematics;
using Frantic.Windows;
using DrawBehindDesktopIcons;
using Shapes;
using System.Collections.Generic;

/*

The app runs as a full-screen window with full transparency.
It can move itself up and down in Z-order to move in front of other windows or behind them.


Todo:

- respond to desktop resolution change
- tray icon / menu
- suspend rendering/logic when user enters another full-screen app

Issues:

Window hopping behavior doesn't work well with OperaGX/Firefox browser window, or for some explorer windows
but they do show up in the visible window list

ScreenToGif window is seen as a mouse target
ignore it in DesktopWindowTracker

*/

public class DesktopHook : ImmediateModeShapeDrawer
{
    [SerializeField] private GameObject _foodPrefab;
    [SerializeField] private ParticleSystem _foodParticles;
    [SerializeField] private Boids _boids;

    [SerializeField] private LayerMask _interactableLayers = -1;
    [SerializeField] private float _maxRaycastDistance = 100f;

    private const int FramerateActive = 30;
    private const int FramerateHidden = 10;

    private Camera _camera;
    private static StringBuilder _text;
    private Vector3 _lastMousePosWorld;
    private float _escapeTimer;
    private float _debugToggleTimer;

    private DesktopWindowTracker _windowTracker;
    private DesktopIconMonitor _iconMonitor;

    private static WndProcDelegate _newWndProcDelegate;
    private static IntPtr _oldWndProc;
    private static DesktopHook _instance;
    public static DesktopHook Instance
    {
        get => _instance;
    }

    private AppWindowMode _windowMode;
    public AppWindowMode WindowMode
    {
        get => _windowMode;
    }
    
    private static IntPtr _hwnd;

    // Cached state for window mouse-cursor focus check
    private static Vector2 _lastTestedMousePos;
    private static bool _lastHitResult;
    private static float _lastTestTime;
    private const float CACHE_DURATION = 0.016f; // ~60fps
    private const float CACHE_DISTANCE = 3f; // pixels

    private double _lastFoodSpawnTime;

    private List<Character> _characters;

    public static IntPtr HWnd
    {
        get => _hwnd;
    }

    private bool _showDebug = false;

    public void Awake()
    {
        if (_instance != null)
        {
            Debug.LogError("Multiple SelectiveClickThrough instances detected!");
            return;
        }
        _instance = this;
        _windowTracker = gameObject.AddComponent<DesktopWindowTracker>();
        _iconMonitor = new DesktopIconMonitor();

        _camera = gameObject.GetComponent<Camera>();
        _characters = new List<Character>();
        _characters.AddRange(GameObject.FindObjectsByType<Character>(FindObjectsSortMode.None));

        _text = new StringBuilder(4096);

        Application.targetFrameRate = FramerateActive;
        Application.runInBackground = true;

        /*
        Get handle to our application window
        IMPORTANT: only works *before* MakeWindowOpaqueBehindIcons
        */
        _hwnd = DesktopWindowTracker.GetUnityWindowHandle();
    }

    private void Start()
    {
        Screen.fullScreenMode = FullScreenMode.Windowed;
        // Screen.SetResolution(3440, 1440, false);

        if (_hwnd == IntPtr.Zero)
        {
            Debug.LogError("Couldn't retrieve app's window handle");
            return;
        } else
        {
            Debug.Log($"Succesfully got our window handle: {_hwnd}");
        }

        // if (ConfigureTransparentFullscreenWindow())
        if (MakeWindowOpaqueBehindIcons())
        {
            Debug.Log("Succesfully hooked into desktop background!");
        }
        else
        {
            Debug.Log("Error: Failed to hook into desktop background...");
        }
        
        if (!_iconMonitor.Initialize())
        {
            Debug.Log("Error: Failed to initialize Desktop Icon Monitor...");
        }

        // DesktopWindowTracker.GetDesktopIconPositions();
        // _iconMonitor.Start();
    }

    private void OnDestroy()
    {
        if (_iconMonitor != null)
        {
            _iconMonitor.Dispose();
        }
    }

    private void Update()
    {
        /* Update input polling (can't use Unity input system for most situation in this app) */

        SystemInput.Process();

        /* See if mouse is over any desktop icons */

        var mousePosWin = SystemInput.GetCursorPosition();

        int mouseIconIndex = _iconMonitor.HitTest((int)mousePosWin.x, mousePosWin.y);
        bool mouseIsOverWindowsUI = mouseIconIndex >= 0;

        mouseIsOverWindowsUI |= _windowTracker.IsPointInTaskbar(mousePosWin.x, mousePosWin.y);

        // If this app is capable of being in front of other windows, manage focus
        // if (ShouldCaptureInput(mousePosWin.x, mousePosWin.y))
        // {
        //     SetWindowTransparent(false);
        // } else
        // {
        //     SetWindowTransparent(true);
        // }

        if (Time.frameCount % 60 == 0) {
            _iconMonitor.Update();
        }

        if (Time.frameCount % 60 == 30)
        {
            UpdatePerformanceSettings();
        }

        /*
        Determine whether our app is in front of or behind the window that the cursor currently
        hovers over. This can be used later to have elements act in front of those other windows
        */

        bool appIsInFrontOfHoveredWindow = false;
        DesktopWindowTracker.WindowInfo hoveredWindowInfo;
        _windowTracker.IsPointCoveredByWindow(mousePosWin, out hoveredWindowInfo, _hwnd);
        var ourWindowInfo = _windowTracker.GetWindowInfo(_hwnd);
        if (hoveredWindowInfo != null && ourWindowInfo != null)
        {
            appIsInFrontOfHoveredWindow = ourWindowInfo.Z < hoveredWindowInfo.Z;
        }

        /* Hit testing! Find out if cursor is hovering over anything interactive in the scene,
        or on any other part of the Windows Desktop that we should know about */

        // mouse, screen to unity coordinates
        var mousePosUnity = mousePosWin;
        mousePosUnity.y = Screen.height - mousePosUnity.y;

        var mouseScreenPoint = new Vector3(mousePosUnity.x, mousePosUnity.y, 0.01f);
        var mouseRay = _camera.ScreenPointToRay(mouseScreenPoint);

        Vector3 mousePosWorld;

        Ray ray = _camera.ScreenPointToRay(new Vector3(mousePosUnity.x, mousePosUnity.y, 0.1f));
        bool mouseHit = Physics.Raycast(ray, out RaycastHit mouseHitInfo, _maxRaycastDistance, _interactableLayers);
        if (mouseHit)
        {
            mousePosWorld = mouseHitInfo.point;
        } else
        {
            var freeFloatingMouseScreenPoint = new Vector3(mousePosUnity.x, mousePosUnity.y, -_camera.transform.position.z);
            mousePosWorld = _camera.ScreenToWorldPoint(freeFloatingMouseScreenPoint);
        }

        var mouseVelocityWorld = mousePosWorld - _lastMousePosWorld;
        _lastMousePosWorld = mousePosWorld;

        _boids.SetMouseData(mouseRay, mouseVelocityWorld);

        foreach (var character in _characters)
        {
            character.SetMouseCursorWorld(mousePosWorld);
        }

        Vector3 foodParticlePos = mousePosWorld + (mouseHit ? mouseHitInfo.normal * 0.5f : Vector3.zero);
        _foodParticles.transform.position = foodParticlePos;
        if (Time.timeAsDouble > _lastFoodSpawnTime + FoodSpawnDelay && !mouseIsOverWindowsUI)
        {
            if (_foodParticles.isStopped) {
                _foodParticles.Play();
            }
        }
        else
        {
            if (_foodParticles.isPlaying)
            {
                _foodParticles.Stop();
            }
        }

        if (SystemInput.GetKeyDown(KeyCode.Mouse0) && !mouseIsOverWindowsUI)
        {
            HandleClick(mousePosWin, mousePosUnity);
        }

        /*
        Todo: encapsulate the hold-button trigger logic
        */

        /* Hold escape for 1 second to quit the app */

        if (SystemInput.GetKey(KeyCode.Escape))
        {
            _escapeTimer += Time.deltaTime;
            if (_escapeTimer >= 1f)
            {
                if (!Application.isEditor)
                {
                    Debug.Log("Bye bye!");
                    Application.Quit();
                }
                _escapeTimer = 0;
            }
        } else
        {
            _escapeTimer = 0;
        }

        /* Hold Z for 1 second to toggle debug */

        if (SystemInput.GetKey(KeyCode.Z))
        {
            _debugToggleTimer += Time.deltaTime;
            if (_debugToggleTimer >= 1f)
            {
                Debug.Log("Toggling debug view");
                _showDebug = !_showDebug;
                _debugToggleTimer = 0;
            }
        }
        else
        {
            _debugToggleTimer = 0;
        }
    }

    private void UpdatePerformanceSettings()
    {
        /*
        Go into low-power mode when user starts playing any full-screen
        game or video.
        */

        /*
        Todo:
        - report which app it is (for debugging, filtering out false positives)
        - filter out Discord Desktop Streaming's invisible window (and others like it)
        - Make all game logic robust to drastically lowered framerate and resolution
        */

        IntPtr fullscreenHwnd;
        bool reduceCompute =
            DesktopWindowTracker.IsAnyWindowFullscreen(out fullscreenHwnd) &&
            fullscreenHwnd != _hwnd; // exclude our own fullscreen window 😅
        if (reduceCompute)
        {
            if (Application.targetFrameRate == FramerateActive)
            {   
                _text.Clear();
                WinApi.GetWindowText(fullscreenHwnd, _text, _text.Capacity);
                Debug.Log($"Full-screen app detected: {fullscreenHwnd} - \"{_text.ToString()}\", setting low power mode");
                Application.targetFrameRate = FramerateHidden;
            }
        }
        else
        {
            if (Application.targetFrameRate == FramerateHidden)
            {
                Debug.Log("Full-screen app closed, setting normal power mode");
                Application.targetFrameRate = FramerateActive;
            }
        }
    }

    private void HandleClick(Vector2Int mousePosWin, Vector2Int mousePosUnity)
    {
        Debug.Log("Handle click");

        // click should not be blocked by any window closer in Z-order
        var ourWindowInfo = _windowTracker.GetWindowInfo(_hwnd);
        if (ourWindowInfo == null)
        {
            Debug.LogError("Failed to get our app's windowInfo");
            return;
        }
        bool clickedCloserWindow = _windowTracker.IsPointCoveredByWindow(mousePosWin, out DesktopWindowTracker.WindowInfo windowInfo, ourWindowInfo.Z);

        if (clickedCloserWindow)
        {
            return;
        }

        Debug.Log("Will raycast");

        bool clickedSolidGeometry = false;

        Ray ray = _camera.ScreenPointToRay(new Vector3(mousePosUnity.x, mousePosUnity.y, 0.1f));
        if (Physics.Raycast(ray, out RaycastHit hitInfo, _maxRaycastDistance, _interactableLayers))
        {
            // We clicked into our world

            Debug.Log("Hit world");

            // Did we click a character?
            var parent = hitInfo.collider.transform.parent;
            var character = parent != null ? parent.GetComponent<Character>() : null;
            bool clickedCharacter = character != null;

            if (clickedCharacter)
            {
                // SetWindowZOrder(ZWindowOrder.Front);
                Debug.Log("Hit character");
                character.OnClicked();
                return;
            }
            
            else
            {
                Debug.Log("Hit other geometry");
                clickedSolidGeometry = true;
            }
        }

        if (clickedSolidGeometry)
        {
            if (Time.timeAsDouble > _lastFoodSpawnTime + FoodSpawnDelay)
            {
                Debug.Log("spawn food on geometry");
                var spawnPos = hitInfo.point + hitInfo.normal * 0.5f; // todo: unify with mouse hover behaviour
                GameObject.Instantiate(_foodPrefab, spawnPos, Quaternion.identity);
                _lastFoodSpawnTime = Time.timeAsDouble;
            }
        }

        else
        {
            Debug.Log("Spawn food in air");
            var spawnPos = _camera.ScreenToWorldPoint(new Vector3(mousePosUnity.x, mousePosUnity.y, -_camera.transform.position.z));
            GameObject.Instantiate(_foodPrefab, spawnPos, Quaternion.identity);
            _lastFoodSpawnTime = Time.timeAsDouble;
        }
    }

    private const float FoodSpawnDelay = 0.5f;

    private void OnGUI()
    {
        if (!_showDebug)
        {
            return;
        }

        float2 guiSize = new float2(800, 600);
        
        var guiRect = new Rect(Screen.width - guiSize.x, 0, guiSize.x, guiSize.y);
        GUILayout.BeginArea(guiRect);
        GUILayout.BeginVertical(GUI.skin.box);
        {
            GUILayout.Label("Desktop Pet");
            GUILayout.Label("Hold ESCAPE for 1 second to quit");
            GUILayout.Label($"Screen: {Screen.width}x{Screen.height}");
            GUILayout.Label($"Camera pixels: {Camera.main.pixelWidth}x{Camera.main.pixelHeight}");
            int rezWidth = (int)Mathf.Ceil(ScalableBufferManager.widthScaleFactor * Screen.currentResolution.width);
            int rezHeight = (int)Mathf.Ceil(ScalableBufferManager.heightScaleFactor * Screen.currentResolution.height);
            string rezText = string.Format("Scale: {0:F3}x{1:F3}\nResolution: {2}x{3}\n",
                1,
                1,
                rezWidth,
                rezHeight);
            GUILayout.Label($"URP Render Scale: {rezText}");
            GUILayout.Label($"Target framerate: {Application.targetFrameRate}, framerate: {(1f / Time.smoothDeltaTime):0.0} fps");
            GUILayout.Space(8f);
            for (int c = 0; c < _characters.Count; c++)
            {
                var character = _characters[c];
                GUILayout.Label($"Character {c}:");
                GUILayout.Label($"\tState: {character.State}");
                GUILayout.Label($"\tIdle State: {character.IdleState}");
            }
            GUILayout.Space(8f);

            IntPtr exStyle = WinApi.GetWindowLongPtr(_hwnd, GWL_Flags.GWL_EXSTYLE);
            long newStyle = exStyle.ToInt64();
            bool isTransparent = Mask.IsBitSet(newStyle, (uint)WindowStylesEx.WS_EX_TRANSPARENT);
            GUILayout.Label($"Window Transparent: {isTransparent}");

            GUILayout.Space(8f);
            GUILayout.Label($"Our window: {_hwnd}");
            GUILayout.Label($"Open windows: {_windowTracker.VisibleWindows.Count}");
            for (int w = 0; w < _windowTracker.VisibleWindows.Count; w++)
            {
                var wnd = _windowTracker.VisibleWindows[w];
                if (wnd.Handle == _hwnd)
                {
                    GUILayout.Label($"{w}: {wnd.Handle} | {wnd.Title} <- It's us!");
                } else {
                    GUILayout.Label($"{w}: {wnd.Handle} | {wnd.Title}");
                }
            }
        }
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    /* 
    Note: currently unused
    
    Installs a callback into the windowing system that runs when anything changes in the window
    
    */

    private bool InstallWindowProc()
    {
        /*
        Note: should run after succesful TryHook() call
        */

        if (Application.platform != RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor)
        {
            Debug.LogError($"Platform not supported: {Application.platform}");
            return false;
        }

        if (_hwnd == IntPtr.Zero)
        {
            Debug.LogError("Failed to get window handle");
            return false;
        }

        // Create delegate and keep it alive
        _newWndProcDelegate = new WndProcDelegate(WndProc);

        // Install based on architecture
        if (IntPtr.Size == 8)
            _oldWndProc = WinApi.SetWindowLongPtr64_Delegate(_hwnd, GWL_Flags.GWL_WNDPROC, _newWndProcDelegate);
        else
            _oldWndProc = WinApi.SetWindowLongPtr32_Delegate(_hwnd, GWL_Flags.GWL_WNDPROC, _newWndProcDelegate);

        if (_oldWndProc == IntPtr.Zero)
        {
            Debug.LogError("Failed to install window procedure");
            return false;
        }
        
        Debug.Log("SelectiveClickThrough: Window procedure installed successfully");
        return true;
    }

    private void UninstallWindowProc()
    {
        if (_oldWndProc != IntPtr.Zero)
        {
            if (_hwnd != IntPtr.Zero)
            {
                // Restore original window proc if we have one
                if (IntPtr.Size == 8)
                    WinApi.SetWindowLongPtr64(_hwnd, GWL_Flags.GWL_WNDPROC, _oldWndProc);
                else
                    WinApi.SetWindowLongPtr32(_hwnd, GWL_Flags.GWL_WNDPROC, _oldWndProc);

                Debug.Log("SelectiveClickThrough: Window procedure uninstalled");
            }
        }
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WinApi.WM_NCHITTEST)
        {
            
            // Extract mouse coordinates from lParam
            int screenX = (short)(lParam.ToInt32() & 0xFFFF);
            int screenY = (short)((lParam.ToInt32() >> 16) & 0xFFFF);

            if (ShouldCaptureInput(screenX, screenY))
            {
                return new IntPtr(WinApi.HTCLIENT); // Capture
            }
            else
            {
                return new IntPtr(WinApi.HTTRANSPARENT); // Pass through
            }
        }

        // Prevent activation unless we really want it
        // if (msg == WinApi.WM_MOUSEACTIVATE)
        // {
        //     // Can add selective focus acceptance throug MA_ACTIVATE
        //     // For now: never gain focus
        //     return new IntPtr(WinApi.MA_NOACTIVATE);
        // } 

        // if (msg == WinApi.WM_ACTIVATE)
        // {
        //     Debug.Log("Window was activated, or another one was");
        //     return IntPtr.Zero;
        // }

        // if (msg == WinApi.WM_ACTIVATEAPP)
        // {
        //     Debug.Log("WindowApp was activated, or another one was");
        //     return IntPtr.Zero;
        // }

        // if (msg == WinApi.WM_WINDOWPOSCHANGED)
        // { 
        //     Debug.Log("Window pos was changed");
        //     return IntPtr.Zero;
        // }

        // Pass all other messages to original window procedure (anything unhandled passes through)
        return WinApi.CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
    }


    /* Determine whether the app needs input focus, or to let everything pass through to windows below */

    private static bool ShouldCaptureInput(int screenX, int screenY)
    {
        if (_instance == null)
            return false;

        Vector2 mousePos = new Vector2(screenX, screenY);

        // Check cache
        if (Time.realtimeSinceStartup - _lastTestTime < CACHE_DURATION &&
            Vector2.Distance(mousePos, _lastTestedMousePos) < CACHE_DISTANCE)
        {
            return _lastHitResult;
        }

        // Perform actual hit test
        _lastTestedMousePos = mousePos;
        _lastTestTime = Time.realtimeSinceStartup;
        _lastHitResult = _instance.PerformHitTest(screenX, screenY);

        return _lastHitResult;
    }

    private bool PerformHitTest(int screenX, int screenY)
    {
        Vector2 unityScreenPos = new Vector2(screenX, Screen.height - screenY);

        // Todo: UI Hittesting

        Ray ray = _camera.ScreenPointToRay(unityScreenPos);
        if (Physics.Raycast(ray, out RaycastHit hit, _maxRaycastDistance, _interactableLayers))
        {
            return true;
        }

        return false;
    }

    private static bool IsWindowsDesktop()
    {
        return !(Application.platform != RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor);
    }

    /* 
    Important:
    URP renderer needs to be configured to render to a buffer with transparency information in there!
    and DXGI swapchain for DX11 needs to be unchecked to old style
    */
    private bool MakeWindowTransparentFullscreen()
    {
        if (!IsWindowsDesktop())
        {
            Debug.LogError($"Platform not supported: {Application.platform}");
            return false;
        }

        // Set Unity camera to transparent
        _camera.backgroundColor = new Color(0, 0, 0, 0);
        _camera.clearFlags = CameraClearFlags.SolidColor;

        try
        {
            const int PROCESS_PER_MONITOR_DPI_AWARE = 2;
            WinApi.SetProcessDpiAwareness(PROCESS_PER_MONITOR_DPI_AWARE);
        }
        catch
        {
            Debug.Log("Couldn't set DPI awarness, using fallback");
            WinApi.SetProcessDPIAware(); // Fallback for older Windows
        }

        // Make it a popup window
        if (WinApi.SetWindowLongPtr(_hwnd, GWL_Flags.GWL_STYLE, new IntPtr((uint)WindowStyles.WS_POPUP | (uint)WindowStyles.WS_VISIBLE)) == IntPtr.Zero)
        {
            Debug.LogError($"Failed to set popup window style");
            return false;
        }

        // Force window to fit full-screen size, instead of work area size (which is minus taskbar?)
        int screenWidth = Screen.currentResolution.width;
        int screenHeight = Screen.currentResolution.height;
        WinApi.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, screenWidth, screenHeight, WinApi.SWP_FRAMECHANGED | WinApi.SWP_SHOWWINDOW);

        // Make it click-through, not take focus, hidden from taskbar and task switcher
        IntPtr exStyle = WinApi.GetWindowLongPtr(_hwnd, GWL_Flags.GWL_EXSTYLE);
        long newExStyle = exStyle.ToInt64();
        // newStyle |= (uint)WindowStylesEx.WS_EX_TOOLWINDOW; // prevent showing in task switcher and task bar (also puts app in separate windows Z-order list, not good)
        newExStyle |= (uint)WindowStylesEx.WS_EX_NOACTIVATE; // prevent taking focus
        newExStyle |= (uint)WindowStylesEx.WS_EX_LAYERED;
        // newStyle |= (uint)WindowStylesEx.WS_EX_TRANSPARENT; // make everything clickthrough, always

        if ((WinApi.SetWindowLongPtr(_hwnd, GWL_Flags.GWL_EXSTYLE, new IntPtr(newExStyle)) == IntPtr.Zero) && (exStyle != IntPtr.Zero))
        {
            Debug.LogError($"Failed to set window ex style");
            return false;
        }

        // Enable DWM transparency (this is what gets the transparency/chromakey to work)
        Margins margins = new Margins { cxLeftWidth = -1 };
        int dwmResult = WinApi.DwmExtendFrameIntoClientArea(_hwnd, ref margins);
        Debug.Log($"DWM result: 0x{dwmResult:X} (0 = S_OK)");
  
        SetWindowZOrder(ZWindowOrder.Bottom);

        _windowMode = AppWindowMode.TransparentZOrdered;

        return true;
    }

    /* 
    Important:
    Only supports opaque render target
    and DXGI swapchain for DX11 needs to be checked to new style
    */
    private bool MakeWindowOpaqueBehindIcons()
    {
        if (!IsWindowsDesktop())
        {
            Debug.LogError($"Platform not supported: {Application.platform}");
            return false;
        }

        // Set Unity camera to skybox
        _camera.clearFlags = CameraClearFlags.Skybox;

        try
        {
            const int PROCESS_PER_MONITOR_DPI_AWARE = 2;
            WinApi.SetProcessDpiAwareness(PROCESS_PER_MONITOR_DPI_AWARE);
        }
        catch
        {
            Debug.Log("Couldn't set DPI awarness, using fallback");
            WinApi.SetProcessDPIAware(); // Fallback for older Windows
        }

        // // Make it a borderless window
        // IntPtr style = WinApi.GetWindowLongPtr(_hwnd, GWL_Flags.GWL_STYLE);
        // long newStyle = style.ToInt64();

        long newStyle = 0;
        newStyle |= (uint)WindowStyles.WS_POPUP;
        // newStyle |= (uint)WindowStyles.WS_CAPTION;
        // newStyle |= (uint)WindowStyles.WS_THICKFRAME;
        // newStyle |= (uint)WindowStyles.WS_SYSMENU;
        // newStyle |= (uint)WindowStyles.WS_MAXIMIZEBOX;
        // newStyle |= (uint)WindowStyles.WS_MINIMIZEBOX;

        if (WinApi.SetWindowLongPtr(_hwnd, GWL_Flags.GWL_STYLE, new IntPtr(newStyle)) == IntPtr.Zero)
        {
            Debug.LogError($"Failed to set popup window style");
            return false;
        }

        IntPtr exStyle = WinApi.GetWindowLongPtr(_hwnd, GWL_Flags.GWL_EXSTYLE);
        long newExStyle = exStyle.ToInt64();
        newExStyle = Mask.UnsetBit(newExStyle, (uint)WindowStylesEx.WS_EX_TOOLWINDOW);
        newExStyle = Mask.SetBit(newExStyle, (uint)WindowStylesEx.WS_EX_TRANSPARENT);
        newExStyle = Mask.SetBit(newExStyle, (uint)WindowStylesEx.WS_EX_LAYERED);

        if ((WinApi.SetWindowLongPtr(_hwnd, GWL_Flags.GWL_EXSTYLE, new IntPtr(newExStyle)) == IntPtr.Zero) && (exStyle != IntPtr.Zero))
        {
            Debug.LogError($"Failed to set window ex style");
            return false;
        }

        // Seems to be needed?
        Margins margins = new Margins { cxLeftWidth = -1 };
        int dwmResult = WinApi.DwmExtendFrameIntoClientArea(_hwnd, ref margins);
        Debug.Log($"DWM result: 0x{dwmResult:X} (0 = S_OK)");

        var workerW = DesktopWindowTracker.GetDesktopBackgroundWindowWorker();
        if (workerW == IntPtr.Zero)
        {
            Debug.LogError($"Failed to set window as workerW child");
            return false;
        }

        // Force window to fit full-screen size, instead of work area size (which is minus taskbar?)
        // IMPORTANT: Doing this *after* setparent seems to mess it up w.r.t. taskbar?
        int screenWidth = Screen.currentResolution.width;
        int screenHeight = Screen.currentResolution.height;
        WinApi.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, screenWidth, screenHeight, WinApi.SWP_FRAMECHANGED | WinApi.SWP_SHOWWINDOW);

        Win32.SetParent(_hwnd, workerW);

        _windowMode = AppWindowMode.BehindDesktopIcons;

        return true;
    }

    private bool SetWindowTransparent(bool makeTransparent)
    {
        if (!IsWindowsDesktop())
        {
            return true;
        }

        if (_windowMode != AppWindowMode.TransparentZOrdered)
        {
            return false;
        }

        IntPtr exStyle = WinApi.GetWindowLongPtr(_hwnd, GWL_Flags.GWL_EXSTYLE);
        long newExStyle = exStyle.ToInt64();
        bool isExTransparent = Mask.IsBitSet(newExStyle, (uint)WindowStylesEx.WS_EX_TRANSPARENT);

        if (makeTransparent == isExTransparent) {
            // no need to do anything
            return true;
        }

        if (makeTransparent)
        {
            newExStyle = Mask.SetBit(newExStyle, (uint)WindowStylesEx.WS_EX_TRANSPARENT); // make everything clickthrough, always
        } else
        {
            newExStyle = Mask.UnsetBit(newExStyle, (uint)WindowStylesEx.WS_EX_TRANSPARENT); // make nothing clickthrough
        }

        if ((WinApi.SetWindowLongPtr(_hwnd, GWL_Flags.GWL_EXSTYLE, new IntPtr(newExStyle)) == IntPtr.Zero) && (exStyle != IntPtr.Zero))
        {
            Debug.LogError($"Failed to set window ex style");
            return false;
        }

        return true;
    }

    private enum ZWindowOrder
    {
        Bottom,
        Front,
        Top   
    }

    private void SetWindowZOrder(ZWindowOrder order)
    {
        /*
        Todo: disable if running behind desktop icons
        */

        switch (order)
        {
            case ZWindowOrder.Bottom:
                // Send to bottom (behind all windows)
                WinApi.SetWindowPos(_hwnd, WinApi.HWND_BOTTOM, 0, 0, 0, 0, WinApi.SWP_NOMOVE | WinApi.SWP_NOSIZE | WinApi.SWP_NOACTIVATE);
                break;
            case ZWindowOrder.Front:
                // Bring to front (but not always-on-top, and allowing click-through)

                // Works for putting in front of any target window
                IntPtr foregroundWindow = WinApi.GetForegroundWindow();
                if (foregroundWindow != IntPtr.Zero)
                {
                    WinApi.SetWindowPos(_hwnd, foregroundWindow, 0, 0, 0, 0, WinApi.SWP_NOMOVE | WinApi.SWP_NOSIZE | WinApi.SWP_NOACTIVATE); // set our window behind the top one
                    WinApi.SetWindowPos(foregroundWindow, _hwnd, 0, 0, 0, 0, WinApi.SWP_NOMOVE | WinApi.SWP_NOSIZE | WinApi.SWP_NOACTIVATE); // now switch them around
                    WinApi.SetWindowPos(_hwnd, WinApi.HWND_NOTOPMOST, 0, 0, 0, 0, WinApi.SWP_NOMOVE | WinApi.SWP_NOSIZE | WinApi.SWP_NOACTIVATE | WinApi.SWP_FRAMECHANGED); // avoid being stickied as topmost
                }

                break;
            case ZWindowOrder.Top:
                // Always on top
                WinApi.SetWindowPos(_hwnd, WinApi.HWND_TOPMOST, 0, 0, 0, 0, WinApi.SWP_NOMOVE | WinApi.SWP_NOSIZE | WinApi.SWP_NOACTIVATE);
                break;
        }
    }

    public override void DrawShapes(Camera cam)
    {
        if (!_showDebug)
        {
            return;
        }

        /*
        If behind desktop icons mode
        When rendering AfterOpaques, the rendering fails, only showing when overlaying something else that is opaque. Why?
        */
        using (Draw.Command(cam, UnityEngine.Rendering.Universal.RenderPassEvent.AfterRenderingTransparents))
        {
            Draw.SizeSpace = ThicknessSpace.Meters;
            Draw.ThicknessSpace = ThicknessSpace.Pixels;
            Draw.RadiusSpace = ThicknessSpace.Meters;
            Draw.Thickness = 1f;
            Draw.BlendMode = ShapesBlendMode.Opaque;

            IntPtr listViewHwnd = DesktopIconMonitor.GetDesktopListView();

            // Get icon spacing/size from ListView
            const uint LVM_GETITEMSPACING = 0x1033;
            IntPtr spacing = WinApi.SendMessage(listViewHwnd, LVM_GETITEMSPACING, IntPtr.Zero, IntPtr.Zero);
            int iconWidth = (int)(spacing.ToInt32() & 0xFFFF);
            int iconHeight = (int)((spacing.ToInt32() >> 16) & 0xFFFF);

            uint desktopDpi = WinApi.GetDpiForSystem();//WinApi.GetDpiForWindow(listViewHwnd);
            // Debug.Log($"desktop listview dpi: {desktopDpi}");
            // Debug.Log($"Screen Resolution: {Screen.width}x{Screen.height}, Windows Screen: {Screen.currentResolution.width}x{Screen.currentResolution.height}");

            const uint LVM_GETVIEW = 0x108F;
            int viewMode = (int)WinApi.SendMessage(listViewHwnd, LVM_GETVIEW, IntPtr.Zero, IntPtr.Zero);
            // 0 = Large icons, 1 = Small icons, 2 = List, 3 = Details, 4 = Tile (but on desktop)

            var iconSize = GetIconSize(iconWidth, iconHeight);
            // Debug.Log($"Icon size detected: {iconWidth}, {iconHeight} -> {iconSize}. Viewmode: {viewMode}");

            float offsetX = 0;
            float offsetY = 0;
            switch (iconSize)
            {
                case DesktopIconSize.Small:
                    offsetX = -22;
                    offsetY = +14;
                    break;
                case DesktopIconSize.Medium:
                    offsetX = -14;
                    offsetY = +14;
                    break;
                case DesktopIconSize.Large:
                    offsetX = -6;
                    offsetY = +14;
                    break;
            }

            var centerOffset = new int2(iconWidth / 2, iconHeight / 2);


            float scale = desktopDpi / 96f;

            int screenHeight = Screen.currentResolution.height;
            
            Draw.Color = Color.magenta;
            Draw.Sphere(cam.ScreenToWorldPoint(new Vector3(0f, 0f, -cam.transform.position.z)), 1f);
            Draw.Sphere(cam.ScreenToWorldPoint(new Vector3(0f, Screen.currentResolution.height, -cam.transform.position.z)), 1f);
            Draw.Color = Color.black;
            Draw.Sphere(cam.ScreenToWorldPoint(new Vector3(0f, 0f, -cam.transform.position.z)), 0.1f);
            Draw.Sphere(cam.ScreenToWorldPoint(new Vector3(0f, Screen.currentResolution.height, -cam.transform.position.z)), 0.1f);

            foreach (var iconPos in _iconMonitor.Icons)
            {
                // Draw.Rectangle(item.bounds);
                float y = screenHeight - iconPos.y - iconHeight;
                var pos = cam.ScreenToWorldPoint(new Vector3(
                    (iconPos.x + offsetX) * scale,
                    (y + offsetY) * scale,
                    -cam.transform.position.z));

                Draw.Color = Color.cyan;
                Draw.RectangleBorder(pos, new Rect(0,0, iconWidth, iconHeight), 1);

                var centerPos = cam.ScreenToWorldPoint(new Vector3(
                    (iconPos.x + offsetX + centerOffset.x) * scale,
                    (y + offsetY + centerOffset.y) * scale,
                    -cam.transform.position.z));

                Draw.Color = Color.black;
                Draw.Sphere(centerPos, 0.025f);
            }
        }
    }

    public enum AppWindowMode
    {
        None,
        TransparentZOrdered,
        BehindDesktopIcons
    }

    private enum DesktopIconSize
    {
        Small,
        Medium,
        Large
    }

    private static readonly int2[] DesktopIconSizes = new int2[]
        {
            new int2(76, 85), // Small, ~80
            new int2(76, 98), // Medium, ~96
            new int2(107, 149) // Large, ~128
        };

    private static DesktopIconSize GetIconSize(int width, int height)
    {
        int closestIdx = 1;
        int closestDist = int.MaxValue;
        for (int i = 0; i < DesktopIconSizes.Length; i++)
        {
            int dist = math.abs(height - DesktopIconSizes[i].y);
            if (dist < closestDist)
            {
                closestDist = dist;
                closestIdx = i;
            }
        }
        return (DesktopIconSize)closestIdx;
    }
}