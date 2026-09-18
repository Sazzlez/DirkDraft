using DraftPilot.Core.Lcu;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// The one distinction the draft panel rests on: a client that answers "there is no champion
/// select" has told us something; a read that never got through has not.
/// <para>
/// This used to be one and the same value. Both came back as <c>null</c>, both closed the panel,
/// and a two-second socket hiccup therefore threw away the build, the fetched counter edges and
/// the whole per-draft fetch budget in the middle of a draft.
/// </para>
/// </summary>
public class LcuReadTests
{
    /// <summary>
    /// Nothing is listening on this port, so the request cannot complete. The client learns
    /// nothing — and "nothing" must never be read as "champion select is over".
    /// </summary>
    [Fact]
    public async Task AReadThatCannotReachTheClient_IsNotAnAnswer()
    {
        // Port 1 is reserved and never served by the League client.
        using var client = new LcuClient(new LcuCredentials(Port: 1, Password: "x"));

        var read = await client.ReadChampSelectSessionAsync(CancellationToken.None);

        Assert.False(read.Answered);
        Assert.Null(read.Json);
    }

    /// <summary>
    /// A cancelled read is the same kind of nothing: it happens on every reconnect, where an
    /// in-flight seed is dropped because a newer one has taken over.
    /// </summary>
    [Fact]
    public async Task ACancelledRead_IsNotAnAnswer()
    {
        using var client = new LcuClient(new LcuCredentials(Port: 1, Password: "x"));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var read = await client.ReadChampSelectSessionAsync(cancelled.Token);

        Assert.False(read.Answered);
    }

    /// <summary>
    /// The default is the safe one: a value nobody filled in claims no knowledge, rather than
    /// claiming the client said there is no draft.
    /// </summary>
    [Fact]
    public void TheEmptyRead_ClaimsNothing()
    {
        Assert.False(default(LcuRead).Answered);
        Assert.Null(default(LcuRead).Json);
    }
}
