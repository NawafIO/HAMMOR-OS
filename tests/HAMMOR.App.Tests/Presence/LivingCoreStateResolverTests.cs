using HAMMOR.App.Presence;
using Xunit;

namespace HAMMOR.App.Tests.Presence;

/// <summary>
/// One owner for the white core: any combination of app signals resolves to
/// exactly one state, ranked as the approved "who moves the core" stack.
/// </summary>
public sealed class LivingCoreStateResolverTests
{
    // Highest first. Bit i of FromBits sets the signal for Rank[i].
    private static readonly LivingCoreState[] Rank =
    [
        LivingCoreState.Wake,
        LivingCoreState.Speaking,
        LivingCoreState.Listening,
        LivingCoreState.Warning,
        LivingCoreState.Thinking,
        LivingCoreState.Blocked,
        LivingCoreState.Error,
        LivingCoreState.Success,
        LivingCoreState.Sleep,
    ];

    [Fact]
    public void No_signal_rests_in_idle() =>
        Assert.Equal(LivingCoreState.Idle, LivingCoreStateResolver.Resolve(default));

    [Theory]
    [InlineData(true, false, false, false, false, LivingCoreState.Speaking)]
    [InlineData(false, true, false, false, false, LivingCoreState.Listening)]
    [InlineData(false, false, true, false, false, LivingCoreState.Thinking)]
    [InlineData(false, false, false, true, false, LivingCoreState.Blocked)]
    [InlineData(false, false, false, false, true, LivingCoreState.Error)]
    public void Each_signal_alone_shows_its_state(
        bool speaking,
        bool listening,
        bool thinking,
        bool blocked,
        bool error,
        LivingCoreState expected)
    {
        var signals = new LivingCoreSignals(speaking, listening, thinking, blocked, error);

        Assert.Equal(expected, LivingCoreStateResolver.Resolve(signals));
    }

    [Theory]
    [InlineData(true, false, false, false, LivingCoreState.Warning)]
    [InlineData(false, true, false, false, LivingCoreState.Success)]
    [InlineData(false, false, true, false, LivingCoreState.Sleep)]
    [InlineData(false, false, false, true, LivingCoreState.Wake)]
    public void Each_reference_state_signal_alone_shows_its_state(
        bool confirming,
        bool succeeding,
        bool resting,
        bool waking,
        LivingCoreState expected)
    {
        var signals = new LivingCoreSignals(
            false, false, false, false, false,
            IsConfirming: confirming,
            IsSucceeding: succeeding,
            IsResting: resting,
            IsWaking: waking);

        Assert.Equal(expected, LivingCoreStateResolver.Resolve(signals));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Listening_comes_first_when_blocked_or_in_error(bool blocked, bool error)
    {
        // Interaction board: "If you speak while it is blocked or in error, it
        // listens first."
        var signals = new LivingCoreSignals(false, true, false, blocked, error);

        Assert.Equal(LivingCoreState.Listening, LivingCoreStateResolver.Resolve(signals));
    }

    [Fact]
    public void An_open_approval_prompt_pauses_thinking()
    {
        var signals = new LivingCoreSignals(false, false, true, true, false, IsConfirming: true);

        Assert.Equal(LivingCoreState.Warning, LivingCoreStateResolver.Resolve(signals));
    }

    [Theory]
    [InlineData(true, false, LivingCoreState.Blocked)]
    [InlineData(false, true, LivingCoreState.Error)]
    public void Success_never_covers_something_that_needs_the_user(bool blocked, bool error, LivingCoreState expected)
    {
        var signals = new LivingCoreSignals(false, false, false, blocked, error, IsSucceeding: true);

        Assert.Equal(expected, LivingCoreStateResolver.Resolve(signals));
    }

    [Fact]
    public void Every_combination_resolves_to_its_highest_ranked_signal()
    {
        for (var bits = 0; bits < (1 << Rank.Length); bits++)
        {
            var expected = LivingCoreState.Idle;
            for (var i = 0; i < Rank.Length; i++)
            {
                if ((bits & (1 << i)) != 0)
                {
                    expected = Rank[i];
                    break;
                }
            }

            Assert.Equal(expected, LivingCoreStateResolver.Resolve(FromBits(bits)));
        }
    }

    [Theory]
    [InlineData(LivingCoreState.Sleep, LivingCoreState.Idle, true)]
    [InlineData(LivingCoreState.Sleep, LivingCoreState.Listening, true)]
    [InlineData(LivingCoreState.Sleep, LivingCoreState.Speaking, true)]
    [InlineData(LivingCoreState.Sleep, LivingCoreState.Blocked, true)]
    [InlineData(LivingCoreState.Sleep, LivingCoreState.Warning, true)]
    [InlineData(LivingCoreState.Sleep, LivingCoreState.Sleep, false)]
    [InlineData(LivingCoreState.Sleep, LivingCoreState.Wake, false)]
    [InlineData(LivingCoreState.Idle, LivingCoreState.Speaking, false)]
    [InlineData(LivingCoreState.Idle, LivingCoreState.Sleep, false)]
    [InlineData(LivingCoreState.Wake, LivingCoreState.Idle, false)]
    public void Only_leaving_sleep_goes_through_wake(LivingCoreState current, LivingCoreState next, bool expected)
    {
        // State board 10: "you speak to it from sleep" and it wakes first.
        Assert.Equal(expected, LivingCoreStateResolver.NeedsWake(current, next));
    }

    private static LivingCoreSignals FromBits(int bits) => new(
        IsSpeaking: (bits & (1 << 1)) != 0,
        IsListening: (bits & (1 << 2)) != 0,
        IsThinking: (bits & (1 << 4)) != 0,
        IsBlocked: (bits & (1 << 5)) != 0,
        HasError: (bits & (1 << 6)) != 0,
        IsConfirming: (bits & (1 << 3)) != 0,
        IsSucceeding: (bits & (1 << 7)) != 0,
        IsResting: (bits & (1 << 8)) != 0,
        IsWaking: (bits & (1 << 0)) != 0);
}
