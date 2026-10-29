using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace FoxFix.App;

public interface IClipboardService
{
    void SetText(string text);
}

public interface IReportFileSaver
{
    /// <summary>Asks the user where to save. Returns the chosen path, or null if they cancelled.</summary>
    Task<string?> SaveAsync(string suggestedFileName, string content, CancellationToken ct);
}

/// <summary>Real clipboard. Needs a hand test: other programs can hold the clipboard for a moment.</summary>
public sealed class WpfClipboardService : IClipboardService
{
    private const int Attempts = 5;
    private const int PauseMilliseconds = 80;

    public void SetText(string text)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, copy: true);
                return;
            }
            catch (COMException) when (attempt < Attempts)
            {
                // CLIPBRD_E_CANT_OPEN: someone else has it open right now. Wait a moment and retry.
                Thread.Sleep(PauseMilliseconds);
            }
        }
    }
}

/// <summary>Real save dialog. Needs a hand test. UTF-8 with a byte-order mark so Notepad shows Turkish letters right.</summary>
public sealed class WindowsReportFileSaver : IReportFileSaver
{
    public async Task<string?> SaveAsync(string suggestedFileName, string content, CancellationToken ct)
    {
        var dialog = new SaveFileDialog
        {
            FileName = suggestedFileName,
            DefaultExt = ".txt",
            Filter = "Metin dosyası (*.txt)|*.txt",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            OverwritePrompt = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        await File.WriteAllTextAsync(dialog.FileName, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), ct);
        return dialog.FileName;
    }
}
