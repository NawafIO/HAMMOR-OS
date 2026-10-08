namespace HAMMOR.App.Presence;

/// <summary>
/// A CSS-style cubic-bezier timing curve through (0,0) and (1,1). Pure math,
/// no allocation: the motion engine evaluates these every frame.
/// </summary>
public readonly struct CubicBezierEasing
{
    private const double Epsilon = 1e-6;

    private readonly double _ax;
    private readonly double _bx;
    private readonly double _cx;
    private readonly double _ay;
    private readonly double _by;
    private readonly double _cy;

    // Control points on the diagonal: the identity (CSS linear), exactly.
    private readonly bool _linear;

    public CubicBezierEasing(double x1, double y1, double x2, double y2)
    {
        _linear = x1 == y1 && x2 == y2;
        _cx = 3.0 * x1;
        _bx = (3.0 * (x2 - x1)) - _cx;
        _ax = 1.0 - _cx - _bx;
        _cy = 3.0 * y1;
        _by = (3.0 * (y2 - y1)) - _cy;
        _ay = 1.0 - _cy - _by;
    }

    /// <summary>Eased progress for linear progress <paramref name="x"/>.</summary>
    public double Evaluate(double x)
    {
        if (x <= 0.0)
        {
            return 0.0;
        }

        if (x >= 1.0)
        {
            return 1.0;
        }

        if (_linear)
        {
            return x;
        }

        var t = SolveCurveX(x);
        return ((((_ay * t) + _by) * t) + _cy) * t;
    }

    private double SampleCurveX(double t) => ((((_ax * t) + _bx) * t) + _cx) * t;

    private double SampleCurveDerivativeX(double t) => (((3.0 * _ax * t) + (2.0 * _bx)) * t) + _cx;

    private double SolveCurveX(double x)
    {
        // Newton-Raphson converges in a few steps for every curve HAMMOR
        // uses; bisection is the guaranteed fallback.
        var t = x;
        for (var i = 0; i < 8; i++)
        {
            var error = SampleCurveX(t) - x;
            if (Math.Abs(error) < Epsilon)
            {
                return t;
            }

            var derivative = SampleCurveDerivativeX(t);
            if (Math.Abs(derivative) < Epsilon)
            {
                break;
            }

            t = Math.Clamp(t - (error / derivative), 0.0, 1.0);
        }

        var low = 0.0;
        var high = 1.0;
        t = x;
        for (var i = 0; i < 40; i++)
        {
            var value = SampleCurveX(t);
            if (Math.Abs(value - x) < Epsilon)
            {
                return t;
            }

            if (value < x)
            {
                low = t;
            }
            else
            {
                high = t;
            }

            t = (low + high) * 0.5;
        }

        return t;
    }
}

/// <summary>
/// The approved easing tokens (Motion board) plus the CSS timing functions
/// the prototype uses: ease, ease-in-out and linear.
/// </summary>
public static class LivingCoreEasings
{
    /// <summary>CSS linear: rotations, flows, the Error stutter.</summary>
    public static readonly CubicBezierEasing Linear = new(0.0, 0.0, 1.0, 1.0);

    /// <summary>CSS ease (.25, .1, .25, 1): the prototype's fades.</summary>
    public static readonly CubicBezierEasing Ease = new(0.25, 0.1, 0.25, 1.0);

    /// <summary>ease.breath (.37, 0, .63, 1): every loop.</summary>
    public static readonly CubicBezierEasing Breath = new(0.37, 0.0, 0.63, 1.0);

    /// <summary>ease.strike (.16, 1, .3, 1): glances and pulses.</summary>
    public static readonly CubicBezierEasing Strike = new(0.16, 1.0, 0.3, 1.0);

    /// <summary>ease.settle (.22, 1, .36, 1): returns and state changes.</summary>
    public static readonly CubicBezierEasing Settle = new(0.22, 1.0, 0.36, 1.0);

    /// <summary>ease.sink (.45, 0, .55, 1): sleep.</summary>
    public static readonly CubicBezierEasing Sink = new(0.45, 0.0, 0.55, 1.0);

    /// <summary>CSS ease-in-out (.42, 0, .58, 1): shimmer, bob, drift.</summary>
    public static readonly CubicBezierEasing InOut = new(0.42, 0.0, 0.58, 1.0);

    /// <summary>Speaking rings leaving the halo (.3, 0, .2, 1).</summary>
    public static readonly CubicBezierEasing RingOut = new(0.3, 0.0, 0.2, 1.0);

    /// <summary>Listening rings drawing inward (.2, .6, .3, 1).</summary>
    public static readonly CubicBezierEasing RingIn = new(0.2, 0.6, 0.3, 1.0);
}
