namespace FoxFix.App;

/// <summary>
/// The release build is marked "requires administrator", so Windows asks for it at launch. This is the second lock:
/// if the program is somehow running without those rights anyway, it says so in plain words and stops.
/// </summary>
public static class ElevationGate
{
    /// <summary>Windows' own number for "access denied", used as the exit code.</summary>
    public const int ExitCode = 5;

    public const string Message =
        "Bu uygulama yönetici yetkileri gerektiriyor. Lütfen sağ tıklayıp 'Yönetici Olarak Çalıştır' seçeneğini kullanın. (Hata Kodu: 5)";

    /// <summary>The text to show when the program must not go on, or null when all is well.</summary>
    public static string? BlockingMessage(bool isElevated) => isElevated ? null : Message;
}