using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace HAMMOR.App.Presence;

/// <summary>
/// The HAMMOR Living Core: a native, procedural WPF control. The app tells it
/// which <see cref="LivingCoreState"/> it is in; the control decides how that
/// looks. No video, GIF or baked animation: every frame is computed from the
/// state's parameters by <see cref="LivingCoreMotion"/> and applied to a
/// retained <see cref="LivingCoreScene"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Cost.</b> The control only draws while it is loaded, visible and its
/// window is not minimised. It renders at the display rate (60 fps target)
/// while its window is active and at 30 fps while it is inactive; minimised or
/// hidden it does no work at all. Under reduced motion it stops entirely once a
/// still state has settled.
/// </para>
/// <para>
/// <b>Reduced motion</b> follows the Windows "Animation effects" setting
/// (<see cref="SystemParameters.ClientAreaAnimation"/>) and reacts when it
/// changes.
/// </para>
/// <para>
/// <b>Right-to-left.</b> The mark is never mirrored. In a right-to-left layout
/// the drawing is flipped back, and the Blocked glance toward
/// <see cref="ApprovalDirection"/> is resolved in reading order.
/// </para>
/// </remarks>
public sealed class LivingCore : FrameworkElement
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State),
        typeof(LivingCoreState),
        typeof(LivingCore),
        new PropertyMetadata(LivingCoreState.Idle, OnStateChanged));

    public static readonly DependencyProperty VoiceEnvelopeProperty = DependencyProperty.Register(
        nameof(VoiceEnvelope),
        typeof(ISpeechEnvelope),
        typeof(LivingCore),
        new PropertyMetadata(defaultValue: null));

    public static readonly DependencyProperty ApprovalDirectionProperty = DependencyProperty.Register(
        nameof(ApprovalDirection),
        typeof(double),
        typeof(LivingCore),
        new PropertyMetadata(-1.0));

    private const double InactiveFrameInterval = 1.0 / 30.0;
    private const double DefaultSize = 240.0;

    private readonly LivingCoreScene _scene = new();
    private readonly LivingCoreFrame _frame = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private LivingCoreMotion? _motion;
    private Window? _window;
    private bool _renderingHooked;
    private bool _settingsHooked;
    private TimeSpan _lastRenderingTime = TimeSpan.MinValue;
    private double _lastFrameSeconds = double.NegativeInfinity;

    public LivingCore()
    {
        AddVisualChild(_scene.Root);

        Focusable = false;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    /// <summary>The state to show. Bind it; the control animates the change.</summary>
    public LivingCoreState State
    {
        get => (LivingCoreState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>
    /// Optional real envelope of the voice the current state is about (HAMMOR's
    /// speech while Speaking, the user's while Listening). Null keeps the
    /// designed speaking rhythm and shows no voice rings while listening.
    /// </summary>
    public ISpeechEnvelope? VoiceEnvelope
    {
        get => (ISpeechEnvelope?)GetValue(VoiceEnvelopeProperty);
        set => SetValue(VoiceEnvelopeProperty, value);
    }

    /// <summary>
    /// Where approval requests appear, in reading order: negative is the
    /// leading side (the sidebar, where Tasks lives), positive the trailing
    /// side. The Blocked state glances there once.
    /// </summary>
    public double ApprovalDirection
    {
        get => (double)GetValue(ApprovalDirectionProperty);
        set => SetValue(ApprovalDirectionProperty, value);
    }

    protected override int VisualChildrenCount => 1;

    private double Now => _clock.Elapsed.TotalSeconds;

    private bool IsMirrored => FlowDirection == FlowDirection.RightToLeft;

    protected override Visual GetVisualChild(int index) =>
        index == 0 ? _scene.Root : throw new ArgumentOutOfRangeException(nameof(index));

    protected override Size MeasureOverride(Size availableSize)
    {
        // Square, as large as offered; a default when nothing constrains it.
        var side = Math.Min(availableSize.Width, availableSize.Height);
        if (double.IsInfinity(side) || double.IsNaN(side))
        {
            side = double.IsInfinity(availableSize.Width) && double.IsInfinity(availableSize.Height)
                ? DefaultSize
                : Math.Min(
                    double.IsInfinity(availableSize.Width) ? availableSize.Height : availableSize.Width,
                    double.IsInfinity(availableSize.Height) ? availableSize.Width : availableSize.Height);
        }

        return new Size(side, side);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _scene.Layout(finalSize, IsMirrored);
        return finalSize;
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == FlowDirectionProperty)
        {
            _scene.Layout(RenderSize, IsMirrored);
        }
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var core = (LivingCore)d;
        if (core._motion is null)
        {
            return;
        }

        core._motion.SetState((LivingCoreState)e.NewValue, core.Now);
        core.RenderFrame();
        core.UpdateRendering();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DesignerProperties.GetIsInDesignMode(this))
        {
            _motion ??= new LivingCoreMotion(State, Now, fromDormant: false, reducedMotion: true);
            RenderFrame();
            return;
        }

        // A core arrives through the cascade the first time it is shown:
        // the white core first, the aura last.
        _motion ??= new LivingCoreMotion(State, Now, fromDormant: true, IsReducedMotion());

        AttachWindow(Window.GetWindow(this));
        if (!_settingsHooked)
        {
            SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
            _settingsHooked = true;
        }

        _motion.ReducedMotion = IsReducedMotion();
        RenderFrame();
        UpdateRendering();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Static and window events would otherwise keep this control alive.
        if (_settingsHooked)
        {
            SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
            _settingsHooked = false;
        }

        AttachWindow(null);
        UnhookRendering();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateRendering();

    private void AttachWindow(Window? window)
    {
        if (ReferenceEquals(window, _window))
        {
            return;
        }

        if (_window is not null)
        {
            _window.StateChanged -= OnWindowStateChanged;
            _window.Activated -= OnWindowActivationChanged;
            _window.Deactivated -= OnWindowActivationChanged;
        }

        _window = window;

        if (_window is not null)
        {
            _window.StateChanged += OnWindowStateChanged;
            _window.Activated += OnWindowActivationChanged;
            _window.Deactivated += OnWindowActivationChanged;
        }
    }

    private void OnWindowStateChanged(object? sender, EventArgs e) => UpdateRendering();

    private void OnWindowActivationChanged(object? sender, EventArgs e)
    {
        // Re-read the system setting on activation as a fallback, in case the
        // change notification was missed while HAMMOR was in the background.
        SyncReducedMotion();
        UpdateRendering();
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
        {
            // Raised on the UI thread by WPF's settings listener; marshal
            // defensively all the same.
            if (Dispatcher.CheckAccess())
            {
                SyncReducedMotion();
            }
            else
            {
                _ = Dispatcher.InvokeAsync(SyncReducedMotion);
            }
        }
    }

    private void SyncReducedMotion()
    {
        if (_motion is null)
        {
            return;
        }

        var reduced = IsReducedMotion();
        if (reduced == _motion.ReducedMotion)
        {
            return;
        }

        _motion.ReducedMotion = reduced;
        RenderFrame();
        UpdateRendering();
    }

    private static bool IsReducedMotion() => !SystemParameters.ClientAreaAnimation;

    /// <summary>
    /// Hooks the per-frame callback only when frames are actually needed.
    /// </summary>
    private void UpdateRendering()
    {
        var shouldRender = _motion is not null
            && IsLoaded
            && IsVisible
            && (_window is null || _window.WindowState != WindowState.Minimized)
            && _motion.NeedsContinuousFrames(Now);

        if (shouldRender)
        {
            HookRendering();
        }
        else
        {
            UnhookRendering();
        }
    }

    private void HookRendering()
    {
        if (_renderingHooked)
        {
            return;
        }

        CompositionTarget.Rendering += OnRendering;
        _renderingHooked = true;
    }

    private void UnhookRendering()
    {
        if (!_renderingHooked)
        {
            return;
        }

        CompositionTarget.Rendering -= OnRendering;
        _renderingHooked = false;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        // WPF can raise Rendering more than once per frame; draw once.
        if (e is RenderingEventArgs args)
        {
            if (args.RenderingTime == _lastRenderingTime)
            {
                return;
            }

            _lastRenderingTime = args.RenderingTime;
        }

        var now = Now;
        var active = _window is null || _window.IsActive;
        if (!active && (now - _lastFrameSeconds) < InactiveFrameInterval - 0.002)
        {
            return;
        }

        RenderFrame();

        if (_motion is not null && !_motion.NeedsContinuousFrames(now))
        {
            // Reduced motion and settled: the last frame stays on screen.
            UnhookRendering();
        }
    }

    private void RenderFrame()
    {
        if (_motion is null)
        {
            return;
        }

        var now = Now;
        _lastFrameSeconds = now;

        var envelope = VoiceEnvelope;
        var level = envelope is not null && envelope.TryGetLevel(out var value) ? value : double.NaN;

        // Reading order to physical side: in right-to-left the leading side is
        // on the right.
        var direction = IsMirrored ? -ApprovalDirection : ApprovalDirection;

        _motion.Advance(now, level, direction, _frame);
        _scene.Apply(_frame);
    }
}
