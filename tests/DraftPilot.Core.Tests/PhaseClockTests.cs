using DraftPilot.Core.Draft;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// The displayed timer used to freeze between client updates, because the client only pushes when
/// something changes and a running clock is not a change to it. These tests pin the local countdown.
/// </summary>
public class PhaseClockTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    private static DraftState StateWith(int secondsLeft, int totalSeconds)
    {
        var session = new SessionBuilder().Phase("BAN_PICK", secondsLeft).OnClock(0, "pick").Build();
        session.Timer.TotalTimeInPhase = totalSeconds * 1000;

        return DraftState.From(session);
    }

    [Fact]
    public void CountsDownAsTimePasses()
    {
        var clock = PhaseClock.FromState(StateWith(30, 30), Now);

        Assert.Equal(30, clock.At(Now).Seconds);
        Assert.Equal(25, clock.At(Now.AddSeconds(5)).Seconds);
        Assert.Equal(1, clock.At(Now.AddSeconds(29)).Seconds);
    }

    [Fact]
    public void NeverGoesBelowZero()
    {
        var clock = PhaseClock.FromState(StateWith(10, 30), Now);

        Assert.Equal(0, clock.At(Now.AddSeconds(60)).Seconds);
        Assert.Equal(0, clock.At(Now.AddSeconds(60)).Fraction);
    }

    [Fact]
    public void FinalSecondStillReadsOne()
    {
        // Rounding up keeps "1" on screen through the last second instead of dropping to 0 early.
        var clock = PhaseClock.FromState(StateWith(30, 30), Now);

        Assert.Equal(1, clock.At(Now.AddSeconds(29.4)).Seconds);
    }

    [Fact]
    public void FractionTracksTheProgressBar()
    {
        var clock = PhaseClock.FromState(StateWith(40, 40), Now);

        Assert.Equal(1.0, clock.At(Now).Fraction, precision: 3);
        Assert.Equal(0.5, clock.At(Now.AddSeconds(20)).Fraction, precision: 3);
        Assert.Equal(0.0, clock.At(Now.AddSeconds(40)).Fraction, precision: 3);
    }

    [Fact]
    public void PhaseWithoutReportedLength_HasNoFraction()
    {
        // The client sometimes reports no total; a bar would then be meaningless, but the number
        // must still count down.
        var clock = PhaseClock.FromState(StateWith(20, 0), Now);

        Assert.Equal(20, clock.At(Now).Seconds);
        Assert.Equal(0, clock.At(Now).Fraction);
    }

    [Fact]
    public void NoTimeLeft_MeansStopped()
    {
        var clock = PhaseClock.FromState(StateWith(0, 30), Now);

        Assert.False(clock.IsRunning);
        Assert.Equal(0, clock.At(Now).Seconds);
    }

    [Fact]
    public void EachUpdateResynchronises()
    {
        // A later update is authoritative: if the client says 18 s, the local clock restarts there
        // rather than continuing from its own estimate.
        var first = PhaseClock.FromState(StateWith(30, 30), Now);
        Assert.Equal(20, first.At(Now.AddSeconds(10)).Seconds);

        var resynced = PhaseClock.FromState(StateWith(18, 30), Now.AddSeconds(10));
        Assert.Equal(18, resynced.At(Now.AddSeconds(10)).Seconds);
    }

    [Fact]
    public void StaleUpdate_IsCorrectedByTheClientTimestamp()
    {
        // The reported remaining time is stamped when the client builds the payload; transport,
        // coalescing and parsing all happen after that. Ignoring the gap made the displayed clock run
        // ahead of the one in the client.
        var session = new SessionBuilder().Phase("BAN_PICK", 30).OnClock(0, "pick").Build();
        session.Timer.TotalTimeInPhase = 30_000;
        session.Timer.InternalNowInEpochMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 2_000;

        var state = DraftState.From(session);
        Assert.InRange(state.AgeAtArrivalMs, 1_500, 3_000);

        var clock = PhaseClock.FromState(state, Now);
        Assert.InRange(clock.At(Now).Seconds, 27, 29);
    }

    [Fact]
    public void ImplausibleClientClock_IsIgnored()
    {
        // A client clock that disagrees with ours by years is a wrong clock, not a stale payload.
        // Correcting by it would be worse than not correcting at all.
        var session = new SessionBuilder().Phase("BAN_PICK", 30).OnClock(0, "pick").Build();
        session.Timer.InternalNowInEpochMs = 1;

        Assert.Equal(0, DraftState.From(session).AgeAtArrivalMs);
    }

    [Fact]
    public void ClientClockInTheFuture_IsIgnored()
    {
        var session = new SessionBuilder().Phase("BAN_PICK", 30).OnClock(0, "pick").Build();
        session.Timer.InternalNowInEpochMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60_000;

        Assert.Equal(0, DraftState.From(session).AgeAtArrivalMs);
    }

    [Fact]
    public void MissingClientClock_LeavesTheReportedTimeAlone()
    {
        // Older payloads, and the replayed recordings, carry no client timestamp.
        var session = new SessionBuilder().Phase("BAN_PICK", 25).OnClock(0, "pick").Build();

        var state = DraftState.From(session);
        Assert.Equal(0, state.AgeAtArrivalMs);
        Assert.Equal(25, PhaseClock.FromState(state, Now).At(Now).Seconds);
    }

    [Fact]
    public void Stopped_ReportsNothing()
    {
        Assert.False(PhaseClock.Stopped.IsRunning);
        Assert.Equal((0, 0.0), PhaseClock.Stopped.At(Now));
    }
}
