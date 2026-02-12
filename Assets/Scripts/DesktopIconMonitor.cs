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

[StructLayout(LayoutKind.Sequential)]
public struct LVHITTESTINFO
{
    public Point pt;
    public uint flags;
    public int iItem;
    public int iSubItem;
    public int iGroup;
}

public class DesktopIconMonitor : IDisposable
{
    private List<Point> _icons;

    private IntPtr _listViewHwnd;
    IntPtr _explorerProcess;
    IntPtr _remoteHitBuffer;

    public List<Point> Icons
    {
        get => _icons;
    }

    public DesktopIconMonitor()
    {
        _icons = new List<Point>();
    }

    ~DesktopIconMonitor()
    {
        Dispose();
    }

    public bool Initialize()
    {
        _listViewHwnd = GetDesktopListView();

        IntPtr processId;
        WinApi.GetWindowThreadProcessId(_listViewHwnd, out processId);

        _explorerProcess = WinApi.OpenProcess(PROCESS_ALL_ACCESS, false, (uint)processId);
        if (_explorerProcess == IntPtr.Zero)
        {
            Debug.LogError("DesktopWindowTracker: Failed to get open process for desktop listview");
            return false;
        }

        int bufferSize = Marshal.SizeOf<LVHITTESTINFO>();
        _remoteHitBuffer = WinApi.VirtualAllocEx(_explorerProcess, IntPtr.Zero, (uint)bufferSize,
            MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);

        if (_remoteHitBuffer == IntPtr.Zero)
        {
            Debug.LogError("DesktopWindowTracker: Failed to allocate memory in explorer process");
            return false;
        }

        return true;
    }

    public void Dispose()
    {
        if (_remoteHitBuffer != IntPtr.Zero)
        {
            WinApi.VirtualFreeEx(_explorerProcess, _remoteHitBuffer, 0, MEM_RELEASE);
            _remoteHitBuffer = IntPtr.Zero;
            Debug.Log("Remote buffer freed");
        }

        if (_explorerProcess != IntPtr.Zero)
        {
            WinApi.CloseHandle(_explorerProcess);
            _explorerProcess = IntPtr.Zero;
            Debug.Log("Explorer process handle closed");
        }
    }

    public void Update() {
        GetDesktopIconPositions(_icons);
    }

    public static IntPtr GetDesktopListView()
    {
        IntPtr listViewHwnd = IntPtr.Zero;

        Win32.EnumWindows(new Win32.EnumWindowsProc((topHandle, topParamHandle) =>
        {
            IntPtr shellView = Win32.FindWindowEx(topHandle, IntPtr.Zero, "SHELLDLL_DefView", IntPtr.Zero);

            if (shellView != IntPtr.Zero)
            {
                // The ListView is a CHILD of SHELLDLL_DefView, not a sibling
                listViewHwnd = WinApi.FindWindowEx(shellView, IntPtr.Zero, "SysListView32", "FolderView");

                if (listViewHwnd != IntPtr.Zero)
                {
                    // Debug.Log($"Found desktop ListView: {listView}");
                    return false; // Stop enumeration
                }
            }

            return true; // Continue enumeration
        }), IntPtr.Zero);

        return listViewHwnd;
    }

    const uint PROCESS_ALL_ACCESS = 0x001F0FFF;
    const uint MEM_COMMIT = 0x1000;
    const uint MEM_RESERVE = 0x2000;
    const uint MEM_RELEASE = 0x8000;
    const uint PAGE_READWRITE = 0x04;
    const uint LVM_HITTEST = 0x1012;

    private bool GetDesktopIconPositions(List<Point> positions)
    {
        positions.Clear();

        try
        {
            // Get icon count
            int count = (int)WinApi.SendMessage(_listViewHwnd, WinApi.LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);

            // Allocate memory in target process

            IntPtr remoteBuffer = WinApi.VirtualAllocEx(_explorerProcess, IntPtr.Zero, (uint)Marshal.SizeOf(typeof(Point)),
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
                WinApi.SendMessage(_listViewHwnd, WinApi.LVM_GETITEMPOSITION, (IntPtr)i, remoteBuffer);

                // Read back the result
                byte[] buffer = new byte[Marshal.SizeOf(typeof(Point))]; // todo: cache this memory
                uint bytesRead;
                WinApi.ReadProcessMemory(_explorerProcess, remoteBuffer, buffer, (uint)buffer.Length, out bytesRead);

                // Convert to POINT
                GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                Point clientPos = (Point)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(Point));
                handle.Free();

                // text.AppendLine($"{i}: {pos}");

                Point screenPos = clientPos;
                WinApi.ClientToScreen(_listViewHwnd, ref screenPos);

                if (i == 0)
                {
                    Debug.Log($"Icon: clientPos {clientPos} -> screenpos {screenPos}");
                }

                positions.Add(screenPos);
            }

            // Debug.Log(text.ToString());

            // Free remote memory
            WinApi.VirtualFreeEx(_explorerProcess, remoteBuffer, 0, MEM_RELEASE);
        }
        catch (Exception e)
        {
            Debug.LogError($"Exception while trying to get desktop icons: {e.Message}");
            return false;
        }

        return true;
    }

    public int HitTest(int screenX, int screenY)
    {
        if (_listViewHwnd == IntPtr.Zero || _explorerProcess == IntPtr.Zero || _remoteHitBuffer == IntPtr.Zero)
        {
            Debug.LogError("Failed to allocate remote buffer");
            return -1;
        }

        // Convert screen coords to ListView client coords
        Point testPoint = new Point { x = screenX, y = screenY };
        WinApi.ScreenToClient(_listViewHwnd, ref testPoint);

        // Prepare hit test structure
        LVHITTESTINFO hitTest = new LVHITTESTINFO
        {
            pt = testPoint,
            flags = 0,
            iItem = -1,
            iSubItem = 0,
            iGroup = 0
        };

        // Marshal to bytes
        byte[] buffer = new byte[Marshal.SizeOf<LVHITTESTINFO>()];
        IntPtr ptr = Marshal.AllocHGlobal(buffer.Length);
        try
        {
            Marshal.StructureToPtr(hitTest, ptr, false);
            Marshal.Copy(ptr, buffer, 0, buffer.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        // Write to remote process
        uint bytesWritten;
        if (!WinApi.WriteProcessMemory(_explorerProcess, _remoteHitBuffer, buffer, (uint)buffer.Length, out bytesWritten))
        {
            Debug.LogWarning("Failed to write hit test data");
            return -1;
        }

        // Perform hit test
        WinApi.SendMessage(_listViewHwnd, LVM_HITTEST, IntPtr.Zero, _remoteHitBuffer);

        // Read result
        byte[] resultBuffer = new byte[buffer.Length];
        uint bytesRead;
        if (!WinApi.ReadProcessMemory(_explorerProcess, _remoteHitBuffer, resultBuffer, (uint)resultBuffer.Length, out bytesRead))
        {
            Debug.LogWarning("Failed to read hit test result");
            return -1;
        }

        // Unmarshal result
        IntPtr resultPtr = Marshal.AllocHGlobal(resultBuffer.Length);
        try
        {
            Marshal.Copy(resultBuffer, 0, resultPtr, resultBuffer.Length);
            LVHITTESTINFO result = Marshal.PtrToStructure<LVHITTESTINFO>(resultPtr);
            return result.iItem;
        }
        finally
        {
            Marshal.FreeHGlobal(resultPtr);
        }
    }
}