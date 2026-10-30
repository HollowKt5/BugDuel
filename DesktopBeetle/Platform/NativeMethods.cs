using System;
using System.Runtime.InteropServices;

namespace BeetleDesktop.Native;

internal static class NativeMethods
{
    internal const int ExtendedStyle = -20;
    internal const int Transparent = 0x20, ToolWindow = 0x80, AppWindow = 0x00040000;
    internal const int Layered = 0x00080000, NoActivate = 0x08000000;
    internal const int MouseActivate = 0x21;
    internal const uint NoSize = 1, NoMove = 2, NoZOrder = 4, NoActivatePosition = 0x10, FrameChanged = 0x20, NoOwnerZOrder = 0x200;
    internal static readonly nint Topmost = new(-1), MouseNoActivate = new(3);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { internal int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        internal int Left, Top, Right, Bottom;
        public override readonly string ToString() => "(" + Left + ", " + Top + ") " + (Right - Left) + "x" + (Bottom - Top);
    }

    internal static bool ChangeExtendedStyle(nint hwnd, int styles)
    {
        SetLastError(0);
        // GWL_EXSTYLE is a 32-bit LONG on both x86 and x64; no pointer truncation.
        return SetWindowLong(hwnd, ExtendedStyle, styles) != 0 || Marshal.GetLastWin32Error() == 0;
    }

    [DllImport("kernel32.dll")] private static extern void SetLastError(uint error);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] internal static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetWindowLong(nint hwnd, int index, int value);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetLayeredWindowAttributes(nint hwnd, out uint key, out byte alpha, out uint flags);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ClientToScreen(nint hwnd, ref Point point);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int index);
}
