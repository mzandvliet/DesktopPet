using UnityEngine;
using DrawBehindDesktopIcons;
using System;
using System.Text;
using System.Threading;
using UnityEngine.InputSystem;
using Unity.Mathematics;
using System.Runtime.InteropServices;
using System.Collections;

/*
Todo: tray icon / menu

Issues:

Window gains focus, comes to top
doesn't allow clickthrough anymore

*/

public class DesktopHook : MonoBehaviour
{
    [SerializeField] private Character _character;
    [SerializeField] private Material _material;

    [SerializeField] private LayerMask _interactableLayers = -1;
    [SerializeField] private float _maxRaycastDistance = 100f;

    private Camera _camera;
    private static StringBuilder _text;
    private Vector2 _mouseClickPos;
    private float _escapeTimer;

    private static WndProcDelegate _newWndProcDelegate;
    private static IntPtr _oldWndProc;
    private static DesktopHook _instance;

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

        Application.targetFrameRate = 60;
        Application.runInBackground = true;

        _camera = gameObject.GetComponent<Camera>();

        // Transparent background
        _camera.backgroundColor = new Color(1f, 0f, 1f, 1f); // Magenta
        _camera.clearFlags = CameraClearFlags.SolidColor;

        _text = new StringBuilder(4096);
    }

    private IEnumerator Start()
    {
        Screen.fullScreenMode = FullScreenMode.Windowed;
        Screen.SetResolution(3440, 1440, false);

        yield return new WaitForSeconds(0.5f);

        if (TryHook())
        {
            Debug.Log("Succesfully hooked into desktop background!");
            InstallWindowProc();
        }
        else
        {
            Debug.Log("Error: Failed to hook into desktop background...");
        }
    }

    private void OnDestroy()
    {
        UninstallWindowProc();
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
        mousePos.y = Screen.height - mousePos.y;
        var mousePosWorld = _camera.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, +8f));
        _character.LookAt(mousePosWorld);

        if (SystemInput.GetKeyDown(KeyCode.Space))
        {
            _character.Jump();
        }

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
        
        var guiRect = new Rect(Screen.width * 0.5f - guiSize.x * 0.5f, 0, guiSize.x, guiSize.y);
        GUILayout.BeginArea(guiRect);
        GUILayout.BeginVertical(GUI.skin.box);
        {
            GUILayout.Label("Desktop Pet");
            GUILayout.Label("Hold ESCAPE for 1 second to quit");
            GUILayout.Label("");
            GUILayout.Label($"Last Click Pos: {_mouseClickPos}");
            GUILayout.Label($"Last Tested Pos: {_lastTestedMousePos}");
            GUILayout.Label($"Last Hit Result: {_lastHitResult}");
        }
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    private void InstallWindowProc()
    {
        IntPtr hwnd = GetActiveWindow();
        if (hwnd == IntPtr.Zero)
        {
            Debug.LogError("Failed to get window handle");
            return;
        }

        // Create delegate and keep it alive
        _newWndProcDelegate = new WndProcDelegate(WndProc);

        // Install based on architecture
        if (IntPtr.Size == 8)
            _oldWndProc = SetWindowLongPtr64_Delegate(hwnd, GWL_Flags.GWL_WNDPROC, _newWndProcDelegate);
        else
            _oldWndProc = SetWindowLongPtr32_Delegate(hwnd, GWL_Flags.GWL_WNDPROC, _newWndProcDelegate);

        if (_oldWndProc == IntPtr.Zero)
        {
            Debug.LogError("Failed to install window procedure");
        }
        else
        {
            Debug.Log("SelectiveClickThrough: Window procedure installed successfully");
        }
    }

    private void UninstallWindowProc()
    {
        if (_oldWndProc != IntPtr.Zero)
        {
            IntPtr hwnd = GetActiveWindow();
            if (hwnd != IntPtr.Zero)
            {
                // Restore original window proc if we have one
                if (IntPtr.Size == 8)
                    SetWindowLongPtr64(hwnd, GWL_Flags.GWL_WNDPROC, _oldWndProc);
                else
                    SetWindowLongPtr32(hwnd, GWL_Flags.GWL_WNDPROC, _oldWndProc);

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
        if (msg == WM_MOUSEACTIVATE)
        {
            // Can add selective focus acceptance throug MA_ACTIVATE
            // For now: never gain focus
            return new IntPtr(MA_NOACTIVATE);
        }

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

    private bool TryHook()
    {
        if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor)
        {
            Debug.LogError($"Platform not supported: {Application.platform}");
            return false;
        }

        IntPtr hwnd = GetActiveWindow();

        // Make it a popup window
        SetWindowLongPtr(hwnd, GWL_Flags.GWL_STYLE, new IntPtr(WS_POPUP | WS_VISIBLE));

        // Make it click-through if desired
        IntPtr exStyle = GetWindowLongPtr(hwnd, GWL_Flags.GWL_EXSTYLE);
        SetWindowLongPtr(hwnd, GWL_Flags.GWL_EXSTYLE, new IntPtr(exStyle.ToInt64() | WS_EX_LAYERED)); //  | WS_EX_TRANSPARENT

        // Enable DWM transparency (this is what gets the transparency/chromakey to work)
        MARGINS margins = new MARGINS { cxLeftWidth = -1 };
        int dwmResult = DwmExtendFrameIntoClientArea(hwnd, ref margins);
        Debug.Log($"DWM result: 0x{dwmResult:X} (0 = S_OK)");
  
        // Set Unity camera to transparent
        _camera.backgroundColor = new Color(0, 0, 0, 0);
        _camera.clearFlags = CameraClearFlags.SolidColor;

        /* 
        Important
        URP renderer needs to be configured to render to a buffer with transparency information in there!
        */

        SetWindowZOrder(ZWindowOrder.Bottom);

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
        IntPtr hwnd = GetActiveWindow();

        switch (order)
        {
            case ZWindowOrder.Bottom:
                // Send to bottom (behind all windows)
                SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                break;
            case ZWindowOrder.Front:
                // Bring to front (but not always-on-top)
                // Todo: doesn't work
                // SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                break;
            case ZWindowOrder.Top:
                // Always on top
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                break;
        }
    }

    // Win32 Imports
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    const uint WS_POPUP = 0x80000000;
    const uint WS_VISIBLE = 0x10000000;
    const uint WS_EX_LAYERED = 0x00080000;
    const uint WS_EX_TRANSPARENT = 0x00000020;

    const uint WM_NCHITTEST = 0x0084;
    const int HTCLIENT = 1;
    const int HTTRANSPARENT = -1;

    private const uint WM_MOUSEACTIVATE = 0x0021;
    private const int MA_NOACTIVATE = 3;
    private const int MA_ACTIVATE = 1;

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

    // Use the correct function for the architecture
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SetWindowLongPtr")]
    static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, GWL_Flags nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SetWindowLong")]
    static extern IntPtr SetWindowLongPtr32(IntPtr hWnd, GWL_Flags nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr GetWindowLongPtr(IntPtr hWnd, GWL_Flags nIndex);

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
    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

    const uint SWP_NOMOVE = 0x0002;
    const uint SWP_NOSIZE = 0x0001;
    const uint SWP_NOACTIVATE = 0x0010;

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

}