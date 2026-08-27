using DraftPilot.Core.Lcu.Models;

namespace DraftPilot.Core.Lcu;

/// <summary>
/// The rune-page surface of the client, as much of it as the import needs. Exists so
/// <see cref="RuneImporter"/> — the tool's only write path — can be tested against a fake
/// instead of a live League client.
/// </summary>
public interface IRunePageClient
{
    /// <summary>All rune pages on the account, presets included; <see langword="null"/> when the
    /// request failed — callers must not confuse that with "no pages".</summary>
    Task<List<LcuRunePage>?> GetRunePagesAsync(CancellationToken ct = default);

    /// <summary>How many editable pages the account owns; <see langword="null"/> on failure.</summary>
    Task<int?> GetOwnedPageCountAsync(CancellationToken ct = default);

    /// <summary>Creates a rune page and makes it current. Returns false with the client's reason.</summary>
    Task<(bool Ok, string? Error)> CreateRunePageAsync(LcuRunePageRequest page, CancellationToken ct = default);

    /// <summary>Deletes one page. Callers must only pass ids the user owns and agreed to lose.</summary>
    Task<(bool Ok, string? Error)> DeleteRunePageAsync(long pageId, CancellationToken ct = default);
}
