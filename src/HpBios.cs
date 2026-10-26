using System;
using System.Management;
using System.Text;

namespace SpectreControl
{
    // HP BIOS interface exposed through WMI (class hpqBIntM, namespace root\wmi).
    // Same commands as HP Omen / Victus laptops: command 0x20008, the "type" selects the action.
    static class HpBios
    {
        const uint ThermalCommand = 0x20008;

        const uint GetFanCount = 0x10;
        const uint GetFanLevel = 0x2D;   // current speed, in hundreds of RPM, one byte per fan
        const uint SetFanLevel = 0x2E;   // fixed speed, same unit

        public static byte[] Call(uint type, byte[] data, int outSize)
        {
            using (ManagementClass inClass = new ManagementClass(@"root\wmi", "hpqBDataIn", null))
            using (ManagementObject inData = inClass.CreateInstance())
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM hpqBIntM"))
            {
                inData["Sign"] = Encoding.ASCII.GetBytes("SECU");
                inData["Command"] = ThermalCommand;
                inData["CommandType"] = type;
                inData["Size"] = (uint)data.Length;
                inData["hpqBData"] = data;

                foreach (ManagementObject bios in searcher.Get())
                {
                    using (bios)
                    {
                        string method = "hpqBIOSInt" + outSize;
                        ManagementBaseObject p = bios.GetMethodParameters(method);
                        p["InData"] = inData;
                        ManagementBaseObject result = bios.InvokeMethod(method, p, null);
                        ManagementBaseObject outData = (ManagementBaseObject)result["OutData"];
                        if (outSize == 0) return new byte[0];
                        return (outData["Data"] as byte[]) ?? new byte[0];
                    }
                }
            }
            throw new InvalidOperationException(Lang.T("Interface BIOS HP introuvable sur ce PC.", "HP BIOS interface not found on this PC."));
        }

        public static int FanCount()
        {
            byte[] d = Call(GetFanCount, new byte[4], 128);
            return d.Length > 0 ? d[0] : 0;
        }

        public static int[] FanRpm()
        {
            byte[] d = Call(GetFanLevel, new byte[4], 128);
            return new int[] { d.Length > 0 ? d[0] * 100 : 0, d.Length > 1 ? d[1] * 100 : 0 };
        }

        // Levels in hundreds of RPM. The BIOS keeps this speed until the next restart.
        public static void SetFanLevels(int fan1, int fan2)
        {
            Call(SetFanLevel, new byte[] { ToBiosLevel(fan1), ToBiosLevel(fan2), 0, 0 }, 0);
        }

        static byte ToBiosLevel(int level)
        {
            level = Math.Max(Settings.MinLevel, Math.Min(Settings.MaxLevel, level));
            // At maximum, ask for more than the hardware ceiling to be sure the fans run flat out
            return (byte)(level >= Settings.MaxLevel ? 60 : level);
        }
    }
}
