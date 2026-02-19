using UnityEngine;
using System.Collections.Generic;
using System;
using DrawBehindDesktopIcons;
using Frantic.Windows;
using System.Runtime.InteropServices;


/*
Todo:

- Make this robust against Explore.exe crashes and reboots
    - Add defense checks everywhere

on forcing explorer.exe to restart:

- This script: Failed to allocate remote buffer on repeat
- DesktopWindowTracker: Failed to allocate remote memory

The app disappears from view, but is still running, filling the log

---

- make screen / dpi scaling robust across systems (Win10 is different, for example?)
- get rect and title for each icon
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
    IntPtr _remotePointBuffer;
    IntPtr _remoteHitBuffer;

    byte[] _localPointBuffer;
    byte[] _localHitBuffer;

    public List<Point> Icons
    {
        get => _icons;
    }

    public DesktopIconMonitor()
    {
        _icons = new List<Point>();
        _localPointBuffer = new byte[Marshal.SizeOf(typeof(Point))];
        _localHitBuffer = new byte[Marshal.SizeOf<LVHITTESTINFO>()];
    }

    ~DesktopIconMonitor()
    {
        Dispose();
    }

    public bool Initialize()
    {
        Dispose();

        _listViewHwnd = GetDesktopListView();

        IntPtr processId;
        WinApi.GetWindowThreadProcessId(_listViewHwnd, out processId);

        _explorerProcess = WinApi.OpenProcess(PROCESS_ALL_ACCESS, false, (uint)processId);
        if (_explorerProcess == IntPtr.Zero)
        {
            Debug.LogError("DesktopWindowTracker: Failed to get open process for desktop listview");
            return false;
        }

        _remotePointBuffer = WinApi.VirtualAllocEx(_explorerProcess, IntPtr.Zero, (uint)Marshal.SizeOf(typeof(Point)),
                MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);

        if (_remotePointBuffer == IntPtr.Zero)
        {
            Debug.LogError("DesktopWindowTracker: Failed to allocate point memory in explorer process");
            return false;
        }

        int bufferSize = Marshal.SizeOf<LVHITTESTINFO>();
        _remoteHitBuffer = WinApi.VirtualAllocEx(_explorerProcess, IntPtr.Zero, (uint)bufferSize,
            MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);

        if (_remoteHitBuffer == IntPtr.Zero)
        {
            Debug.LogError("DesktopWindowTracker: Failed to allocate hittest memory in explorer process");
            return false;
        }

        return true;
    }

    public void Dispose()
    {
        try
        {
            if (_remotePointBuffer != IntPtr.Zero)
            {
                WinApi.VirtualFreeEx(_explorerProcess, _remotePointBuffer, 0, MEM_RELEASE);
                _remotePointBuffer = IntPtr.Zero;
            }
        }
        catch (Exception e)
        {
            Debug.Log($"DesktopIconMonitor: cleanup exception caught: {e.Message}");
        }

        try
        {
            if (_remoteHitBuffer != IntPtr.Zero)
            {
                WinApi.VirtualFreeEx(_explorerProcess, _remoteHitBuffer, 0, MEM_RELEASE);
                _remoteHitBuffer = IntPtr.Zero;
                Debug.Log("Remote buffer freed");
            }
        }
        catch (Exception e)
        {
            Debug.Log($"DesktopIconMonitor: cleanup exception caught: {e.Message}");
        }

        try
        {
            if (_explorerProcess != IntPtr.Zero)
            {
                WinApi.CloseHandle(_explorerProcess);
                _explorerProcess = IntPtr.Zero;
                Debug.Log("Explorer process handle closed");
            }
        }
        catch(Exception e)
        {
            Debug.Log($"DesktopIconMonitor: cleanup exception caught: {e.Message}");
        }

        Debug.Log("DesktopIconMonitor: everything cleaned up");
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

            if (_remotePointBuffer == IntPtr.Zero)
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
                WinApi.SendMessage(_listViewHwnd, WinApi.LVM_GETITEMPOSITION, (IntPtr)i, _remotePointBuffer);

                // Read back the result
                uint bytesRead;
                if (!WinApi.ReadProcessMemory(_explorerProcess, _remotePointBuffer, _localPointBuffer, (uint)_localPointBuffer.Length, out bytesRead))
                {
                    int error = Marshal.GetLastWin32Error();
                    Debug.LogWarning($"ReadProcessMemory failed: {error}");

                    if (error == 5) // ACCESS_DENIED
                    {
                        // Explorer restarted or process died - reinitialize
                        Initialize();
                    }
                    return false; // Don't crash, just fail gracefully
                }

                // Convert to Point
                GCHandle handle = GCHandle.Alloc(_localPointBuffer, GCHandleType.Pinned);
                Point clientPos = (Point)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(Point));
                handle.Free();

                // text.AppendLine($"{i}: {pos}");

                // To screen coordinates
                Point screenPos = clientPos;
                WinApi.ClientToScreen(_listViewHwnd, ref screenPos);

                positions.Add(screenPos);
            }

            // Debug.Log(text.ToString());
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
        IntPtr ptr = Marshal.AllocHGlobal(_localHitBuffer.Length);
        try
        {
            Marshal.StructureToPtr(hitTest, ptr, false);
            Marshal.Copy(ptr, _localHitBuffer, 0, _localHitBuffer.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        // Write to remote process
        uint bytesWritten;
        if (!WinApi.WriteProcessMemory(_explorerProcess, _remoteHitBuffer, _localHitBuffer, (uint)_localHitBuffer.Length, out bytesWritten))
        {
            Debug.LogWarning("Failed to write hit test data");
            int error = Marshal.GetLastWin32Error();
            Debug.LogWarning($"WriteProcessMemory failed: {error}");

            if (error == 5) // ACCESS_DENIED
            {
                // Explorer restarted or process died - reinitialize
                Initialize();
            }
            return -1;// Don't crash, just fail gracefully
        }

        // Perform hit test
        WinApi.SendMessage(_listViewHwnd, LVM_HITTEST, IntPtr.Zero, _remoteHitBuffer); // perf: 1ms? wow

        // Read result
        uint bytesRead;
        if (!WinApi.ReadProcessMemory(_explorerProcess, _remoteHitBuffer, _localHitBuffer, (uint)_localHitBuffer.Length, out bytesRead))
        {
            Debug.LogWarning("Failed to read hit test result");
            int error = Marshal.GetLastWin32Error();
            Debug.LogWarning($"WriteProcessMemory failed: {error}");

            if (error == 5) // ACCESS_DENIED
            {
                // Explorer restarted or process died - reinitialize
                Initialize();
            }
            return -1;// Don't crash, just fail gracefully
        }

        // Unmarshal result
        IntPtr resultPtr = Marshal.AllocHGlobal(_localHitBuffer.Length);
        try
        {
            Marshal.Copy(_localHitBuffer, 0, resultPtr, _localHitBuffer.Length);
            LVHITTESTINFO result = Marshal.PtrToStructure<LVHITTESTINFO>(resultPtr);
            return result.iItem;
        }
        finally
        {
            Marshal.FreeHGlobal(resultPtr);
        }
    }
}