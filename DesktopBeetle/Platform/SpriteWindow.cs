#nullable enable
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace BeetleDesktop.Native;

/// <summary>
/// A small, independently positioned desktop sprite. WPF owns per-pixel alpha;
/// no fullscreen overlay, color key, desktop parenting or system settings are used.
/// Configure Width, Height, Left, Top and Content, then call Show().
/// </summary>
public class SpriteWindow : Window
{
    private readonly HwndSourceHook messageHook;
    private HwndSource? source;
    private nint handle;

    public SpriteWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Manual;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.None);
        // HwndSource holds hooks weakly. Keep the delegate alive with this window.
        messageHook = WindowMessage;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        handle = new WindowInteropHelper(this).Handle;
        source = HwndSource.FromHwnd(handle)
            ?? throw new InvalidOperationException("The sprite window has no WPF HWND source.");
        source.AddHook(messageHook);
        if (source.CompositionTarget is not null)
            source.CompositionTarget.BackgroundColor = Colors.Transparent;

        int previous = NativeMethods.GetWindowLong(handle, NativeMethods.ExtendedStyle);
        // Leave WPF's WS_EX_LAYERED alone: WPF supplies the complete alpha bitmap.
        // WS_EX_TRANSPARENT would disable mouse input on the beetle as well.
        int styles = (previous & ~(NativeMethods.AppWindow | NativeMethods.Transparent))
            | NativeMethods.NoActivate | NativeMethods.ToolWindow;
        if (!NativeMethods.ChangeExtendedStyle(handle, styles))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not configure the desktop sprite window.");
        if (!NativeMethods.SetWindowPos(handle, NativeMethods.Topmost, 0, 0, 0, 0,
                NativeMethods.NoMove | NativeMethods.NoSize | NativeMethods.NoActivatePosition
                | NativeMethods.NoOwnerZOrder | NativeMethods.FrameChanged))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not position the desktop sprite without activation.");
    }

    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == NativeMethods.MouseActivate)
        {
            // MA_NOACTIVATE preserves both the existing foreground app and the
            // mouse message. MA_NOACTIVATEANDEAT would discard the first click.
            handled = true;
            return NativeMethods.MouseNoActivate;
        }
        return nint.Zero;
    }

    /// <summary>Move both native coordinates together without a separate visual offset.</summary>
    public void MoveOnDesktop(Point topLeft)
    {
        VerifyAccess();
        if (handle == nint.Zero || source?.CompositionTarget is null)
        {
            Left = topLeft.X; Top = topLeft.Y;
            return;
        }
        Point pixels = source.CompositionTarget.TransformToDevice.Transform(topLeft);
        int x = checked((int)Math.Round(pixels.X));
        int y = checked((int)Math.Round(pixels.Y));
        if (NativeMethods.GetWindowRect(handle, out NativeMethods.Rect current)
            && current.Left == x && current.Top == y) return;
        // Setting Left and Top independently performs two native moves. Submit
        // one XY move, and let WPF observe WM_WINDOWPOSCHANGED to track its bounds.
        if (!NativeMethods.SetWindowPos(handle, nint.Zero, x, y, 0, 0,
                NativeMethods.NoSize | NativeMethods.NoZOrder | NativeMethods.NoActivatePosition
                | NativeMethods.NoOwnerZOrder))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not move the desktop sprite.");
    }

    protected override void OnClosed(EventArgs e)
    {
        if (source is { IsDisposed: false }) source.RemoveHook(messageHook);
        source = null;
        handle = nint.Zero;
        base.OnClosed(e);
    }

    /// <summary>
    /// Checks the native/WPF configuration needed for per-pixel transparency.
    /// Returns false before Show; this is not a compositor screenshot assertion.
    /// </summary>
    public bool IsActuallyTransparent
    {
        get
        {
            VerifyAccess();
            return handle != nint.Zero && NativeMethods.IsWindow(handle)
                && AllowsTransparency && WindowStyle == WindowStyle.None
                && IsZeroAlpha(Background)
                && source is { IsDisposed: false, UsesPerPixelOpacity: true }
                && source.CompositionTarget is { BackgroundColor.A: 0 }
                && (NativeMethods.GetWindowLong(handle, NativeMethods.ExtendedStyle) & NativeMethods.Layered) != 0;
        }
    }

    /// <summary>
    /// Returns whether Windows currently targets this sprite at a window-local DIP
    /// point. Call after the window has rendered. Zero-alpha layered pixels should
    /// return false; an overlapping topmost window can also make a point return false.
    /// This only compares the returned HWND with our own; it does not inspect or
    /// expose the underlying window, move the pointer, or synthesize any input.
    /// </summary>
    public bool IsNativeHitAt(Point localDip)
    {
        VerifyAccess();
        if (handle == nint.Zero || !IsVisible || !NativeMethods.IsWindow(handle)
            || !double.IsFinite(localDip.X) || !double.IsFinite(localDip.Y)
            || localDip.X < 0 || localDip.Y < 0
            || localDip.X >= ActualWidth || localDip.Y >= ActualHeight)
            return false;

        Point screen = PointToScreen(localDip);
        var pixel = new NativeMethods.Point
        {
            X = checked((int)Math.Floor(screen.X)),
            Y = checked((int)Math.Floor(screen.Y))
        };
        return NativeMethods.WindowFromPoint(pixel) == handle;
    }

    public string Diagnostics
    {
        get
        {
            VerifyAccess();
            var report = new StringBuilder();
            report.AppendLine("Surface: independent WPF sprite window");
            report.AppendLine("Transparency configuration valid: " + IsActuallyTransparent);
            report.AppendLine("AllowsTransparency: " + AllowsTransparency);
            report.AppendLine("WindowStyle: " + WindowStyle);
            report.AppendLine("ShowActivated: " + ShowActivated);
            report.AppendLine("ShowInTaskbar: " + ShowInTaskbar);
            report.AppendLine("Topmost: " + Topmost);
            report.AppendLine("WPF bounds (DIP): " + new Rect(Left, Top, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight)));
            report.AppendLine("Primary work area (DIP): " + DesktopCoordinates.WorkArea);
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            report.AppendLine("DPI scale: " + dpi.DpiScaleX + ", " + dpi.DpiScaleY);
            report.AppendLine("Per-pixel alpha: " + (source is { IsDisposed: false, UsesPerPixelOpacity: true }));
            report.AppendLine("Color key: unused; WPF owns the per-pixel alpha surface");
            if (handle == nint.Zero || !NativeMethods.IsWindow(handle))
            {
                report.AppendLine("Native HWND: not created or already closed");
                return report.ToString();
            }

            int styles = NativeMethods.GetWindowLong(handle, NativeMethods.ExtendedStyle);
            report.AppendLine("Native HWND: 0x" + handle.ToInt64().ToString("X"));
            report.AppendLine("Native extended style: 0x" + styles.ToString("X8"));
            report.AppendLine("WS_EX_LAYERED: " + ((styles & NativeMethods.Layered) != 0));
            report.AppendLine("WS_EX_NOACTIVATE: " + ((styles & NativeMethods.NoActivate) != 0));
            report.AppendLine("WS_EX_TOOLWINDOW: " + ((styles & NativeMethods.ToolWindow) != 0));
            report.AppendLine("WS_EX_TRANSPARENT: " + ((styles & NativeMethods.Transparent) != 0));
            report.AppendLine("Owns foreground focus: " + (NativeMethods.GetForegroundWindow() == handle));
            if (NativeMethods.GetWindowRect(handle, out NativeMethods.Rect bounds))
                report.AppendLine("Native bounds (px): " + bounds);
            if (NativeMethods.GetClientRect(handle, out NativeMethods.Rect client))
                report.AppendLine("Native client (px): " + client);
            if (NativeMethods.GetLayeredWindowAttributes(handle, out uint key, out byte alpha, out uint flags))
                report.AppendLine("Layered attribute query: key=0x" + key.ToString("X6") + " alpha=" + alpha + " flags=0x" + flags.ToString("X"));
            else
                report.AppendLine("Layered attribute query: unavailable, expected for WPF UpdateLayeredWindow per-pixel alpha");
            return report.ToString();
        }
    }

    private static bool IsZeroAlpha(Brush? brush) => brush is null || brush.Opacity == 0
        || brush is SolidColorBrush solid && solid.Color.A == 0;
}
