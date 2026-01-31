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
*/

public class DesktopHook : MonoBehaviour
{
    [SerializeField] private Character _character;
    [SerializeField] private Material _material;

    private Camera _camera;
    private static StringBuilder _text;
    private Vector2 _mouseClickPos;
    private float _escapeTimer;

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

        }
        else
        {
            Debug.Log("Error: Failed to hook into desktop background...");
        }
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

        if (SystemInput.GetKeyDown(KeyCode.Space))
        {
            _character.Jump();
        }

        if (SystemInput.GetKeyDown(KeyCode.Mouse0))
        {
            _mouseClickPos = SystemInput.GetCursorPosition();
        }

        var mousePos = SystemInput.GetCursorPosition();
        mousePos.y = Screen.height - mousePos.y;
        var mousePosWorld = _camera.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, +8f));
        _character.LookAt(mousePosWorld);

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

    // void OnRenderImage(RenderTexture from, RenderTexture to)
    // {
    //     Graphics.Blit(from, to, _material);
    // }

    private void OnGUI()
    {
        float2 guiSize = new float2(800, 600);
        
        var guiRect = new Rect(Screen.width * 0.5f - guiSize.x * 0.5f, 0, guiSize.x, guiSize.y);
        GUILayout.BeginArea(guiRect);
        GUILayout.BeginVertical(GUI.skin.box);
        {
            GUILayout.Label("Desktop Pet");
            GUILayout.Label("Hold ESCAPE for 1 second to quit");
            GUILayout.Label($"Last Click Pos: {_mouseClickPos}");
        }
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    static IntPtr GetUnityWindowHandle()
    {
        IntPtr returnHwnd = IntPtr.Zero;
        var threadId = Win32.GetCurrentThreadId();
        Win32.EnumThreadWindows(threadId,
            (hWnd, lParam) =>
            {
                if (returnHwnd == IntPtr.Zero) returnHwnd = hWnd;
                return true;
            }, IntPtr.Zero);
        return returnHwnd;
    }

    // 
    // Retrieves the handle of a window with the specified name.
    //
    // Parameters:
    // - windowName: The name of the window to retrieve the handle for.
    //
    // Returns:
    // - The handle of the window, or IntPtr.Zero if the window is not found.
    // 
    public static IntPtr GetWindowHandle(string windowName)
    {
        var procs = System.Diagnostics.Process.GetProcesses();
        _text.Clear();
        _text.AppendLine($"Found {procs.Length} processes:");

        IntPtr windowHandle = IntPtr.Zero;
        // Enumerate through all open windows.
        foreach (System.Diagnostics.Process process in procs)
        {
            _text.AppendFormat("- {1}, {0}\n", process.ProcessName, process.Handle);
            if (process.MainWindowTitle == windowName)
            {
                Debug.Log($"found process match: {windowName} -> {process.Handle}");
                windowHandle = process.MainWindowHandle;
                break;
            }
        }

        UnityEngine.Debug.Log(_text.ToString());

        return windowHandle;
    }

    private static IntPtr GetProgramManagerWindowHandle()
    {
        // IntPtr wHandle = GetWindowHandle("progman");
        IntPtr wHandle = Win32.FindWindow("Progman", null);
        return wHandle;
    }

    private string GetWindowText(IntPtr windowPtr)
    {
        _text.Clear();
        int returnCode = Win32.GetWindowText(windowPtr, _text, 4096);
        if (returnCode == 0)
        {
            Debug.LogError($"Failed to get window text for: {windowPtr}");
        }
        return _text.ToString();
    }

    // private bool TryHook()
    // {
    //     if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor)
    //     {
    //         Debug.LogError($"Platform not supported: {Application.platform}");
    //         return false;
    //     }

    //     IntPtr unityWHandle = GetUnityWindowHandle();
    //     if (unityWHandle == IntPtr.Zero)
    //     {
    //         Debug.LogError("Could not find Unity app window handle");
    //         return false;
    //     }

    //     Debug.Log($"unity wHandle found: {unityWHandle}.");

    //     IntPtr progmanHandle = GetProgramManagerWindowHandle();
    //     Debug.Log($"progmanHandle found: {progmanHandle}.");

    //     IntPtr result = IntPtr.Zero;

    //     // Send 0x052C to Progman. This message directs Progman to spawn a 
    //     // WorkerW behind the desktop icons. If it is already there, nothing 
    //     // happens.
    //     Debug.Log("Triggering ProgramManager WorkerW spawn...");
    //     Win32.SendMessageTimeout(progmanHandle,
    //                            0x052C,
    //                            new IntPtr(0),
    //                            IntPtr.Zero,
    //                            Win32.SendMessageTimeoutFlags.SMTO_NORMAL,
    //                            1000,
    //                            out result);

    //     Debug.Log("Attempting to find WorkerW through progman procHandle...");
    //     IntPtr workerW = Win32.FindWindowEx(progmanHandle, IntPtr.Zero, "WorkerW", IntPtr.Zero); // windowName was null in example

    //     // If that doesn't work, try searching alternative layout

    //     if (workerW == IntPtr.Zero)
    //     {
    //         Debug.Log("Alternatively, enumerate top-level windows to find SHELLDLL_DefView as child...");

    //         // Enumerate top-level windows until finding SHELLDLL_DefView as child.
    //         Win32.EnumWindows(new Win32.EnumWindowsProc((topHandle, topParamHandle) => 
    //         {
    //             IntPtr SHELLDLL_DefView = Win32.FindWindowEx(topHandle, IntPtr.Zero, "SHELLDLL_DefView", IntPtr.Zero);

    //             Debug.Log($"{topHandle}: {GetWindowText(topHandle)}");

    //             if (SHELLDLL_DefView != IntPtr.Zero)
    //             {
    //                 // If found, take next sibling as workerW
    //                 // > Gets the WorkerW Window after the current one.
    //                 workerW = Win32.FindWindowEx(IntPtr.Zero, topHandle, "WorkerW", IntPtr.Zero);
    //                 Debug.Log("Found SHELLDLL as child!");
    //             }

    //             return true; // Continue enumeration
    //         }), IntPtr.Zero);
    //     }

    //     if (workerW != IntPtr.Zero)
    //     {
    //         Debug.Log($"WorkerW found: {workerW} {GetWindowText(workerW)}.");

    //         if (!Application.isEditor)
    //         {
    //             /*
    //             Configure window size and transparancy
    //             */

    //             Vector2Int screenResolution = new Vector2Int(Screen.width, Screen.height);

    //             const uint WS_POPUP = 0x80000000;
    //             const uint WS_VISIBLE = 0x10000000;
    //             const uint WS_EX_LAYERED = 0x00080000;
    //             const uint WS_EX_TRANSPARENT = 0x00000020;
    //             // const int HWND_TOPMOST = -1;
    //             // const int WM_SYSCOMMAND = 0x112;
    //             // const int WM_MOUSE_MOVE = 0xF012;

    //             int fWidth;
    //             int fHeight;
    //             IntPtr hwnd = IntPtr.Zero;
    //             Rectangle margins;
    //             Rectangle windowRect;

    //             fWidth = screenResolution.x;
    //             fHeight = screenResolution.y;
    //             margins = new Rectangle() { Left = -1 };
    //             hwnd = GetActiveWindow();

    //             // var exStyle = (uint)GetWindowLongPtr(hwnd, GWL_Flags.GWL_EXSTYLE);
    //             IntPtr exStyleBefore = GetWindowLongPtr(hwnd, GWL_Flags.GWL_EXSTYLE);
    //             Debug.Log($"GWL_EXSTYLE: {exStyleBefore}");
    //             Debug.Log($"WS_EX_LAYERED: {((ulong)exStyleBefore & WS_EX_LAYERED) > 0}");
    //             Debug.Log($"WS_EX_TRANSPARENT: {((ulong)exStyleBefore & WS_EX_TRANSPARENT) > 0}");

    //             // exStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
    //            IntPtr exStyleAfter = new IntPtr(exStyleBefore.ToInt64() | WS_EX_LAYERED | WS_EX_TRANSPARENT);

    //             Debug.Log($"GWL_EXSTYLE override: {exStyleAfter}");
    //             Debug.Log($"WS_EX_LAYERED: {((ulong)exStyleAfter & WS_EX_LAYERED) > 0}");
    //             Debug.Log($"WS_EX_TRANSPARENT: {((ulong)exStyleAfter & WS_EX_TRANSPARENT) > 0}");

    //             // Transparent windows with click through
    //             if (SetWindowLongPtr(hwnd, GWL_Flags.GWL_STYLE, new IntPtr(WS_POPUP | WS_VISIBLE)) == IntPtr.Zero)
    //             {
    //                 Debug.LogError("Failed to set GWL_STYLE");
    //             }
    //             // SetLastError(0);
    //             if ((SetWindowLongPtr(hwnd, GWL_Flags.GWL_EXSTYLE, exStyleAfter) == IntPtr.Zero) && (exStyleBefore != IntPtr.Zero)) 
    //             // if (SetWindowLongPtr(hwnd, GWL_Flags.GWL_EXSTYLE, (UIntPtr)exStyle) == (UIntPtr)0)
    //             {
    //                 // uint error = GetLastError();
    //                 // Debug.LogError($"Failed to set GWL_EXSTYLE, error: {error}");
    //                 int error = Marshal.GetLastWin32Error();
    //                 Debug.LogError($"Failed to set GWL_EXSTYLE. Error: {error}");
    //             }

    //             IntPtr exStyle = GetWindowLongPtr(hwnd, GWL_Flags.GWL_EXSTYLE);
    //             bool hasLayered = (exStyle.ToInt64() & WS_EX_LAYERED) != 0;
    //             bool hasTransparent = (exStyle.ToInt64() & WS_EX_TRANSPARENT) != 0;
    //             Debug.Log($"WS_EX_LAYERED is set: {hasLayered}");
    //             Debug.Log($"WS_EX_TRANSPARENT is set: {hasTransparent}");

    //             if (!SetLayeredWindowAttributes(hwnd, new COLORREF(255,0,255), 0, LayeredWindowAttr.LWA_COLORKEY))
    //             {
    //                 Debug.LogError("Failed to set SetLayeredWindowAttributes");
    //             }

    //             // DwmExtendFrameIntoClientArea(hwnd, ref margins);

    //             Debug.Log($"Setting application window as desktop background child");
    //             Win32.SetParent(unityWHandle, workerW);
    //         } else
    //         {
    //             Debug.Log("Running in editor, will not hook to desktop...");
    //         }

    //         return true;
    //     } else
    //     {
    //         Debug.LogError("Failed to find window handle");
    //         return false;
    //     }
    // }

    private bool TryHook()
    {
        if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor)
        {
            Debug.LogError($"Platform not supported: {Application.platform}");
            return false;
        }

        IntPtr unityWHandle = GetUnityWindowHandle();
        if (unityWHandle == IntPtr.Zero)
        {
            Debug.LogError("Could not find Unity app window handle");
            return false;
        }

        Debug.Log($"unity wHandle found: {unityWHandle}.");

        IntPtr progmanHandle = GetProgramManagerWindowHandle();
        Debug.Log($"progmanHandle found: {progmanHandle}.");

        IntPtr result = IntPtr.Zero;

        // Send 0x052C to Progman. This message directs Progman to spawn a 
        // WorkerW behind the desktop icons. If it is already there, nothing 
        // happens.
        Debug.Log("Triggering ProgramManager WorkerW spawn...");
        Win32.SendMessageTimeout(progmanHandle,
                               0x052C,
                               new IntPtr(0),
                               IntPtr.Zero,
                               Win32.SendMessageTimeoutFlags.SMTO_NORMAL,
                               1000,
                               out result);

        Debug.Log("Attempting to find WorkerW through progman procHandle...");
        IntPtr workerW = Win32.FindWindowEx(progmanHandle, IntPtr.Zero, "WorkerW", IntPtr.Zero); // windowName was null in example

        // If that doesn't work, try searching alternative layout

        if (workerW == IntPtr.Zero)
        {
            Debug.Log("Alternatively, enumerate top-level windows to find SHELLDLL_DefView as child...");

            // Enumerate top-level windows until finding SHELLDLL_DefView as child.
            Win32.EnumWindows(new Win32.EnumWindowsProc((topHandle, topParamHandle) =>
            {
                IntPtr SHELLDLL_DefView = Win32.FindWindowEx(topHandle, IntPtr.Zero, "SHELLDLL_DefView", IntPtr.Zero);

                Debug.Log($"{topHandle}: {GetWindowText(topHandle)}");

                if (SHELLDLL_DefView != IntPtr.Zero)
                {
                    // If found, take next sibling as workerW
                    // > Gets the WorkerW Window after the current one.
                    workerW = Win32.FindWindowEx(IntPtr.Zero, topHandle, "WorkerW", IntPtr.Zero);
                    Debug.Log("Found SHELLDLL as child!");
                }

                return true; // Continue enumeration
            }), IntPtr.Zero);
        }

        if (workerW != IntPtr.Zero)
        {
            Debug.Log($"WorkerW found: {workerW} {GetWindowText(workerW)}.");

            if (!Application.isEditor)
            {
                IntPtr hwnd = GetActiveWindow();

                const uint WS_POPUP = 0x80000000;
                const uint WS_VISIBLE = 0x10000000;
                const uint WS_EX_LAYERED = 0x00080000;
                const uint WS_EX_TRANSPARENT = 0x00000020;

                // Make it a popup window
                SetWindowLongPtr(hwnd, GWL_Flags.GWL_STYLE, new IntPtr(WS_POPUP | WS_VISIBLE));

                // Make it click-through if desired
                IntPtr exStyle = GetWindowLongPtr(hwnd, GWL_Flags.GWL_EXSTYLE);
                SetWindowLongPtr(hwnd, GWL_Flags.GWL_EXSTYLE, new IntPtr(exStyle.ToInt64() | WS_EX_TRANSPARENT));

                // Enable DWM transparency - THIS is the key
                MARGINS margins = new MARGINS { cxLeftWidth = -1 };
                int dwmResult = DwmExtendFrameIntoClientArea(hwnd, ref margins);
                Debug.Log($"DWM result: 0x{dwmResult:X} (0 = S_OK)");

                // Set Unity camera to transparent
                _camera.backgroundColor = new Color(0, 0, 0, 0);
                _camera.clearFlags = CameraClearFlags.SolidColor;

                // Position in z-order - send to bottom (behind other windows)
                SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
            else
            {
                Debug.Log("Running in editor, will not hook to desktop...");
            }

            return true;
        }
        else
        {
            Debug.LogError("Failed to find window handle");
            return false;
        }
    }

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

    // [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    // internal static extern UIntPtr GetWindowLongPtr(IntPtr hWnd, GWL_Flags nIndex);

    // [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    // static extern int SetWindowLong(IntPtr hWnd, GWL_Flags nIndex, uint dwNewLong);

    // [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    // internal static extern UIntPtr SetWindowLongPtr(IntPtr hWnd, GWL_Flags nIndex, UIntPtr dwNewLong);

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



    //     [DllImport("user32.dll", EntryPoint = "SetLayeredWindowAttributes")]
    //     static extern bool SetLayeredWindowAttributes(IntPtr hwnd, COLORREF crKey, byte bAlpha, LayeredWindowAttr dwFlags);

    //     [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
    //     static extern bool GetWindowRect(IntPtr hwnd, out Rectangle rect);

    //     [DllImport("user32.dll")]
    //     static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    //     [DllImportAttribute("user32.dll")]
    //     static extern bool ReleaseCapture();

    //     [DllImport("user32.dll", EntryPoint = "SetWindowPos")]
    //     static extern int SetWindowPos(IntPtr hwnd, int hwndInsertAfter, int x, int y, int cx, int cy, int uFlags);

    //     [DllImport("Dwmapi.dll")]
    //     static extern uint DwmExtendFrameIntoClientArea(IntPtr hWnd, ref Rectangle margins);


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

}