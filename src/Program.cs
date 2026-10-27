using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Spectre Control")]
[assembly: AssemblyProduct("Spectre Control")]
[assembly: AssemblyDescription("Fan control for HP laptops")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]

namespace SpectreControl
{
    static class Program
    {
        public const string Title = "Spectre Control";

        [STAThread]
        static int Main(string[] args)
        {
            if (Array.IndexOf(args, "--apply") >= 0) return ApplyAtStartup();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Lang.English = Settings.Load().Language == "en";

            if (!Installer.IsAdmin())
            {
                // Installed copy: the scheduled task opens it as admin with no UAC prompt
                if (Installer.IsRunningInstalled() && Installer.TryStartElevatedViaTask()) return 0;
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath);
                    psi.UseShellExecute = true;
                    psi.Verb = "runas";
                    Process.Start(psi);
                }
                catch (Win32Exception)
                {
                    MessageBox.Show(Lang.T(
                        "Spectre Control a besoin des droits administrateur pour piloter les ventilateurs.\n\n" +
                        "Relance l'application et clique sur « Oui » dans la fenêtre de Windows.",
                        "Spectre Control needs administrator rights to control the fans.\n\n" +
                        "Start the app again and click \"Yes\" in the Windows prompt."),
                        Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return 0;
            }

            int fans;
            try { fans = HpBios.FanCount(); }
            catch { fans = 0; }
            if (fans <= 0)
            {
                MessageBox.Show(Lang.T(
                    "Ce PC ne semble pas compatible : l'interface BIOS HP de contrôle des ventilateurs est introuvable.\n\n" +
                    "Spectre Control fonctionne sur les PC portables HP récents qui la proposent (Spectre, Envy, Omen, Victus…).",
                    "This PC does not seem compatible: the HP BIOS fan control interface was not found.\n\n" +
                    "Spectre Control works on recent HP laptops that provide it (Spectre, Envy, Omen, Victus…)."),
                    Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }

            if (!Installer.IsRunningInstalled())
            {
                DialogResult choice = MessageBox.Show(Lang.T(
                    "Installer Spectre Control sur ce PC ? (recommandé)\n\n" +
                    "•  Ton réglage est réappliqué tout seul à chaque démarrage\n" +
                    "•  Plus de fenêtre de confirmation à l'ouverture\n" +
                    "•  Raccourci sur le Bureau et dans le menu Démarrer\n\n" +
                    "Non = utiliser sans installer (le réglage est perdu au redémarrage).",
                    "Install Spectre Control on this PC? (recommended)\n\n" +
                    "•  Your setting is re-applied automatically at every startup\n" +
                    "•  No more confirmation prompt when opening the app\n" +
                    "•  Shortcut on the Desktop and in the Start menu\n\n" +
                    "No = use without installing (setting is lost on restart)."),
                    Title, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (choice == DialogResult.Cancel) return 0;
                if (choice == DialogResult.Yes)
                {
                    try
                    {
                        Installer.Install();
                        Process.Start(Installer.InstalledExe);
                        return 0;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(Lang.T(
                            "L'installation a échoué :\n" + ex.Message + "\n\nSi Spectre Control est déjà ouvert, ferme-le puis réessaie.",
                            "Installation failed:\n" + ex.Message + "\n\nIf Spectre Control is already open, close it and try again."),
                            Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            else if (!Installer.IsInstalled())
            {
                // Tasks were deleted by hand: repair them
                try { Installer.Install(); } catch { }
            }

            bool firstInstance;
            using (Mutex mutex = new Mutex(true, @"Global\SpectreControl.GUI", out firstInstance))
            {
                if (!firstInstance)
                {
                    BringExistingToFront();
                    return 0;
                }
                Application.Run(new MainForm(fans));
            }
            return 0;
        }

        // Run by the SYSTEM task at startup, at sign-in and on wake from sleep
        static int ApplyAtStartup()
        {
            Settings s = Settings.Load();
            if (s.Mode == FanMode.Auto)
            {
                Settings.Log("Mode=Auto, nothing to do");
                return 0;
            }
            // Right after boot the WMI interface may not be ready yet
            for (int attempt = 1; attempt <= 6; attempt++)
            {
                try
                {
                    s.ApplyFans();
                    Settings.Log("Mode=" + s.Mode + " Fan1=" + s.Fan1 + " Fan2=" + s.Fan2);
                    return 0;
                }
                catch (Exception ex)
                {
                    if (attempt == 6) { Settings.Log("Failed: " + ex.Message); return 1; }
                    Thread.Sleep(5000);
                }
            }
            return 1;
        }

        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);

        static void BringExistingToFront()
        {
            int me = Process.GetCurrentProcess().Id;
            foreach (Process p in Process.GetProcessesByName("SpectreControl"))
            {
                if (p.Id != me && p.MainWindowHandle != IntPtr.Zero)
                {
                    ShowWindow(p.MainWindowHandle, 9);
                    SetForegroundWindow(p.MainWindowHandle);
                }
            }
        }
    }
}
