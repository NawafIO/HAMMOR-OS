namespace HAMMOR.App.Presence;

/// <summary>
/// The pointer's dead zone (Interaction board: "ignores moves under 24 px"):
/// a move counts only once the pointer is 24 px from the last move that
/// counted. After the white core has let go (5 s of stillness) or the pointer
/// left the window, the next move always counts. Pure, so it is tested
/// without WPF.
/// </summary>
internal sealed class PointerDeadZone
{
    /// <summary>Moves shorter than this, in device-independent pixels, are ignored.</summary>
    public const double Distance = 24.0;

    private bool _hasLast;
    private double _lastX;
    private double _lastY;
    private double _lastAt;

    /// <summary>Whether a move to (<paramref name="x"/>, <paramref name="y"/>) counts.</summary>
    /// <param name="x">Pointer position, device-independent pixels.</param>
    /// <param name="y">Pointer position, device-independent pixels.</param>
    /// <param name="now">Current time, seconds.</param>
    public bool Accept(double x, double y, double now)
    {
        if (_hasLast && (now - _lastAt) < LivingCoreMotion.PointerStillness)
        {
            var dx = x - _lastX;
            var dy = y - _lastY;
            if (Math.Sqrt((dx * dx) + (dy * dy)) < Distance)
            {
                return false;
            }
        }

        _hasLast = true;
        _lastX = x;
        _lastY = y;
        _lastAt = now;
        return true;
    }

    /// <summary>The pointer left: the next move counts wherever it is.</summary>
    public void Reset() => _hasLast = false;
}
