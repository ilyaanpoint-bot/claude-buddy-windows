// Updates: asks GitHub for the latest release (once a day, or when you ask) and, if you say
// yes, re-runs the installer, which swaps in the new ClaudeBuddy.exe and restarts buddy.

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;

namespace ClaudeBuddy;

static class Updater
{
    const string Repo = "ilyaanpoint-bot/claude-buddy-windows";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    static bool _checking;

    static Version Current => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
    public static string CurrentText => $"{Current.Major}.{Current.Minor}.{Math.Max(0, Current.Build)}";

    /// The newer version on GitHub, once a check has found one.
    public static string Available { get; private set; }

    static bool IsNewer(Version v) =>
        (v.Major, v.Minor, Math.Max(0, v.Build)).CompareTo((Current.Major, Current.Minor, Math.Max(0, Current.Build))) > 0;

    public static async void Check(BuddyApp app, bool manual)
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var data = Progress.Shared.Data;
        if (_checking || (!manual && data.LastUpdateCheck == today)) return;
        _checking = true;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
            req.Headers.UserAgent.ParseAdd("ClaudeBuddy/" + CurrentText);
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var res = await Http.SendAsync(req);
            res.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            data.LastUpdateCheck = today;
            Progress.Shared.Save();

            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest) || !IsNewer(latest))
            {
                Available = null;
                if (manual) MessageBox.Show($"You're up to date! (v{CurrentText})", "Claude Buddy", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Available = $"{latest.Major}.{latest.Minor}.{Math.Max(0, latest.Build)}";
            if (!manual) { app.Announce($"Update available! v{Available} ✨", "right-click me → Update"); return; }
            var answer = MessageBox.Show($"Claude Buddy v{Available} is out (you have v{CurrentText}).\n\nUpdate now? Buddy will restart, and your XP and unlocks are kept.",
                                         "Claude Buddy update", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer == DialogResult.Yes) Install();
        }
        catch (Exception e)
        {
            Program.Log(e);
            if (manual) MessageBox.Show("Couldn't reach GitHub to check for updates.\n\n" + e.Message, "Claude Buddy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { _checking = false; }
    }

    /// Runs the latest install.ps1 with -Update in a visible PowerShell window. It stops buddy,
    /// downloads the new exe, refreshes the hooks and starts buddy again.
    static void Install()
    {
        var script = $"https://raw.githubusercontent.com/{Repo}/main/install.ps1";
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"& ([scriptblock]::Create((Invoke-RestMethod '{script}'))) -Update\"",
            WorkingDirectory = Path.GetTempPath(),
            UseShellExecute = true,
        });
    }
}
