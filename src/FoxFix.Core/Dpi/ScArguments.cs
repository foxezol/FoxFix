namespace FoxFix.Core.Dpi;

/// <summary>
/// sc.exe argument lists and exit-code meaning. Exit codes are locale independent;
/// sc's text output is not, so it is never parsed (plan section 7, item 11).
/// </summary>
public static class ScArguments
{
    private const int ErrorServiceDoesNotExist = 1060;
    private const int ErrorServiceNotActive = 1062;
    private const int ErrorServiceMarkedForDelete = 1072;

    public static IReadOnlyList<string> Stop(string name) => ["stop", name];

    public static IReadOnlyList<string> Delete(string name) => ["delete", name];

    public static IReadOnlyList<string> Start(string name) => ["start", name];

    /// <summary>sc wants "binPath=" and its value as two separate tokens.</summary>
    public static IReadOnlyList<string> Create(ServiceConfig config)
    {
        return
        [
            "create", config.Name,
            "binPath=", StripNtPrefix(config.BinaryPath),
            "start=", StartTypeName(config.StartType),
            "type=", ServiceTypeName(config.ServiceType),
            "DisplayName=", config.DisplayName,
        ];
    }

    public static StopOutcome MapStop(int exitCode) => exitCode switch
    {
        0 => StopOutcome.Stopped,
        ErrorServiceNotActive => StopOutcome.AlreadyStopped,
        ErrorServiceDoesNotExist => StopOutcome.NotFound,
        _ => StopOutcome.Failed,
    };

    public static DeleteOutcome MapDelete(int exitCode) => exitCode switch
    {
        0 => DeleteOutcome.Deleted,
        ErrorServiceMarkedForDelete => DeleteOutcome.MarkedForDeletion,
        ErrorServiceDoesNotExist => DeleteOutcome.NotFound,
        _ => DeleteOutcome.Failed,
    };

    public static string StartTypeName(int start) => start switch
    {
        0 => "boot",
        1 => "system",
        2 => "auto",
        3 => "demand",
        4 => "disabled",
        _ => throw new NotSupportedException($"Bilinmeyen başlangıç türü: {start}"),
    };

    public static string ServiceTypeName(int type)
    {
        if ((type & 0x1) != 0)
        {
            return "kernel";
        }

        if ((type & 0x2) != 0)
        {
            return "filesys";
        }

        if ((type & 0x10) != 0)
        {
            return "own";
        }

        if ((type & 0x20) != 0)
        {
            return "share";
        }

        throw new NotSupportedException($"Bilinmeyen hizmet türü: {type}");
    }

    private static string StripNtPrefix(string path) =>
        path.StartsWith(@"\??\", StringComparison.Ordinal) ? path[4..] : path;
}
