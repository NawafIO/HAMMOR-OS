using HAMMOR.App.Presence;
using Xunit;

namespace HAMMOR.App.Tests.Presence;

/// <summary>
/// The pointer's dead zone (Interaction board: "ignores moves under 24 px"),
/// as the approved prototype applies it.
/// </summary>
public sealed class PointerDeadZoneTests
{
    [Fact]
    public void The_first_move_always_counts() =>
        Assert.True(new PointerDeadZone().Accept(400.0, 300.0, 0.0));

    [Theory]
    [InlineData(10.0, 0.0, false)]
    [InlineData(0.0, 23.9, false)]
    [InlineData(16.0, 16.0, false)] // 22.6 px
    [InlineData(24.0, 0.0, true)]
    [InlineData(0.0, -24.0, true)]
    [InlineData(-17.0, 17.0, true)] // 24.04 px
    public void Moves_under_24_px_are_ignored(double dx, double dy, bool expected)
    {
        var zone = new PointerDeadZone();
        zone.Accept(400.0, 300.0, 0.0);

        Assert.Equal(expected, zone.Accept(400.0 + dx, 300.0 + dy, 0.5));
    }

    [Fact]
    public void Distance_is_measured_from_the_last_move_that_counted()
    {
        var zone = new PointerDeadZone();
        zone.Accept(400.0, 300.0, 0.0);

        Assert.False(zone.Accept(410.0, 300.0, 0.1));
        Assert.False(zone.Accept(420.0, 300.0, 0.2));
        Assert.True(zone.Accept(430.0, 300.0, 0.3));
        Assert.False(zone.Accept(440.0, 300.0, 0.4));
    }

    [Fact]
    public void Once_the_core_has_let_go_the_next_move_counts()
    {
        // The core lets go after 5 s of stillness; from then on any move is
        // a fresh one.
        var zone = new PointerDeadZone();
        zone.Accept(400.0, 300.0, 0.0);

        Assert.False(zone.Accept(405.0, 300.0, LivingCoreMotion.PointerStillness - 0.01));
        Assert.True(zone.Accept(405.0, 300.0, LivingCoreMotion.PointerStillness));
    }

    [Fact]
    public void After_the_pointer_leaves_the_next_move_counts()
    {
        var zone = new PointerDeadZone();
        zone.Accept(400.0, 300.0, 0.0);
        zone.Reset();

        Assert.True(zone.Accept(402.0, 300.0, 0.2));
    }
}
