using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

/*
Tracks all other visible windows in the OS, such that
our desktop creatures can be aware of them, and play
with them.
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
            if ((exStyle & WS_EX_TOOLWINDOW) != 0)
                return true;

            // Get window bounds
            if (!GetWindowRect(hWnd, out RECT rect))
                return true;

            // Skip tiny windows (likely not real windows)
            if (rect.Width < 50 || rect.Height < 50)
                return true;

            // Get title
            StringBuilder title = new StringBuilder(256);
            GetWindowText(hWnd, title, title.Capacity);

            // Skip windows without titles (usually background processes)
            if (string.IsNullOrEmpty(title.ToString()))
                return true;

            _visibleWindows.Add(new WindowInfo
            {
                Handle = hWnd,
                Rect = rect,
                Z = z++,
                Title = title.ToString(),
                IsActive = hWnd == foregroundWindow
            });

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
}