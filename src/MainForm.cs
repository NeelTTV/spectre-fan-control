using System;
using System.Diagnostics;
using System.Drawing;
using System.Management;
using System.Windows.Forms;

namespace SpectreControl
{
    class MainForm : Form
    {
        static readonly Color Bg = Color.FromArgb(22, 24, 29);
        static readonly Color Card = Color.FromArgb(34, 37, 44);
        static readonly Color Fg = Color.FromArgb(236, 238, 241);
        static readonly Color Muted = Color.FromArgb(148, 154, 164);
        static readonly Color Accent = Color.FromArgb(56, 148, 255);
        static readonly Color Warn = Color.FromArgb(255, 184, 77);

        readonly int fanCount;
        readonly Settings settings;
        // Le BIOS ne repasse en automatique qu'au redémarrage : on retient si un régime fixe est actif
        bool manualActive;

        readonly Label[] fanCaptions = new Label[2];
        readonly Label[] rpmLabels = new Label[2];
        readonly Panel[] rpmBars = new Panel[2];
        readonly int[] lastRpm = new int[2];
        readonly Label modeTitle;
        readonly RadioButton rbAuto, rbSame, rbSeparate;
        readonly TrackBar[] tracks = new TrackBar[2];
        readonly Label[] trackNames = new Label[2];
        readonly Label[] trackValues = new Label[2];
        readonly Button[] presets = new Button[3];
        readonly Button btnFr, btnEn, apply, restartLink, uninstall;
        readonly CheckBox cbCpu;
        readonly Label status;
        readonly Timer timer;
        // Dernier message affiché, dans les deux langues, pour le retraduire si on change de langue
        string statusFr = "", statusEn = "";

        public MainForm(int fanCount)
        {
            this.fanCount = Math.Min(2, fanCount);
            settings = Settings.Load();
            Lang.English = settings.Language == "en";
            manualActive = Installer.IsRunningInstalled() && settings.Mode != FanMode.Auto;

            // Mise en page en pixels à 96 ppp, mise à l'échelle par WinForms selon l'écran
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = Program.Title;
            ClientSize = new Size(470, 660);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg;
            ForeColor = Fg;
            Font = new Font("Segoe UI", 10f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            AddLabel(Program.Title, 24, 18, 300, 32, new Font("Segoe UI Semibold", 16f), Fg);
            AddLabel(ModelName(), 24, 50, 420, 20, Font, Muted);

            btnFr = MakeButton("FR", Card);
            btnFr.Bounds = new Rectangle(358, 22, 42, 28);
            btnFr.Click += delegate { SetLanguage("fr"); };
            btnEn = MakeButton("EN", Card);
            btnEn.Bounds = new Rectangle(404, 22, 42, 28);
            btnEn.Click += delegate { SetLanguage("en"); };
            Controls.Add(btnFr);
            Controls.Add(btnEn);

            // Vitesse en direct
            for (int i = 0; i < this.fanCount; i++)
            {
                Panel card = new Panel();
                card.Bounds = new Rectangle(24 + i * 216, 84, this.fanCount == 1 ? 422 : 206, 92);
                card.BackColor = Card;
                Controls.Add(card);
                fanCaptions[i] = MakeLabel("", 14, 10, 180, 20, Font, Muted);
                card.Controls.Add(fanCaptions[i]);
                rpmLabels[i] = MakeLabel("…", 12, 30, 190, 40, new Font("Segoe UI Semibold", 18f), Fg);
                // Fond opaque : un fond transparent est mal redessiné quand le texte change toutes les 2 s
                rpmLabels[i].BackColor = Card;
                fanCaptions[i].BackColor = Card;
                card.Controls.Add(rpmLabels[i]);
                rpmLabels[i].BringToFront();
                rpmBars[i] = new Panel();
                rpmBars[i].Bounds = new Rectangle(14, 74, 0, 5);
                rpmBars[i].BackColor = Accent;
                card.Controls.Add(rpmBars[i]);
                card.Paint += PaintBarTrack;
            }

            modeTitle = AddLabel("", 24, 194, 200, 22, new Font("Segoe UI Semibold", 11f), Fg);
            rbAuto = AddRadio(216);
            rbSame = AddRadio(242);
            rbSeparate = AddRadio(268);
            rbSeparate.Visible = this.fanCount > 1;

            for (int i = 0; i < 2; i++)
            {
                int y = 308 + i * 62;
                trackNames[i] = AddLabel("", 24, y, 260, 22, Font, Fg);
                trackValues[i] = AddLabel("", 300, y, 146, 22, new Font("Segoe UI Semibold", 10f), Fg);
                trackValues[i].TextAlign = ContentAlignment.TopRight;
                tracks[i] = new TrackBar();
                tracks[i].Minimum = Settings.MinLevel;
                tracks[i].Maximum = Settings.MaxLevel;
                tracks[i].TickFrequency = 3;
                tracks[i].LargeChange = 3;
                tracks[i].Bounds = new Rectangle(18, y + 22, 434, 40);
                tracks[i].BackColor = Bg;
                tracks[i].ValueChanged += delegate { UpdateControls(); };
                Controls.Add(tracks[i]);
            }

            int[] presetLevels = { 30, 40, Settings.MaxLevel };
            for (int i = 0; i < 3; i++)
            {
                presets[i] = MakeButton("", Card);
                presets[i].Bounds = new Rectangle(24 + i * 144, 438, 134, 34);
                int level = presetLevels[i];
                presets[i].Click += delegate
                {
                    if (rbAuto.Checked) rbSame.Checked = true;
                    tracks[0].Value = level;
                    tracks[1].Value = level;
                };
                Controls.Add(presets[i]);
            }

            cbCpu = new CheckBox();
            cbCpu.Bounds = new Rectangle(24, 486, 420, 24);
            cbCpu.Checked = settings.CpuMax;
            Controls.Add(cbCpu);

            apply = MakeButton("", Accent);
            apply.ForeColor = Color.White;
            apply.Font = new Font("Segoe UI Semibold", 10.5f);
            apply.Bounds = new Rectangle(24, 522, 422, 44);
            apply.Click += delegate { Apply(); };
            Controls.Add(apply);
            AcceptButton = apply;

            status = AddLabel("", 24, 576, 422, 42, Font, Muted);

            restartLink = MakeButton("", Bg);
            restartLink.ForeColor = Warn;
            restartLink.TextAlign = ContentAlignment.MiddleLeft;
            restartLink.Bounds = new Rectangle(20, 622, 200, 28);
            restartLink.Visible = false;
            restartLink.Click += delegate { Restart(); };
            Controls.Add(restartLink);

            uninstall = MakeButton("", Bg);
            uninstall.ForeColor = Muted;
            uninstall.TextAlign = ContentAlignment.MiddleRight;
            uninstall.Bounds = new Rectangle(306, 622, 144, 28);
            uninstall.Visible = Installer.IsRunningInstalled();
            uninstall.Click += delegate { Uninstall(); };
            Controls.Add(uninstall);

            if (settings.Mode == FanMode.Auto) rbAuto.Checked = true;
            else if (settings.Mode == FanMode.Separate && this.fanCount > 1) rbSeparate.Checked = true;
            else rbSame.Checked = true;
            // Après le choix du mode, sinon le mode "même vitesse" écraserait la valeur du ventilateur 2
            tracks[0].Value = settings.Fan1;
            tracks[1].Value = settings.Fan2;
            rbAuto.CheckedChanged += delegate { UpdateControls(); };
            rbSame.CheckedChanged += delegate { UpdateControls(); };
            rbSeparate.CheckedChanged += delegate { UpdateControls(); };

            if (!Installer.IsRunningInstalled())
                SetStatus("Mode sans installation : le réglage sera perdu au redémarrage.",
                          "Portable mode: your setting will be lost on restart.", Warn);
            else if (manualActive)
                SetStatus("Ton réglage est réappliqué à chaque démarrage et sortie de veille.",
                          "Your setting is re-applied at every startup and wake from sleep.", Muted);

            ApplyTexts();
            ResumeLayout(false);

            timer = new Timer();
            timer.Interval = 2000;
            timer.Tick += delegate { RefreshRpm(); };
            Shown += delegate { RefreshRpm(); timer.Start(); };
            FormClosed += delegate { timer.Stop(); };
        }

        void SetLanguage(string lang)
        {
            settings.Language = lang;
            Lang.English = lang == "en";
            try { settings.Save(); } catch { }
            ApplyTexts();
        }

        void ApplyTexts()
        {
            btnFr.BackColor = Lang.English ? Card : Accent;
            btnEn.BackColor = Lang.English ? Accent : Card;
            for (int i = 0; i < fanCount; i++)
            {
                fanCaptions[i].Text = Lang.T("Ventilateur ", "Fan ") + (i + 1);
                if (lastRpm[i] > 0) rpmLabels[i].Text = RpmText(lastRpm[i]);
            }
            modeTitle.Text = Lang.T("Mode", "Mode");
            rbAuto.Text = Lang.T("Automatique (gestion HP d'origine)", "Automatic (HP default control)");
            rbSame.Text = fanCount > 1
                ? Lang.T("Vitesse fixe, la même pour les deux", "Fixed speed, same for both fans")
                : Lang.T("Vitesse fixe", "Fixed speed");
            rbSeparate.Text = Lang.T("Réglage séparé pour chaque ventilateur", "Separate speed for each fan");
            presets[0].Text = Lang.T("Silencieux", "Quiet");
            presets[1].Text = Lang.T("Équilibré", "Balanced");
            presets[2].Text = Lang.T("Maximum", "Maximum");
            cbCpu.Text = Lang.T("Processeur en performances maximales", "Maximum CPU performance");
            apply.Text = Lang.T("Appliquer et garder au redémarrage", "Apply and keep after restart");
            restartLink.Text = Lang.T("Redémarrer maintenant", "Restart now");
            uninstall.Text = Lang.T("Désinstaller", "Uninstall");
            status.Text = Lang.T(statusFr, statusEn);
            UpdateControls();
        }

        FanMode SelectedMode
        {
            get { return rbAuto.Checked ? FanMode.Auto : rbSeparate.Checked ? FanMode.Separate : FanMode.Same; }
        }

        void UpdateControls()
        {
            FanMode mode = SelectedMode;
            if (mode == FanMode.Same && tracks[1].Value != tracks[0].Value) tracks[1].Value = tracks[0].Value;

            trackNames[0].Text = mode == FanMode.Separate ? Lang.T("Ventilateur 1", "Fan 1") :
                fanCount > 1 ? Lang.T("Les deux ventilateurs", "Both fans") : Lang.T("Ventilateur", "Fan");
            trackNames[1].Text = Lang.T("Ventilateur 2", "Fan 2");
            bool showSecond = mode == FanMode.Separate;
            trackNames[1].Visible = trackValues[1].Visible = tracks[1].Visible = showSecond;

            for (int i = 0; i < 2; i++)
            {
                tracks[i].Enabled = mode != FanMode.Auto;
                trackValues[i].Text = mode == FanMode.Auto ? "Auto" : LevelText(tracks[i].Value);
                trackValues[i].ForeColor = mode == FanMode.Auto ? Muted : Fg;
            }
        }

        static string LevelText(int level)
        {
            return level >= Settings.MaxLevel ? Lang.T("Maximum", "Maximum") : RpmText(level * 100);
        }

        static string RpmText(int rpm)
        {
            return rpm.ToString("N0") + Lang.T(" tr/min", " RPM");
        }

        void Apply()
        {
            try
            {
                settings.Mode = SelectedMode;
                settings.Fan1 = tracks[0].Value;
                settings.Fan2 = settings.Mode == FanMode.Same ? tracks[0].Value : tracks[1].Value;
                settings.CpuMax = cbCpu.Checked;
                settings.Save();
                settings.ApplyCpu();

                if (settings.Mode != FanMode.Auto)
                {
                    settings.ApplyFans();
                    manualActive = true;
                    restartLink.Visible = false;
                    SetStatus("✔  Appliqué. La vitesse change dans les 20 secondes.",
                              "✔  Applied. Fan speed changes within 20 seconds.", Fg);
                }
                else if (manualActive)
                {
                    restartLink.Visible = true;
                    SetStatus("Enregistré. Redémarre le PC pour rendre les ventilateurs à la gestion HP.",
                              "Saved. Restart the PC to give fan control back to HP.", Warn);
                }
                else
                {
                    SetStatus("✔  Enregistré. Les ventilateurs restent en gestion automatique HP.",
                              "✔  Saved. Fans stay under HP automatic control.", Fg);
                }
            }
            catch (Exception ex)
            {
                SetStatus("Erreur : " + ex.Message, "Error: " + ex.Message, Warn);
            }
        }

        void RefreshRpm()
        {
            try
            {
                int[] rpm = HpBios.FanRpm();
                for (int i = 0; i < fanCount; i++)
                {
                    lastRpm[i] = rpm[i];
                    rpmLabels[i].Text = RpmText(rpm[i]);
                    rpmBars[i].Width = (int)(BarFullWidth(rpmBars[i]) * Math.Min(1.0, rpm[i] / (Settings.MaxLevel * 100.0)));
                }
            }
            catch
            {
                for (int i = 0; i < fanCount; i++) rpmLabels[i].Text = "?";
            }
        }

        // Positions lues après mise à l'échelle DPI : la barre a la même marge à gauche et à droite
        static int BarFullWidth(Control bar)
        {
            return bar.Parent.ClientSize.Width - 2 * bar.Left;
        }

        void PaintBarTrack(object sender, PaintEventArgs e)
        {
            foreach (Panel bar in rpmBars)
            {
                if (bar == null || bar.Parent != sender) continue;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(52, 56, 66)))
                    e.Graphics.FillRectangle(b, bar.Left, bar.Top, BarFullWidth(bar), bar.Height);
            }
        }

        void Restart()
        {
            if (MessageBox.Show(Lang.T("Redémarrer le PC maintenant ?", "Restart the PC now?"), Program.Title,
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") { CreateNoWindow = true, UseShellExecute = false });
        }

        void Uninstall()
        {
            if (MessageBox.Show(Lang.T(
                    "Désinstaller Spectre Control ?\n\nLes ventilateurs repasseront en gestion HP au prochain redémarrage.",
                    "Uninstall Spectre Control?\n\nFans will return to HP control at the next restart."),
                    Program.Title, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            try
            {
                if (cbCpu.Checked) { settings.CpuMax = false; settings.ApplyCpu(); }
                Installer.Uninstall();
                MessageBox.Show(Lang.T("Spectre Control a été désinstallé.", "Spectre Control has been uninstalled."),
                    Program.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex)
            {
                SetStatus("Erreur : " + ex.Message, "Error: " + ex.Message, Warn);
            }
        }

        void SetStatus(string fr, string en, Color color)
        {
            statusFr = fr;
            statusEn = en;
            status.Text = Lang.T(fr, en);
            status.ForeColor = color;
        }

        static string ModelName()
        {
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Name FROM Win32_ComputerSystemProduct"))
                    foreach (ManagementObject o in s.Get())
                        using (o) return Convert.ToString(o["Name"]);
            }
            catch { }
            return "";
        }

        RadioButton AddRadio(int y)
        {
            RadioButton r = new RadioButton();
            r.Bounds = new Rectangle(24, y, 420, 24);
            Controls.Add(r);
            return r;
        }

        Label AddLabel(string text, int x, int y, int w, int h, Font font, Color color)
        {
            Label l = MakeLabel(text, x, y, w, h, font, color);
            Controls.Add(l);
            return l;
        }

        static Label MakeLabel(string text, int x, int y, int w, int h, Font font, Color color)
        {
            Label l = new Label();
            l.Text = text;
            l.Bounds = new Rectangle(x, y, w, h);
            l.Font = font;
            l.ForeColor = color;
            l.BackColor = Color.Transparent;
            return l;
        }

        static Button MakeButton(string text, Color back)
        {
            Button b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = back;
            b.ForeColor = Fg;
            b.Cursor = Cursors.Hand;
            return b;
        }
    }
}
