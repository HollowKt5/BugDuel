#nullable enable
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace BeetleDesktop.Native;

/// <summary>
/// Receives wheel input while a beetle is being held without activating its
/// desktop window. The caller enables this only after the long press has grabbed
/// a beetle, and disables it immediately when that interaction ends.
/// </summary>
public sealed class HeldWheelInput : IDisposable
{
    private const int LowLevelMouseHook = 14;
    private const int MouseWheel = 0x020A;
    private const int InvalidHookHandle = 1404;
    private readonly Dispatcher dispatcher;
    // Native code does not keep the managed delegate alive. Root it until unhooked.
    private readonly MouseHook callback;
    private nint hook;
    private int pendingDelta;
    private bool active;
    private bool disposed;

    public HeldWheelInput()
    {
        dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        dispatcher.VerifyAccess();
        callback = OnMouseInput;
        // Also roots this instance until disposal and covers shutdown during a drag.
        dispatcher.ShutdownStarted += OnDispatcherShutdown;
    }

    public bool IsActive => active && hook != nint.Zero;

    /// <summary>Call from the WPF UI thread with the current confirmed grab state.</summary>
    public void SetActive(bool value)
    {
        dispatcher.VerifyAccess();
        if (disposed)
        {
            if (value) throw new ObjectDisposedException(nameof(HeldWheelInput));
            return;
        }

        if (!value)
        {
            // Stop swallowing input before unregistering, even if removal fails.
            active = false;
            Interlocked.Exchange(ref pendingDelta, 0);
            Uninstall();
            return;
        }

        if (IsActive) return;
        Interlocked.Exchange(ref pendingDelta, 0);
        if (hook == nint.Zero)
        {
            // WH_MOUSE_LL executes on this UI thread; it does not inject our code
            // into another application. Its message loop already exists in WPF.
            nint module = GetModuleHandle(null);
            if (module == nint.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not find the desktop pet module.");
            hook = SetWindowsHookEx(LowLevelMouseHook, callback, module, 0);
            if (hook == nint.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not receive wheel input while holding a beetle.");
        }
        active = true;
    }

    /// <summary>Consumes raw signed wheel movement; a normal detent is 120 units.</summary>
    public int ConsumeDelta() => Interlocked.Exchange(ref pendingDelta, 0);

    private nint OnMouseInput(int code, nint message, nint data)
    {
        // Keep this callback short. Forward every event except a held beetle's
        // vertical wheel input, including the button-up that ends the grab.
        if (code >= 0 && message == MouseWheel && active && DesktopCoordinates.RightHeld)
        {
            var input = Marshal.PtrToStructure<MouseHookData>(data);
            int delta = unchecked((short)(input.MouseData >> 16));
            Interlocked.Add(ref pendingDelta, delta);
            return new nint(1);
        }
        return CallNextHookEx(hook, code, message, data);
    }

    private void Uninstall()
    {
        if (hook == nint.Zero) return;
        if (!UnhookWindowsHookEx(hook))
        {
            int error = Marshal.GetLastWin32Error();
            // Windows may already have removed a timed-out low-level hook.
            if (error != InvalidHookHandle)
                throw new Win32Exception(error, "Could not release the held-beetle wheel input hook.");
        }
        hook = nint.Zero;
        GC.KeepAlive(callback);
    }

    public void Dispose()
    {
        dispatcher.VerifyAccess();
        if (disposed) return;
        SetActive(false);
        disposed = true;
        dispatcher.ShutdownStarted -= OnDispatcherShutdown;
    }

    private void OnDispatcherShutdown(object? sender, EventArgs e) => Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookData
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public nuint ExtraInfo;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint MouseHook(int code, nint message, nint data);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int type, MouseHook callback, nint module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
}
