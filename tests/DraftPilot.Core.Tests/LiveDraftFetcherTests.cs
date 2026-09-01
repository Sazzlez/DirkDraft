using DraftPilot.Core.Draft;
using DraftPilot.Meta;
using Xunit;

namespace DraftPilot.Core.Tests;

public class LiveDraftFetcherTests
{
    /// <summary>
    /// The caller remembers what it fetched by (champion, lane), and it has to use the lane the call
    /// was actually made with. When the two disagree — which is exactly when the prediction was
    /// unclear — the champion counts as done under a lane nobody ever looks up.
    /// </summary>
    [Theory]
    [InlineData(Lane.Unknown, Lane.Mid)]
    [InlineData(Lane.Top, Lane.Top)]
    [InlineData(Lane.Support, Lane.Support)]
    public void AnUnknownLane_IsRequestedAsMid(Lane predicted, Lane expected)
        => Assert.Equal(expected, LiveDraftFetcher.RequestedLane(predicted));
}
