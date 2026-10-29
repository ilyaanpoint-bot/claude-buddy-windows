// Ordinary windows: the Copycat game and buddy's console (where the secret commands live).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ClaudeBuddy;

/// Copycat: buddy lights up coloured pads in a pattern; you copy it. One more step each round.
class CopycatForm : Form
{
    enum Phase { Showing, Input, Over }

    readonly Animator _anim;
    readonly Timer _timer = new() { Interval = 16 };
    readonly List<int> _seq = new();
    readonly uint[] _colors = { 0xE5395B, 0x3F7FE0, 0xF2C94C, 0x5DBB63 };
    readonly string[] _arrows = { "↑", "→", "↓", "←" };
    static readonly Random Rng = new();
    Phase _phase;
    int _showIndex, _inputIndex, _score;
    double _nextStepAt, _flashUntil;
    int? _flashPad;
    public Action<int> OnEnd;

    const float W = 460, H = 320;
    static readonly PointF Center = new(W / 2, 170);
    RectangleF Again => new(136, H - 68, 88, 28);
    RectangleF Done => new(236, H - 68, 88, 28);

    public CopycatForm(Animator anim)
    {
        _anim = anim;
        Text = "Copycat";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size((int)(W * DeviceDpi / 96f), (int)(H * DeviceDpi / 96f));
        DoubleBuffered = true;
        BackColor = Palette.Rgb(0x1E1B2E);
        KeyPreview = true;
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        FormClosed += (_, _) => _timer.Stop();
        MouseDown += (_, e) => Click_(new PointF(e.X / Sc, e.Y / Sc));
        Restart();
    }

    float Sc => DeviceDpi / 96f;

    PointF Pad(int i) => i switch
    {
        0 => new(Center.X, Center.Y - 84), 1 => new(Center.X + 104, Center.Y), 2 => new(Center.X, Center.Y + 84), _ => new(Center.X - 104, Center.Y),
    };

    void Restart()
    {
        _seq.Clear(); _seq.Add(Rng.Next(4));
        _phase = Phase.Showing; _showIndex = 0; _nextStepAt = Clock.Now + 1; _score = 0;
    }

    void Flash(int i) { _flashPad = i; _flashUntil = Clock.Now + 0.32; Synth.Pad(i); }

    void Tick()
    {
        double t = Clock.Now;
        if (_phase == Phase.Showing && t >= _nextStepAt)
        {
            if (_showIndex < _seq.Count) { Flash(_seq[_showIndex++]); _nextStepAt = t + Math.Max(0.34, 0.62 - _seq.Count * 0.025); }
            else { _phase = Phase.Input; _inputIndex = 0; }
        }
        Invalidate();
    }

    void Press(int i)
    {
        if (_phase != Phase.Input) return;
        Flash(i);
        if (_seq[_inputIndex] == i)
        {
            if (++_inputIndex == _seq.Count)
            {
                _score = _seq.Count; _anim.Celebrate();
                _seq.Add(Rng.Next(4)); _phase = Phase.Showing; _showIndex = 0; _nextStepAt = Clock.Now + 1.1;
            }
        }
        else { _phase = Phase.Over; Synth.Buzz(); _anim.Dizzy(); OnEnd?.Invoke(_score); }
    }

    void Click_(PointF p)
    {
        if (_phase == Phase.Over) { if (Again.Contains(p)) Restart(); else if (Done.Contains(p)) Close(); return; }
        for (int i = 0; i < 4; i++) { var c = Pad(i); if (Math.Pow(p.X - c.X, 2) + Math.Pow(p.Y - c.Y, 2) < 36 * 36) { Press(i); return; } }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Up: Press(0); return true;
            case Keys.Right: Press(1); return true;
            case Keys.Down: Press(2); return true;
            case Keys.Left: Press(3); return true;
            case Keys.Escape: Close(); return true;
            case Keys.Space: if (_phase == Phase.Over) Restart(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        g.ScaleTransform(Sc, Sc);
        double t = Clock.Now;
        using var big = new Font("Segoe UI", 13, FontStyle.Bold, GraphicsUnit.Pixel);
        using var small = new Font("Segoe UI", 11.5f, GraphicsUnit.Pixel);
        g.DrawString("COPYCAT", big, Brushes.White, 14, 10);
        using (var y = new SolidBrush(Palette.Spark)) g.DrawString($"Round {_seq.Count}", big, y, W / 2 - 30, 10);
        g.DrawString($"Best {Progress.Shared.Data.BestCopycat}", small, Brushes.Gray, W - 70, 12);
        string hint = _phase == Phase.Showing ? "Watch buddy…" : _phase == Phase.Input ? "Your turn! Click the pads or use the arrow keys" : "";
        g.DrawString(hint, small, Brushes.Silver, 14, H - 26);

        for (int i = 0; i < 4; i++)
        {
            var c = Pad(i);
            bool lit = _flashPad == i && t < _flashUntil;
            float r = lit ? 36 : 32;
            if (lit) using (var glow = new SolidBrush(Palette.Rgb(_colors[i], 70))) g.FillEllipse(glow, c.X - r - 8, c.Y - r - 8, 2 * r + 16, 2 * r + 16);
            using (var b = new SolidBrush(Palette.Rgb(_colors[i], lit ? 255 : 97))) g.FillEllipse(b, c.X - r, c.Y - r, 2 * r, 2 * r);
            using var af = new Font("Segoe UI", 18, FontStyle.Bold, GraphicsUnit.Pixel);
            using var ab = new SolidBrush(Color.FromArgb(lit ? 255 : 150, 255, 255, 255));
            g.DrawString(_arrows[i], af, ab, c.X - 9, c.Y - 13);
        }

        float lx = 0, ly = 0;
        if (_flashPad is int f && t < _flashUntil) { var pc = Pad(f); lx = (pc.X - Center.X) / 104; ly = -(pc.Y - Center.Y) / 84; }
        var pose = _anim.GetPose(lx, ly);
        pose.Lift = Math.Min(pose.Lift, 12);
        Renderer.Draw(g, Progress.Shared.Data.Skin, Progress.Shared.Data.Hat, pose, Center, 1.05f, false);

        if (_phase == Phase.Over)
        {
            var card = new RectangleF(120, H - 150, 220, 120);
            using (var path = CornerWindow.RoundRect(card, 14)) using (var cb = new SolidBrush(Palette.Rgb(0x2B2D42))) g.FillPath(cb, path);
            using var title = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel);
            g.DrawString("Oops!", title, Brushes.White, card.X + 84, card.Y + 10);
            using (var y = new SolidBrush(Palette.Spark)) g.DrawString($"You reached round {_score + 1}  ·  +{Math.Min(60, _score * 3)} XP", small, y, card.X + 22, card.Y + 38);
            using (var path = CornerWindow.RoundRect(Again, 8)) using (var ob = new SolidBrush(Palette.Orange)) g.FillPath(ob, path);
            using (var path = CornerWindow.RoundRect(Done, 8)) using (var gb = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) g.FillPath(gb, path);
            g.DrawString("Play again", small, Brushes.White, Again.X + 14, Again.Y + 6);
            g.DrawString("Done", small, Brushes.White, Done.X + 28, Done.Y + 6);
        }
    }
}

/// Buddy's console: type commands (and find the secret ones).
class ConsoleForm : Form
{
    readonly TextBox _output = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical };
    readonly TextBox _input = new() { Dock = DockStyle.Bottom, BorderStyle = BorderStyle.FixedSingle };
    readonly Func<string, IEnumerable<string>> _run;

    public ConsoleForm(Func<string, IEnumerable<string>> run)
    {
        _run = run;
        Text = "Buddy's console";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size((int)(480 * DeviceDpi / 96f), (int)(260 * DeviceDpi / 96f));
        var mono = new Font("Consolas", 10);
        foreach (var t in new[] { _output, _input }) { t.Font = mono; t.BackColor = Palette.Rgb(0x0F1117); t.ForeColor = Palette.Rgb(0xCFE8D2); }
        Controls.Add(_output);
        Controls.Add(_input);
        _output.Text = "Buddy OS 1.0 — type 'help'" + Environment.NewLine;
        _input.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            var cmd = _input.Text.Trim();
            _input.Clear();
            if (cmd.Length == 0) return;
            if (cmd == "clear") { _output.Clear(); return; }
            if (cmd == "exit") { Close(); return; }
            _output.AppendText("> " + cmd + Environment.NewLine);
            foreach (var line in _run(cmd)) _output.AppendText(line + Environment.NewLine);
        };
        Shown += (_, _) => _input.Focus();
    }
}
