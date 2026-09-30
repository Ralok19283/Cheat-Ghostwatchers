using System;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;

namespace GhostWatchersTrainer
{
    // Background check of version.txt on the repo's main branch against our version.
    // A newer DLL is downloaded next to ours as ".new"; the GhostWatchersUpdater preloader
    // patcher swaps it in on the next game start (a loaded DLL can't replace itself).
    // The repo slug comes from repo.txt next to the plugin DLL (copied there by the installer).
    internal static class UpdateChecker
    {
        public static volatile string NewerVersion; // null = none / unknown
        public static volatile bool Downloaded;

        public static void Start(string pluginDir)
        {
            new Thread(() =>
            {
                try
                {
                    string repoFile = Path.Combine(pluginDir, "repo.txt");
                    if (!File.Exists(repoFile)) return;
                    string repo = File.ReadAllText(repoFile).Trim();
                    if (repo.Length == 0) return;

                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    string raw = $"https://raw.githubusercontent.com/{repo}/main";
                    long t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    using (var wc = new WebClient())
                    {
                        wc.Headers[HttpRequestHeader.UserAgent] = "GhostWatchersTrainer";
                        string text = wc.DownloadString($"{raw}/version.txt?t={t}");
                        Match m = Regex.Match(text, "v?([0-9]+(\\.[0-9]+)+)");
                        if (!m.Success) return;
                        if (new Version(m.Groups[1].Value) <= new Version(TrainerVersion.Current)) return;
                        NewerVersion = m.Groups[1].Value;

                        string target = Path.Combine(pluginDir, "GhostWatchersTrainer.dll.new");
                        string tmp = target + ".part";
                        wc.DownloadFile($"{raw}/GhostWatchersTrainer.dll?t={t}", tmp);
                        if (new FileInfo(tmp).Length < 4096) { File.Delete(tmp); return; }
                        if (File.Exists(target)) File.Delete(target);
                        File.Move(tmp, target);
                        Downloaded = true;
                        Plugin.Log?.LogInfo($"Update {NewerVersion} downloaded, installs on next game start");
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log?.LogInfo("Update check failed: " + e.Message);
                }
            }) { IsBackground = true }.Start();
        }
    }
}
