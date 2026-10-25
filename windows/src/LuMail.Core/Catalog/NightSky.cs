namespace LuMail.Core.Catalog;

/// <summary>Färbung eines Sterns: weiß bis leicht bläulich/gelblich.</summary>
public enum SkyStarTint { White, Blue, Yellow }

/// <summary>
/// Ein Stern des Nachthimmels: Position relativ zur Fläche (0…1), Größe in px, Funkelperiode und Versatz in s,
/// Deckkraft für den stehenden Himmel (Animationen aus).
/// </summary>
public readonly record struct SkyStar(double X, double Y, double Size, SkyStarTint Tint, double Period, double Phase, double RestOpacity);

/// <summary>Schlüsselbild einer Animation (Zeit in s, Wert).</summary>
public readonly record struct SkyKeyFrame(double Time, double Value);

/// <summary>
/// Animierter Nachthimmel (FUNKTIONEN §22): feste Werte und die plattformunabhängige Berechnung von Sternen,
/// Funkeln und Sternschnuppen-Abständen. Die Darstellung selbst baut die App (XAML-Formen + Storyboards).
/// </summary>
public static class NightSky
{
    public const int StarCount = 160;

    /// <summary>Fester Startwert – gleiche Sterne bei jedem Start ("LuMa").</summary>
    public const uint Seed = 0x4C754D61;

    /// <summary>Himmelsverlauf von oben nach unten.</summary>
    public static IReadOnlyList<string> Gradient { get; } = new[] { "#070A1C", "#141638", "#24205A" };

    /// <summary>Violetter Schleier unten links: Akzentfarbe mit 8 % Deckkraft.</summary>
    public const double VeilOpacity = 0.08;

    public const double StarMinSize = 0.8, StarMaxSize = 2.4;
    public const double TwinkleMin = 0.25, TwinkleMax = 1.0;
    public const double TwinkleMinPeriod = 2, TwinkleMaxPeriod = 6;

    /// <summary>Waagerechte Drift der Sternenebene: ±12 px, 90 s hin und zurück.</summary>
    public const double DriftPixels = 12, DriftSeconds = 90;

    /// <summary>Mond: Mittelpunkt bei 82 % Breite / 16 % Höhe, Radius 38 px, Verlauf #FFF4D6 → Theme-Farbe moon.</summary>
    public const double MoonX = 0.82, MoonY = 0.16, MoonRadius = 38;
    public const string MoonCore = "#FFF4D6";

    /// <summary>Schein: Radius 3×, „atmet“ zwischen 0,25 und 0,4 in 6 s.</summary>
    public const double GlowFactor = 3, GlowMin = 0.25, GlowMax = 0.4, GlowRest = 0.35, GlowSeconds = 6;

    /// <summary>Mond schwebt ±3 px in 8 s.</summary>
    public const double FloatPixels = 3, FloatSeconds = 8;

    /// <summary>Sternschnuppe alle 9–18 s, Flug 0,9 s.</summary>
    public const double ShootingMinDelay = 9, ShootingMaxDelay = 18, ShootingSeconds = 0.9;

    /// <summary>Seitenleiste und Nachrichtenliste im dunklen Modus: 72 % deckend.</summary>
    public const double PaneOpacity = 0.72;

    /// <summary>Lesebereich ohne geöffnete Nachricht im dunklen Modus: nur 15 % deckend – dort steht der Mond.</summary>
    public const double EmptyReaderOpacity = 0.15;

    /// <summary>Farbe einer Sternfärbung.</summary>
    public static string TintColor(SkyStarTint tint) => tint switch
    {
        SkyStarTint.Blue => "#DCE6FF",
        SkyStarTint.Yellow => "#FFF1CF",
        _ => "#FFFFFF",
    };

    /// <summary>Die 160 Sterne – deterministisch aus dem Startwert (mulberry32, auf allen Plattformen gleich).</summary>
    public static IReadOnlyList<SkyStar> Stars(uint seed = Seed, int count = StarCount)
    {
        var random = new Mulberry32(seed);
        var stars = new SkyStar[count];
        for (var i = 0; i < count; i++)
        {
            var x = random.Next();
            var y = random.Next();
            var size = StarMinSize + random.Next() * (StarMaxSize - StarMinSize);
            var t = random.Next();
            var tint = t < 0.7 ? SkyStarTint.White : t < 0.85 ? SkyStarTint.Blue : SkyStarTint.Yellow;
            var period = TwinkleMinPeriod + random.Next() * (TwinkleMaxPeriod - TwinkleMinPeriod);
            var phase = random.Next() * period;
            var rest = 0.35 + random.Next() * 0.65;
            stars[i] = new SkyStar(x, y, size, tint, period, phase, rest);
        }
        return stars;
    }

    /// <summary>
    /// Funkeln als Dreieckswelle 0,25 → 1 → 0,25 über eine Periode, um <paramref name="phase"/> Sekunden versetzt –
    /// so laufen alle Sterne von Anfang an gegeneinander verschoben (kein Startverzug nötig).
    /// Erstes und letztes Schlüsselbild haben denselben Wert (nahtlose Wiederholung).
    /// </summary>
    public static IReadOnlyList<SkyKeyFrame> Twinkle(double period, double phase, double low = TwinkleMin, double high = TwinkleMax)
    {
        if (period <= 0) throw new ArgumentOutOfRangeException(nameof(period));
        var half = period / 2;
        phase = ((phase % period) + period) % period;

        double Wave(double tau)
        {
            tau = ((tau % period) + period) % period;
            return tau < half ? low + (high - low) * (tau / half) : high - (high - low) * ((tau - half) / half);
        }

        var frames = new List<SkyKeyFrame> { new(0, Wave(phase)) };
        var peak = ((half - phase) % period + period) % period;  // Zeitpunkt, an dem die Welle oben ist
        var trough = ((period - phase) % period + period) % period; // … und unten
        foreach (var (time, value) in new[] { (peak, high), (trough, low) }.OrderBy(p => p.Item1))
            if (time > 1e-9 && time < period - 1e-9) frames.Add(new SkyKeyFrame(time, value));
        frames.Add(new SkyKeyFrame(period, Wave(phase)));
        return frames;
    }

    /// <summary>Zufälliger Abstand bis zur nächsten Sternschnuppe (9–18 s).</summary>
    public static TimeSpan NextShootingDelay(Random random) =>
        TimeSpan.FromSeconds(ShootingMinDelay + random.NextDouble() * (ShootingMaxDelay - ShootingMinDelay));

    /// <summary>Kleiner, plattformunabhängiger Zufallsgenerator (mulberry32).</summary>
    private sealed class Mulberry32(uint seed)
    {
        private uint _state = seed;

        public double Next()
        {
            unchecked
            {
                _state += 0x6D2B79F5;
                var t = _state;
                t = (t ^ (t >> 15)) * (t | 1);
                t ^= t + (t ^ (t >> 7)) * (t | 61);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }
    }
}
