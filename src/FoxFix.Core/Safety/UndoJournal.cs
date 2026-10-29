using System.Text.Json;

namespace FoxFix.Core.Safety;

public sealed record UndoRecord(
    Guid Id,
    string FixId,
    DateTimeOffset Timestamp,
    string Description,
    IReadOnlyDictionary<string, string> Data);

/// <summary>
/// Persistent list of reversible changes (default: %LocalAppData%\FoxFix\undo.json).
/// Writes are atomic so a crash never leaves a half-written journal.
/// </summary>
public sealed class UndoJournal
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public UndoJournal(string path)
    {
        _path = path;
    }

    public static string DefaultPath => Path.Combine(AppPaths.LocalRoot, "undo.json");

    public async Task AppendAsync(UndoRecord record, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var records = await ReadAsync(ct);
            records.Add(record);
            await WriteAsync(records, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<UndoRecord>> LoadAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await ReadAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var records = await ReadAsync(ct);
            if (records.RemoveAll(r => r.Id == id) > 0)
            {
                await WriteAsync(records, ct);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<UndoRecord>> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<List<UndoRecord>>(stream, JsonOptions, ct) ?? [];
        }
        catch (JsonException)
        {
            // Keep the unreadable file for support instead of overwriting it.
            var backup = $"{_path}.bad-{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Move(_path, backup, overwrite: true);
            return [];
        }
    }

    private async Task WriteAsync(List<UndoRecord> records, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);

        var temp = _path + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, records, JsonOptions, ct);
        }

        File.Move(temp, _path, overwrite: true);
    }
}
