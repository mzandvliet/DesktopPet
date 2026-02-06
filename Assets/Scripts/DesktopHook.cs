using UnityEngine;
using System;
using System.Text;
using Unity.Mathematics;
using Frantic.Windows;
using DrawBehindDesktopIcons;

/*

The app runs as a full-screen window with full transparency.
It can move itself up and down in Z-order to move in front of other windows or behind them.


Todo: tray icon / menu

Issues:

Window hopping behavior doesn't work well with OperaGX/Firefox browser window, or for some explorer windows
but they do show up in the visible window list

ScreenToGif window is seen as a mouse target
ignore it in DesktopWindowTracker

*/

public class DesktopHook : MonoBehaviour
{
    [SerializeField] private Character _character;
    [SerializeField] private GameObject _foodPrefab;

    [SerializeField] private LayerMask _interactableLayers = -1;
    [SerializeField] private float _maxRaycastDistance = 100f;

    private Camera _camera;
    private static StringBuilder _text;
    private Vector2 _mouseClickPos;
    private float _escapeTimer;

    private DesktopWindowTracker _windowTracker;

    private static WndProcDelegate _newWndProcDelegate;
    private static IntPtr _oldWndProc;
    private static DesktopHook _instance;
    private IntPtr _hwnd;

    // Cached state for window mouse-cursor focus check
    private static Vector2 _lastTestedMousePos;
    private static bool _lastHitResult;
    private static float _lastTestTime;
    private const float CACHE_DURATION = 0.016f; // ~60fps
    private const float CACHE_DISTANCE = 3f; // pixels

    private double _lastFoodSpawnTime;

    public void Awake()
    {
        if (_instance != null)
        {
            Debug.LogError("Multiple SelectiveClickThrough instances detected!");
            return;
        }
        _instance = this;
        _windowTracker = gameObject.AddComponent<DesktopWindowTracker>();

        Application.targetFrameRate = 60;
        Application.runInBackground = true;

        _camera = gameObject.GetComponent<Camera>();

        // Transparent background
        _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        _camera.clearFlags = CameraClearFlags.SolidColor;

        _text = new StringBuilder(4096);
    }

    private void Start()
    {
        Screen.fullScreenMode = FullScreenMode.Windowed;
        // Screen.SetResolution(3440, 1440, false);

        if (ConfigureApplicationWindow())
        {
            Debug.Log("Succesfully hooked into desktop background!");
        }
        else
        {
            Debug.Log("Error: Failed to hook into desktop background...");
        }
    }

    private void OnDestroy()
    {
    }

    private void Update()
    {
        /* Update input polling (can't use Unity input system for most situation in this app) */

        SystemInput.Process();

        /* Decide on window input-transparency based on whether cursor is hovering over anything interactive in the scene */

        var mousePosWin = SystemInput.GetCursorPosition();
        if (ShouldCaptureInput(mousePosWin.x, mousePosWin.y))
        {
            SetWindowTransparent(false);
        } else
        {
            SetWindowTransparent(true);
        }

        /* Update character, and determine whether it is in front of or behind the window that the cursor currently hovers over */

        bool characterInFrontOfHoveredWindow = false;
        DesktopWindowTracker.WindowInfo hoveredWindowInfo;
        _windowTracker.IsPointCoveredByWindow(mousePosWin, out hoveredWindowInfo, _hwnd);
        var ourWindowInfo = _windowTracker.GetWindowInfo(_hwnd);
        if (hoveredWindowInfo != null && ourWindowInfo != null)
        {
            characterInFrontOfHoveredWindow = ourWindowInfo.Z < hoveredWindowInfo.Z;
        }
        if (SystemInput.GetKeyDown(KeyCode.Space))
        {
            Debug.Log($"hov: {(hoveredWindowInfo != null ? hoveredWindowInfo : 0)} char: {(ourWindowInfo != null ? ourWindowInfo : 0)} | char in front: {characterInFrontOfHoveredWindow}");
        }

        // to unity coordinates
        var mousePosUnity = mousePosWin;
        mousePosUnity.y = Screen.height - mousePosUnity.y;

        var camCharDist = math.abs(_character.transform.position.z - _camera.transform.position.z);
        float lookWorldZ = camCharDist + (characterInFrontOfHoveredWindow ? +2f : -2f);
        var mousePosWorld = _camera.ScreenToWorldPoint(new Vector3(mousePosUnity.x, mousePosUnity.y, lookWorldZ));
        _character.LookAt(mousePosWorld);

        /* If clicking on the character, change its Z-order to sit above the currently active window */

        if (SystemInput.GetKeyDown(KeyCode.Mouse0))
        {
            _mouseClickPos = mousePosUnity;

            HandleClick(mousePosWin, mousePosUnity);
        }

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
            }
        } else
        {
            _escapeTimer = 0;
        }
    }

    private void HandleClick(Vector2Int mousePosWin, Vector2Int mousePosUnity)
    {
        Ray ray = _camera.ScreenPointToRay(new Vector3(mousePosUnity.x, mousePosUnity.y, 0.1f));
        if (Physics.Raycast(ray, out RaycastHit hit, _maxRaycastDistance, _interactableLayers))
        {
            bool clickedCharacter = hit.collider.transform.parent?.GetComponent<Character>();
            Debug.Log($"clickedCharacter: {clickedCharacter}");
            if (clickedCharacter)
            {
                SetWindowZOrder(ZWindowOrder.Front);
                _character.OnClicked();
                return;
            }
        }

        // If clicked desktop background, with nothing else in focus, this is our territory

        // get active window?
        // if none of our tracked visible windows catches the click?
        // if click doesn't hit any desktop icons

        bool clickedDesktopBackground;

        if (Application.isEditor)
        {
            clickedDesktopBackground = true;
        }
        else
        {
            var windowUnderCursor = WinApi.WindowFromPoint(new Point(mousePosWin.x, mousePosWin.y));
            var activeWindow = WinApi.GetActiveWindow();
            var desktopWindow = WinApi.GetDesktopWindow();
            var desktopShellWindow = GetDesktopBackgroundWindow();

            DesktopWindowTracker.WindowInfo windowInfo;
            var isPointCoveredByWindow = _windowTracker.IsPointCoveredByWindow(mousePosWin, out windowInfo, ignoreHWnd: _hwnd);

            Debug.Log($"Click: windowUnderCursor: {windowUnderCursor}, activeWindow: {activeWindow}, pointCovered: {isPointCoveredByWindow}, windowInfo: {windowInfo}");
            Debug.Log($"desktopWindow: {desktopWindow}, desktopShellWindow: {desktopShellWindow}");

            clickedDesktopBackground = !isPointCoveredByWindow;
        }

        Debug.Log($"clickedBackground: {clickedDesktopBackground}");

        if (clickedDesktopBackground)
        {
            if (Time.timeAsDouble > _lastFoodSpawnTime + 1.0)
            {
                var spawnPos = _camera.ScreenToWorldPoint(new Vector3(mousePosUnity.x, mousePosUnity.y, -_camera.transform.position.z));
                GameObject.Instantiate(_foodPrefab, spawnPos, Quaternion.identity);
                _lastFoodSpawnTime = Time.timeAsDouble;
            }
        }
    }

    private void OnGUI()
    {
        float2 guiSize = new float2(800, 600);
        
        var guiRect = new Rect(Screen.width - guiSize.x, 0, guiSize.x, guiSize.y);
        GUILayout.BeginArea(guiRect);
        GUILayout.BeginVertical(GUI.skin.box);
        {
            GUILayout.Label("Desktop Pet");
            GUILayout.Label("Hold ESCAPE for 1 second to quit");
            GUILayout.Space(8f);
            GUILayout.Label($"Character State: {_character.State}");
            GUILayout.Space(8f);
            GUILayout.Label($"Last Click Pos: {_mouseClickPos}");
            GUILayout.Label($"Last Tested Pos: {_lastTestedMousePos}");
            GUILayout.Label($"Last Hit Result: {_lastHitResult}");

            IntPtr exStyle = WinApi.GetWindowLongPtr(_hwnd, GWL_Flags.GWL_EXSTYLE);
            long newStyle = exStyle.ToInt64();
            bool isTransparent = Mask.IsBitSet(newStyle, (uint)WindowStylesEx.WS_EX_TRANSPARENT);
            GUILayout.Label($"Window Transparent: {isTransparent}");

            GUILayout.Space(8f);
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

    /* 
    Important:
    URP renderer needs to be configured to render to a buffer with transparency information in there!
    */
    private bool ConfigureApplicationWindow()
    {
        if (Application.platform != RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor)
        {
            Debug.LogError($"Platform not supported: {Application.platform}");
            return false;
        }

        _hwnd = WinApi.GetActiveWindow();

        // Make it a popup window
        if(WinApi.SetWindowLongPtr(_hwnd, GWL_Flags.GWL_STYLE, new IntPtr((uint)WindowStyles.WS_POPUP | (uint)WindowStyles.WS_VISIBLE)) == IntPtr.Zero)
        {
            Debug.LogError($"Failed to set popup window style");
            return false;
        }

        // Make it click-through, not take focus, hidden from taskbar and task switcher
        IntPtr exStyle = WinApi.GetWindowLongPtr(_hwnd, GWL_Flags.GWL_EXSTYLE);
        long newStyle = exStyle.ToInt64();
        // newStyle |= (uint)WindowStylesEx.WS_EX_TOOLWINDOW; // prevent showing in task switcher and task bar (also puts app in separate windows Z-order list, not good)
        newStyle |= (uint)WindowStylesEx.WS_EX_NOACTIVATE; // prevent taking focus
        newStyle |= (uint)WindowStylesEx.WS_EX_LAYERED;
        // newStyle |= (uint)WindowStylesEx.WS_EX_TRANSPARENT; // make everything clickthrough, always

        if ((WinApi.SetWindowLongPtr(_hwnd, GWL_Flags.GWL_EXSTYLE, new IntPtr(newStyle)) == IntPtr.Zero) && (exStyle != IntPtr.Zero))
        {
            Debug.LogError($"Failed to set window ex style");
            return false;
        }

        // Enable DWM transparency (this is what gets the transparency/chromakey to work)
        Margins margins = new Margins { cxLeftWidth = -1 };
        int dwmResult = WinApi.DwmExtendFrameIntoClientArea(_hwnd, ref margins);
        Debug.Log($"DWM result: 0x{dwmResult:X} (0 = S_OK)");
  
        // Set Unity camera to transparent
        _camera.backgroundColor = new Color(0, 0, 0, 0);
        _camera.clearFlags = CameraClearFlags.SolidColor;

        SetWindowZOrder(ZWindowOrder.Bottom);

        return true;
    }

    private bool SetWindowTransparent(bool makeTransparent)
    {
        IntPtr exStyle = WinApi.GetWindowLongPtr(_hwnd, GWL_Flags.GWL_EXSTYLE);
        long newStyle = exStyle.ToInt64();
        bool isTransparent = Mask.IsBitSet(newStyle, (uint)WindowStylesEx.WS_EX_TRANSPARENT);

        if (makeTransparent == isTransparent) {
            // no need to do anything
            return true;
        }

        if (makeTransparent)
        {
            newStyle = Mask.SetBit(newStyle, (uint)WindowStylesEx.WS_EX_TRANSPARENT); // make everything clickthrough, always
        } else
        {
            newStyle = Mask.UnsetBit(newStyle, (uint)WindowStylesEx.WS_EX_TRANSPARENT); // make nothing clickthrough
        }

        if ((WinApi.SetWindowLongPtr(_hwnd, GWL_Flags.GWL_EXSTYLE, new IntPtr(newStyle)) == IntPtr.Zero) && (exStyle != IntPtr.Zero))
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

    private static IntPtr GetProgramManagerWindowHandle()
    {
        // IntPtr wHandle = GetWindowHandle("progman");
        IntPtr wHandle = Win32.FindWindow("Progman", null);
        return wHandle;
    }

    private static IntPtr GetDesktopBackgroundWindow()
    {
        IntPtr progmanHandle = GetProgramManagerWindowHandle();
        Debug.Log($"progmanHandle found: {progmanHandle}.");

        IntPtr result = IntPtr.Zero;

        // Send 0x052C to Progman. This message directs Progman to spawn a 
        // WorkerW behind the desktop icons. If it is already there, nothing 
        // happens.
        // Debug.Log("Triggering ProgramManager WorkerW spawn...");
        // Win32.SendMessageTimeout(progmanHandle,
        //                         0x052C,
        //                         new IntPtr(0),
        //                         IntPtr.Zero,
        //                         Win32.SendMessageTimeoutFlags.SMTO_NORMAL,
        //                         1000,
        //                         out result);

        // Debug.Log("Attempting to find WorkerW through progman procHandle...");
        // IntPtr workerW = Win32.FindWindowEx(progmanHandle, IntPtr.Zero, "WorkerW", IntPtr.Zero); // windowName was null in example

        // If that doesn't work, try searching alternative layout

        IntPtr desktopHwnd = IntPtr.Zero;

        Debug.Log("Alternatively, enumerate top-level windows to find SHELLDLL_DefView as child...");

        // Enumerate top-level windows until finding SHELLDLL_DefView as child.
        Win32.EnumWindows(new Win32.EnumWindowsProc((topHandle, topParamHandle) => 
        {
            IntPtr SHELLDLL_DefView = Win32.FindWindowEx(topHandle, IntPtr.Zero, "SHELLDLL_DefView", IntPtr.Zero);

            if (SHELLDLL_DefView != IntPtr.Zero)
            {
                Debug.Log("Found SHELLDLL!");

                // If found, take next sibling as workerW
                // > Gets the WorkerW Window after the current one.
                // workerW = Win32.FindWindowEx(IntPtr.Zero, topHandle, "WorkerW", IntPtr.Zero);
                desktopHwnd = SHELLDLL_DefView;
                return false;
            }

            return true; // Continue enumeration
        }), IntPtr.Zero);

        return desktopHwnd;
    }
}