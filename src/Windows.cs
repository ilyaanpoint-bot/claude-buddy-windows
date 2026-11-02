// Ordinary windows: buddy's console (where the secret commands live) and the Settings window.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace ClaudeBuddy;

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
        _output.Text = "Buddy OS 2.0 — type 'help'" + Environment.NewLine;
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

/// Settings: name, size, sound, chatter, the second buddy's skin and starting with Windows.
class SettingsForm : Form
{
    public SettingsForm(Action<double> onScale, Func<bool> startsWithWindows, Action toggleStartup, Action comeBack)
    {
        var p = Progress.Shared;
        var d = p.Data;
        Text = "Claude Buddy Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 9.5f);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Padding = new Padding(16), Dock = DockStyle.Fill };
        void Row(string label, Control c)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(4, 8, 8, 4) });
            c.Margin = new Padding(4, 4, 4, 4);
            grid.Controls.Add(c);
        }

        var name = new TextBox { Text = d.Name, Width = 180, MaxLength = 16 };
        name.Leave += (_, _) => p.SetName(name.Text);
        name.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; p.SetName(name.Text); } };
        Row("Name", name);

        var sizes = new (string, double)[] { ("Small", 0.75), ("Normal", 1), ("Large", 1.25), ("Huge", 1.5) };
        var size = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        foreach (var (n, _) in sizes) size.Items.Add(n);
        size.SelectedIndex = Math.Max(0, Array.FindIndex(sizes, s => Math.Abs(s.Item2 - d.Scale) < 0.01));
        size.SelectedIndexChanged += (_, _) => { d.Scale = sizes[size.SelectedIndex].Item2; p.Save(); onScale(d.Scale); };
        Row("Size", size);

        var sound = new CheckBox { Text = "Sound effects", Checked = !d.Muted, AutoSize = true };
        var volume = new TrackBar { Minimum = 0, Maximum = 100, TickFrequency = 25, Value = (int)(Math.Clamp(d.Volume, 0, 1) * 100), Width = 180, Enabled = !d.Muted };
        sound.CheckedChanged += (_, _) => { Synth.Muted = !sound.Checked; volume.Enabled = sound.Checked; Synth.Pop(); };
        volume.MouseUp += (_, _) => { d.Volume = volume.Value / 100.0; p.Save(); Synth.Pop(); };
        Row("Sound", sound);
        Row("Volume", volume);

        var quiet = new CheckBox { Text = "Quiet mode (no babbling or random chatter)", Checked = d.Quiet, AutoSize = true };
        quiet.CheckedChanged += (_, _) => { d.Quiet = quiet.Checked; p.Save(); };
        Row("", quiet);
        var thoughts = new CheckBox { Text = "Random thoughts and jokes", Checked = !d.NoThoughts, AutoSize = true };
        thoughts.CheckedChanged += (_, _) => { d.NoThoughts = !thoughts.Checked; p.Save(); };
        Row("Chatter", thoughts);

        var friend = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        var skins = Catalog.Items.Where(i => i.Kind == ItemKind.Skin && p.IsUnlocked(i)).ToList();
        foreach (var s in skins) friend.Items.Add(s.Name);
        friend.SelectedIndex = Math.Max(0, skins.FindIndex(s => s.Id == d.FriendSkin));
        friend.SelectedIndexChanged += (_, _) => { d.FriendSkin = skins[friend.SelectedIndex].Id; p.Save(); };
        Row("Second buddy", friend);

        var startup = new CheckBox { Text = "Start Claude Buddy with Windows", Checked = startsWithWindows(), AutoSize = true };
        startup.CheckedChanged += (_, _) => { if (startup.Checked != startsWithWindows()) toggleStartup(); };
        Row("Startup", startup);

        var back = new Button { Text = "Move buddy back to the corner", AutoSize = true };
        back.Click += (_, _) => comeBack();
        Row("", back);

        Controls.Add(grid);
    }
}
