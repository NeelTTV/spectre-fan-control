using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;

namespace SpectreControl
{
    class ReleaseInfo
    {
        public Version Version;
        public string Tag;
        public string DownloadUrl;
        public string PageUrl;
    }

    // Checks GitHub for a newer release and installs it in place
    static class Updater
    {
        public const string Repo = "NeelTTV/spectre-fan-control";
        const string LatestApi = "https://api.github.com/repos/" + Repo + "/releases/latest";
        public const string ReleasesPage = "https://github.com/" + Repo + "/releases/latest";

        public static Version Current
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version; }
        }

        public static string Format(Version v)
        {
            return v.Major + "." + v.Minor + "." + Math.Max(0, v.Build);
        }

        public static ReleaseInfo GetLatest()
        {
            // .NET Framework does not enable TLS 1.2 by default, and GitHub requires it
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string json;
            using (WebClient web = new WebClient())
            {
                web.Headers[HttpRequestHeader.UserAgent] = "SpectreControl/" + Format(Current);
                web.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                json = web.DownloadString(LatestApi);
            }

            // Only three fields are needed: no JSON library, just targeted patterns
            Match tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([0-9][^\"]*)\"");
            Match asset = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]*/SpectreControl\\.exe)\"");
            Match page = Regex.Match(json, "\"html_url\"\\s*:\\s*\"(https://github\\.com/[^\"]*/releases/tag/[^\"]*)\"");
            Version version;
            if (!tag.Success || !Version.TryParse(tag.Groups[1].Value, out version))
                throw new InvalidOperationException(Lang.T("Réponse GitHub inattendue.", "Unexpected response from GitHub."));

            ReleaseInfo r = new ReleaseInfo();
            r.Version = Normalize(version);
            r.Tag = tag.Groups[1].Value;
            r.DownloadUrl = asset.Success ? asset.Groups[1].Value : null;
            r.PageUrl = page.Success ? page.Groups[1].Value : ReleasesPage;
            return r;
        }

        public static bool IsNewer(ReleaseInfo r)
        {
            return r.Version > Normalize(Current);
        }

        // "1.3" and "1.3.0.0" must compare equal
        static Version Normalize(Version v)
        {
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }

        public static string Download(ReleaseInfo r)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string dir = Path.Combine(Path.GetTempPath(), "SpectreControl-update");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "SpectreControl-" + r.Tag + ".exe");
            using (WebClient web = new WebClient())
            {
                web.Headers[HttpRequestHeader.UserAgent] = "SpectreControl/" + Format(Current);
                web.DownloadFile(r.DownloadUrl, path);
            }
            // A truncated download would leave a broken exe: make sure it is a real program
            if (new FileInfo(path).Length < 20000 || FileVersionInfo.GetVersionInfo(path).FileVersion == null)
                throw new InvalidOperationException(Lang.T("Téléchargement incomplet.", "Incomplete download."));
            return path;
        }

        // Starts the downloaded exe, which waits for this process to exit, then installs itself over the old copy
        public static void LaunchInstaller(string downloadedExe)
        {
            ProcessStartInfo psi = new ProcessStartInfo(downloadedExe,
                "--install-update " + Process.GetCurrentProcess().Id);
            psi.UseShellExecute = false;
            Process.Start(psi);
        }

        // Runs in the downloaded exe
        public static int InstallUpdate(string[] args)
        {
            int i = Array.IndexOf(args, "--install-update");
            int pid;
            if (i + 1 < args.Length && int.TryParse(args[i + 1], out pid))
            {
                try { using (Process old = Process.GetProcessById(pid)) old.WaitForExit(20000); }
                catch (ArgumentException) { }   // already gone
            }
            // The old exe can stay locked a moment after its window closes
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    Installer.Install();
                    break;
                }
                catch (IOException)
                {
                    if (attempt == 10) throw;
                    Thread.Sleep(1000);
                }
            }
            Settings.Log("Updated to " + Format(Current));
            Process.Start(Installer.InstalledExe);
            return 0;
        }
    }
}
