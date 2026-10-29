using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;

namespace FoxFix.App;

public interface IElevation
{
    bool IsElevated { get; }

    /// <summary>Relaunches the app with administrator rights and closes this instance. False if the user declined.</summary>
    bool RestartElevated(string? arguments = null);
}

public sealed class WindowsElevation : IElevation
{
    public bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public bool RestartElevated(string? arguments = null)
    {
        var path = Environment.ProcessPath;
        if (path is null)
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path, arguments ?? "") { UseShellExecute = true, Verb = "runas" });
        }
        catch (Win32Exception)
        {
            return false; // the user cancelled the UAC prompt
        }

        Application.Current.Shutdown();
        return true;
    }
}
