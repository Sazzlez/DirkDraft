using DraftPilot.Core.Data;
using DraftPilot.Core.Lcu;
using DraftPilot.Core.Lcu.Models;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// The rune import is the tool's only write to the client, so every branch that deletes something
/// is pinned down here: what gets deleted, in which order relative to the user's confirmation,
/// and what the messages claim afterwards.
/// </summary>
public class RuneImporterTests
{
    /// <summary>An in-memory client that records every call the importer makes.</summary>
    private sealed class FakeClient : IRunePageClient
    {
        public List<LcuRunePage>? Pages { get; set; } = [];
        public int? Owned { get; set; } = 2;

        /// <summary>Page ids whose DELETE should fail.</summary>
        public HashSet<long> FailingDeletes { get; } = [];

        public string? CreateError { get; set; }

        public List<long> Deleted { get; } = [];
        public List<LcuRunePageRequest> Created { get; } = [];

        public Task<List<LcuRunePage>?> GetRunePagesAsync(CancellationToken ct = default)
            => Task.FromResult(Pages);

        public Task<int?> GetOwnedPageCountAsync(CancellationToken ct = default)
            => Task.FromResult(Owned);

        public Task<(bool Ok, string? Error)> CreateRunePageAsync(LcuRunePageRequest page, CancellationToken ct = default)
        {
            if (CreateError is not null)
                return Task.FromResult((false, (string?)CreateError));

            Created.Add(page);
            return Task.FromResult((true, (string?)null));
        }

        public Task<(bool Ok, string? Error)> DeleteRunePageAsync(long pageId, CancellationToken ct = default)
        {
            if (FailingDeletes.Contains(pageId))
                return Task.FromResult((false, (string?)"kaputt"));

            Deleted.Add(pageId);
            return Task.FromResult((true, (string?)null));
        }
    }

    private static RunePage Runes(int primaryCount = 4, int secondaryCount = 2, int shardCount = 3) => new()
    {
        PrimaryPath = "Präzision",
        PrimaryPathId = 8000,
        PrimaryRunes = ["Eroberer"],
        PrimaryRuneIds = [.. Enumerable.Range(8010, primaryCount)],
        SecondaryPath = "Zauberei",
        SecondaryPathId = 8200,
        SecondaryRuneIds = [.. Enumerable.Range(8224, secondaryCount)],
        ShardIds = [.. Enumerable.Range(5005, shardCount)],
    };

    private static LcuRunePage Page(long id, string name, bool editable = true, long modified = 0) => new()
    {
        Id = id,
        Name = name,
        IsEditable = editable,
        LastModified = modified,
    };

    private static Task<RuneImportResult> Import(FakeClient client, bool confirm = true, CancellationToken ct = default)
        => RuneImporter.ImportAsync(client, Runes(), "Darius vs Jax", _ => confirm, ct);

    [Fact]
    public async Task ReplacesEveryOwnPage_IncludingTheLegacyName()
    {
        var client = new FakeClient
        {
            Pages = [Page(1, "DirkRunen · Alt"), Page(2, "DraftPilot · Uralt"), Page(3, "Meine Seite")],
            Owned = 3,
        };

        var result = await Import(client);

        Assert.True(result.Ok);
        Assert.Equal([1L, 2L], client.Deleted);
        Assert.Single(client.Created);
    }

    [Fact]
    public async Task DecliningTheDialog_DeletesNothingAtAll()
    {
        // Even our own page must survive a "no": the message promises "keine Seite gelöscht".
        var client = new FakeClient
        {
            Pages = [Page(1, "DirkRunen · Alt"), Page(2, "Fremd A"), Page(3, "Fremd B")],
            Owned = 2,
        };

        var result = await Import(client, confirm: false);

        Assert.False(result.Ok);
        Assert.Empty(client.Deleted);
        Assert.Empty(client.Created);
    }

    [Fact]
    public async Task SacrificesTheOldestForeignPage_NeverAnOwnOne()
    {
        string? asked = null;
        var client = new FakeClient
        {
            Pages = [Page(1, "Fremd neu", modified: 200), Page(2, "Fremd alt", modified: 100)],
            Owned = 2,
        };

        var result = await RuneImporter.ImportAsync(
            client, Runes(), "Darius vs Jax", name => { asked = name; return true; });

        Assert.True(result.Ok);
        Assert.Equal("Fremd alt", asked);
        Assert.Equal([2L], client.Deleted);
    }

    [Fact]
    public async Task FailedDeleteOfOwnPage_AbortsBeforeCreating()
    {
        var client = new FakeClient
        {
            Pages = [Page(1, "DirkRunen · Alt")],
            Owned = 1,
        };
        client.FailingDeletes.Add(1);

        var result = await Import(client);

        Assert.False(result.Ok);
        Assert.Empty(client.Created);
        Assert.Contains("löschen", result.Message);
    }

    [Fact]
    public async Task CreateFailureAfterASacrifice_NamesTheLoss()
    {
        var client = new FakeClient
        {
            Pages = [Page(1, "Fremd", editable: true)],
            Owned = 1,
            CreateError = "400: kaputt",
        };

        var result = await Import(client);

        Assert.False(result.Ok);
        Assert.Contains("Fremd", result.Message);
        Assert.Contains("entfernt", result.Message);
    }

    [Fact]
    public async Task OversuppliedGuide_IsTrimmedToExactlyNinePerks()
    {
        var client = new FakeClient { Pages = [], Owned = 2 };

        var result = await RuneImporter.ImportAsync(
            client, Runes(primaryCount: 5, secondaryCount: 3, shardCount: 4), "Darius vs Jax", _ => true);

        Assert.True(result.Ok);
        Assert.Equal(9, client.Created.Single().SelectedPerkIds.Count);
    }

    [Fact]
    public async Task FailedPageRead_DoesNotCountAsZeroPages()
    {
        // null = the request failed; treating it as "no pages" would justify a delete.
        var client = new FakeClient { Pages = null, Owned = 2 };

        var result = await Import(client);

        Assert.False(result.Ok);
        Assert.Empty(client.Deleted);
        Assert.Empty(client.Created);
    }

    [Fact]
    public async Task FullAccountWithNothingDeletable_AbortsWithAdvice()
    {
        var client = new FakeClient
        {
            Pages = [Page(1, "Fremd", editable: true)],
            Owned = 1,
        };
        client.Pages![0].IsDeletable = false;

        var result = await Import(client);

        Assert.False(result.Ok);
        Assert.Empty(client.Deleted);
        Assert.Empty(client.Created);
    }

    [Fact]
    public async Task NeedingMoreThanOneSacrifice_AbortsBeforeAnythingIsDeleted()
    {
        // Two foreign pages over the limit: deleting one would not free a slot, so the old flow
        // sacrificed a page AND failed. Now it must refuse up front.
        var client = new FakeClient
        {
            Pages = [Page(1, "Fremd A"), Page(2, "Fremd B"), Page(3, "Fremd C")],
            Owned = 1,
        };

        var result = await Import(client);

        Assert.False(result.Ok);
        Assert.Empty(client.Deleted);
        Assert.Empty(client.Created);
    }

    [Fact]
    public async Task AccountWithoutPageSlots_GetsAnHonestMessage()
    {
        var client = new FakeClient { Pages = [], Owned = 0 };

        var result = await Import(client);

        Assert.False(result.Ok);
        Assert.Contains("keine Runenseiten-Slots", result.Message);
    }

    [Fact]
    public async Task CancellationBeforeTheFirstDelete_LeavesEverythingUntouched()
    {
        var client = new FakeClient
        {
            Pages = [Page(1, "DirkRunen · Alt")],
            Owned = 1,
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Import(client, ct: cts.Token));

        Assert.Empty(client.Deleted);
        Assert.Empty(client.Created);
    }

    [Fact]
    public async Task PresetPages_NeitherCountAgainstTheLimit_NorGetSacrificed()
    {
        var client = new FakeClient
        {
            Pages = [Page(1, "Preset", editable: false), Page(2, "Fremd", editable: true)],
            Owned = 2,
        };

        var result = await Import(client);

        Assert.True(result.Ok);
        Assert.Empty(client.Deleted);
    }

    [Fact]
    public async Task IncompleteRunes_AreRefusedUpFront()
    {
        var client = new FakeClient { Pages = [], Owned = 2 };

        var result = await RuneImporter.ImportAsync(
            client, Runes(primaryCount: 3), "Darius vs Jax", _ => true);

        Assert.False(result.Ok);
        Assert.Empty(client.Created);
    }

    [Fact]
    public async Task LongMatchupTitle_YieldsAClientSafeName()
    {
        var client = new FakeClient { Pages = [], Owned = 2 };

        await RuneImporter.ImportAsync(
            client, Runes(), "Nunu & Willump vs Kai'Sa im Spiegel", _ => true);

        var name = client.Created.Single().Name;
        Assert.True(name.Length <= 30);
        Assert.StartsWith(RuneImporter.PagePrefix, name);
    }
}
