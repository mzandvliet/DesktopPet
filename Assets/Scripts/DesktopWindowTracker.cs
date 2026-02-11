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
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;

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
    private IntPtr _myWindow;

    [SerializeField] private float _updateInterval = 0.5f; // Update twice per second
    private float _lastUpdate;

    public List<WindowInfo> VisibleWindows
    {
        get => _visibleWindows;
    }

    private void Start()
    {
        _myWindow = GetActiveWindow();
        RefreshWindowList();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

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
        IntPtr foregroundWindow = GetForegroundWindow();

        int z = 0;

        EnumWindows((hWnd, lParam) =>
        {
            // Skip our own window
            // if (hWnd == _myWindow)
            //     return true;

            // Only visible windows
            if (!IsWindowVisible(hWnd))
                return true;

            // Skip tool windows (like tooltips)
            uint exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
            if (Mask.IsBitSet(exStyle, (uint)WindowStylesEx.WS_EX_TOOLWINDOW))
                return true;
            // if (Mask.IsBitSet(exStyle, (uint)WindowStylesEx.WS_EX_TRANSPARENT))
            //     return true;

            // Get window bounds
            if (!GetWindowRect(hWnd, out RECT rect))
                return true;

            // Skip tiny windows (likely not real windows)
            if (rect.Width < 50 || rect.Height < 50)
                return true;

            // Get title
            
            _title.Clear();
            GetWindowText(hWnd, _title, _title.Capacity);

            // Skip windows without titles (usually background processes)
            if (string.IsNullOrEmpty(_title.ToString()))
                return true;

            if (_title.ToString().Contains("Windows Input Experience"))
                return true;

            var info = new WindowInfo
            {
                Handle = hWnd,
                Rect = rect,
                Z = z++,
                Title = _title.ToString(),
                IsActive = hWnd == foregroundWindow
            };
            _visibleWindows.Add(info);

            // Todo: filter these innocent windows out
            // if (_title.ToString().Contains("Settings"))
            // {
            //     Debug.Log("Found a settings window:");
            //     Debug.Log(info);
            //     var threadId = WinApi.GetWindowThreadProcessId(hWnd, out IntPtr procId);
            //     Debug.Log($"threadID: {threadId}, processID: {procId}");

            //     if (procId != IntPtr.Zero)
            //     {
            //         var process = System.Diagnostics.Process.GetProcessById((int)procId);
            //         if (process != null)
            //         {
            //             Debug.Log($"Found process: {process.ProcessName}, {process.MachineName}");
            //         }
            //     }
            // }

            return true; // Continue enumeration
        }, IntPtr.Zero);

        // Debug.Log($"Found {_visibleWindows.Count} visible windows");
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

    public static IntPtr GetDesktopBackgroundWindowWorker()
    {
        IntPtr progmanHandle = GetProgramManagerWindowHandle();
        // Debug.Log($"progmanHandle found: {progmanHandle}.");

        IntPtr result = IntPtr.Zero;

        // Send 0x052C to Progman. This message directs Progman to spawn a 
        // WorkerW behind the desktop icons. If it is already there, nothing 
        // happens.
        // Debug.Log("Triggering ProgramManager WorkerW spawn...");
        Win32.SendMessageTimeout(progmanHandle,
                                0x052C,
                                new IntPtr(0),
                                IntPtr.Zero,
                                SendMessageTimeoutFlags.SMTO_NORMAL,
                                1000,
                                out result);

        // Debug.Log("Attempting to find WorkerW through progman procHandle...");
        IntPtr workerW = Win32.FindWindowEx(progmanHandle, IntPtr.Zero, "WorkerW", IntPtr.Zero); // windowName was null in example

        // If that doesn't work, try searching alternative layout

        // Debug.Log("Alternatively, enumerate top-level windows to find SHELLDLL_DefView as child...");

        // Enumerate top-level windows until finding SHELLDLL_DefView as child.
        Win32.EnumWindows(new Win32.EnumWindowsProc((topHandle, topParamHandle) =>
        {
            IntPtr SHELLDLL_DefView = Win32.FindWindowEx(topHandle, IntPtr.Zero, "SHELLDLL_DefView", IntPtr.Zero);

            if (SHELLDLL_DefView != IntPtr.Zero)
            {
                // If found, take next sibling as workerW
                // > Gets the WorkerW Window after the current one.
                workerW = Win32.FindWindowEx(IntPtr.Zero, topHandle, "WorkerW", IntPtr.Zero);
                return false;
            }

            return true; // Continue enumeration
        }), IntPtr.Zero);

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