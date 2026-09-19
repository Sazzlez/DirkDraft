using System.Text.Json;
using DraftPilot.Core.Config;

namespace DraftPilot.Core.Data;

public enum SnapshotLoadStatus
{
    Ok,

    /// <summary>No file at the expected path.</summary>
    Missing,

    /// <summary>The file is there but could not be read or parsed.</summary>
    Unreadable,

    /// <summary>Written by a newer version of the tool than this one understands.</summary>
    VersionTooNew,

    /// <summary>Parsed, but carries no champions — nothing to work with.</summary>
    Empty,
}

/// <summary>
/// Outcome of a load attempt. The reason travels with the result so the window can say what is wrong
/// instead of only that something is: a missing file, an unreadable one and one from a newer build
/// all look identical as "no data", and telling them apart otherwise costs a debugging round trip.
/// </summary>
public sealed record SnapshotLoadResult(MetaSnapshot? Snapshot, SnapshotLoadStatus Status, string Detail)
{
    public bool IsOk => Status == SnapshotLoadStatus.Ok && Snapshot is not null;
}

/// <summary>Reads and writes the meta snapshot on disk.</summary>
public sealed class SnapshotStore(string? path = null)
{
    private readonly string _path = path ?? AppPaths.SnapshotPath;

    public string Path => _path;

    public bool Exists => File.Exists(_path);

    /// <summary>
    /// Loads the snapshot, or returns <see langword="null"/> when there is none or it cannot be
    /// read. A corrupt file must leave the app usable, just without meta data.
    /// </summary>
    public MetaSnapshot? Load() => LoadWithStatus().Snapshot;

    /// <summary>Loads the snapshot and reports why, if it did not work.</summary>
    public SnapshotLoadResult LoadWithStatus()
    {
        if (!File.Exists(_path))
            return new SnapshotLoadResult(null, SnapshotLoadStatus.Missing, $"Datei fehlt: {_path}");

        try
        {
            using var stream = File.OpenRead(_path);
            var snapshot = JsonSerializer.Deserialize(stream, MetaJson.Default.MetaSnapshot);

            if (snapshot is null)
                return new SnapshotLoadResult(null, SnapshotLoadStatus.Unreadable, "Datei enthielt kein Objekt");

            if (snapshot.Version > MetaSnapshot.CurrentVersion)
            {
                return new SnapshotLoadResult(
                    null,
                    SnapshotLoadStatus.VersionTooNew,
                    $"Version {snapshot.Version}, dieses Programm kennt nur {MetaSnapshot.CurrentVersion}");
            }

            if (snapshot.Champions.Count == 0)
                return new SnapshotLoadResult(null, SnapshotLoadStatus.Empty, "keine Champs enthalten");

            return new SnapshotLoadResult(snapshot, SnapshotLoadStatus.Ok, $"{snapshot.Champions.Count} Champs");
        }
        catch (JsonException ex)
        {
            return new SnapshotLoadResult(null, SnapshotLoadStatus.Unreadable, $"JSON-Fehler: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SnapshotLoadResult(null, SnapshotLoadStatus.Unreadable, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Writes the snapshot atomically: a temporary file first, then a replace. If the update is
    /// cancelled or the network drops, the previous snapshot stays intact and in use.
    /// </summary>
    public void Save(MetaSnapshot snapshot)
    {
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temporary = _path + ".tmp";

        using (var stream = File.Create(temporary))
        {
            JsonSerializer.Serialize(stream, snapshot, MetaJson.Default.MetaSnapshot);
        }

        // Move with overwrite is atomic enough on NTFS: readers see either the old file or the new
        // one, never a half-written mixture.
        File.Move(temporary, _path, overwrite: true);
    }

    /// <summary>Age of the snapshot on disk, or <see langword="null"/> when there is none.</summary>
    public TimeSpan? Age()
    {
        if (!File.Exists(_path))
            return null;

        return DateTimeOffset.UtcNow - new DateTimeOffset(File.GetLastWriteTimeUtc(_path), TimeSpan.Zero);
    }
}
