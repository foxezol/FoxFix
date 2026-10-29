// FoxDPI.exe: the shortcut that starts only FoxDPI, without opening FoxFix.
//
// It is a tiny .NET Framework program (it ships with Windows, so it needs nothing installed). Its manifest asks for
// administrator rights, so Windows shows the UAC question here; FoxFix.exe, started from this elevated process,
// does not ask a second time. All the work happens inside FoxFix.exe (FoxFix.exe --sadece-foxdpi).
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

internal static class Program
{
    private static int Main()
    {
        var folder = AppDomain.CurrentDomain.BaseDirectory;
        var foxFix = Path.Combine(folder, "FoxFix.exe");

        if (!File.Exists(foxFix))
        {
            MessageBox.Show(
                "FoxFix.exe bulunamadı. FoxDPI.exe, FoxFix.exe ile aynı klasörde durmalı.",
                "FoxDPI", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 2;
        }

        try
        {
            Process.Start(new ProcessStartInfo(foxFix, "--sadece-foxdpi") { UseShellExecute = false, WorkingDirectory = folder });
            return 0;
        }
        catch (Win32Exception ex)
        {
            MessageBox.Show("FoxDPI başlatılamadı: " + ex.Message, "FoxDPI", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
    }
}
