namespace DraftPilot.Core.Draft;

/// <summary>
/// The phase countdown, kept separate from the client's updates.
/// <para>
/// The client pushes only when something changes, and a ticking clock is not a change to it — the
/// remaining seconds merely ride along on the next update. A display fed straight from those updates
/// therefore freezes between events. So the deadline is captured once and the remaining time is
/// derived from the wall clock, re-synchronised on every update that arrives.
/// </para>
/// </summary>
/// <param name="EndsAt">When the phase runs out, or <see langword="null"/> when there is no clock.</param>
/// <param name="TotalSeconds">Length of the phase, used for the progress fraction.</param>
public readonly record struct PhaseClock(DateTimeOffset? EndsAt, int TotalSeconds)
{
    /// <summary>No phase running.</summary>
    public static PhaseClock Stopped => new(null, 0);

    /// <summary>
    /// Captures the deadline from a fresh client update.
    /// <para>
    /// The reported remaining time was already stale on arrival — the client stamped it when it
    /// built the payload, and transport, coalescing and parsing all happened afterwards. Subtracting
    /// that age is what keeps this clock in step with the one the client shows instead of running
    /// ahead of it.
    /// </para>
    /// </summary>
    public static PhaseClock FromState(DraftState state, DateTimeOffset now)
    {
        if (state.SecondsLeft <= 0)
            return Stopped;

        var remaining = TimeSpan.FromSeconds(state.SecondsLeft) - TimeSpan.FromMilliseconds(state.AgeAtArrivalMs);

        return remaining > TimeSpan.Zero
            ? new PhaseClock(now + remaining, state.TotalSeconds)
            : Stopped;
    }

    public bool IsRunning => EndsAt is not null;

    /// <summary>
    /// Seconds still to run, rounded up so the display shows "1" through the final second rather
    /// than dropping to zero early, and the share of the phase left for the progress bar.
    /// </summary>
    public (int Seconds, double Fraction) At(DateTimeOffset now)
    {
        if (EndsAt is not { } deadline)
            return (0, 0);

        var remaining = (deadline - now).TotalSeconds;
        var seconds = (int)Math.Ceiling(Math.Max(0, remaining));

        var fraction = TotalSeconds > 0
            ? Math.Clamp(remaining / TotalSeconds, 0, 1)
            : 0;

        return (seconds, fraction);
    }
}
