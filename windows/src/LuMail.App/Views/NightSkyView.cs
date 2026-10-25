using LuMail.Core.Catalog;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace LuMail.App.Views;

/// <summary>
/// Animierter Nachthimmel (FUNKTIONEN §22): Verlauf, violetter Schleier, 160 funkelnde Sterne (fester Startwert,
/// langsame Drift), Mond mit Kratern, atmendem Schein und Schweben, gelegentlich eine Sternschnuppe.
/// Alles sind XAML-Formen mit Storyboards auf Opacity/RenderTransform – diese laufen unabhängig im
/// Kompositions-Thread. Einziger Zeitgeber: der Abstand bis zur nächsten Sternschnuppe.
/// Angehalten, solange <see cref="Shown"/> oder <see cref="WindowVisible"/> falsch ist; bei ausgeschalteten
/// Windows-Animationen steht der Himmel still.
/// </summary>
public sealed class NightSkyView : Grid
{
    private static readonly IReadOnlyList<SkyStar> StarData = NightSky.Stars();

    private readonly Rectangle _veil = new() { IsHitTestVisible = false };
    private readonly Canvas _starLayer = new() { IsHitTestVisible = false };
    private readonly TranslateTransform _drift = new();
    private readonly List<Ellipse> _stars = new();
    private readonly Canvas _moonLayer = new() { IsHitTestVisible = false };
    private readonly Canvas _moon = new();
    private readonly TranslateTransform _float = new();
    private readonly Ellipse _glow = new();
    private readonly Ellipse _disc = new();
    private readonly List<Ellipse> _craters = new();
    private readonly Canvas _shootLayer = new() { IsHitTestVisible = false };
    private readonly Rectangle _shooting = new();
    private readonly CompositeTransform _shootTransform = new();
    private readonly DoubleAnimation _shootX = new();
    private readonly DoubleAnimation _shootY = new();
    private readonly Storyboard _loop = new();
    private readonly Storyboard _shoot = new();
    private readonly Random _random = new();
    private readonly UISettings _ui = new();
    private DispatcherQueueTimer? _shootTimer;
    private bool _shown;
    private bool _windowVisible = true;
    private bool _animations;
    private bool _started;
    private bool _running;

    // Krater: Versatz zum Mittelpunkt und Radius (px)
    private static readonly (double X, double Y, double R)[] CraterData = { (-13, -9, 7), (11, 5, 5), (-3, 16, 4) };
    private const double ShootingLength = 130;

    public NightSkyView()
    {
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
        Background = Vertical(NightSky.Gradient);

        _starLayer.RenderTransform = _drift;
        foreach (var data in StarData)
        {
            var star = new Ellipse
            {
                Width = data.Size,
                Height = data.Size,
                Fill = new SolidColorBrush(Hex(NightSky.TintColor(data.Tint))),
                Opacity = data.RestOpacity, // stehender Himmel
            };
            _stars.Add(star);
            _starLayer.Children.Add(star);
        }

        var glowSize = NightSky.MoonRadius * NightSky.GlowFactor * 2;
        _glow.Width = _glow.Height = glowSize;
        _glow.Opacity = NightSky.GlowRest;
        Canvas.SetLeft(_glow, -glowSize / 2);
        Canvas.SetTop(_glow, -glowSize / 2);
        _disc.Width = _disc.Height = NightSky.MoonRadius * 2;
        Canvas.SetLeft(_disc, -NightSky.MoonRadius);
        Canvas.SetTop(_disc, -NightSky.MoonRadius);
        _moon.Children.Add(_glow);
        _moon.Children.Add(_disc);
        foreach (var (x, y, r) in CraterData)
        {
            var crater = new Ellipse { Width = r * 2, Height = r * 2 };
            Canvas.SetLeft(crater, x - r);
            Canvas.SetTop(crater, y - r);
            _craters.Add(crater);
            _moon.Children.Add(crater);
        }
        _moon.RenderTransform = _float;
        _moonLayer.Children.Add(_moon);

        _shooting.Width = ShootingLength;
        _shooting.Height = 2;
        _shooting.RadiusX = _shooting.RadiusY = 1;
        _shooting.Opacity = 0;
        _shooting.RenderTransform = _shootTransform;
        var tail = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        tail.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, 255, 255, 255), Offset = 0 });
        tail.GradientStops.Add(new GradientStop { Color = Color.FromArgb(150, 220, 230, 255), Offset = 0.75 });
        tail.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 255, 255, 255), Offset = 1 });
        _shooting.Fill = tail;
        _shootLayer.Children.Add(_shooting);

        Children.Add(_veil);
        Children.Add(_starLayer);
        Children.Add(_moonLayer);
        Children.Add(_shootLayer);

        ApplyColors("#F5D06F", "#8B7CF6");
        BuildLoop();
        BuildShootingStar();

        try { _animations = _ui.AnimationsEnabled; } catch (Exception) { _animations = true; }
        _ui.AnimationsEnabledChanged += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            try { _animations = _ui.AnimationsEnabled; } catch (Exception) { }
            Update();
        });
        SizeChanged += (_, e) => Layout(e.NewSize.Width, e.NewSize.Height);
    }

    /// <summary>Himmel sichtbar (läuft nur dann).</summary>
    public bool Shown
    {
        get => _shown;
        set
        {
            if (_shown == value) return;
            _shown = value;
            Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            Update();
        }
    }

    /// <summary>Fenster sichtbar (nicht minimiert/verborgen) – sonst angehalten.</summary>
    public bool WindowVisible
    {
        get => _windowVisible;
        set
        {
            if (_windowVisible == value) return;
            _windowVisible = value;
            Update();
        }
    }

    /// <summary>Mondfarbe (Theme-Token <c>moon</c>) und Akzent (Schleier) übernehmen.</summary>
    public void ApplyColors(string moonHex, string accentHex)
    {
        var moon = Hex(moonHex);
        var accent = Hex(accentHex);

        var disc = new RadialGradientBrush { Center = new Point(0.42, 0.4), GradientOrigin = new Point(0.36, 0.32), RadiusX = 0.62, RadiusY = 0.62 };
        disc.GradientStops.Add(new GradientStop { Color = Hex(NightSky.MoonCore), Offset = 0 });
        disc.GradientStops.Add(new GradientStop { Color = moon, Offset = 1 });
        _disc.Fill = disc;

        var craterColor = Color.FromArgb(70, (byte)(moon.R * 0.7), (byte)(moon.G * 0.7), (byte)(moon.B * 0.7));
        foreach (var crater in _craters) crater.Fill = new SolidColorBrush(craterColor);

        var glow = new RadialGradientBrush();
        glow.GradientStops.Add(new GradientStop { Color = WithAlpha(moon, 255), Offset = 0 });
        glow.GradientStops.Add(new GradientStop { Color = WithAlpha(moon, 150), Offset = 0.34 });
        glow.GradientStops.Add(new GradientStop { Color = WithAlpha(moon, 0), Offset = 1 });
        _glow.Fill = glow;

        var veil = new RadialGradientBrush { Center = new Point(0, 1), GradientOrigin = new Point(0, 1), RadiusX = 0.75, RadiusY = 0.9 };
        veil.GradientStops.Add(new GradientStop { Color = WithAlpha(accent, (byte)Math.Round(255 * NightSky.VeilOpacity)), Offset = 0 });
        veil.GradientStops.Add(new GradientStop { Color = WithAlpha(accent, 0), Offset = 1 });
        _veil.Fill = veil;
    }

    // ------------------------------------------------------------------ Animationen

    private void BuildLoop()
    {
        for (var i = 0; i < _stars.Count; i++)
        {
            var data = StarData[i];
            var twinkle = new DoubleAnimationUsingKeyFrames
            {
                Duration = new Duration(TimeSpan.FromSeconds(data.Period)),
                RepeatBehavior = RepeatBehavior.Forever,
            };
            foreach (var frame in NightSky.Twinkle(data.Period, data.Phase))
                twinkle.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(frame.Time)), Value = frame.Value });
            Add(_loop, twinkle, _stars[i], "Opacity");
        }
        Add(_loop, Swing(-NightSky.DriftPixels, NightSky.DriftPixels, NightSky.DriftSeconds), _drift, "X");
        Add(_loop, Swing(NightSky.GlowMin, NightSky.GlowMax, NightSky.GlowSeconds), _glow, "Opacity");
        Add(_loop, Swing(-NightSky.FloatPixels, NightSky.FloatPixels, NightSky.FloatSeconds), _float, "Y");
    }

    /// <summary>Hin und zurück in <paramref name="cycleSeconds"/>, sanft an den Enden, endlos.</summary>
    private static DoubleAnimation Swing(double from, double to, double cycleSeconds) => new()
    {
        From = from,
        To = to,
        Duration = new Duration(TimeSpan.FromSeconds(cycleSeconds / 2)),
        AutoReverse = true,
        RepeatBehavior = RepeatBehavior.Forever,
        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
    };

    private void BuildShootingStar()
    {
        var duration = new Duration(TimeSpan.FromSeconds(NightSky.ShootingSeconds));
        _shootX.Duration = duration;
        _shootY.Duration = duration;
        _shootX.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        _shootY.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        Add(_shoot, _shootX, _shootTransform, "TranslateX");
        Add(_shoot, _shootY, _shootTransform, "TranslateY");
        var fade = new DoubleAnimationUsingKeyFrames { Duration = duration };
        foreach (var (time, value) in new[] { (0.0, 0.0), (0.12, 1.0), (0.5, 0.9), (NightSky.ShootingSeconds, 0.0) })
            fade.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(time)), Value = value });
        Add(_shoot, fade, _shooting, "Opacity");
    }

    private static void Add(Storyboard storyboard, Timeline animation, DependencyObject target, string property)
    {
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }

    /// <summary>Eine Sternschnuppe: kurze helle Linie, die schräg nach unten links zieht und ausblendet.</summary>
    private void LaunchShootingStar()
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 120 || h < 120) return;
        var angle = 145 + _random.NextDouble() * 15; // Grad, 0° = nach rechts, y nach unten → unten links
        var radians = angle * Math.PI / 180;
        var distance = Math.Clamp(w * 0.28, 180, 420);
        var x = w * (0.35 + _random.NextDouble() * 0.6);
        var y = h * (0.04 + _random.NextDouble() * 0.3);
        _shootTransform.Rotation = angle;
        _shootX.From = x;
        _shootX.To = x + Math.Cos(radians) * distance;
        _shootY.From = y;
        _shootY.To = y + Math.Sin(radians) * distance;
        _shoot.Stop();
        _shoot.Begin();
    }

    private void ScheduleShootingStar()
    {
        if (_shootTimer is null)
        {
            _shootTimer = DispatcherQueue.CreateTimer();
            _shootTimer.IsRepeating = false;
            _shootTimer.Tick += (_, _) =>
            {
                if (!_running) return;
                LaunchShootingStar();
                ScheduleShootingStar();
            };
        }
        _shootTimer.Stop();
        _shootTimer.Interval = NightSky.NextShootingDelay(_random);
        _shootTimer.Start();
    }

    /// <summary>Läuft nur, wenn sichtbar, Fenster sichtbar und Windows-Animationen an.</summary>
    private void Update()
    {
        if (!_animations && _started)
        {
            _loop.Stop(); // zurück auf die Ruhewerte (stehender Himmel)
            _started = false;
        }
        var run = _shown && _windowVisible && _animations;
        if (run == _running) return;
        _running = run;
        if (run)
        {
            if (_started) _loop.Resume();
            else
            {
                _loop.Begin();
                _started = true;
            }
            ScheduleShootingStar();
        }
        else
        {
            if (_started) _loop.Pause();
            _shootTimer?.Stop();
            _shoot.Stop();
        }
    }

    // ------------------------------------------------------------------ Layout

    private void Layout(double w, double h)
    {
        if (w <= 0 || h <= 0) return;
        Clip = new RectangleGeometry { Rect = new Rect(0, 0, w, h) };
        // Sterne über die Breite plus Drift verteilen, damit an den Rändern keine Lücke entsteht.
        var span = w + 2 * NightSky.DriftPixels;
        for (var i = 0; i < _stars.Count; i++)
        {
            var data = StarData[i];
            Canvas.SetLeft(_stars[i], data.X * span - NightSky.DriftPixels - data.Size / 2);
            Canvas.SetTop(_stars[i], data.Y * h - data.Size / 2);
        }
        Canvas.SetLeft(_moon, Math.Round(w * NightSky.MoonX));
        Canvas.SetTop(_moon, Math.Round(h * NightSky.MoonY));
    }

    // ------------------------------------------------------------------ Farben

    private static LinearGradientBrush Vertical(IReadOnlyList<string> colors)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        for (var i = 0; i < colors.Count; i++)
            brush.GradientStops.Add(new GradientStop { Color = Hex(colors[i]), Offset = colors.Count == 1 ? 0 : (double)i / (colors.Count - 1) });
        return brush;
    }

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static Color Hex(string hex)
    {
        var (a, r, g, b) = ThemeCatalog.ParseHex(hex);
        return Color.FromArgb(a, r, g, b);
    }
}
