

using System;
using System.Runtime.InteropServices;
using System.Text;
using DrawBehindDesktopIcons;

/*
Author: Mar Zandvliet
With thanks to: DrawBehindDesktopIcons, Claude

Todo: merge in the stuff from DrawBehindDesktopIcons, leaving a thank you
*/

namespace Frantic.Windows
{
    public enum GWL_Flags : int
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

    public enum LayeredWindowAttr : uint
    {
        LWA_ALPHA = 0x00000002,     //Use bAlpha to determine the opacity of the layered window.
        LWA_COLORKEY = 0x00000001   //Use crKey as the transparency color.
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct COLORREF
    {
        public byte R;
        public byte G;
        public byte B;

        public COLORREF(byte red, byte green, byte blue)
        {
            R = red; G = green; B = blue;
        }
    }

    public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public static class WinApi
    {
        public const int HTCLIENT = 1;
        public const int HTTRANSPARENT = -1;

        public const uint WM_NCHITTEST = 0x0084;
        public const uint WM_ACTIVATE = 0x0006;
        public const uint WM_MOUSEACTIVATE = 0x0021;
        public const uint WM_ACTIVATEAPP = 0x001C;
        public const uint WM_WINDOWPOSCHANGED = 0x0047;

        public const int MA_NOACTIVATE = 3;
        public const int MA_ACTIVATE = 1;

        // Z-order constants
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        public static readonly IntPtr HWND_TOP = new IntPtr(0);
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_FRAMECHANGED = 0x0020;
        public const uint SWP_SHOWWINDOW = 0x0040;

        public const uint GW_HWNDFIRST = 0; // Topmost window in z-order
        public const uint GW_HWNDLAST = 1; // Window above us in z-order
        public const uint GW_HWNDNEXT = 2; // Window above us in z-order
        public const uint GW_HWNDPREV = 3; // Window above us in z-order
        public const uint GW_OWNER = 4; // Window above us in z-order

        public const uint LVM_GETITEMCOUNT = 0x1004;
        public const uint LVM_GETITEMPOSITION = 0x1010;
        public const uint LVM_GETITEMTEXT = 0x1000 + 45;
        // GETITEM

        [DllImport("kernel32.dll")]
        public static extern uint GetLastError();

        [DllImport("kernel32.dll")]
        public static extern void SetLastError(uint error);

        [DllImport("user32.dll")]
        public static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        internal static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, GWL_Flags nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        internal static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, GWL_Flags nIndex);

        /*
        Account for 32-bit and 64-platform compilation differences
        */
        public static IntPtr GetWindowLongPtr(IntPtr hWnd, GWL_Flags nIndex)
        {
            if (IntPtr.Size == 8)
                return GetWindowLongPtr64(hWnd, nIndex);
            else
                return GetWindowLongPtr32(hWnd, nIndex);
        }

        // Use the correct function for the architecture
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SetWindowLongPtr")]
        internal static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, GWL_Flags nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SetWindowLong")]
        internal static extern IntPtr SetWindowLongPtr32(IntPtr hWnd, GWL_Flags nIndex, IntPtr dwNewLong);

        /*
        Account for 32-bit and 64-platform compilation differences
        */
        public static IntPtr SetWindowLongPtr(IntPtr hWnd, GWL_Flags nIndex, IntPtr dwNewLong)
        {
            if (IntPtr.Size == 8)
                return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
            else
                return SetWindowLongPtr32(hWnd, nIndex, dwNewLong);
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        public static extern IntPtr SetWindowLongPtr64_Delegate(IntPtr hWnd, GWL_Flags nIndex, WndProcDelegate newProc);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        public static extern IntPtr SetWindowLongPtr32_Delegate(IntPtr hWnd, GWL_Flags nIndex, WndProcDelegate newProc);

        [DllImport("dwmapi.dll")]
        public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", EntryPoint = "BringWindowToTop", SetLastError = true)]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "GetForegroundWindow", SetLastError = true)]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", EntryPoint = "SetForegroundWindow", SetLastError = true)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr GetTopWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(Point point);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindowThreadProcessId(IntPtr hWnd, out IntPtr processId);

        [DllImport("user32.dll")]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, ref Point lParam);



        [DllImport("user32.dll")]
        public static extern uint GetDpiForSystem();

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("SHCore.dll")]
        public static extern int SetProcessDpiAwareness(int awareness);



        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);
        
        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll")]
        public static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll")]
        public static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

        [DllImport("kernel32.dll")]
        public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out uint lpNumberOfBytesWritten);

        [DllImport("kernel32.dll")]
        public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out uint lpNumberOfBytesRead);

        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(IntPtr hWnd, ref Point point);

        [DllImport("user32.dll")]
        public static extern bool ScreenToClient(IntPtr hWnd, ref Point lpPoint);
    }
}
