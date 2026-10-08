using System.Windows;
using System.Windows.Media;

namespace HAMMOR.App.Presence;

/// <summary>
/// The Living Core's retained visual tree: nine layers drawn once, in design
/// units, then moved every frame only through transforms, opacity, offsets
/// and dash offsets.
/// </summary>
/// <remarks>
/// <para>
/// Every geometry and brush is created and frozen here, at construction, and
/// every pen except the five whose dashes move (two energy lines, two lateral
/// lines, and the rim line Wake draws around). <see cref="Apply"/>
/// writes values into existing transforms, visual offsets and opacities, and
/// skips any value that did not change, so a frame allocates nothing beyond
/// the boxing WPF itself does for dependency-property doubles.
/// </para>
/// <para>
/// Everything from the halo inward sits in one body container, as in the
/// approved prototype, so Sleep can sink, shrink and dim the core as one. The
/// aura, energy lines and state rings stay outside it.
/// </para>
/// <para>
/// No live blur: the design's blurred glows (inner halo ring, thread heads,
/// paused threads) are rebuilt as soft gradients and wide translucent strokes,
/// which render at the same cost as any other fill.
/// </para>
/// </remarks>
internal sealed class LivingCoreScene
{
    // ---- Sovereign Deep, Abyss Cyan ----
    private static readonly Color Accent = Color.FromRgb(0x3E, 0xD0, 0xC8);
    private static readonly Color GlowTone = Color.FromRgb(0x14, 0x70, 0x78);
    private static readonly Color Spot = Color.FromRgb(0xE6, 0x9F, 0x68);
    private static readonly Color Deep = Color.FromRgb(0x0B, 0x29, 0x2C);
    private static readonly Color Pearl = Color.FromRgb(0xF3, 0xEF, 0xE6);
    private static readonly Color Danger = Color.FromRgb(0xED, 0x53, 0x50);
    private static readonly Color White = Color.FromRgb(0xFF, 0xFF, 0xFF);

    // ---- State tones of the approved prototype ----
    private static readonly Color SuccessTone = Color.FromRgb(0x6A, 0xD8, 0x95);
    private static readonly Color WarningTone = Color.FromRgb(0xE9, 0x9B, 0x2A);
    private static readonly Color EmberTone = Color.FromRgb(0xE9, 0xC9, 0xA8);
    private static readonly Color WarmTone = Color.FromRgb(0x9B, 0x6A, 0x4A);

    private const double Energy1Width = 0.45;
    private const double Energy2Width = 0.4;
    private const double LateralWidth = 0.6;
    private const double RimWidth = 0.7;
    private const double RimLength = 2.0 * Math.PI * LivingCoreDesign.MembraneRadius;

    private readonly ContainerVisual _root = new();
    private readonly MatrixTransform _rootTransform = new();

    // 0 · Aura and the Success bloom; both lean toward an opening panel.
    private readonly ContainerVisual _auraGroup = new();
    private readonly DrawingVisual _aura;
    private readonly ScaleTransform _auraScale = Centred(1.0);
    private readonly DrawingVisual _warm;
    private readonly ScaleTransform _warmScale = Centred(1.0);

    private readonly DrawingVisual _energy1;
    private readonly DrawingVisual _energy2;
    private readonly RotateTransform _energy1Rotate = Rotation();
    private readonly RotateTransform _energy2Rotate = Rotation();
    private readonly DashStyle _energy1Dash = new([14.0 / Energy1Width, 6.0 / Energy1Width], 0.0);
    private readonly DashStyle _energy2Dash = new([22.0 / Energy2Width, 10.0 / Energy2Width], 0.0);

    private readonly DrawingVisual[] _inward = new DrawingVisual[3];
    private readonly ScaleTransform[] _inwardScale = [Centred(1.0), Centred(1.0), Centred(1.0)];
    private readonly DrawingVisual[] _outward = new DrawingVisual[3];
    private readonly ScaleTransform[] _outwardScale = [Centred(1.0), Centred(1.0), Centred(1.0)];
    private readonly DrawingVisual _successRing;
    private readonly ScaleTransform _successRingScale = Centred(1.0);
    private readonly DrawingVisual _gapRing;
    private readonly DrawingVisual _warningMark;

    // The body: everything from the halo inward.
    private readonly ContainerVisual _body = new();
    private readonly ScaleTransform _bodyScale = Centred(1.0);
    private readonly TranslateTransform _bodyShift = new();

    private readonly DrawingVisual[] _halo = new DrawingVisual[3];
    private readonly ScaleTransform[] _haloScale = [Centred(1.0), Centred(1.0), Centred(1.0)];
    private readonly DrawingVisual _haloDanger;
    private readonly DrawingVisual _haloSuccess;
    private readonly DrawingVisual _haloWarning;
    private readonly ScaleTransform _haloToneScale = Centred(1.0);

    private readonly ContainerVisual _threads = new();
    private readonly ScaleTransform _threadsScale = Centred(1.0);
    private readonly RotateTransform[] _threadRotate = [Rotation(), Rotation(), Rotation(), Rotation()];
    private readonly DrawingVisual _park;
    private readonly ContainerVisual _fragments = new();
    private readonly RotateTransform _fragment1Rotate = Rotation();
    private readonly RotateTransform _fragment2Rotate = Rotation();

    private readonly DrawingVisual _rimAccent;
    private readonly DrawingVisual _rimAccentLine;
    private readonly DashStyle _rimDash = new([RimLength / RimWidth, RimLength / RimWidth], 0.0);
    private readonly DrawingVisual _rimDanger;
    private readonly DrawingVisual _rimSuccess;
    private readonly DrawingVisual _rimWarning;
    private readonly DrawingVisual _crack;

    private readonly ContainerVisual _stars = new();
    private readonly ScaleTransform _starsScale = Centred(1.0);
    private readonly RotateTransform _starsRotate = Rotation();
    private readonly DrawingVisual[] _starClock = new DrawingVisual[4];

    private readonly ContainerVisual _cellsWrap = new();
    private readonly ScaleTransform _cellsSpread = new(1.0, 1.0, LivingCoreLooks.ZoneX, LivingCoreLooks.ZoneY);
    private readonly TranslateTransform _cellsDrop = new();
    private readonly ContainerVisual _cellsTurn = new();
    private readonly RotateTransform _cellsRotate = Rotation();
    private readonly DrawingVisual _links;
    private readonly DrawingVisual[] _signals = new DrawingVisual[6];
    private readonly DrawingVisual[] _bob = new DrawingVisual[3];

    private readonly ContainerVisual _orbits = new();
    private readonly RotateTransform _orbit1Rotate = Rotation();
    private readonly RotateTransform _orbit2Rotate = Rotation();

    private readonly DrawingVisual _lateralA;
    private readonly DrawingVisual _lateralB;
    private readonly DashStyle _lateralADash = new([1.6 / LateralWidth, 2.0 / LateralWidth], 0.0);
    private readonly DashStyle _lateralBDash = new([1.6 / LateralWidth, 2.0 / LateralWidth], 0.0);

    private readonly ContainerVisual _lens = new();
    private readonly ScaleTransform _lensScale = Centred(1.0);
    private readonly RotateTransform _lensRotate = Rotation();
    private readonly RotateTransform _crestRotate = Rotation();
    private readonly TranslateTransform _crestShift = new();

    private readonly DrawingVisual _voiceWave;
    private readonly ScaleTransform _voiceWaveScale = new(1.0, 1.0, 60.0, 92.0);

    private readonly DrawingVisual _ripple;
    private readonly ScaleTransform _rippleScale = Centred(1.0);

    private readonly ContainerVisual _core = new();
    private readonly DrawingVisual _attention;
    private readonly ContainerVisual _coreScaled = new();
    private readonly ScaleTransform _coreScale = new(1.0, 1.0, 0.0, 0.0);
    private readonly DrawingVisual _glow;
    private readonly ScaleTransform _glowScale = new(1.0, 1.0, 0.0, 0.0);
    private readonly DrawingVisual _speakRing;
    private readonly DrawingVisual _coreDot;
    private readonly DrawingVisual _ember;

    private Size _size;
    private bool _mirrored;

    public LivingCoreScene()
    {
        _root.Transform = _rootTransform;

        // ---- 0 · Aura, and the warm bloom of Success; they lean together ----
        _aura = Draw(dc => dc.DrawEllipse(AuraBrush(), null, CentrePoint, 100.0, 100.0));
        _aura.Transform = _auraScale;
        Add(_auraGroup, _aura);
        _warm = Draw(dc => dc.DrawEllipse(WarmBrush(), null, CentrePoint, 96.0, 96.0));
        _warm.Transform = _warmScale;
        Hide(_warm);
        Add(_auraGroup, _warm);
        Add(_root, _auraGroup);

        // ---- 1 · Energy lines: turn slowly while their dashes flow ----
        _energy2 = Draw(dc => dc.DrawGeometry(null, FlowPen(Accent, 0.10, Energy2Width, _energy2Dash), Polygon(LivingCoreDesign.EnergyLine2)));
        _energy2.Transform = _energy2Rotate;
        Add(_root, _energy2);
        _energy1 = Draw(dc => dc.DrawGeometry(null, FlowPen(Accent, 0.16, Energy1Width, _energy1Dash), Polygon(LivingCoreDesign.EnergyLine1)));
        _energy1.Transform = _energy1Rotate;
        Add(_root, _energy1);

        // ---- Rings that only appear in a state ----
        double[] inwardLight = [0.3, 0.45, 0.6];
        double[] outwardLight = [0.6, 0.45, 0.3];
        for (var k = 0; k < 3; k++)
        {
            var light = inwardLight[k];
            _inward[k] = Draw(dc => dc.DrawEllipse(null, PenOf(Accent, light, 0.8), CentrePoint, 56.0, 56.0));
            _inward[k].Transform = _inwardScale[k];
            Hide(_inward[k]);
            Add(_root, _inward[k]);
        }

        for (var k = 0; k < 3; k++)
        {
            var light = outwardLight[k];
            _outward[k] = Draw(dc => dc.DrawEllipse(null, PenOf(Accent, light, 0.8), CentrePoint, 54.0, 54.0));
            _outward[k].Transform = _outwardScale[k];
            Hide(_outward[k]);
            Add(_root, _outward[k]);
        }

        // Success: one clean ring expands and dissolves.
        _successRing = Draw(dc => dc.DrawEllipse(null, PenOf(SuccessTone, 1.0, 1.2), CentrePoint, 56.0, 56.0));
        _successRing.Transform = _successRingScale;
        Hide(_successRing);
        Add(_root, _successRing);

        // Blocked: the territory ring opens a 24-unit gap at the top, where
        // the work paused.
        _gapRing = Draw(dc => dc.DrawGeometry(null, PenOf(Pearl, 0.3, 0.8), Arc(62.0, 304.2, (2.0 * Math.PI * 62.0) - 24.0)));
        Hide(_gapRing);
        Add(_root, _gapRing);

        // Warning: one amber segment marks the concern, up and forward.
        _warningMark = Draw(dc => dc.DrawGeometry(null, PenOf(WarningTone, 1.0, 2.2), Parse("M 91,6.3 A 62,62 0 0 1 117.6,37.2")));
        Hide(_warningMark);
        Add(_root, _warningMark);

        // ---- The body: Sleep sinks and dims everything from here inward ----
        var bodyTransform = new TransformGroup();
        bodyTransform.Children.Add(_bodyScale);
        bodyTransform.Children.Add(_bodyShift);
        _body.Transform = bodyTransform;

        // ---- 2 · Halo rings: inner glowing, a third of a cycle apart ----
        _halo[2] = Draw(dc => dc.DrawEllipse(null, PenOf(Pearl, 0.07, 0.35), CentrePoint, 66.5, 66.5));
        _halo[1] = Draw(dc => dc.DrawEllipse(null, PenOf(Accent, 0.14, 0.4), CentrePoint, 61.5, 61.5));
        _halo[0] = Draw(dc => DrawInnerHalo(dc, Accent));
        for (var i = 2; i >= 0; i--)
        {
            _halo[i].Transform = _haloScale[i];
            Add(_body, _halo[i]);
        }

        // The inner ring in each state tone, crossfaded over the accent one.
        _haloDanger = Draw(dc => DrawInnerHalo(dc, Danger));
        _haloSuccess = Draw(dc => DrawInnerHalo(dc, SuccessTone));
        _haloWarning = Draw(dc => DrawInnerHalo(dc, WarningTone));
        foreach (var tone in new[] { _haloDanger, _haloSuccess, _haloWarning })
        {
            tone.Transform = _haloToneScale;
            Hide(tone);
            Add(_body, tone);
        }

        // ---- 3 · Light threads ----
        _threads.Transform = _threadsScale;
        for (var i = LivingCoreDesign.Threads.Length - 1; i >= 0; i--)
        {
            var spec = LivingCoreDesign.Threads[i];
            var thread = Draw(dc => DrawThread(dc, spec));
            thread.Transform = _threadRotate[i];
            Add(_threads, thread);
        }

        Add(_body, _threads);

        // Blocked: threads gathered at the top.
        _park = Draw(dc =>
        {
            dc.DrawGeometry(null, PenOf(Pearl, 0.25, 3.2), Parse("M 49.1,5.9 A 55,55 0 0 1 70.9,5.9"));
            dc.DrawGeometry(null, PenOf(White, 1.0, 1.0), Parse("M 49.1,5.9 A 55,55 0 0 1 70.9,5.9"));
            dc.DrawGeometry(null, PenOf(Pearl, 0.45, 0.8), Parse("M 44,2.9 A 59,59 0 0 1 76,2.9"));
        });
        Hide(_park);
        Add(_body, _park);

        // Error: threads broken into slow fragments.
        var fragment1 = Draw(dc => dc.DrawEllipse(null, DashedPen(Accent, 0.35, 0.8, [6, 9, 3, 14, 8, 22, 2, 30, 5, 41, 4, 60, 10, 66.7]), CentrePoint, 59.0, 59.0));
        fragment1.Transform = _fragment1Rotate;
        var fragment2 = Draw(dc => dc.DrawEllipse(null, DashedPen(Accent, 0.18, 0.7, [4, 18, 7, 35, 3, 52, 9, 70, 2, 102.1]), CentrePoint, 64.0, 64.0));
        fragment2.Transform = _fragment2Rotate;
        Add(_fragments, fragment1);
        Add(_fragments, fragment2);
        Hide(_fragments);
        Add(_body, _fragments);

        // ---- 4 · Membrane: dark glass, lit rim, one faint highlight ----
        Add(_body, Draw(dc => dc.DrawEllipse(BodyBrush(), null, CentrePoint, LivingCoreDesign.MembraneRadius, LivingCoreDesign.MembraneRadius)));

        // The accent rim's line has its own pen so Wake can draw it around.
        _rimAccent = Draw(dc => DrawRimGlow(dc, Accent));
        _rimAccentLine = Draw(dc => dc.DrawEllipse(null, RimDrawPen(), CentrePoint, LivingCoreDesign.MembraneRadius, LivingCoreDesign.MembraneRadius));
        _rimDanger = Draw(dc => DrawRim(dc, Danger));
        _rimSuccess = Draw(dc => DrawRim(dc, SuccessTone));
        _rimWarning = Draw(dc => DrawRim(dc, WarningTone));
        Hide(_rimDanger);
        Hide(_rimSuccess);
        Hide(_rimWarning);
        Add(_body, _rimAccent);
        Add(_body, _rimAccentLine);
        Add(_body, _rimDanger);
        Add(_body, _rimSuccess);
        Add(_body, _rimWarning);
        Add(_body, Draw(dc => dc.DrawGeometry(null, PenOf(White, 0.07, 2.2), Parse("M 16.77,44.27 A 46,46 0 0 1 44.27,16.77"))));

        // Error: a restrained hairline split.
        _crack = Draw(dc => dc.DrawGeometry(null, PenOf(Danger, 1.0, 0.8), Parse("M 94.8,25.2 L 89,33 L 92,36 L 85,44")));
        Hide(_crack);
        Add(_body, _crack);

        // ---- 5 · Deep stars: one visual per shimmer clock ----
        var starTransform = new TransformGroup();
        starTransform.Children.Add(_starsScale);
        starTransform.Children.Add(_starsRotate);
        _stars.Transform = starTransform;
        for (var k = 0; k < _starClock.Length; k++)
        {
            var clock = k;
            _starClock[k] = Draw(dc =>
            {
                foreach (var star in LivingCoreDesign.Stars)
                {
                    if (star.Clock == clock)
                    {
                        dc.DrawEllipse(Solid(White, star.Opacity), null, new Point(star.X, star.Y), star.Radius, star.Radius);
                    }
                }
            });
            Add(_stars, _starClock[k]);
        }

        Add(_body, _stars);

        // ---- 6 · Floating cells, their Thinking links and signals ----
        var cellsTransform = new TransformGroup();
        cellsTransform.Children.Add(_cellsSpread);
        cellsTransform.Children.Add(_cellsDrop);
        _cellsWrap.Transform = cellsTransform;
        _cellsTurn.Transform = _cellsRotate;
        _links = Draw(dc =>
        {
            var pen = PenOf(Accent, 0.3, 0.4);
            foreach (var link in LivingCoreDesign.Links)
            {
                dc.DrawLine(pen, new Point(link.X1, link.Y1), new Point(link.X2, link.Y2));
            }
        });
        Hide(_links);
        Add(_cellsTurn, _links);

        var signalBrush = Solid(White, 1.0);
        for (var s = 0; s < _signals.Length; s++)
        {
            _signals[s] = Draw(dc => dc.DrawEllipse(signalBrush, null, new Point(0.0, 0.0), 0.65, 0.65));
            Hide(_signals[s]);
            Add(_cellsTurn, _signals[s]);
        }

        for (var j = 0; j < _bob.Length; j++)
        {
            var clock = j;
            _bob[j] = Draw(dc =>
            {
                foreach (var cell in LivingCoreDesign.Cells)
                {
                    if (cell.Clock == clock)
                    {
                        dc.DrawEllipse(Solid(ToneOf(cell.Tone), cell.Opacity), null, new Point(cell.X, cell.Y), cell.Radius, cell.Radius);
                    }
                }
            });
            Add(_cellsTurn, _bob[j]);
        }

        Add(_cellsWrap, _cellsTurn);
        Add(_body, _cellsWrap);

        // Thinking: orbit lanes.
        var orbit1 = Draw(dc => dc.DrawGeometry(null, DashedPen(Accent, 0.35, 0.5, [1, 2]), Ellipse(40.0, 14.0, -18.0)));
        orbit1.Transform = _orbit1Rotate;
        var orbit2 = Draw(dc => dc.DrawGeometry(null, DashedPen(Accent, 0.28, 0.5, [1, 2]), Ellipse(32.0, 11.0, 36.0)));
        orbit2.Transform = _orbit2Rotate;
        Add(_orbits, orbit1);
        Add(_orbits, orbit2);
        Hide(_orbits);
        Add(_body, _orbits);

        // ---- 7 · Lens current: lateral lines flow, the crest sways ----
        _lateralA = Draw(dc => dc.DrawGeometry(null, FlowPen(Accent, 1.0, LateralWidth, _lateralADash, round: true), Parse("M 28.2,62.1 A 37.5,37.5 0 0 1 78.75,49.5")));
        _lateralB = Draw(dc => dc.DrawGeometry(null, FlowPen(Accent, 1.0, LateralWidth, _lateralBDash, round: true), Parse("M 35.5,61.4 A 32,32 0 0 1 72,52.3")));
        Add(_body, _lateralA);
        Add(_body, _lateralB);

        // Scale first (Warning narrows it, Wake sweeps it in), then the turn.
        var lensTransform = new TransformGroup();
        lensTransform.Children.Add(_lensScale);
        lensTransform.Children.Add(_lensRotate);
        _lens.Transform = lensTransform;
        var crest = Draw(dc => dc.DrawGeometry(CrestBrush(), null, Parse("M 21.9,60 A 44,44 0 0 1 98.1,60 A 51.19,51.19 0 0 0 21.9,60 Z")));
        var crestTransform = new TransformGroup();
        crestTransform.Children.Add(_crestRotate);
        crestTransform.Children.Add(_crestShift);
        crest.Transform = crestTransform;
        Add(_lens, crest);
        Add(_lens, Draw(dc =>
        {
            dc.DrawGeometry(null, PenOf(Accent, 0.65, 1.3), Parse("M 98.1,60 A 44,44 0 0 1 26,65.93"));

            // The wake leaving the open rear.
            dc.DrawEllipse(Solid(Accent, 0.55), null, new Point(18.0, 64.5), 0.8, 0.8);
            dc.DrawEllipse(Solid(Accent, 0.38), null, new Point(14.2, 66.0), 0.6, 0.6);
        }));
        Add(_body, _lens);

        // Speaking: the voice along the lower membrane.
        _voiceWave = Draw(dc => dc.DrawGeometry(null, PenOf(Accent, 1.0, 1.0), Parse(
            "M 30,92 Q 33.75,89 37.5,92 Q 41.25,95 45,92 Q 48.75,86 52.5,92 Q 56.25,98 60,92 Q 63.75,85 67.5,92 Q 71.25,99 75,92 Q 78.75,88 82.5,92 Q 86.25,95 90,92")));
        _voiceWave.Transform = _voiceWaveScale;
        Hide(_voiceWave);
        Add(_body, _voiceWave);

        // A new message: one soft ripple leaves the membrane.
        _ripple = Draw(dc => dc.DrawEllipse(null, PenOf(Accent, 1.0, 1.0), CentrePoint, LivingCoreDesign.MembraneRadius, LivingCoreDesign.MembraneRadius));
        _ripple.Transform = _rippleScale;
        Hide(_ripple);
        Add(_body, _ripple);

        // ---- 8 · White core: the nearest layer and the only white ----
        _attention = Draw(dc => dc.DrawEllipse(null, PenOf(Pearl, 0.8, 0.7), new Point(0.0, 0.0), 12.5, 12.5));
        Hide(_attention);
        _glow = Draw(dc => dc.DrawEllipse(PupilGlowBrush(), null, new Point(0.0, 0.0), 24.0, 24.0));
        _glow.Transform = _glowScale;
        _speakRing = Draw(dc => dc.DrawEllipse(null, PenOf(Pearl, 0.4, 0.6), new Point(0.0, 0.0), 10.5, 10.5));
        Hide(_speakRing);
        _coreDot = Draw(dc => dc.DrawEllipse(CoreBrush(), null, new Point(0.0, 0.0), 7.0, 7.0));

        // Sleep: a dim warm ember over the white, crossfaded.
        _ember = Draw(dc => dc.DrawEllipse(Solid(EmberTone, 1.0), null, new Point(0.0, 0.0), 7.0, 7.0));
        Hide(_ember);

        _coreScaled.Transform = _coreScale;
        Add(_coreScaled, _glow);
        Add(_coreScaled, _speakRing);
        Add(_coreScaled, _coreDot);
        Add(_coreScaled, _ember);
        Add(_core, _attention);
        Add(_core, _coreScaled);
        _core.Offset = new Vector(LivingCoreLooks.RestX, LivingCoreLooks.RestY);
        Add(_body, _core);

        Add(_root, _body);
    }

    /// <summary>The single visual the control hosts.</summary>
    public Visual Root => _root;

    private static Point CentrePoint => new(LivingCoreDesign.Centre, LivingCoreDesign.Centre);

    /// <summary>
    /// Fits the 200-unit drawing into <paramref name="size"/>, centred. When
    /// <paramref name="mirrored"/> is set the drawing is flipped back, because
    /// a right-to-left layout mirrors everything under it and the mark is
    /// never mirrored.
    /// </summary>
    public void Layout(Size size, bool mirrored)
    {
        if (size == _size && mirrored == _mirrored)
        {
            return;
        }

        _size = size;
        _mirrored = mirrored;

        var side = Math.Min(size.Width, size.Height);
        if (double.IsNaN(side) || double.IsInfinity(side) || side <= 0.0)
        {
            return;
        }

        var scale = side / LivingCoreDesign.ViewSize;
        var offsetX = ((size.Width - side) / 2.0) - (LivingCoreDesign.ViewMin * scale);
        var offsetY = ((size.Height - side) / 2.0) - (LivingCoreDesign.ViewMin * scale);

        _rootTransform.Matrix = mirrored
            ? new Matrix(-scale, 0.0, 0.0, scale, size.Width - offsetX, offsetY)
            : new Matrix(scale, 0.0, 0.0, scale, offsetX, offsetY);
    }

    /// <summary>Moves the retained layers to <paramref name="frame"/>.</summary>
    public void Apply(LivingCoreFrame frame)
    {
        // 0 · Aura and the warm bloom
        SetOffset(_auraGroup, frame.AuraLeanX, 0.0);
        SetOpacity(_aura, frame.AuraOpacity);
        SetScale(_auraScale, frame.AuraScale);
        SetOpacity(_warm, frame.WarmOpacity);
        if (frame.WarmOpacity > 0.0)
        {
            SetScale(_warmScale, frame.WarmScale);
        }

        // 1 · Energy lines
        SetOpacity(_energy1, frame.EnergyOpacity);
        SetOpacity(_energy2, frame.EnergyOpacity);
        if (frame.EnergyOpacity > 0.0)
        {
            SetAngle(_energy1Rotate, frame.Energy1Angle);
            SetAngle(_energy2Rotate, frame.Energy2Angle);
            SetDashOffset(_energy1Dash, frame.Energy1Flow / Energy1Width);
            SetDashOffset(_energy2Dash, frame.Energy2Flow / Energy2Width);
        }

        // State rings
        for (var k = 0; k < 3; k++)
        {
            SetOpacity(_inward[k], frame.InwardOpacity[k]);
            if (frame.InwardOpacity[k] > 0.0)
            {
                SetScale(_inwardScale[k], frame.InwardScale[k]);
            }

            SetOpacity(_outward[k], frame.OutwardOpacity[k]);
            if (frame.OutwardOpacity[k] > 0.0)
            {
                SetScale(_outwardScale[k], frame.OutwardScale[k]);
            }
        }

        SetOpacity(_successRing, frame.SuccessRingOpacity);
        if (frame.SuccessRingOpacity > 0.0)
        {
            SetScale(_successRingScale, frame.SuccessRingScale);
        }

        SetOpacity(_gapRing, frame.GapRingOpacity);
        SetOpacity(_warningMark, frame.WarningMarkOpacity);

        // The body
        SetScale(_bodyScale, frame.BodyScale);
        SetTranslate(_bodyShift, 0.0, frame.BodyY);
        SetOpacity(_body, frame.BodyOpacity);

        // 2 · Halo rings; the inner ring crossfades to the state tones
        var toned = frame.RimDanger + frame.RimSuccess + frame.RimWarning;
        var accentShare = Math.Clamp(1.0 - toned, 0.0, 1.0);
        for (var i = 0; i < 3; i++)
        {
            var light = i == 0 ? frame.HaloOpacity[0] * accentShare : frame.HaloOpacity[i];
            SetOpacity(_halo[i], light);
            SetScale(_haloScale[i], frame.HaloScale[i]);
        }

        SetOpacity(_haloDanger, frame.HaloOpacity[0] * frame.RimDanger);
        SetOpacity(_haloSuccess, frame.HaloOpacity[0] * frame.RimSuccess);
        SetOpacity(_haloWarning, frame.HaloOpacity[0] * frame.RimWarning);
        if (toned > 0.0)
        {
            SetScale(_haloToneScale, frame.HaloScale[0]);
        }

        // 3 · Threads and their state forms
        SetOpacity(_threads, frame.ThreadsOpacity);
        if (frame.ThreadsOpacity > 0.0)
        {
            SetScale(_threadsScale, frame.ThreadsScale);
            for (var i = 0; i < _threadRotate.Length; i++)
            {
                SetAngle(_threadRotate[i], frame.ThreadAngle[i]);
            }
        }

        SetOpacity(_park, frame.ParkOpacity);
        SetOpacity(_fragments, frame.FragmentOpacity);
        if (frame.FragmentOpacity > 0.0)
        {
            SetAngle(_fragment1Rotate, frame.Fragment1Angle);
            SetAngle(_fragment2Rotate, frame.Fragment2Angle);
        }

        // 4 · Membrane: the rim in its tone; Wake draws the line around
        SetOpacity(_rimAccent, accentShare);
        SetOpacity(_rimAccentLine, accentShare);
        SetDashOffset(_rimDash, (1.0 - frame.RimDrawn) * RimLength / RimWidth);
        SetOpacity(_rimDanger, frame.RimDanger);
        SetOpacity(_rimSuccess, frame.RimSuccess);
        SetOpacity(_rimWarning, frame.RimWarning);
        SetOpacity(_crack, frame.CrackOpacity);

        // 5 · Deep stars
        SetOffset(_stars, frame.StarsShiftX, frame.StarsShiftY);
        SetScale(_starsScale, frame.StarsScale);
        SetAngle(_starsRotate, frame.StarsAngle);
        for (var k = 0; k < _starClock.Length; k++)
        {
            SetOpacity(_starClock[k], frame.StarClockOpacity[k]);
        }

        // 6 · Floating cells
        SetOpacity(_cellsWrap, frame.CellsOpacity);
        SetOffset(_cellsWrap, frame.CellsShiftX, frame.CellsShiftY);
        SetScaleXY(_cellsSpread, frame.CellsSpread, frame.CellsScaleY);
        SetTranslate(_cellsDrop, 0.0, frame.CellsY);
        SetAngle(_cellsRotate, frame.CellsAngle);
        for (var j = 0; j < _bob.Length; j++)
        {
            SetOffset(_bob[j], frame.BobX[j], frame.BobY[j]);
        }

        SetOpacity(_links, frame.LinkOpacity);
        for (var s = 0; s < _signals.Length; s++)
        {
            SetOpacity(_signals[s], frame.SignalOpacity[s]);
            if (frame.SignalOpacity[s] > 0.0)
            {
                SetOffset(_signals[s], frame.SignalX[s], frame.SignalY[s]);
            }
        }

        SetOpacity(_orbits, frame.OrbitOpacity);
        if (frame.OrbitOpacity > 0.0)
        {
            SetAngle(_orbit1Rotate, frame.Orbit1Angle);
            SetAngle(_orbit2Rotate, frame.Orbit2Angle);
        }

        // 7 · Lens current
        SetOpacity(_lateralA, frame.LateralAOpacity);
        SetOpacity(_lateralB, frame.LateralBOpacity);
        if (frame.LateralAOpacity > 0.0 || frame.LateralBOpacity > 0.0)
        {
            SetDashOffset(_lateralADash, frame.LateralFlow / LateralWidth);
            SetDashOffset(_lateralBDash, frame.LateralFlow / LateralWidth);
        }

        SetOpacity(_lens, frame.LensOpacity);
        SetScaleXY(_lensScale, frame.LensScaleX, frame.LensScaleY);
        SetAngle(_lensRotate, frame.LensAngle);
        SetAngle(_crestRotate, frame.CrestAngle);
        SetTranslate(_crestShift, 0.0, frame.CrestY);
        SetOpacity(_voiceWave, frame.VoiceWaveOpacity);
        if (frame.VoiceWaveOpacity > 0.0)
        {
            SetScaleY(_voiceWaveScale, frame.VoiceWaveScaleY);
        }

        SetOpacity(_ripple, frame.RippleOpacity);
        if (frame.RippleOpacity > 0.0)
        {
            SetScale(_rippleScale, frame.RippleScale);
        }

        // 8 · White core
        SetOffset(_core, frame.CoreX, frame.CoreY);
        SetScale(_coreScale, frame.CoreScale);
        SetOpacity(_coreDot, frame.CoreOpacity);
        SetOpacity(_ember, frame.EmberOpacity);
        SetOpacity(_glow, frame.GlowOpacity);
        SetScale(_glowScale, frame.GlowScale);
        SetOpacity(_speakRing, frame.SpeakRingOpacity);
        SetOpacity(_attention, frame.AttentionOpacity);
    }

    // ---- Per-frame setters: skip unchanged values ----

    private static void SetOpacity(ContainerVisual visual, double value)
    {
        if (Math.Abs(visual.Opacity - value) > 0.002)
        {
            visual.Opacity = value;
        }
    }

    private static void SetOffset(ContainerVisual visual, double x, double y)
    {
        var current = visual.Offset;
        if (Math.Abs(current.X - x) > 0.001 || Math.Abs(current.Y - y) > 0.001)
        {
            visual.Offset = new Vector(x, y);
        }
    }

    private static void SetAngle(RotateTransform transform, double angle)
    {
        if (Math.Abs(transform.Angle - angle) > 0.0005)
        {
            transform.Angle = angle;
        }
    }

    private static void SetScale(ScaleTransform transform, double scale)
    {
        if (Math.Abs(transform.ScaleX - scale) > 0.00005 || Math.Abs(transform.ScaleY - scale) > 0.00005)
        {
            transform.ScaleX = scale;
            transform.ScaleY = scale;
        }
    }

    private static void SetScaleXY(ScaleTransform transform, double scaleX, double scaleY)
    {
        if (Math.Abs(transform.ScaleX - scaleX) > 0.00005)
        {
            transform.ScaleX = scaleX;
        }

        if (Math.Abs(transform.ScaleY - scaleY) > 0.00005)
        {
            transform.ScaleY = scaleY;
        }
    }

    private static void SetScaleY(ScaleTransform transform, double scale)
    {
        if (Math.Abs(transform.ScaleY - scale) > 0.0005)
        {
            transform.ScaleY = scale;
        }
    }

    private static void SetTranslate(TranslateTransform transform, double x, double y)
    {
        if (Math.Abs(transform.X - x) > 0.001)
        {
            transform.X = x;
        }

        if (Math.Abs(transform.Y - y) > 0.001)
        {
            transform.Y = y;
        }
    }

    private static void SetDashOffset(DashStyle dash, double offset)
    {
        if (Math.Abs(dash.Offset - offset) > 0.001)
        {
            dash.Offset = offset;
        }
    }

    // ---- Construction helpers (run once) ----

    private static DrawingVisual Draw(Action<DrawingContext> render)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            render(dc);
        }

        return visual;
    }

    private static void Add(ContainerVisual parent, Visual child) => parent.Children.Add(child);

    private static void Hide(ContainerVisual visual) => visual.Opacity = 0.0;

    private static ScaleTransform Centred(double scale) =>
        new(scale, scale, LivingCoreDesign.Centre, LivingCoreDesign.Centre);

    private static RotateTransform Rotation() =>
        new(0.0, LivingCoreDesign.Centre, LivingCoreDesign.Centre);

    private static void DrawInnerHalo(DrawingContext dc, Color tone)
    {
        // The board's blurred glow ring (2.4 wide, σ 1.4) as a soft band.
        var band = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = CentrePoint,
            GradientOrigin = CentrePoint,
            RadiusX = 60.0,
            RadiusY = 60.0,
        };
        band.GradientStops.Add(new GradientStop(WithAlpha(tone, 0.0), 0.88));
        band.GradientStops.Add(new GradientStop(WithAlpha(tone, 0.16), 56.0 / 60.0));
        band.GradientStops.Add(new GradientStop(WithAlpha(tone, 0.0), 59.2 / 60.0));
        band.Freeze();

        dc.DrawEllipse(band, null, CentrePoint, 60.0, 60.0);
        dc.DrawEllipse(null, PenOf(tone, 0.38, 0.55), CentrePoint, 56.0, 56.0);
    }

    private static void DrawRim(DrawingContext dc, Color tone)
    {
        DrawRimGlow(dc, tone);
        dc.DrawEllipse(null, PenOf(tone, 0.55, RimWidth), CentrePoint, LivingCoreDesign.MembraneRadius, LivingCoreDesign.MembraneRadius);
    }

    private static void DrawRimGlow(DrawingContext dc, Color tone)
    {
        var rim = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = CentrePoint,
            GradientOrigin = CentrePoint,
            RadiusX = LivingCoreDesign.MembraneRadius,
            RadiusY = LivingCoreDesign.MembraneRadius,
        };
        rim.GradientStops.Add(new GradientStop(WithAlpha(tone, 0.0), 0.8));
        rim.GradientStops.Add(new GradientStop(WithAlpha(tone, 0.18), 0.96));
        rim.GradientStops.Add(new GradientStop(WithAlpha(tone, 0.5), 1.0));
        rim.Freeze();

        dc.DrawEllipse(rim, null, CentrePoint, LivingCoreDesign.MembraneRadius, LivingCoreDesign.MembraneRadius);
    }

    private static void DrawThread(DrawingContext dc, ThreadSpec spec)
    {
        DrawSpan(dc, spec.Radius, spec.Tail, Accent);
        DrawSpan(dc, spec.Radius, spec.Mid, Accent);

        if (spec.GlowOpacity > 0.0)
        {
            // The blurred head glow as a soft dot on the head's centre.
            var middle = (spec.Head.Start + (spec.Head.Length / 2.0)) / spec.Radius;
            var centre = new Point(
                LivingCoreDesign.Centre + (spec.Radius * Math.Cos(middle)),
                LivingCoreDesign.Centre + (spec.Radius * Math.Sin(middle)));
            var radius = spec.GlowWidth * 1.8;
            var glow = new RadialGradientBrush();
            glow.GradientStops.Add(new GradientStop(WithAlpha(Accent, spec.GlowOpacity * 0.55), 0.0));
            glow.GradientStops.Add(new GradientStop(WithAlpha(Accent, 0.0), 1.0));
            glow.Freeze();
            dc.DrawEllipse(glow, null, centre, radius, radius);
        }

        DrawSpan(dc, spec.Radius, spec.Head, spec.HeadIsWhite ? White : Accent);
    }

    private static void DrawSpan(DrawingContext dc, double radius, ArcSpan span, Color tone) =>
        dc.DrawGeometry(null, PenOf(tone, span.Opacity, span.Width), Arc(radius, span.Start, span.Length));

    /// <summary>
    /// An arc of the circle at the centre, starting <paramref name="start"/>
    /// units clockwise from 3 o'clock, as SVG dash patterns measure it.
    /// </summary>
    private static Geometry Arc(double radius, double start, double length)
    {
        var a0 = start / radius;
        var a1 = (start + length) / radius;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(
                new Point(LivingCoreDesign.Centre + (radius * Math.Cos(a0)), LivingCoreDesign.Centre + (radius * Math.Sin(a0))),
                false,
                false);
            context.ArcTo(
                new Point(LivingCoreDesign.Centre + (radius * Math.Cos(a1)), LivingCoreDesign.Centre + (radius * Math.Sin(a1))),
                new Size(radius, radius),
                0.0,
                (a1 - a0) > Math.PI,
                SweepDirection.Clockwise,
                true,
                false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Geometry Polygon(double[] coordinates)
    {
        var points = new List<Point>(coordinates.Length / 2);
        for (var i = 2; i < coordinates.Length; i += 2)
        {
            points.Add(new Point(coordinates[i], coordinates[i + 1]));
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(coordinates[0], coordinates[1]), false, true);
            context.PolyLineTo(points, true, true);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Geometry Ellipse(double radiusX, double radiusY, double angle)
    {
        var geometry = new EllipseGeometry(CentrePoint, radiusX, radiusY)
        {
            Transform = new RotateTransform(angle, LivingCoreDesign.Centre, LivingCoreDesign.Centre),
        };
        geometry.Freeze();
        return geometry;
    }

    private static Geometry Parse(string data)
    {
        var geometry = Geometry.Parse(data);
        if (geometry.CanFreeze)
        {
            geometry.Freeze();
        }

        return geometry;
    }

    private static Brush AuraBrush()
    {
        var brush = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = CentrePoint,
            GradientOrigin = CentrePoint,
            RadiusX = 100.0,
            RadiusY = 100.0,
        };
        brush.GradientStops.Add(new GradientStop(WithAlpha(GlowTone, 0.6), 0.0));
        brush.GradientStops.Add(new GradientStop(WithAlpha(GlowTone, 0.18), 0.42));
        brush.GradientStops.Add(new GradientStop(WithAlpha(GlowTone, 0.0), 1.0));
        brush.Freeze();
        return brush;
    }

    /// <summary>Success: the warm bloom (prototype "pt-warm").</summary>
    private static Brush WarmBrush()
    {
        var brush = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = CentrePoint,
            GradientOrigin = CentrePoint,
            RadiusX = 96.0,
            RadiusY = 96.0,
        };
        brush.GradientStops.Add(new GradientStop(WithAlpha(Spot, 0.5), 0.0));
        brush.GradientStops.Add(new GradientStop(WithAlpha(WarmTone, 0.2), 0.38));
        brush.GradientStops.Add(new GradientStop(WithAlpha(GlowTone, 0.0), 1.0));
        brush.Freeze();
        return brush;
    }

    private static Brush BodyBrush()
    {
        var focus = new Point(70.0, 52.0);
        var brush = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = focus,
            GradientOrigin = focus,
            RadiusX = 60.0,
            RadiusY = 60.0,
        };
        brush.GradientStops.Add(new GradientStop(Deep, 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x07, 0x10, 0x16), 0.7));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x04, 0x08, 0x0C), 1.0));
        brush.Freeze();
        return brush;
    }

    private static Brush CrestBrush()
    {
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(21.9, 0.0),
            EndPoint = new Point(98.1, 0.0),
        };
        brush.GradientStops.Add(new GradientStop(WithAlpha(Accent, 0.12), 0.0));
        brush.GradientStops.Add(new GradientStop(WithAlpha(Accent, 0.8), 0.55));
        brush.GradientStops.Add(new GradientStop(WithAlpha(Accent, 1.0), 1.0));
        brush.Freeze();
        return brush;
    }

    private static Brush PupilGlowBrush()
    {
        var brush = new RadialGradientBrush();
        brush.GradientStops.Add(new GradientStop(WithAlpha(White, 0.95), 0.0));
        brush.GradientStops.Add(new GradientStop(WithAlpha(Pearl, 0.4), 0.22));
        brush.GradientStops.Add(new GradientStop(WithAlpha(Pearl, 0.1), 0.5));
        brush.GradientStops.Add(new GradientStop(WithAlpha(Pearl, 0.0), 1.0));
        brush.Freeze();
        return brush;
    }

    private static Brush CoreBrush()
    {
        var brush = new RadialGradientBrush();
        brush.GradientStops.Add(new GradientStop(White, 0.0));
        brush.GradientStops.Add(new GradientStop(White, 0.7));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xE9, 0xE4, 0xDA), 1.0));
        brush.Freeze();
        return brush;
    }

    private static Color ToneOf(CellTone tone) => tone switch
    {
        CellTone.Spot => Spot,
        CellTone.Pearl => Pearl,
        _ => Accent,
    };

    private static Color WithAlpha(Color color, double opacity) =>
        Color.FromArgb((byte)Math.Round(Math.Clamp(opacity, 0.0, 1.0) * 255.0), color.R, color.G, color.B);

    private static SolidColorBrush Solid(Color color, double opacity)
    {
        var brush = new SolidColorBrush(WithAlpha(color, opacity));
        brush.Freeze();
        return brush;
    }

    private static Pen PenOf(Color color, double opacity, double width)
    {
        var pen = new Pen(Solid(color, opacity), width)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        return pen;
    }

    private static Pen DashedPen(Color color, double opacity, double width, double[] dashes)
    {
        // WPF dash lengths are multiples of the pen width; the board's are
        // design units.
        var scaled = new double[dashes.Length];
        for (var i = 0; i < dashes.Length; i++)
        {
            scaled[i] = dashes[i] / width;
        }

        var pen = new Pen(Solid(color, opacity), width)
        {
            DashStyle = new DashStyle(scaled, 0.0),
            DashCap = PenLineCap.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        pen.Freeze();
        return pen;
    }

    /// <summary>
    /// A pen whose dash style stays live so its offset can flow. The pen is
    /// left unfrozen for that one property; its brush is frozen.
    /// </summary>
    private static Pen FlowPen(Color color, double opacity, double width, DashStyle dash, bool round = false)
    {
        var pen = new Pen(Solid(color, opacity), width)
        {
            DashStyle = dash,
            LineJoin = PenLineJoin.Round,
        };

        if (round)
        {
            pen.DashCap = PenLineCap.Round;
            pen.StartLineCap = PenLineCap.Round;
            pen.EndLineCap = PenLineCap.Round;
        }

        return pen;
    }

    /// <summary>
    /// The accent rim line: one dash as long as the rim and one gap as long,
    /// so moving the offset from a full length to zero draws the rim around
    /// (Wake). Flat dash ends, so a hidden rim leaves no dot.
    /// </summary>
    private Pen RimDrawPen() => new(Solid(Accent, 0.55), RimWidth)
    {
        DashStyle = _rimDash,
        DashCap = PenLineCap.Flat,
        LineJoin = PenLineJoin.Round,
    };
}
