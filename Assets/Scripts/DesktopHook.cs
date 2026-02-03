using UnityEngine;
using System;
using System.Text;
using Unity.Mathematics;
using System.Runtime.InteropServices;

/*
Todo: tray icon / menu

Issues:

ScreenToGif window is seen as a mouse target
ignore it in DesktopWindowTracker

*/

public class DesktopHook : MonoBehaviour
{
    [SerializeField] private Character _character;

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

    // Caching for performance
    private static Vector2 _lastTestedMousePos;
    private static bool _lastHitResult;
    private static float _lastTestTime;
    private const float CACHE_DURATION = 0.016f; // ~60fps
    private const float CACHE_DISTANCE = 3f; // pixels



    [StructLayout(LayoutKind.Sequential)]
    public struct Rectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

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
        _camera.backgroundColor = new Color(1f, 0f, 1f, 1f); // Magenta 
        _camera.clearFlags = CameraClearFlags.SolidColor;

        _text = new StringBuilder(4096);

        // long mask = 0;
        // Debug.Log(IsBitSet(mask, WS_EX_TRANSPARENT));
        // mask = SetBit(mask, WS_EX_TRANSPARENT);
        // Debug.Log(IsBitSet(mask, WS_EX_TRANSPARENT));
        // mask = UnsetBit(mask, WS_EX_TRANSPARENT);
        // Debug.Log(IsBitSet(mask, WS_EX_TRANSPARENT));
    }

    private void Start()
    {
        Screen.fullScreenMode = FullScreenMode.Windowed;
        Screen.SetResolution(3440, 1440, false);

        // yield return new WaitForSeconds(0.5f);

        if (TryHook())
        {
            Debug.Log("Succesfully hooked into desktop background!");
            // InstallWindowProc();
        }
        else
        {
            Debug.Log("Error: Failed to hook into desktop background...");
        }
    }

    private void OnDestroy()
    {
        // UninstallWindowProc();
    }

    private void Update()
    {
        // if (Keyboard.current.anyKey.wasPressedThisFrame || Mouse.current.leftButton.wasPressedThisFrame)
        // {
        //     _character.Jump();
        // }

        // if (Mouse.current.leftButton.wasPressedThisFrame)
        // {
        //     _mouseClickPos = Mouse.current.position.value;
        // }

        SystemInput.Process();

        var mousePos = SystemInput.GetCursorPosition();
        if (ShouldCaptureInput(mousePos.x, mousePos.y))
        {
            SetWindowTransparent(false);
        } else
        {
            SetWindowTransparent(true);
        }

        bool characterInFrontOfHoveredWindow = false;
        // var hoveredWindowHandle = WindowFromPoint(new POINT(mousePos.x, mousePos.y));
        // var hoveredWindowInfo = _windowTracker.GetWindowInfo(hoveredWindowHandle);
        DesktopWindowTracker.WindowInfo hoveredWindowInfo;
        _windowTracker.IsPointCoveredByWindow(mousePos, out hoveredWindowInfo, _hwnd);
        var ourWindowInfo = _windowTracker.GetWindowInfo(_hwnd);
        if (hoveredWindowInfo != null && ourWindowInfo != null)
        {
            characterInFrontOfHoveredWindow = ourWindowInfo.Z < hoveredWindowInfo.Z;
        }
        if (SystemInput.GetKeyDown(KeyCode.Space))
        {
            Debug.Log($"hov: {(hoveredWindowInfo != null ? hoveredWindowInfo : 0)} char: {(ourWindowInfo != null ? ourWindowInfo : 0)} | char in front: {characterInFrontOfHoveredWindow}");
        }

        mousePos.y = Screen.height - mousePos.y; // to unity coordinates

        var camCharDist = math.abs(_character.transform.position.z - _camera.transform.position.z);
        float lookWorldZ = camCharDist + (characterInFrontOfHoveredWindow ? +2f : -2f);
        var mousePosWorld = _camera.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, lookWorldZ));
        _character.LookAt(mousePosWorld);

        if (SystemInput.GetKeyDown(KeyCode.Mouse0))
        {
            _mouseClickPos = mousePos;

            Ray ray = _camera.ScreenPointToRay(new Vector3(mousePos.x, mousePos.y, 0.1f));
            if (Physics.Raycast(ray, out RaycastHit hit, _maxRaycastDistance, _interactableLayers))
            {
                SetWindowZOrder(ZWindowOrder.Front);
                _character.Jump();
            }
        }

        // Hold ESC for 1 second to quit
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
            GUILayout.Label($"Last Click Pos: {_mouseClickPos}");
            GUILayout.Label($"Last Tested Pos: {_lastTestedMousePos}");
            GUILayout.Label($"Last Hit Result: {_lastHitResult}");

            IntPtr exStyle = GetWindowLongPtr(_hwnd, GWL_Flags.GWL_EXSTYLE);
            long newStyle = exStyle.ToInt64();
            bool isTransparent = IsBitSet(newStyle, WS_EX_TRANSPARENT);
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
            _oldWndProc = SetWindowLongPtr64_Delegate(_hwnd, GWL_Flags.GWL_WNDPROC, _newWndProcDelegate);
        else
            _oldWndProc = SetWindowLongPtr32_Delegate(_hwnd, GWL_Flags.GWL_WNDPROC, _newWndProcDelegate);

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
                    SetWindowLongPtr64(_hwnd, GWL_Flags.GWL_WNDPROC, _oldWndProc);
                else
                    SetWindowLongPtr32(_hwnd, GWL_Flags.GWL_WNDPROC, _oldWndProc);

                Debug.Log("SelectiveClickThrough: Window procedure uninstalled");
            }
        }
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_NCHITTEST)
        {
            
            // Extract mouse coordinates from lParam
            int screenX = (short)(lParam.ToInt32() & 0xFFFF);
            int screenY = (short)((lParam.ToInt32() >> 16) & 0xFFFF);

            if (ShouldCaptureInput(screenX, screenY))
            {
                return new IntPtr(HTCLIENT); // Capture
            }
            else
            {
                return new IntPtr(HTTRANSPARENT); // Pass through
            }
        }

        // Prevent activation unless we really want it
        // if (msg == WM_MOUSEACTIVATE)
        // {
        //     // Can add selective focus acceptance throug MA_ACTIVATE
        //     // For now: never gain focus
        //     return new IntPtr(MA_NOACTIVATE);
        // } 

        // if (msg == WM_ACTIVATE)
        // {
        //     Debug.Log("Window was activated, or another one was");
        //     return IntPtr.Zero;
        // }

        // if (msg == WM_ACTIVATEAPP)
        // {
        //     Debug.Log("WindowApp was activated, or another one was");
        //     return IntPtr.Zero;
        // }

        // if (msg == WM_WINDOWPOSCHANGED)
        // { 
        //     Debug.Log("Window pos was changed");
        //     return IntPtr.Zero;
        // }

        // Pass all other messages to original window procedure (anything unhandled passes through)
        return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
    }

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
    private bool TryHook()
    {
        if (Application.platform != RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor)
        {
            Debug.LogError($"Platform not supported: {Application.platform}");
            return false;
        }

        _hwnd = GetActiveWindow();

        // Make it a popup window
        if(SetWindowLongPtr(_hwnd, GWL_Flags.GWL_STYLE, new IntPtr(WS_POPUP | WS_VISIBLE)) == IntPtr.Zero)
        {
            Debug.LogError($"Failed to set popup window style");
            return false;
        }

        // Make it click-through, not take focus, hidden from taskbar and task switcher
        IntPtr exStyle = GetWindowLongPtr(_hwnd, GWL_Flags.GWL_EXSTYLE);
        long newStyle = exStyle.ToInt64();
        // newStyle |= WS_EX_TOOLWINDOW; // prevent showing in task switcher and task bar (also puts app in separate windows Z-order list, not good)
        newStyle |= WS_EX_NOACTIVATE; // prevent taking focus
        newStyle |= WS_EX_LAYERED;
        // newStyle |= WS_EX_TRANSPARENT; // make everything clickthrough, always

        if ((SetWindowLongPtr(_hwnd, GWL_Flags.GWL_EXSTYLE, new IntPtr(newStyle)) == IntPtr.Zero) && (exStyle != IntPtr.Zero))
        {
            Debug.LogError($"Failed to set window ex style");
            return false;
        }

        // Enable DWM transparency (this is what gets the transparency/chromakey to work)
        MARGINS margins = new MARGINS { cxLeftWidth = -1 };
        int dwmResult = DwmExtendFrameIntoClientArea(_hwnd, ref margins);
        Debug.Log($"DWM result: 0x{dwmResult:X} (0 = S_OK)");
  
        // Set Unity camera to transparent
        _camera.backgroundColor = new Color(0, 0, 0, 0);
        _camera.clearFlags = CameraClearFlags.SolidColor;

        SetWindowZOrder(ZWindowOrder.Bottom);

        return true;
    }

    private bool SetWindowTransparent(bool makeTransparent)
    {
        IntPtr exStyle = GetWindowLongPtr(_hwnd, GWL_Flags.GWL_EXSTYLE);
        long newStyle = exStyle.ToInt64();
        bool isTransparent = IsBitSet(newStyle, WS_EX_TRANSPARENT);

        if (makeTransparent == isTransparent) {
            // no need to do anything
            return true;
        }

        if (makeTransparent)
        {
            newStyle = SetBit(newStyle, WS_EX_TRANSPARENT); // make everything clickthrough, always
        } else
        {
            newStyle = UnsetBit(newStyle, WS_EX_TRANSPARENT); // make nothing clickthrough
        }

        if ((SetWindowLongPtr(_hwnd, GWL_Flags.GWL_EXSTYLE, new IntPtr(newStyle)) == IntPtr.Zero) && (exStyle != IntPtr.Zero))
        {
            Debug.LogError($"Failed to set window ex style");
            return false;
        }

        return true;
    }

    private static bool IsBitSet(long mask, uint bits)
    {
        return (mask & bits) != 0;
    }

    private static long SetBit(long mask, uint bits)
    {
        return mask | bits;
    }

    private static long UnsetBit(long mask, uint bits)
    {
        return mask & (~bits);
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
                SetWindowPos(_hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                break;
            case ZWindowOrder.Front:
                // Bring to front (but not always-on-top, and allowing click-through)

                // Todo: works but gains focus
                // SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                // SetWindowPos(_hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);

                // works but gains focus
                // SetWindowPos(_hwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                // SetWindowPos(_hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);

                // BringWindowToTop(_hwnd);
                // SetForegroundWindow(_hwnd);

                // IntPtr previousForeground = GetForegroundWindow();
                // SetForegroundWindow(_hwnd);
                // BringWindowToTop(_hwnd);
                // // Restore focus to whoever had it
                // SetForegroundWindow(previousForeground);

                // Works for putting in front of any target window
                IntPtr foregroundWindow = GetForegroundWindow();
                if (foregroundWindow != IntPtr.Zero)
                {
                    SetWindowPos(_hwnd, foregroundWindow, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE); // set our window behind the top one
                    SetWindowPos(foregroundWindow, _hwnd, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE); // now switch them around
                    SetWindowPos(_hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED); // avoid being stickied as topmost
                }

                break;
            case ZWindowOrder.Top:
                // Always on top
                SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                break;
        }
    }


    void StepUp()
    {
        Debug.Log("Stepping along Z order...");

        // // Window above us in z-order (GW_HWNDPREV = visually in front)
        // IntPtr aboveUs = GetWindow(hwnd, GW_HWNDPREV);

        // // Skip invisible windows
        // while (aboveUs != IntPtr.Zero && !IsWindowVisible(aboveUs))
        // {
        //     StringBuilder title = new StringBuilder(256);
        //     GetWindowText(aboveUs, title, title.Capacity);
        //     bool visible = IsWindowVisible(aboveUs);
        //     bool isUs = aboveUs == hwnd;
        //     Debug.Log($"Skipping invisible: '{title}' | Visible: {visible} | IsUs: {isUs} | Handle: {aboveUs}");
        //     aboveUs = GetWindow(aboveUs, GW_HWNDPREV);
        // }

        // if (aboveUs != IntPtr.Zero) 
        // {
        //     StringBuilder title = new StringBuilder(256);
        //     GetWindowText(aboveUs, title, title.Capacity);
        //     bool visible = IsWindowVisible(aboveUs);
        //     bool isUs = aboveUs == hwnd;
        //     Debug.Log($"Stepping past: '{title}' | Visible: {visible} | IsUs: {isUs} | Handle: {aboveUs}");

        //     SetWindowPos(hwnd, aboveUs, 0, 0, 0, 0,
        //         SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        // }
        // else
        // {
        //     Debug.Log("At top");
        // }

        // Find the visible window directly above us
        // IntPtr aboveUs = GetWindow(hwnd, GW_HWNDPREV);
        // while (aboveUs != IntPtr.Zero && !IsWindowVisible(aboveUs))
        // {
        //     aboveUs = GetWindow(aboveUs, GW_HWNDPREV);
        // }

        // if (aboveUs == IntPtr.Zero)
        // {
        //     Debug.Log("Already at top");
        //     return;
        // }

        // StringBuilder text = new StringBuilder(256);
        // GetWindowText(aboveUs, text, text.Capacity);
        // string aboveUsTitle = text.ToString();
        // Debug.Log($"Window above us: {aboveUsTitle}");

        // // Find the next visible window above THAT one
        // IntPtr twoAbove = GetWindow(aboveUs, GW_HWNDPREV);
        // while (twoAbove != IntPtr.Zero && !IsWindowVisible(twoAbove))
        // {
        //     twoAbove = GetWindow(twoAbove, GW_HWNDPREV);
        // }
        
        // text.Clear();
        // GetWindowText(twoAbove, text, text.Capacity);
        // string twoAboveUsTitle = text.ToString();
        // Debug.Log($"Window 2 above us: {twoAboveUsTitle}");

        // if (twoAbove != IntPtr.Zero)
        // {
        //     // Insert after twoAbove = behind twoAbove but in front of aboveUs
        //     SetWindowPos(hwnd, twoAbove, 0, 0, 0, 0,
        //         SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                
        //     Debug.Log($"Inserting after {twoAbove} | '{twoAboveUsTitle}'");
        // }
        // else
        // {
        //     // Nothing above aboveUs, so we go to the very top
        //     SetWindowPos(hwnd, HWND_TOP, 0, 0, 0, 0,
        //         SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        //     Debug.Log($"Stepped past: '{aboveUsTitle}' (now at top)");
        // }

        // var windows = _windowTracker.VisibleWindows;
        // for (int wi = 0; wi < length; wi++)
        // {
            
        // }
    }

    void MapZOrder()
    {
        // IntPtr desktop = GetDesktopWindow(); // Doesn't work as a parent for GetWindow on modern windows
        // IntPtr current = GetWindow(desktop, GW_HWNDFIRST);

        // Get the topmost window in the z-order directly
        // IntPtr current = GetWindow(IntPtr.Zero, GW_HWNDFIRST);

        // Debug.Log($"Mapping Z Order, starting from: {current}");

        // int index = 0;
        // while (current != IntPtr.Zero)
        // {
        //     StringBuilder title = new StringBuilder(256);
        //     GetWindowText(current, title, title.Capacity);
        //     bool visible = IsWindowVisible(current);
        //     bool isUs = current == hwnd;

        //     if (visible || isUs)
        //     {
        //         Debug.Log($"[{index}] '{title}' | Visible: {visible} | IsUs: {isUs} | Handle: {current}");
        //     }

        //     current = GetWindow(current, GW_HWNDNEXT);
        //     index++;
        // }

        // Debug.Log($"Mapped Z Order");

        StringBuilder title = new StringBuilder(256);

        // IntPtr current = GetForegroundWindow();
        IntPtr current = GetTopWindow(IntPtr.Zero);

        {
            GetWindowText(current, title, title.Capacity);
            bool visible = IsWindowVisible(current);
            bool isUs = current == _hwnd;
            Debug.Log($"Foreground: {current} | Visible: {visible} | IsUs: {isUs} | '{title}'");
        }

        // Walk closer first
        Debug.Log("=== Z-order Ascending ===");
        int index = 0;
        IntPtr closer = GetWindow(current, GW_HWNDPREV);
        while (closer != IntPtr.Zero)
        {
            // log it
            closer = GetWindow(closer, GW_HWNDPREV);
            if (closer != IntPtr.Zero)
            {
                current = closer;
            }
            
            GetWindowText(closer, title, title.Capacity);
            bool visible = IsWindowVisible(closer);
            bool isUs = closer == _hwnd;
            if (visible || isUs)
            {
                Debug.Log($"{index++:000}{closer} | Visible: {visible} | IsUs: {isUs} | '{title}'");
            }
        }

        Debug.Log("=== Z-order Descending ===");
        index = 0;
        IntPtr further = GetWindow(current, GW_HWNDNEXT);
        while (further != IntPtr.Zero)
        {
            // log it
            further = GetWindow(further, GW_HWNDNEXT);

            GetWindowText(further, title, title.Capacity);
            bool visible = IsWindowVisible(further);
            bool isUs = further == _hwnd;
            if (visible || isUs)
            {
                Debug.Log($"{index++:000}{further} | Visible: {visible} | IsUs: {isUs} | '{title}'");
            }
        }
        Debug.Log("======");
    }

    // Win32 Imports
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    const uint WS_POPUP = 0x80000000;
    const uint WS_VISIBLE = 0x10000000;
    const uint WS_EX_LAYERED = 0x00080000;
    const uint WS_EX_TRANSPARENT = 0x00000020;
    const uint WS_EX_TOOLWINDOW = 0x00000080;
    const uint WS_EX_NOACTIVATE = 0x08000000;

    const int HTCLIENT = 1;
    const int HTTRANSPARENT = -1;

    const uint WM_NCHITTEST = 0x0084;
    const uint WM_ACTIVATE = 0x0006;
    const uint WM_MOUSEACTIVATE = 0x0021;
    const uint WM_ACTIVATEAPP = 0x001C;
    const uint WM_WINDOWPOSCHANGED = 0x0047;

    const int MA_NOACTIVATE = 3;
    const int MA_ACTIVATE = 1;

    internal enum GWL_Flags : int
    {
        GWL_EXSTYLE = -20,
        GWLP_HINSTANCE = -6,
        GWLP_HWNDPARENT = -8,
        GWL_ID = -12,
        GWL_STYLE = -16,
        GWL_USERDATA = -21,
        GWL_WNDPROC = -4,
        DWLP_USER = 0x8,
        DWLP_MSGRESULT = 0x0,
        DWLP_DLGPROC = 0x4
    }

    internal enum LayeredWindowAttr : uint
    {
        LWA_ALPHA = 0x00000002,     //Use bAlpha to determine the opacity of the layered window.
        LWA_COLORKEY = 0x00000001   //Use crKey as the transparency color.
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct COLORREF
    {
        public byte R;
        public byte G;
        public byte B;

        public COLORREF(byte red, byte green, byte blue)
        {
            R = red; G = green; B = blue;
        }
    }

    [DllImport("kernel32.dll")]
    static extern uint GetLastError();

    [DllImport("kernel32.dll")]
    static extern void SetLastError(uint error);

    [DllImport("user32.dll")]
    static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, GWL_Flags nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, GWL_Flags nIndex);

    static IntPtr GetWindowLongPtr(IntPtr hWnd, GWL_Flags nIndex)
    {
        if (IntPtr.Size == 8)
            return GetWindowLongPtr64(hWnd, nIndex);
        else
            return GetWindowLongPtr32(hWnd, nIndex);
    }

    // Use the correct function for the architecture
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SetWindowLongPtr")]
    static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, GWL_Flags nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SetWindowLong")]
    static extern IntPtr SetWindowLongPtr32(IntPtr hWnd, GWL_Flags nIndex, IntPtr dwNewLong);

    static IntPtr SetWindowLongPtr(IntPtr hWnd, GWL_Flags nIndex, IntPtr dwNewLong)
    {
        if (IntPtr.Size == 8)
            return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
        else
            return SetWindowLongPtr32(hWnd, nIndex, dwNewLong);
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64_Delegate(IntPtr hWnd, GWL_Flags nIndex, WndProcDelegate newProc);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern IntPtr SetWindowLongPtr32_Delegate(IntPtr hWnd, GWL_Flags nIndex, WndProcDelegate newProc);


    // Z-order constants
    static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
    static readonly IntPtr HWND_TOP = new IntPtr(0);
    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

    const uint SWP_NOMOVE = 0x0002;
    const uint SWP_NOSIZE = 0x0001;
    const uint SWP_NOACTIVATE = 0x0010;
    const uint SWP_FRAMECHANGED = 0x0020;

    const uint GW_HWNDFIRST = 0; // Topmost window in z-order
    const uint GW_HWNDLAST = 1; // Window above us in z-order
    const uint GW_HWNDNEXT = 2; // Window above us in z-order
    const uint GW_HWNDPREV = 3; // Window above us in z-order
    const uint GW_OWNER = 4; // Window above us in z-order

    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int x;
        public int y;

        public POINT(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", EntryPoint = "BringWindowToTop", SetLastError = true)]
    static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow", SetLastError = true)]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow", SetLastError = true)]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetTopWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);
}