using System.Runtime.InteropServices;

namespace FoxFix.App;

/// <summary>The process of the window in front: the game the numbers belong to.</summary>
public static class ForegroundProcessId
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    public static int Get()
    {
        var window = GetForegroundWindow();
        return window == IntPtr.Zero ? 0 : GetWindowThreadProcessId(window, out var pid) == 0 ? 0 : (int)pid;
    }
}
