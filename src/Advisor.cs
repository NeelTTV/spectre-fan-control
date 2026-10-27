using System;
using System.Collections.Generic;
using System.Management;

namespace SpectreControl
{
    enum Advice { Unknown, Lower, Ok, Raise, Hot }

    // Reads CPU temperature and load from Windows performance counters (no driver needed)
    // and says whether the fans should spin faster for what the PC is doing right now.
    class Advisor
    {
        // ACPI thermal zone: follows the CPU package temperature, updated every ~15 s on the Spectre x360 16
        const string ThermalQuery = "SELECT HighPrecisionTemperature, PercentPassiveLimit, ThrottleReasons FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation";
        const string LoadQuery = "SELECT PercentProcessorUtility FROM Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name='_Total'";

        public const double HotC = 90, WarmC = 80, CoolC = 65;

        readonly Queue<double> loadHistory = new Queue<double>();

        public double TempC = double.NaN;
        public double AvgLoad;          // % over the last ~30 s
        public bool Throttling;         // Windows reports the CPU is being slowed down by heat

        public void Sample()
        {
            double temp = double.NaN;
            bool throttling = false;
            using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\cimv2", ThermalQuery))
                foreach (ManagementObject o in s.Get())
                    using (o)
                    {
                        // Tenths of a kelvin
                        double c = Convert.ToDouble(o["HighPrecisionTemperature"]) / 10.0 - 273.15;
                        if (c > 0 && (double.IsNaN(temp) || c > temp)) temp = c;
                        if (Convert.ToUInt32(o["PercentPassiveLimit"]) < 100 || Convert.ToUInt32(o["ThrottleReasons"]) != 0)
                            throttling = true;
                    }

            double load = 0;
            using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\cimv2", LoadQuery))
                foreach (ManagementObject o in s.Get())
                    using (o) load = Math.Min(100, Convert.ToDouble(o["PercentProcessorUtility"]));

            loadHistory.Enqueue(load);
            while (loadHistory.Count > 15) loadHistory.Dequeue();
            double sum = 0;
            foreach (double l in loadHistory) sum += l;

            lock (this)
            {
                TempC = temp;
                Throttling = throttling;
                AvgLoad = sum / loadHistory.Count;
            }
        }

        public Advice Evaluate(int fastestFanRpm)
        {
            lock (this)
            {
                if (double.IsNaN(TempC)) return Advice.Unknown;
                bool fansNearMax = fastestFanRpm >= (Settings.MaxLevel - 3) * 100;
                if (Throttling || TempC >= HotC) return fansNearMax ? Advice.Hot : Advice.Raise;
                if (TempC >= WarmC) return fansNearMax ? Advice.Ok : Advice.Raise;
                if (TempC < CoolC && AvgLoad < 15 && fastestFanRpm > 4000) return Advice.Lower;
                return Advice.Ok;
            }
        }
    }
}
