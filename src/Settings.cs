using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace SpectreControl
{
    enum FanMode { Auto, Same, Separate }

    // A named set of fan + CPU settings the user can create, rename and switch between
    class Profile
    {
        public string Name;
        public FanMode Mode = FanMode.Same;
        public int Fan1 = Settings.MaxLevel;
        public int Fan2 = Settings.MaxLevel;
        public bool CpuMax = true;

        public Profile(string name) { Name = name; }

        public Profile(string name, FanMode mode, int level, bool cpuMax)
        {
            Name = name;
            Mode = mode;
            Fan1 = Fan2 = level;
            CpuMax = cpuMax;
        }

        public void ApplyFans()
        {
            if (Mode == FanMode.Auto) return;
            int f2 = Mode == FanMode.Same ? Fan1 : Fan2;
            HpBios.SetFanLevels(Fan1, f2);
        }

        public void ApplyCpu()
        {
            const string sub = "54533251-82be-4824-96c1-47b60b740d00";
            const string boost = "be337238-0d82-4146-a960-4f3749d470c7";  // 2 = aggressive
            const string epp = "36687f9e-e3a5-4dbf-b1dc-15eb381c6863";    // 0 = max performance
            const string cores = "0cc5b647-c1df-4637-891a-dec35c318583";  // % of cores never parked
            foreach (string s in new string[] { boost, epp, cores })
                PowerCfg("-attributes " + sub + " " + s + " -ATTRIB_HIDE");
            PowerCfg("/setacvalueindex SCHEME_CURRENT " + sub + " " + boost + (CpuMax ? " 2" : " 1"));
            PowerCfg("/setacvalueindex SCHEME_CURRENT " + sub + " " + epp + (CpuMax ? " 0" : " 25"));
            PowerCfg("/setacvalueindex SCHEME_CURRENT " + sub + " " + cores + (CpuMax ? " 100" : " 10"));
            PowerCfg("/setactive SCHEME_CURRENT");
        }

        public override string ToString() { return Name; }

        static void PowerCfg(string args)
        {
            ProcessStartInfo psi = new ProcessStartInfo("powercfg.exe", args);
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            using (Process p = Process.Start(psi)) p.WaitForExit(10000);
        }
    }

    class Settings
    {
        // Hundreds of RPM. 0 = the BIOS minimum, same as HP's own idle state:
        // fan 1 stays at ~2,100 RPM (firmware floor) and fan 2 stops.
        public const int MinLevel = 0;
        public const int MaxLevel = 51;
        public const int MaxNameLength = 32;
        // Measured on the Spectre x360 16: fan 1 keeps spinning at this speed even at level 0
        public const int Fan1FloorRpm = 2100;

        public static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SpectreControl");
        static readonly string FilePath = Path.Combine(Dir, "settings.ini");

        public List<Profile> Profiles = new List<Profile>();
        public int Active;
        public string Language = Lang.Default;   // "en" or "fr"
        public bool KeepAfterRestart = true;     // false: the startup task leaves the fans to HP
        public bool ShowAdvice = true;           // the "should the fans speed up?" panel

        public Profile Current { get { return Profiles[Active]; } }

        static List<Profile> DefaultProfiles()
        {
            return new List<Profile> {
                new Profile("Quiet", FanMode.Same, 20, false),
                new Profile("Balanced", FanMode.Same, 35, true),
                new Profile("Max performance", FanMode.Same, MaxLevel, true),
            };
        }

        // settings.ini:
        //   Language=en
        //   Active=2
        //   Profile.0.Name=Quiet
        //   Profile.0.Mode=Same  (+ Fan1, Fan2, CpuMax)
        // Older versions stored a single unnamed setting as Mode=, Fan1=, Fan2=, CpuMax=.
        public static Settings Load()
        {
            Settings s = new Settings();
            Dictionary<string, string> v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(FilePath))
                    foreach (string line in File.ReadAllLines(FilePath))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0) v[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                    }
            }
            catch { }

            string str;
            int n;
            if (v.TryGetValue("Language", out str) && (str == "fr" || str == "en")) s.Language = str;
            if (v.TryGetValue("KeepAfterRestart", out str)) s.KeepAfterRestart = str != "0";
            if (v.TryGetValue("ShowAdvice", out str)) s.ShowAdvice = str != "0";

            for (int i = 0; v.ContainsKey("Profile." + i + ".Name"); i++)
                s.Profiles.Add(ReadProfile(v, "Profile." + i + ".", CleanName(v["Profile." + i + ".Name"], "Profile " + (i + 1))));

            if (s.Profiles.Count == 0 && v.ContainsKey("Mode"))
                s.Profiles.Add(ReadProfile(v, "", "Profile 1"));

            if (s.Profiles.Count == 0)
            {
                s.Profiles = DefaultProfiles();
                s.Active = s.Profiles.Count - 1;
            }
            else if (v.TryGetValue("Active", out str) && int.TryParse(str, out n))
            {
                s.Active = Math.Max(0, Math.Min(s.Profiles.Count - 1, n));
            }
            return s;
        }

        static Profile ReadProfile(Dictionary<string, string> v, string prefix, string name)
        {
            Profile p = new Profile(name);
            string str;
            FanMode mode;
            int n;
            if (v.TryGetValue(prefix + "Mode", out str) && Enum.TryParse(str, true, out mode)) p.Mode = mode;
            if (v.TryGetValue(prefix + "Fan1", out str) && int.TryParse(str, out n)) p.Fan1 = Clamp(n);
            if (v.TryGetValue(prefix + "Fan2", out str) && int.TryParse(str, out n)) p.Fan2 = Clamp(n);
            if (v.TryGetValue(prefix + "CpuMax", out str)) p.CpuMax = str == "1" || str.Equals("true", StringComparison.OrdinalIgnoreCase);
            return p;
        }

        public void Save()
        {
            List<string> lines = new List<string> {
                "Language=" + Language, "KeepAfterRestart=" + (KeepAfterRestart ? "1" : "0"),
                "ShowAdvice=" + (ShowAdvice ? "1" : "0"), "Active=" + Active };
            for (int i = 0; i < Profiles.Count; i++)
            {
                Profile p = Profiles[i];
                string k = "Profile." + i + ".";
                lines.Add(k + "Name=" + p.Name);
                lines.Add(k + "Mode=" + p.Mode);
                lines.Add(k + "Fan1=" + p.Fan1);
                lines.Add(k + "Fan2=" + p.Fan2);
                lines.Add(k + "CpuMax=" + (p.CpuMax ? "1" : "0"));
            }
            Directory.CreateDirectory(Dir);
            File.WriteAllLines(FilePath, lines.ToArray());
        }

        // Names live on one ini line: no line breaks, trimmed, length-limited
        public static string CleanName(string name, string fallback)
        {
            name = (name ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength).Trim();
            return name.Length > 0 ? name : fallback;
        }

        public static int Clamp(int level)
        {
            return Math.Max(MinLevel, Math.Min(MaxLevel, level));
        }

        public static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(Path.Combine(Dir, "apply.log"),
                    DateTime.Now.ToString("s") + "  " + message + Environment.NewLine);
            }
            catch { }
        }
    }
}
