// Tiny synthesizer: every buddy sound is generated on the fly as a WAV in memory.

using System;
using System.IO;
using System.Linq;
using System.Media;

namespace ClaudeBuddy;

enum Wave { Sine, Square, Triangle, Noise }

record struct Note(double F0, double F1, double Dur, Wave W = Wave.Sine, double Vol = 0.45, double Gap = 0);

static class Synth
{
    static SoundPlayer _current;
    static readonly Random Rng = new();

    public static bool Muted
    {
        get => Progress.Shared.Data.Muted;
        set { Progress.Shared.Data.Muted = value; Progress.Shared.Save(); }
    }

    public static void Play(params Note[] notes)
    {
        if (Muted || notes.Length == 0) return;
        const int sr = 22050;
        double total = notes.Sum(n => n.Dur + n.Gap);
        int frames = (int)(total * sr) + 1;
        var pcm = new short[frames];
        int i = 0;
        foreach (var n in notes)
        {
            int count = (int)(n.Dur * sr);
            double phase = 0;
            for (int k = 0; k < count && i < frames; k++, i++)
            {
                double x = (double)k / count;
                phase += (n.F0 + (n.F1 - n.F0) * x) / sr;
                double p = phase - Math.Floor(phase);
                double s = n.W switch
                {
                    Wave.Sine => Math.Sin(2 * Math.PI * p),
                    Wave.Square => p < 0.5 ? 0.55 : -0.55,
                    Wave.Triangle => 4 * Math.Abs(p - 0.5) - 1,
                    _ => Rng.NextDouble() * 2 - 1,
                };
                double env = Math.Min(1, x * 25) * Math.Pow(1 - x, 1.4);
                pcm[i] = (short)Math.Clamp(s * env * n.Vol * 0.5 * short.MaxValue, short.MinValue, short.MaxValue);
            }
            i += (int)(n.Gap * sr);
        }

        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        int bytes = pcm.Length * 2;
        w.Write("RIFF"u8.ToArray()); w.Write(36 + bytes); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(sr); w.Write(sr * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(bytes);
        foreach (var v in pcm) w.Write(v);
        ms.Position = 0;
        try
        {
            _current?.Stop();
            _current = new SoundPlayer(ms);
            _current.Play();
        }
        catch { }
    }

    static Note[] Arp(double[] f, double dur, Wave w = Wave.Triangle, double vol = 0.4, double? last = null) =>
        f.Select((x, i) => new Note(x, x, i == f.Length - 1 ? (last ?? dur) : dur, w, vol)).ToArray();

    public static void Chirp() => Play(new Note(700, 1300, 0.09));
    public static void Giggle(double p = 1) => Play(new[] { 900.0, 1000, 1150 }.Select(f => new Note(f * p, f * 1.3 * p, 0.06, Gap: 0.025)).ToArray());
    public static void Babble(string text, double pitch = 1) =>
        Play(Enumerable.Range(0, Math.Clamp(text.Length / 3, 2, 9)).Select(_ =>
        {
            double f = 380 * pitch * (0.85 + Rng.NextDouble() * 0.4);
            return new Note(f, f * (0.9 + Rng.NextDouble() * 0.25), 0.05, Wave.Square, 0.13, 0.022);
        }).ToArray());
    public static void Done(double p = 1) => Play(Arp(new[] { 523 * p, 659 * p, 784 * p, 1047 * p }, 0.09, last: 0.3));
    public static void LevelUp() => Play(Arp(new[] { 523.0, 659, 784, 1047, 1319, 1568 }, 0.07, Wave.Square, 0.18).Append(new Note(2093, 2093, 0.45, Wave.Triangle, 0.35)).ToArray());
    public static void Unlock() => Play(Arp(new[] { 1568.0, 2093, 2637 }, 0.06, vol: 0.3, last: 0.25));
    public static void Chomp() => Play(Enumerable.Range(0, 3).SelectMany(_ => new[] { new Note(220, 140, 0.05, Wave.Noise, 0.25), new Note(180, 120, 0.04, Wave.Sine, 0.3, 0.06) }).ToArray());
    public static void Dizzy() => Play(new Note(900, 500, 0.18, Wave.Triangle), new Note(750, 420, 0.18, Wave.Triangle), new Note(620, 300, 0.3, Wave.Triangle));
    public static void Boop() => Play(new Note(660, 660, 0.12, Wave.Square, 0.2, 0.05), new Note(520, 520, 0.16, Wave.Square, 0.2));
    public static void Grumble() => Play(new Note(230, 190, 0.14, Wave.Square, 0.12, 0.04), new Note(210, 160, 0.2, Wave.Square, 0.12));
    public static void Yawn() => Play(new Note(500, 260, 0.6, Wave.Sine, 0.25));
    public static void Whee() => Play(new Note(500, 1600, 0.35, Wave.Triangle, 0.3));
    public static void Scream() => Play(new Note(1400, 1900, 0.12, Wave.Square, 0.15), new Note(1900, 900, 0.3, Wave.Square, 0.15));
    public static void Growl() => Play(new Note(140, 110, 0.35, Wave.Square, 0.16), new Note(120, 90, 0.3, Wave.Square, 0.16));
    public static void DevilLaugh() => Play(Enumerable.Range(0, 5).Select(k => new Note(520 - k * 40, (520 - k * 40) * 0.8, 0.11, Wave.Square, 0.18, 0.05))
        .Append(new Note(300, 160, 0.4, Wave.Square, 0.18)).ToArray());
    public static void Transform() => Play(new Note(200, 900, 0.5, Wave.Triangle, 0.3), new Note(90, 60, 0.6, Wave.Square, 0.2));
    public static void Boom(bool big = false) => Play(new Note(90, 30, big ? 0.9 : 0.6, Wave.Noise, big ? 0.6 : 0.45));
    public static void Fizzle() => Play(new Note(1800, 400, 0.35, Wave.Noise, 0.12));
    public static void CalmDown() => Play(new Note(880, 440, 0.5, Wave.Sine, 0.25), new Note(523, 523, 0.12, Gap: 0.04), new Note(784, 784, 0.3));
    public static void Burp() => Play(new Note(110, 70, 0.35, Wave.Square, 0.2));
    public static void Hurl() => Play(new Note(300, 140, 0.18, Wave.Square, 0.15, 0.05), new Note(200, 80, 0.4, Wave.Noise, 0.25));
    public static void Heal() => Play(new[] { 523.0, 659, 784, 1047, 1568 }.Select(f => new Note(f, f * 1.02, 0.07, Wave.Sine, 0.3)).ToArray());
    public static void Honk() => Play(new Note(330, 320, 0.18, Wave.Square, 0.25, 0.04), new Note(330, 300, 0.28, Wave.Square, 0.25));
    public static void Boing() => Play(new Note(180, 620, 0.12, Wave.Triangle, 0.35), new Note(620, 260, 0.25, Wave.Triangle, 0.3));
    public static void Sneeze() => Play(new Note(600, 900, 0.35, Wave.Sine, 0.18, 0.08), new Note(4000, 1500, 0.18, Wave.Noise, 0.3));
    public static void Whistle() => Play(new[] { 784.0, 988, 880, 1175, 988 }.Select(f => new Note(f * 1.5, f * 1.52, 0.16, Wave.Sine, 0.18, 0.02)).ToArray());
    public static void Thud() => Play(new Note(140, 60, 0.12, Wave.Sine, 0.5));
    public static void Pad(int i) { double f = new[] { 392.0, 523, 659, 784 }[i]; Play(new Note(f, f, 0.3, Wave.Triangle, 0.4)); }
    public static void Buzz() => Play(new Note(110, 100, 0.6, Wave.Square, 0.2));
}
