using System;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;

namespace GhostWatchersTrainer
{
    // Background check of version.txt on the repo's main branch against our version.
    // The repo slug comes from repo.txt next to the plugin DLL (copied there by the installer).
    internal static class UpdateChecker
    {
        public static volatile string NewerVersion; // null = none / unknown

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
                    using (var wc = new WebClient())
                    {
                        wc.Headers[HttpRequestHeader.UserAgent] = "GhostWatchersTrainer";
                        long t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        string text = wc.DownloadString($"https://raw.githubusercontent.com/{repo}/main/version.txt?t={t}");
                        Match m = Regex.Match(text, "v?([0-9]+(\\.[0-9]+)+)");
                        if (!m.Success) return;
                        if (new Version(m.Groups[1].Value) > new Version(TrainerVersion.Current))
                            NewerVersion = m.Groups[1].Value;
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
