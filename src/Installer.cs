using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

namespace SpectreControl
{
    // Installation "un clic" :
    //  - copie l'exe dans C:\Program Files\SpectreControl (modifiable seulement en admin)
    //  - tâche "SpectreControl Apply" (SYSTEM) : réapplique le réglage au démarrage, à la connexion et en sortie de veille
    //  - tâche "SpectreControl GUI" : ouvre l'appli en admin sans fenêtre UAC à chaque fois
    //  - raccourcis Bureau + menu Démarrer (épinglables)
    static class Installer
    {
        public const string ApplyTask = "SpectreControl Apply";
        public const string GuiTask = "SpectreControl GUI";

        public static readonly string InstallDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SpectreControl");
        public static readonly string InstalledExe = Path.Combine(InstallDir, "SpectreControl.exe");

        static string DesktopShortcut
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Spectre Control.lnk"); }
        }
        static string StartMenuShortcut
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Spectre Control.lnk"); }
        }

        public static bool IsAdmin()
        {
            return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static bool IsRunningInstalled()
        {
            return string.Equals(Path.GetFullPath(Application.ExecutablePath), InstalledExe, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsInstalled()
        {
            return File.Exists(InstalledExe) && Run("schtasks.exe", "/query /tn \"" + ApplyTask + "\"") == 0;
        }

        // Lance l'appli installée en admin via la tâche planifiée (pas d'UAC). Renvoie false si la tâche n'existe pas.
        public static bool TryStartElevatedViaTask()
        {
            return File.Exists(InstalledExe) && Run("schtasks.exe", "/run /tn \"" + GuiTask + "\"") == 0;
        }

        public static void Install()
        {
            Directory.CreateDirectory(InstallDir);
            if (!IsRunningInstalled())
                File.Copy(Application.ExecutablePath, InstalledExe, true);

            // Restes de l'ancienne version PowerShell
            TryDelete(Path.Combine(InstallDir, "SpectreControl.ps1"));
            TryDelete(Path.Combine(Settings.Dir, "config.json"));

            string user = WindowsIdentity.GetCurrent().Name;
            CreateTask(ApplyTask, ApplyTaskXml());
            CreateTask(GuiTask, GuiTaskXml(user));

            CreateShortcut(DesktopShortcut);
            CreateShortcut(StartMenuShortcut);
        }

        public static void Uninstall()
        {
            Run("schtasks.exe", "/delete /tn \"" + ApplyTask + "\" /f");
            Run("schtasks.exe", "/delete /tn \"" + GuiTask + "\" /f");
            TryDelete(DesktopShortcut);
            TryDelete(StartMenuShortcut);
            try { Directory.Delete(Settings.Dir, true); } catch { }

            // L'exe ne peut pas se supprimer lui-même pendant qu'il tourne : on laisse cmd le faire juste après
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe",
                "/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"" + InstallDir + "\"");
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            Process.Start(psi);
        }

        static string ApplyTaskXml()
        {
            return TaskXml(
                "Réapplique la vitesse des ventilateurs choisie dans Spectre Control.",
                "<BootTrigger><Delay>PT20S</Delay></BootTrigger>" +
                "<LogonTrigger><Delay>PT10S</Delay></LogonTrigger>" +
                "<EventTrigger><Delay>PT5S</Delay><Subscription>" + SecurityElement.Escape(
                    "<QueryList><Query Id=\"0\" Path=\"System\"><Select Path=\"System\">" +
                    "*[System[Provider[@Name='Microsoft-Windows-Power-Troubleshooter'] and EventID=1]]" +
                    "</Select></Query></QueryList>") + "</Subscription></EventTrigger>",
                "<UserId>S-1-5-18</UserId>",
                "PT5M", "--apply");
        }

        static string GuiTaskXml(string user)
        {
            return TaskXml("Ouvre Spectre Control en administrateur.", "",
                "<UserId>" + SecurityElement.Escape(user) + "</UserId><LogonType>InteractiveToken</LogonType>",
                "PT0S", "");
        }

        static string TaskXml(string description, string triggers, string principal, string timeLimit, string args)
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" +
                "<Task version=\"1.4\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
                "<RegistrationInfo><Description>" + SecurityElement.Escape(description) + "</Description></RegistrationInfo>" +
                "<Triggers>" + triggers + "</Triggers>" +
                "<Principals><Principal id=\"Author\">" + principal + "<RunLevel>HighestAvailable</RunLevel></Principal></Principals>" +
                "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>" +
                "<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>" +
                "<ExecutionTimeLimit>" + timeLimit + "</ExecutionTimeLimit><AllowStartOnDemand>true</AllowStartOnDemand>" +
                "<Priority>4</Priority><Enabled>true</Enabled></Settings>" +
                "<Actions Context=\"Author\"><Exec><Command>" + SecurityElement.Escape(InstalledExe) + "</Command>" +
                (args.Length > 0 ? "<Arguments>" + args + "</Arguments>" : "") +
                "</Exec></Actions></Task>";
        }

        static void CreateTask(string name, string xml)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "SpectreControl-" + Guid.NewGuid().ToString("N") + ".xml");
            File.WriteAllText(tmp, xml, Encoding.Unicode);
            try
            {
                if (Run("schtasks.exe", "/create /tn \"" + name + "\" /xml \"" + tmp + "\" /f") != 0)
                    throw new InvalidOperationException(Lang.T("Impossible de créer la tâche planifiée « ", "Could not create scheduled task \"") +
                        name + Lang.T(" ».", "\"."));
            }
            finally { TryDelete(tmp); }
        }

        static void CreateShortcut(string path)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            object lnk = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
            Type t = lnk.GetType();
            t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { InstalledExe });
            t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { InstallDir });
            t.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "Vitesse des ventilateurs HP" });
            t.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
        }

        static int Run(string file, string args)
        {
            ProcessStartInfo psi = new ProcessStartInfo(file, args);
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
