using UnityEngine;
using System.Collections.Generic;
using System;
using DrawBehindDesktopIcons;
using Frantic.Windows;
using System.Runtime.InteropServices;


/*
Todo:
get rect and title for each icon
*/

public struct IconData
{
    public string name;
    public Rect bounds;
}

public class DesktopIconMonitor
{
    private List<Point> _icons;

    public List<Point> Icons
    {
        get => _icons;
    }

    public DesktopIconMonitor()
    {
        _icons = new List<Point>();
    }

    public void Update() {
        GetDesktopIconPositions(_icons);
    }

    public static IntPtr GetDesktopListView()
    {
        IntPtr listView = IntPtr.Zero;

        Win32.EnumWindows(new Win32.EnumWindowsProc((topHandle, topParamHandle) =>
        {
            IntPtr shellView = Win32.FindWindowEx(topHandle, IntPtr.Zero, "SHELLDLL_DefView", IntPtr.Zero);

            if (shellView != IntPtr.Zero)
            {
                // The ListView is a CHILD of SHELLDLL_DefView, not a sibling
                listView = WinApi.FindWindowEx(shellView, IntPtr.Zero, "SysListView32", "FolderView");

                if (listView != IntPtr.Zero)
                {
                    // Debug.Log($"Found desktop ListView: {listView}");
                    return false; // Stop enumeration
                }
            }

            return true; // Continue enumeration
        }), IntPtr.Zero);

        return listView;
    }

    private static bool GetDesktopIconPositions(List<Point> positions)
    {
        positions.Clear();

        IntPtr listView = GetDesktopListView();

        // Get process ID of the ListView window
        IntPtr processId;
        WinApi.GetWindowThreadProcessId(listView, out processId);

        // Open the process
        const uint PROCESS_ALL_ACCESS = 0x001F0FFF;
        IntPtr hProcess = WinApi.OpenProcess(PROCESS_ALL_ACCESS, false, (uint)processId);
        if (hProcess == IntPtr.Zero)
        {
            Debug.LogError("DesktopWindowTracker: Failed to get open process for desktop listview");
            return false;
        }

        try
        {
            // Get icon count
            int count = (int)WinApi.SendMessage(listView, WinApi.LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);

            // Allocate memory in target process
            const uint MEM_COMMIT = 0x1000;
            const uint MEM_RESERVE = 0x2000;
            const uint MEM_RELEASE = 0x8000;
            const uint PAGE_READWRITE = 0x04;

            IntPtr remoteBuffer = WinApi.VirtualAllocEx(hProcess, IntPtr.Zero, (uint)Marshal.SizeOf(typeof(Point)),
                MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);

            if (remoteBuffer == IntPtr.Zero)
            {
                Debug.LogError("DesktopWindowTracker: Failed to allocate remote memory");
                return false;
            }

            // var text = new StringBuilder();
            // text.AppendLine($"Desktop Icon Positions: {count}");

            // Get each icon position
            for (int i = 0; i < count; i++)
            {
                // Send message with remote buffer address
                WinApi.SendMessage(listView, WinApi.LVM_GETITEMPOSITION, (IntPtr)i, remoteBuffer);

                // Read back the result
                byte[] buffer = new byte[Marshal.SizeOf(typeof(Point))];
                uint bytesRead;
                WinApi.ReadProcessMemory(hProcess, remoteBuffer, buffer, (uint)buffer.Length, out bytesRead);

                // Convert to POINT
                GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                Point pos = (Point)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(Point));
                handle.Free();

                // text.AppendLine($"{i}: {pos}");

                positions.Add(pos);
            }

            // Debug.Log(text.ToString());

            // Free remote memory
            WinApi.VirtualFreeEx(hProcess, remoteBuffer, 0, MEM_RELEASE);
        }
        catch (Exception e)
        {
            Debug.LogError($"Exception while trying to get desktop icons: {e.Message}");
            return false;
        }
        finally
        {
            bool success = WinApi.CloseHandle(hProcess);
            if (!success)
            {
                Debug.LogError("Failed to close process handle");
            }
        }

        return true;
    }
}