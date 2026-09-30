using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;

namespace GhostWatchersTrainer.Updater
{
    // BepInEx preloader patcher: runs before any plugin is loaded, so it can safely swap the
    // trainer DLL. 1) apply an update the plugin downloaded last session (.new file),
    // 2) check GitHub (version.txt on main) and install a newer DLL right away.
    // Failure is always silent: the game starts with whatever is installed.
    public static class AutoUpdater
    {
        public static IEnumerable<string> TargetDLLs { get; } = new string[0];

        public static void Patch(AssemblyDefinition assembly) { }

        private static readonly ManualLogSource Log = Logger.CreateLogSource("GW Trainer Updater");

        private const string DllName = "GhostWatchersTrainer.dll";

        public static void Initialize()
        {
            try
            {
                string pluginDir = Path.Combine(Paths.PluginPath, "");
                string dll = Path.Combine(pluginDir, DllName);
                string pending = dll + ".new";

                if (File.Exists(pending))
                {
                    if (LooksLikeDll(pending))
                    {
                        File.Copy(pending, dll, true);
                        Log.LogInfo("Installed downloaded update " + FileVersion(dll));
                    }
                    File.Delete(pending);
                }

                string repoFile = Path.Combine(pluginDir, "repo.txt");
                if (!File.Exists(repoFile)) return;
                string repo = File.ReadAllText(repoFile).Trim();
                if (repo.Length == 0) return;

                string raw = $"https://raw.githubusercontent.com/{repo}/main";
                long t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                string versionFile = dll + ".version";
                Download($"{raw}/version.txt?t={t}", versionFile);
                Version remote = new Version(File.ReadAllText(versionFile).Trim().TrimStart('v', 'V'));
                File.Delete(versionFile);
                Version local = FileVersion(dll);
                if (remote <= local) { Log.LogInfo($"Trainer is up to date ({local})"); return; }

                string tmp = dll + ".download";
                Download($"{raw}/{DllName}?t={t}", tmp);
                if (!LooksLikeDll(tmp)) { File.Delete(tmp); Log.LogWarning("Downloaded update looks broken, skipped"); return; }
                File.Copy(tmp, dll, true);
                File.Delete(tmp);
                Log.LogInfo($"Updated trainer {local} -> {remote}");
            }
            catch (Exception e)
            {
                Log.LogInfo("Update check at startup skipped: " + e.Message);
            }
        }

        // Mono's own TLS isn't initialised this early in startup ("TLS Support not available"),
        // so use Windows' built-in curl.exe (Schannel) and fall back to WebClient.
        private static void Download(string url, string file)
        {
            string curl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "curl.exe");
            if (File.Exists(curl))
            {
                var psi = new ProcessStartInfo(curl, $"-fsSL --max-time 8 -A GhostWatchersTrainer-Updater -o \"{file}\" \"{url}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (Process p = Process.Start(psi))
                {
                    if (!p.WaitForExit(10000)) { try { p.Kill(); } catch { } throw new Exception("curl timed out"); }
                    if (p.ExitCode == 0 && File.Exists(file)) return;
                    throw new Exception("curl exit code " + p.ExitCode);
                }
            }

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var wc = new TimeoutWebClient(5000))
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "GhostWatchersTrainer-Updater";
                wc.DownloadFile(url, file);
            }
        }

        private static Version FileVersion(string path)
        {
            try
            {
                if (!File.Exists(path)) return new Version(0, 0, 0);
                string v = FileVersionInfo.GetVersionInfo(path).FileVersion;
                return string.IsNullOrEmpty(v) ? new Version(0, 0, 0) : new Version(v);
            }
            catch { return new Version(0, 0, 0); }
        }

        private static bool LooksLikeDll(string path)
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length < 4096) return false;
            using (var fs = fi.OpenRead())
                return fs.ReadByte() == 'M' && fs.ReadByte() == 'Z';
        }

        private class TimeoutWebClient : WebClient
        {
            private readonly int timeoutMs;
            public TimeoutWebClient(int timeoutMs) { this.timeoutMs = timeoutMs; }

            protected override WebRequest GetWebRequest(Uri address)
            {
                WebRequest r = base.GetWebRequest(address);
                if (r != null) r.Timeout = timeoutMs;
                return r;
            }
        }
    }
}
