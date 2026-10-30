using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BeetleDesktop.Native;

/// <summary>Primary-monitor desktop coordinates in WPF device-independent units.</summary>
public static class DesktopCoordinates
{
    /// <summary>Excludes the Windows taskbar and desktop toolbars; reads current settings.</summary>
    public static Rect WorkArea => SystemParameters.WorkArea;
    /// <summary>The full primary display, including the taskbar region.</summary>
    public static Rect ScreenArea => new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);

    /// <summary>
    /// Reads the real cursor even when another app has keyboard focus. The window
    /// origin is converted separately so DPI scaling doesn't move the drag anchor.
    /// </summary>
    public static Point PointerPosition(SpriteWindow reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        reference.VerifyAccess();
        if (!NativeMethods.GetCursorPos(out NativeMethods.Point cursor))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read the desktop pointer.");

        var hwnd = new WindowInteropHelper(reference).Handle;
        var source = hwnd == nint.Zero ? null : HwndSource.FromHwnd(hwnd);
        var origin = new NativeMethods.Point();
        if (source is { IsDisposed: false, CompositionTarget: not null }
            && NativeMethods.ClientToScreen(hwnd, ref origin))
        {
            var local = source.CompositionTarget.TransformFromDevice.Transform(
                new Point(cursor.X - origin.X, cursor.Y - origin.Y));
            return new Point(reference.Left + local.X, reference.Top + local.Y);
        }

        // Before a HWND exists the primary monitor's physical size provides the
        // same primary-monitor DIP space as SystemParameters.WorkArea.
        int pixelWidth = NativeMethods.GetSystemMetrics(0);
        int pixelHeight = NativeMethods.GetSystemMetrics(1);
        return new Point(cursor.X * SystemParameters.PrimaryScreenWidth / Math.Max(1, pixelWidth),
            cursor.Y * SystemParameters.PrimaryScreenHeight / Math.Max(1, pixelHeight));
    }

    // Respect swapped primary/secondary mouse buttons. Never use the shared low
    // "pressed since last call" bit; WPF mouse-down events start an interaction.
    public static bool LeftHeld => Held(NativeMethods.GetSystemMetrics(23) == 0 ? 1 : 2);
    public static bool RightHeld => Held(NativeMethods.GetSystemMetrics(23) == 0 ? 2 : 1);
    private static bool Held(int key) => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;
}
