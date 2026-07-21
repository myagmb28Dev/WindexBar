using WindexBar.Core.Config;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WindexBar.Windows.UI;

internal sealed class GaugeBar(
    string key,
    Grid track,
    Border fill,
    Border sweep,
    double targetValue = 0)
{
    public string Key { get; } = key;
    public Grid Track { get; } = track;
    public Border Fill { get; } = fill;
    public Border Sweep { get; } = sweep;
    public double CurrentValue { get; set; }
    public double TargetValue { get; set; } = targetValue;
}

internal sealed class GaugeAnimator : IDisposable
{
    private const double StandardSweepStep = 0.035;
    private const double FastSweepStep = 0.075;
    private const double StandardEaseFactor = 0.16;
    private const double FastEaseFactor = 0.28;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Func<string> _animationProvider;
    private readonly Func<bool> _fastTierProvider;
    private readonly Dictionary<string, double> _sessionValues = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<GaugeBar> _primaryBars = [];
    private IReadOnlyList<GaugeBar> _sessionBars = [];
    private GaugeBar? _previewBar;
    private Func<StyleConfig>? _previewStyleProvider;
    private Func<bool>? _previewVisibleProvider;
    private double _sweepPhase;
    private double _previewSweepPhase;

    public GaugeAnimator(Func<string> animationProvider, Func<bool> fastTierProvider)
    {
        _animationProvider = animationProvider;
        _fastTierProvider = fastTierProvider;
        _timer.Tick += OnTick;
    }

    public void Start() => _timer.Start();

    public void SetPrimaryBars(params GaugeBar[] bars) => _primaryBars = bars;

    public void SetPrimaryTargets(double current, double weekly)
    {
        if (_primaryBars.Count > 0)
        {
            _primaryBars[0].TargetValue = current;
        }

        if (_primaryBars.Count > 1)
        {
            _primaryBars[1].TargetValue = weekly;
        }
    }

    public void ReplaceSessionBars(IReadOnlyList<GaugeBar> bars)
    {
        foreach (var existing in _sessionBars)
        {
            _sessionValues[existing.Key] = existing.CurrentValue;
        }

        foreach (var bar in bars)
        {
            bar.CurrentValue = _sessionValues.TryGetValue(bar.Key, out var previous) ? previous : 0;
        }

        _sessionBars = bars;
    }

    public void SetPreview(
        GaugeBar bar,
        Func<StyleConfig> styleProvider,
        Func<bool> visibleProvider)
    {
        _previewBar = bar;
        _previewBar.CurrentValue = bar.TargetValue;
        _previewStyleProvider = styleProvider;
        _previewVisibleProvider = visibleProvider;
        _previewSweepPhase = 0;
        RefreshPreview();
    }

    public void ApplyAppearance(StyleConfig style)
    {
        foreach (var bar in _primaryBars.Concat(_sessionBars))
        {
            ApplyAppearance(bar, style);
        }
    }

    public void RefreshPreview()
    {
        if (_previewBar is null || _previewStyleProvider is null)
        {
            return;
        }

        var style = _previewStyleProvider().Normalized();
        ApplyAppearance(_previewBar, style);
        ApplyProgress(
            _previewBar,
            EaseSweep(_previewSweepPhase),
            StyleConfig.NormalizeGaugeAnimation(style.GaugeAnimation) != "off");
    }

    private void OnTick(object? sender, object args)
    {
        var animation = StyleConfig.NormalizeGaugeAnimation(_animationProvider());
        var animationEnabled = animation != "off";
        var fastTier = _fastTierProvider();
        var sweepStep = animation switch
        {
            "fast" => FastSweepStep * 1.6,
            "off" => 0,
            _ => fastTier ? FastSweepStep : StandardSweepStep
        };
        var primaryEase = animation switch
        {
            "fast" => 0.42,
            "off" => 1,
            _ => fastTier ? FastEaseFactor : StandardEaseFactor
        };
        var sessionEase = animation switch
        {
            "fast" => 0.42,
            "off" => 1,
            _ => StandardEaseFactor
        };

        _sweepPhase = (_sweepPhase + sweepStep) % 1d;
        var sweep = EaseSweep(_sweepPhase);
        AnimateBars(_primaryBars, primaryEase, sweep, animationEnabled);
        AnimateBars(_sessionBars, sessionEase, sweep, animationEnabled);
        foreach (var bar in _sessionBars)
        {
            _sessionValues[bar.Key] = bar.CurrentValue;
        }

        if (_previewBar is not null
            && _previewStyleProvider is not null
            && (_previewVisibleProvider?.Invoke() ?? false))
        {
            var previewStyle = _previewStyleProvider().Normalized();
            var previewAnimation = StyleConfig.NormalizeGaugeAnimation(previewStyle.GaugeAnimation);
            var previewStep = previewAnimation switch
            {
                "fast" => FastSweepStep * 1.6,
                "off" => 0,
                _ => StandardSweepStep
            };
            _previewSweepPhase = (_previewSweepPhase + previewStep) % 1d;
            ApplyAppearance(_previewBar, previewStyle);
            ApplyProgress(_previewBar, EaseSweep(_previewSweepPhase), previewAnimation != "off");
        }
    }

    private static void AnimateBars(
        IEnumerable<GaugeBar> bars,
        double easeFactor,
        double sweep,
        bool animationEnabled)
    {
        foreach (var bar in bars)
        {
            bar.CurrentValue = EaseValue(bar.CurrentValue, bar.TargetValue, easeFactor);
            ApplyProgress(bar, sweep, animationEnabled);
        }
    }

    private static double EaseValue(double current, double target, double factor)
    {
        var delta = target - current;
        return Math.Abs(delta) < 0.1 ? target : current + (delta * factor);
    }

    private static double EaseSweep(double amount) => 1 - Math.Pow(1 - amount, 2);

    private static void ApplyProgress(GaugeBar bar, double sweep, bool animationEnabled)
    {
        var width = Math.Max(0, bar.Track.ActualWidth);
        var value = Math.Clamp(bar.CurrentValue, 0, 100);
        var target = Math.Clamp(bar.TargetValue, 0, 100);
        var safeSweep = Math.Clamp(sweep, 0, 1);
        bar.Fill.Width = width * (value / 100d);
        bar.Sweep.Width = animationEnabled ? width * (target * safeSweep / 100d) : 0;
        bar.Sweep.Opacity = animationEnabled ? 0.22 + (0.28 * safeSweep) : 0;
    }

    internal static void ApplyAppearance(GaugeBar bar, StyleConfig style)
    {
        var thickness = StyleConfig.NormalizeGaugeThickness(style.GaugeThickness) switch
        {
            "thin" => 4d,
            "thick" => 9d,
            _ => 6d
        };
        var radius = new CornerRadius(thickness / 2d);
        bar.Track.Height = thickness;
        foreach (var border in bar.Track.Children.OfType<Border>())
        {
            border.CornerRadius = radius;
        }

        var normalized = StyleConfig.NormalizeGaugeColor(style.GaugeColor);
        var color = global::Windows.UI.Color.FromArgb(
            0xFF,
            Convert.ToByte(normalized.Substring(1, 2), 16),
            Convert.ToByte(normalized.Substring(3, 2), 16),
            Convert.ToByte(normalized.Substring(5, 2), 16));
        bar.Fill.Background = new SolidColorBrush(color);
        bar.Sweep.Background = new SolidColorBrush(Lighten(color, 0.45));
    }

    private static global::Windows.UI.Color Lighten(global::Windows.UI.Color color, double amount)
    {
        byte Blend(byte channel) => (byte)Math.Clamp(
            Math.Round(channel + ((255 - channel) * amount)),
            byte.MinValue,
            byte.MaxValue);
        return global::Windows.UI.Color.FromArgb(0xFF, Blend(color.R), Blend(color.G), Blend(color.B));
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
