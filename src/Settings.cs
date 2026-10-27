using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace SpectreControl
{
    enum FanMode { Auto, Same, Separate }

    class Settings
    {
        // Hundreds of RPM. 0 = the BIOS minimum, same as HP's own idle state:
        // fan 1 stays at ~2,100 RPM (firmware floor) and fan 2 stops.
        public const int MinLevel = 0;
        public const int MaxLevel = 51;

        public static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SpectreControl");
        static readonly string FilePath = Path.Combine(Dir, "settings.ini");

        public FanMode Mode = FanMode.Same;
        public int Fan1 = MaxLevel;
        public int Fan2 = MaxLevel;
        public bool CpuMax = true;
        public string Language = Lang.Default;   // "en" or "fr"

        public static Settings Load()
        {
            Settings s = new Settings();
            if (!File.Exists(FilePath)) return s;
            try
            {
                Dictionary<string, string> v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) v[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
                string str;
                FanMode mode;
                int n;
                if (v.TryGetValue("Mode", out str) && Enum.TryParse(str, true, out mode)) s.Mode = mode;
                if (v.TryGetValue("Fan1", out str) && int.TryParse(str, out n)) s.Fan1 = Clamp(n);
                if (v.TryGetValue("Fan2", out str) && int.TryParse(str, out n)) s.Fan2 = Clamp(n);
                if (v.TryGetValue("CpuMax", out str)) s.CpuMax = str == "1" || str.Equals("true", StringComparison.OrdinalIgnoreCase);
                if (v.TryGetValue("Language", out str) && (str == "fr" || str == "en")) s.Language = str;
            }
            catch { }
            return s;
        }

        public void Save()
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllLines(FilePath, new string[] {
                "Mode=" + Mode, "Fan1=" + Fan1, "Fan2=" + Fan2, "CpuMax=" + (CpuMax ? "1" : "0"),
                "Language=" + Language
            });
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

        static void PowerCfg(string args)
        {
            ProcessStartInfo psi = new ProcessStartInfo("powercfg.exe", args);
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            using (Process p = Process.Start(psi)) p.WaitForExit(10000);
        }
    }
}
