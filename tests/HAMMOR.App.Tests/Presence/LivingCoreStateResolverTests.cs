using HAMMOR.App.Presence;
using Xunit;

namespace HAMMOR.App.Tests.Presence;

/// <summary>
/// One owner for the white core: any combination of app signals resolves to
/// exactly one state, ranked as the approved "who moves the core" stack.
/// </summary>
public sealed class LivingCoreStateResolverTests
{
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
    public void Every_combination_resolves_to_its_highest_ranked_signal()
    {
        LivingCoreState[] rank =
        [
            LivingCoreState.Speaking,
            LivingCoreState.Listening,
            LivingCoreState.Thinking,
            LivingCoreState.Blocked,
            LivingCoreState.Error,
        ];

        for (var bits = 0; bits < 32; bits++)
        {
            var signals = new LivingCoreSignals(
                IsSpeaking: (bits & 1) != 0,
                IsListening: (bits & 2) != 0,
                IsThinking: (bits & 4) != 0,
                IsBlocked: (bits & 8) != 0,
                HasError: (bits & 16) != 0);

            var expected = LivingCoreState.Idle;
            for (var i = 0; i < rank.Length; i++)
            {
                if ((bits & (1 << i)) != 0)
                {
                    expected = rank[i];
                    break;
                }
            }

            Assert.Equal(expected, LivingCoreStateResolver.Resolve(signals));
        }
    }
}
