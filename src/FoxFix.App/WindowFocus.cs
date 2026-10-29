using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FoxFix.App;

/// <summary>
/// A program started with administrator rights (through the UAC question) opens behind the other windows, because Windows only
/// lets the program that just got input take the foreground. Tapping Alt first counts as input, then the window is lifted.
/// </summary>
public static class WindowFocus
{
    private const byte VkMenu = 0x12;
    private const uint KeyUp = 0x0002;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extraInfo);

    public static void BringToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        keybd_event(VkMenu, 0, 0, UIntPtr.Zero);
        keybd_event(VkMenu, 0, KeyUp, UIntPtr.Zero);

        window.Topmost = true;
        window.Activate();
        window.Topmost = false;
        window.Focus();

        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            SetForegroundWindow(handle);
        }
    }
}