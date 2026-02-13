using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using DrawBehindDesktopIcons;
using Frantic.Windows;
using UnityEngine;
using UnityEngine.UIElements;

/*
Tracks all other visible windows in the OS, such that
our desktop creatures can be aware of them, and play
with them.

Todo: could lower the update rate, or do it only based on windows events?
*/

public class DesktopWindowTracker : MonoBehaviour
{
    private static readonly StringBuilder _title = new StringBuilder(256);

    public class WindowInfo
    {
        public IntPtr Handle;
        public RECT Rect;
        public int Z;
        public string Title;
        public bool IsActive;

        public override string ToString()
        {
            return $"{Handle} | Z: {Z} | Active {IsActive} | {Title}";
        }
    }

    private List<WindowInfo> _visibleWindows = new List<WindowInfo>();
    private WindowInfo _ourWindowInfo;

    [SerializeField] private float _updateInterval = 0.5f; // Update twice per second
    private float _lastUpdate;

    public List<WindowInfo> VisibleWindows
    {
        get => _visibleWindows;
    }

    private void Start()
    {
        RefreshWindowList();
    }

    private void Update()
    {
        if (Time.time - _lastUpdate > _updateInterval)
        {
            RefreshWindowList();
            _lastUpdate = Time.time;
        }
    }

    private void RefreshWindowList()
    {
        _visibleWindows.Clear();

        if (DesktopHook.HWnd == IntPtr.Zero)
        {
            Debug.LogError("Cannot refresh window list, our application handle is zero");
            return;
        }

        IntPtr foregroundWindow = WinApi.GetForegroundWindow();
        int z = 0;

        void TrackWindow(IntPtr hWnd, RECT rect)
        {
            var info = new WindowInfo
            {
                Handle = hWnd,
                Rect = rect,
                Z = z++,
                Title = _title.ToString(),
                IsActive = hWnd == foregroundWindow
            };
            _visibleWindows.Add(info);
        }

        WinApi.EnumWindows((hWnd, lParam) =>
        {
            // Get window rect
            bool validRect = WinApi.GetWindowRect(hWnd, out RECT rect);

            // Get window title
            _title.Clear();
            WinApi.GetWindowText(hWnd, _title, _title.Capacity);

            // Debug.Log($"{hWnd}, self: {_hWnd}");

            // Always include our own window
            if (hWnd == DesktopHook.HWnd) {
                Debug.Log("Tracking self window");
                TrackWindow(hWnd, rect);
                return true;
            }

            // skip if no valid rect
            if (!validRect)
                return true;

            // Skip invisible windows
            if (!WinApi.IsWindowVisible(hWnd))
                return true;

            // Skip tool windows
            long exStyle = WinApi.GetWindowLongPtr(hWnd, GWL_Flags.GWL_EXSTYLE).ToInt64();
            if (Mask.IsBitSet(exStyle, (uint)WindowStylesEx.WS_EX_TOOLWINDOW))
                return true;
            // Skip transparent and layered (unless its our own window...)
            // if (Mask.IsBitSet(exStyle, (uint)WindowStylesEx.WS_EX_LAYERED) && hWnd != _myWindow)
            //     return true;
            // if (Mask.IsBitSet(exStyle, (uint)WindowStylesEx.WS_EX_TRANSPARENT) && hWnd != _myWindow)
            //     return true;

            // Skip tiny windows (likely not real windows)
            if (rect.Width < 50 || rect.Height < 50)
                return true;

            // Skip windows without titles (usually background processes)
            if (string.IsNullOrEmpty(_title.ToString()))
                return true;

            // Skip windows with specific titles
            if (_title.ToString().Contains("Windows Input Experience"))
                return true;
            if(_title.ToString().Contains("ScreenToGif"))
                return true;

            // Filter these innocent windows out
            if (_title.ToString().Contains("Settings"))
            {
                var threadId = WinApi.GetWindowThreadProcessId(hWnd, out IntPtr procId);
                // Debug.Log($"threadID: {threadId}, processID: {procId}");

                if (procId != IntPtr.Zero)
                {
                    var process = System.Diagnostics.Process.GetProcessById((int)procId);
                    if (process != null)
                    {
                        if (
                            process.ProcessName.Contains("ApplicationFrameHost") ||
                            process.ProcessName.Contains("SystemSettings"))
                        {
                            // if (!WinApi.IsWindowVisible(hWnd)) {
                                return true;
                            // }
                        }
                        // Debug.Log($"Found process: {process.ProcessName}, {process.MachineName}");
                    }
                }
            }

            // If we made it past all the filters, track this window
            TrackWindow(hWnd, rect);

            return true; // Continue enumeration
        }, IntPtr.Zero);


        /*
        our window sitting behind desktop icons is in a different Z list, so is not enumerated
        add it manually
        */
        if (DesktopHook.Instance.WindowMode == DesktopHook.AppWindowMode.BehindDesktopIcons) {
            // Get window rect
            bool validRect = WinApi.GetWindowRect(DesktopHook.HWnd, out RECT rect);
            // Get window title
            _title.Clear();
            WinApi.GetWindowText(DesktopHook.HWnd, _title, _title.Capacity);
            _ourWindowInfo = new WindowInfo
            {
                Handle = DesktopHook.HWnd,
                Rect = rect,
                Z = z++,
                Title = _title.ToString(),
                IsActive = DesktopHook.HWnd == foregroundWindow
            };
            _visibleWindows.Add(_ourWindowInfo);
        }

        // Debug.Log($"Found {_visibleWindows.Count} visible windows");
    }

    public static IntPtr GetUnityWindowHandle()
    {
        IntPtr returnHwnd = IntPtr.Zero;

        var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
        if (currentProcess != null) {
            returnHwnd = currentProcess.MainWindowHandle;
            Debug.Log($"Current process: {currentProcess.ProcessName}");
        } else
        {
            Debug.LogError("Failed to get current process, have no window handle");
        }

        // var threadId = Win32.GetCurrentThreadId();
        // /*
        // Bug: using a lambda here can mess up when compiling with IL2CPP
        // see : https://discussions.unity.com/t/how-do-you-reliably-the-hwnd-window-handle-of-the-games-own-window/699477/7
        // */
        // Win32.EnumThreadWindows(threadId,
        //     (hWnd, lParam) =>
        //     {
        //         if (returnHwnd == IntPtr.Zero) returnHwnd = hWnd;
        //         return true;
        //     }, IntPtr.Zero);

        if (returnHwnd == IntPtr.Zero)
        {
            Debug.LogError("Curernt window process handle is zero?");
        }

        return returnHwnd;
    }

    public WindowInfo GetWindowInfo(IntPtr hWnd)
    {
        for (int w = 0; w < _visibleWindows.Count; w++)
        {
            if (_visibleWindows[w].Handle == hWnd)
            {
                return _visibleWindows[w];
            }
        }

        return null;
    }

    public bool IsPointCoveredByWindow(Vector2 screenPoint, out WindowInfo coveringWindow, int maxZ)
    {
        foreach (var window in _visibleWindows)
        {
            if (window.Z >= maxZ)
            {
                coveringWindow = null;
                return false;
            }

            if (screenPoint.x >= window.Rect.Left && screenPoint.x <= window.Rect.Right &&
                screenPoint.y >= window.Rect.Top && screenPoint.y <= window.Rect.Bottom)
            {
                coveringWindow = window;
                return true;
            }
        }
        coveringWindow = null;
        return false;
    }

    // Helper: Check if a point (in screen coordinates) is covered by any window
    public bool IsPointCoveredByWindow(Vector2 screenPoint, out WindowInfo coveringWindow, IntPtr ignoreHWnd)
    {
        foreach (var window in _visibleWindows)
        {
            if (window.Handle == ignoreHWnd)
            {
                continue;
            }

            if (screenPoint.x >= window.Rect.Left && screenPoint.x <= window.Rect.Right &&
                screenPoint.y >= window.Rect.Top && screenPoint.y <= window.Rect.Bottom)
            {
                coveringWindow = window;
                return true;
            }
        }
        coveringWindow = null;
        return false;
    }

    // Helper: Get all windows overlapping a rect
    public List<WindowInfo> GetWindowsInRect(RECT rect)
    {
        List<WindowInfo> overlapping = new List<WindowInfo>();

        foreach (var window in _visibleWindows)
        {
            // Check rect intersection
            if (!(rect.Right < window.Rect.Left || rect.Left > window.Rect.Right ||
                  rect.Bottom < window.Rect.Top || rect.Top > window.Rect.Bottom))
            {
                overlapping.Add(window);
            }
        }

        return overlapping;
    }

    private static IntPtr GetProgramManagerWindowHandle()
    {
        // IntPtr wHandle = GetWindowHandle("progman");
        IntPtr wHandle = Win32.FindWindow("Progman", null);
        return wHandle;
    }

    public static bool IsAnyWindowFullscreen(out IntPtr hWnd)
    {
        /*
        Todo:
        the update loop is already gathering all of this info
        use that instead of doing it again here
        */
        IntPtr foregroundHwnd = WinApi.GetForegroundWindow();

        RECT rect;
        WinApi.GetWindowRect(foregroundHwnd, out rect);

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        bool isFullscreen =
            width >= Screen.currentResolution.width &&
            height >= Screen.currentResolution.height;

        // if (!WinApi.IsWindowVisible(foregroundHwnd))
        //     isFullscreen = false;

        // Skip tool windows
        long exStyle = WinApi.GetWindowLongPtr(foregroundHwnd, GWL_Flags.GWL_EXSTYLE).ToInt64();
        if (Mask.IsBitSet(exStyle, (uint)WindowStylesEx.WS_EX_TOOLWINDOW))
            isFullscreen = false;
        
        // Skip empty titles (mostly background processes)
        _title.Clear();
        WinApi.GetWindowText(foregroundHwnd, _title, _title.Capacity);
        if (string.IsNullOrEmpty(_title.ToString()))
            isFullscreen = false;

        if (isFullscreen)
        {
            hWnd = foregroundHwnd;
        } else
        {
            hWnd = IntPtr.Zero;
        }

        return isFullscreen;
    }

    /*
    Original code was found here:
    https://x.com/TheMirzaBeig/status/1780088441448837276

    Parenting your window to this window puts it *behind* the
    desktop icons
    */
    public static IntPtr GetDesktopBackgroundWindowWorker()
    {
        IntPtr progmanHandle = GetProgramManagerWindowHandle();
        Debug.Log($"progmanHandle found: {progmanHandle}.");

        // Send 0x052C to Progman. This message directs Progman to spawn a 
        // WorkerW behind the desktop icons. If it is already there, nothing 
        // happens.
        Debug.Log("Triggering ProgramManager WorkerW spawn...");
        Win32.SendMessageTimeout(progmanHandle,
                               0x052C,
                               IntPtr.Zero,
                               IntPtr.Zero,
                               SendMessageTimeoutFlags.SMTO_NORMAL,
                               1000,
                               out _);

        Debug.Log("Attempting to find WorkerW through progman procHandle...");
        IntPtr workerW = IntPtr.Zero;
        workerW = Win32.FindWindowEx(progmanHandle, IntPtr.Zero, "WorkerW", IntPtr.Zero);

        // If that doesn't work, try searching alternative layout

        if (workerW == IntPtr.Zero)
        {
            Debug.Log("Alternatively, enumerate top-level windows to find SHELLDLL_DefView as child...");

            WinApi.EnumWindows((hwnd, lParam) =>
            {
                IntPtr shellView = Win32.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", IntPtr.Zero);
                if (shellView != IntPtr.Zero)
                {
                    // Found the WorkerW with SHELLDLL_DefView
                    // Get its next sibling under the same parent (Progman)
                    IntPtr parent = WinApi.GetParent(hwnd);
                    workerW = Win32.FindWindowEx(parent, hwnd, "WorkerW", IntPtr.Zero);
                    return false;
                }
                return true;
            }, IntPtr.Zero);
        }

        if (workerW != IntPtr.Zero)
        {
            Debug.Log($"Found WorkerW: {workerW}");
        } else
        {
            Debug.LogError("Did not find WorkerW...");
        }

        return workerW;
    }

    public static IntPtr GetDesktopBackgroundWindow()
    {
        IntPtr progmanHandle = GetProgramManagerWindowHandle();

        IntPtr result = IntPtr.Zero;
        IntPtr desktopHwnd = IntPtr.Zero;

        // Enumerate top-level windows until finding SHELLDLL_DefView as child.
        Win32.EnumWindows(new Win32.EnumWindowsProc((topHandle, topParamHandle) =>
        {
            IntPtr SHELLDLL_DefView = Win32.FindWindowEx(topHandle, IntPtr.Zero, "SHELLDLL_DefView", IntPtr.Zero);

            if (SHELLDLL_DefView != IntPtr.Zero)
            {
                desktopHwnd = SHELLDLL_DefView;
                return false;
            }

            return true; // Continue enumeration
        }), IntPtr.Zero);

        return desktopHwnd;
    }
}