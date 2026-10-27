using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Management;
using System.Windows.Forms;

namespace SpectreControl
{
    class MainForm : Form
    {
        internal static readonly Color Bg = Color.FromArgb(22, 24, 29);
        internal static readonly Color Card = Color.FromArgb(34, 37, 44);
        internal static readonly Color Fg = Color.FromArgb(236, 238, 241);
        internal static readonly Color Muted = Color.FromArgb(148, 154, 164);
        // Accent for text and bars; AccentFill behind white text (3.1:1 contrast was too low, this is 5:1)
        internal static readonly Color Accent = Color.FromArgb(56, 148, 255);
        internal static readonly Color AccentFill = Color.FromArgb(20, 110, 215);
        // Status colors checked with a protanopia / deuteranopia / tritanopia simulation:
        // every pair stays distinguishable (CIE76 ΔE >= 29), and each state also has its own symbol
        internal static readonly Color Warn = Color.FromArgb(240, 190, 40);
        static readonly Color Danger = Color.FromArgb(240, 85, 60);
        static readonly Color Okay = Color.FromArgb(210, 214, 220);
        static readonly Color Cool = Color.FromArgb(86, 170, 255);

        readonly int fanCount;
        readonly Settings settings;
        // The BIOS only returns to automatic control after a restart: remember whether a fixed speed is active
        bool manualActive;
        // Layout is done by hand in pixels: S() converts 96-DPI units to the screen's DPI
        readonly float scale;

        readonly Label title, model;
        readonly Panel[] cards = new Panel[2];
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
        readonly Button btnFr, btnEn, apply, restartLink, uninstall, updateButton;
        ReleaseInfo pendingUpdate;
        bool checkingUpdate;
        readonly CheckBox cbCpu, cbKeep, cbAdvice;
        readonly Label status;
        readonly Timer timer;
        readonly ComboBox profileBox;
        readonly Button btnAdd, btnRename, btnDelete;
        readonly ToolTip tips = new ToolTip();
        readonly Advisor advisor = new Advisor();
        readonly Panel advicePanel;
        readonly Label adviceIcon, adviceText, adviceStats;
        bool sampling;
        bool loading;
        // Last status message in both languages, so it can be re-translated when the language changes
        string statusFr = "", statusEn = "";

        readonly Font rpmFontLarge = new Font("Segoe UI Semibold", 18f);
        readonly Font rpmFontSmall = new Font("Segoe UI Semibold", 14f);

        int S(int v) { return (int)Math.Round(v * scale); }

        public MainForm(int fanCount)
        {
            this.fanCount = Math.Min(2, fanCount);
            settings = Settings.Load();
            Lang.English = settings.Language == "en";
            manualActive = Installer.IsRunningInstalled() && settings.Current.Mode != FanMode.Auto;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;

            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            Text = Program.Title;
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            // Vertical scrolling only: the layout always fits the width
            HorizontalScroll.Maximum = 0;
            AutoScroll = false;
            HorizontalScroll.Visible = false;
            HorizontalScroll.Enabled = false;
            AutoScroll = true;
            DoubleBuffered = true;
            BackColor = Bg;
            ForeColor = Fg;
            Font = new Font("Segoe UI", 10f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            title = AddLabel(Program.Title, new Font("Segoe UI Semibold", 16f), Fg);
            model = AddLabel(ModelName(), Font, Muted);

            btnFr = MakeButton("FR", Card);
            btnFr.Click += delegate { SetLanguage("fr"); };
            btnEn = MakeButton("EN", Card);
            btnEn.Click += delegate { SetLanguage("en"); };
            Controls.Add(btnFr);
            Controls.Add(btnEn);

            // Profile bar: owner-drawn so the list is dark and the active profile gets a check mark
            profileBox = new ComboBox();
            profileBox.DropDownStyle = ComboBoxStyle.DropDownList;
            profileBox.DrawMode = DrawMode.OwnerDrawFixed;
            profileBox.FlatStyle = FlatStyle.Flat;
            profileBox.BackColor = Card;
            profileBox.ForeColor = Fg;
            profileBox.DrawItem += DrawProfileItem;
            Controls.Add(profileBox);

            // Segoe MDL2 Assets glyphs: add, rename (pencil), delete
            btnAdd = MakeIconButton("");
            btnRename = MakeIconButton("");
            btnDelete = MakeIconButton("");
            btnAdd.Click += delegate { AddProfile(); };
            btnRename.Click += delegate { RenameProfile(); };
            btnDelete.Click += delegate { DeleteProfile(); };

            // Live speed
            for (int i = 0; i < this.fanCount; i++)
            {
                cards[i] = new Panel();
                cards[i].BackColor = Card;
                Controls.Add(cards[i]);
                fanCaptions[i] = MakeLabel("", Font, Muted);
                rpmLabels[i] = MakeLabel("…", new Font("Segoe UI Semibold", 18f), Fg);
                // Opaque background: a transparent one redraws badly when the text changes every 2 s
                rpmLabels[i].BackColor = Card;
                fanCaptions[i].BackColor = Card;
                cards[i].Controls.Add(fanCaptions[i]);
                cards[i].Controls.Add(rpmLabels[i]);
                rpmBars[i] = new Panel();
                rpmBars[i].BackColor = Accent;
                cards[i].Controls.Add(rpmBars[i]);
                cards[i].Paint += PaintBarTrack;
            }

            // Advice: should the fans spin faster for what the PC is doing right now?
            advicePanel = new Panel();
            advicePanel.BackColor = Card;
            Controls.Add(advicePanel);
            adviceIcon = MakeLabel("", new Font("Segoe MDL2 Assets", 12f), Muted);
            adviceText = MakeLabel("", new Font("Segoe UI Semibold", 10f), Fg);
            adviceText.AutoEllipsis = true;
            adviceStats = MakeLabel("", Font, Muted);
            adviceStats.TextAlign = ContentAlignment.TopRight;
            foreach (Label l in new Label[] { adviceIcon, adviceText, adviceStats })
            {
                l.BackColor = Card;
                advicePanel.Controls.Add(l);
            }

            modeTitle = AddLabel("", new Font("Segoe UI Semibold", 11f), Fg);
            rbAuto = AddRadio();
            rbSame = AddRadio();
            rbSeparate = AddRadio();

            for (int i = 0; i < 2; i++)
            {
                trackNames[i] = AddLabel("", Font, Fg);
                trackValues[i] = AddLabel("", new Font("Segoe UI Semibold", 10f), Fg);
                trackValues[i].TextAlign = ContentAlignment.TopRight;
                tracks[i] = new TrackBar();
                tracks[i].Minimum = Settings.MinLevel;
                tracks[i].Maximum = Settings.MaxLevel;
                tracks[i].TickFrequency = 5;
                tracks[i].LargeChange = 3;
                tracks[i].AutoSize = false;
                tracks[i].BackColor = Bg;
                tracks[i].ValueChanged += delegate { UpdateControls(); OnEdited(); };
                Controls.Add(tracks[i]);
            }

            int[] presetLevels = { 30, 40, Settings.MaxLevel };
            for (int i = 0; i < 3; i++)
            {
                presets[i] = MakeButton("", Card);
                int level = presetLevels[i];
                presets[i].Click += delegate
                {
                    if (rbAuto.Checked) rbSame.Checked = true;
                    tracks[0].Value = level;
                    tracks[1].Value = level;
                };
                Controls.Add(presets[i]);
            }

            cbCpu = AddCheckBox();

            // Global, not per profile: whether the startup task re-applies the active profile
            cbKeep = AddCheckBox();
            cbKeep.Checked = settings.KeepAfterRestart;
            // Without installation there is no startup task to re-apply anything
            cbKeep.Enabled = Installer.IsRunningInstalled();
            cbKeep.CheckedChanged += delegate { OnKeepChanged(); };

            cbAdvice = AddCheckBox();
            cbAdvice.Checked = settings.ShowAdvice;
            cbAdvice.CheckedChanged += delegate { OnShowAdviceChanged(); };

            apply = MakeButton("", AccentFill);
            apply.ForeColor = Color.White;
            apply.Font = new Font("Segoe UI Semibold", 10.5f);
            // As the default button it gets an outline: same color as the fill, so it doesn't show as a white frame
            apply.FlatAppearance.BorderColor = AccentFill;
            apply.Click += delegate { Apply(); };
            Controls.Add(apply);
            AcceptButton = apply;

            status = AddLabel("", Font, Muted);

            restartLink = MakeButton("", Bg);
            restartLink.ForeColor = Warn;
            restartLink.TextAlign = ContentAlignment.MiddleLeft;
            restartLink.Visible = false;
            restartLink.Click += delegate { Restart(); };
            Controls.Add(restartLink);

            uninstall = MakeButton("", Bg);
            uninstall.ForeColor = Muted;
            uninstall.TextAlign = ContentAlignment.MiddleRight;
            uninstall.Visible = Installer.IsRunningInstalled();
            uninstall.Click += delegate { Uninstall(); };
            Controls.Add(uninstall);

            updateButton = MakeButton("", Bg);
            updateButton.ForeColor = Muted;
            updateButton.TextAlign = ContentAlignment.MiddleLeft;
            updateButton.Click += delegate { OnUpdateClicked(); };
            Controls.Add(updateButton);

            // A radio fires CheckedChanged when it loses its check too: only react to the newly checked one
            foreach (RadioButton rb in new RadioButton[] { rbAuto, rbSame, rbSeparate })
                rb.CheckedChanged += delegate(object sender, EventArgs e)
                {
                    UpdateControls();
                    if (((RadioButton)sender).Checked) OnEdited();
                };
            cbCpu.CheckedChanged += delegate { OnEdited(); };

            foreach (Profile p in settings.Profiles) profileBox.Items.Add(p);
            profileBox.SelectedIndex = settings.Active;
            LoadProfile(settings.Current);
            profileBox.SelectedIndexChanged += delegate { OnProfileSelected(); };

            if (!Installer.IsRunningInstalled())
                SetStatus("Mode sans installation : le réglage sera perdu au redémarrage.",
                          "Portable mode: your setting will be lost on restart.", Warn);
            else if (manualActive && settings.KeepAfterRestart)
                SetStatus("Ton réglage est réappliqué à chaque démarrage et sortie de veille.",
                          "Your setting is re-applied at every startup and wake from sleep.", Muted);

            ApplyTexts();

            // Start at the full content height (capped to the screen); smaller windows scroll
            ClientSize = new Size(S(470), S(400));
            int contentHeight = LayoutAll();
            int maxHeight = Screen.FromPoint(Cursor.Position).WorkingArea.Height - S(60);
            ClientSize = new Size(S(470), Math.Min(contentHeight, maxHeight));
            MinimumSize = SizeFromClientSize(new Size(S(400), S(320)));
            ResumeLayout(false);
            ClientSizeChanged += delegate { LayoutAll(); };

            timer = new Timer();
            timer.Interval = 2000;
            timer.Tick += delegate { RefreshRpm(); };
            Shown += delegate
            {
                Program.EnsureTaskbarButton(Handle);
                Program.ForceForeground(Handle);
                Activate();
                RefreshRpm();
                timer.Start();
                CheckForUpdatesAsync(true);
            };
            FormClosed += delegate { timer.Stop(); };
        }

        // Places every control for the current width; returns the total content height
        int LayoutAll()
        {
            SuspendLayout();
            int adviceH = settings.ShowAdvice ? S(52) : 0;
            int contentHeight = S(242) + adviceH + S(526);
            // Leave room for the vertical scrollbar before it appears, otherwise the content overflows sideways
            int scrollbar = contentHeight > ClientSize.Height && !VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0;
            int m = S(24);
            int w = Math.Max(S(360), ClientSize.Width - scrollbar);
            int inner = w - 2 * m;
            int right = m + inner;
            int top = AutoScrollPosition.Y;   // negative when scrolled down
            Func<int, int> Y = v => top + v;

            title.Bounds = new Rectangle(m, Y(S(18)), inner - S(100), S(34));
            model.Bounds = new Rectangle(m, Y(S(50)), inner, S(22));
            btnEn.Bounds = new Rectangle(right - S(42), Y(S(22)), S(42), S(28));
            btnFr.Bounds = new Rectangle(right - S(88), Y(S(22)), S(42), S(28));

            int y = S(88);
            btnDelete.Bounds = new Rectangle(right - S(36), Y(y), S(36), S(30));
            btnRename.Bounds = new Rectangle(right - S(78), Y(y), S(36), S(30));
            btnAdd.Bounds = new Rectangle(right - S(120), Y(y), S(36), S(30));
            profileBox.Bounds = new Rectangle(m, Y(y), inner - S(128), S(30));

            y = S(132);
            int gap = S(10);
            int cardW = fanCount > 1 ? (inner - gap) / 2 : inner;
            // Smaller digits on narrow cards so "2 800 tr/min" stays on one line
            Font rpmFont = cardW < S(200) ? rpmFontSmall : rpmFontLarge;
            for (int i = 0; i < fanCount; i++)
            {
                rpmLabels[i].Font = rpmFont;
                cards[i].Bounds = new Rectangle(m + i * (cardW + gap), Y(y), cardW, S(92));
                fanCaptions[i].Bounds = new Rectangle(S(14), S(10), cardW - S(28), S(22));
                rpmLabels[i].Bounds = new Rectangle(S(12), S(30), cardW - S(24), S(40));
                rpmBars[i].Bounds = new Rectangle(S(14), S(74), BarWidth(i, cardW), S(5));
                cards[i].Invalidate();
            }
            y += S(100);

            advicePanel.Visible = settings.ShowAdvice;
            if (settings.ShowAdvice)
            {
                advicePanel.Bounds = new Rectangle(m, Y(y), inner, S(44));
                int statsW = S(110);
                adviceIcon.Bounds = new Rectangle(S(12), S(11), S(24), S(24));
                adviceStats.Bounds = new Rectangle(inner - S(12) - statsW, S(12), statsW, S(22));
                adviceText.Bounds = new Rectangle(S(42), S(12), adviceStats.Left - S(46), S(22));
                y += S(52);
            }

            int b = y + S(10);   // top of the "Mode" section
            modeTitle.Bounds = new Rectangle(m, Y(b), inner, S(24));
            rbAuto.Bounds = new Rectangle(m, Y(b + S(22)), inner, S(26));
            rbSame.Bounds = new Rectangle(m, Y(b + S(48)), inner, S(26));
            rbSeparate.Bounds = new Rectangle(m, Y(b + S(74)), inner, S(26));
            for (int i = 0; i < 2; i++)
            {
                int ty = b + S(114) + i * S(62);
                trackNames[i].Bounds = new Rectangle(m, Y(ty), inner - S(160), S(22));
                trackValues[i].Bounds = new Rectangle(right - S(160), Y(ty), S(160), S(22));
                tracks[i].Bounds = new Rectangle(m - S(6), Y(ty + S(22)), inner + S(12), S(40));
            }
            int presetW = (inner - 2 * gap) / 3;
            for (int i = 0; i < 3; i++)
                presets[i].Bounds = new Rectangle(m + i * (presetW + gap), Y(b + S(244)), presetW, S(34));
            cbCpu.Bounds = new Rectangle(m, Y(b + S(292)), inner, S(26));
            cbKeep.Bounds = new Rectangle(m, Y(b + S(320)), inner, S(26));
            cbAdvice.Bounds = new Rectangle(m, Y(b + S(348)), inner, S(26));
            apply.Bounds = new Rectangle(m, Y(b + S(384)), inner, S(44));
            status.Bounds = new Rectangle(m, Y(b + S(436)), inner, S(44));
            // Bottom row; "Restart now" takes the update button's place while it is needed
            Rectangle bottomLeft = new Rectangle(m - S(4), Y(b + S(484)), inner - S(150), S(28));
            restartLink.Bounds = bottomLeft;
            updateButton.Bounds = bottomLeft;
            updateButton.Visible = !restartLink.Visible;
            uninstall.Bounds = new Rectangle(right - S(144), Y(b + S(484)), S(148), S(28));

            AutoScrollMinSize = new Size(0, contentHeight);
            ResumeLayout(false);
            return contentHeight;
        }

        int BarWidth(int i, int cardWidth)
        {
            int full = cardWidth - 2 * S(14);
            return (int)(full * Math.Min(1.0, lastRpm[i] / (Settings.MaxLevel * 100.0)));
        }

        // "Restart now" and the update button share the bottom-left spot
        void ShowRestartLink(bool show)
        {
            restartLink.Visible = show;
            updateButton.Visible = !show;
        }

        void OnShowAdviceChanged()
        {
            settings.ShowAdvice = cbAdvice.Checked;
            SaveQuietly();
            LayoutAll();
            if (settings.ShowAdvice) SampleSensorsAsync();
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
            btnFr.BackColor = Lang.English ? Card : AccentFill;
            btnEn.BackColor = Lang.English ? AccentFill : Card;
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
            cbKeep.Text = Lang.T("Garder au redémarrage", "Keep after restart");
            cbAdvice.Text = Lang.T("Afficher l'indicateur de chauffe", "Show the heat indicator");
            apply.Text = Lang.T("Appliquer", "Apply");
            restartLink.Text = Lang.T("Redémarrer maintenant", "Restart now");
            uninstall.Text = Lang.T("Désinstaller", "Uninstall");
            UpdateUpdateButton();
            tips.SetToolTip(profileBox, Lang.T("Profil (✔ = actif)", "Profile (✔ = active)"));
            tips.SetToolTip(btnAdd, Lang.T("Nouveau profil", "New profile"));
            tips.SetToolTip(btnRename, Lang.T("Renommer le profil", "Rename profile"));
            tips.SetToolTip(btnDelete, Lang.T("Supprimer le profil", "Delete profile"));
            status.Text = Lang.T(statusFr, statusEn);
            UpdateControls();
            UpdateAdvice();
        }

        Profile SelectedProfile
        {
            get { return settings.Profiles[profileBox.SelectedIndex]; }
        }

        void LoadProfile(Profile p)
        {
            // Filling the controls fires their change events: don't let them write back into a profile
            loading = true;
            try
            {
                if (p.Mode == FanMode.Auto) rbAuto.Checked = true;
                else if (p.Mode == FanMode.Separate && fanCount > 1) rbSeparate.Checked = true;
                else rbSame.Checked = true;
                // After choosing the mode, otherwise "same speed" mode would overwrite fan 2's value
                tracks[0].Value = p.Fan1;
                tracks[1].Value = p.Fan2;
                cbCpu.Checked = p.CpuMax;
                UpdateControls();
            }
            finally { loading = false; }
        }

        // Every change is saved into the selected profile right away, so switching profiles never loses it
        void OnEdited()
        {
            if (loading || profileBox.SelectedIndex < 0) return;
            Profile p = SelectedProfile;
            SaveControlsTo(p);
            SaveQuietly();
            SetStatus("Modifié et enregistré dans « " + p.Name + " ». Clique sur Appliquer pour l'appliquer.",
                      "Saved to \"" + p.Name + "\". Click Apply to apply the change.", Accent);
        }

        void SaveControlsTo(Profile p)
        {
            p.Mode = SelectedMode;
            p.Fan1 = tracks[0].Value;
            p.Fan2 = p.Mode == FanMode.Same ? tracks[0].Value : tracks[1].Value;
            p.CpuMax = cbCpu.Checked;
        }

        // Picking a profile in the list activates it immediately
        void OnProfileSelected()
        {
            if (profileBox.SelectedIndex < 0) return;
            LoadProfile(SelectedProfile);
            ApplyProfile(SelectedProfile);
        }

        void AddProfile()
        {
            string name = Dialogs.AskName(this, Lang.T("Nouveau profil", "New profile"),
                Lang.T("Nom du profil :", "Profile name:"),
                Lang.T("Profil ", "Profile ") + (settings.Profiles.Count + 1));
            if (name == null) return;
            // Starts from what is on screen, so you can tweak it and click Apply
            Profile p = new Profile(Settings.CleanName(name, Lang.T("Profil ", "Profile ") + (settings.Profiles.Count + 1)));
            SaveControlsTo(p);
            settings.Profiles.Add(p);
            profileBox.Items.Add(p);
            SaveQuietly();
            profileBox.SelectedIndex = profileBox.Items.Count - 1;
        }

        void RenameProfile()
        {
            Profile p = SelectedProfile;
            string name = Dialogs.AskName(this, Lang.T("Renommer le profil", "Rename profile"),
                Lang.T("Nouveau nom :", "New name:"), p.Name);
            if (name == null) return;
            p.Name = Settings.CleanName(name, p.Name);
            // Re-assigning the item makes the ComboBox redraw its text
            profileBox.Items[profileBox.SelectedIndex] = p;
            SaveQuietly();
        }

        void DeleteProfile()
        {
            if (settings.Profiles.Count <= 1)
            {
                SetStatus("Il faut garder au moins un profil.", "You need to keep at least one profile.", Warn);
                return;
            }
            int index = profileBox.SelectedIndex;
            Profile p = settings.Profiles[index];
            if (MessageBox.Show(Lang.T("Supprimer le profil « " + p.Name + " » ?", "Delete profile \"" + p.Name + "\"?"),
                    Program.Title, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            bool wasActive = index == settings.Active;
            settings.Profiles.RemoveAt(index);
            if (index < settings.Active || settings.Active >= settings.Profiles.Count) settings.Active--;
            if (wasActive) settings.Active = 0;
            profileBox.Items.RemoveAt(index);
            SaveQuietly();
            // Selecting re-activates the remaining active profile (or the first one if the active was deleted)
            profileBox.SelectedIndex = settings.Active;
        }

        void UpdateUpdateButton()
        {
            if (pendingUpdate != null)
            {
                updateButton.Text = "⬆  " + Lang.T("Mise à jour ", "Update ") + "v" + Updater.Format(pendingUpdate.Version) +
                                    Lang.T(" disponible", " available");
                updateButton.ForeColor = Accent;
            }
            else
            {
                updateButton.Text = checkingUpdate ? Lang.T("Recherche…", "Checking…")
                                                   : Lang.T("Rechercher des mises à jour", "Check for updates");
                updateButton.ForeColor = Muted;
            }
            tips.SetToolTip(updateButton, Lang.T("Version installée : ", "Installed version: ") + Updater.Format(Updater.Current));
        }

        // Network calls run off the UI thread; silent = the automatic check at launch (no message if up to date or offline)
        void CheckForUpdatesAsync(bool silent)
        {
            if (checkingUpdate) return;
            checkingUpdate = true;
            UpdateUpdateButton();
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                ReleaseInfo r = null;
                string error = null;
                try { r = Updater.GetLatest(); }
                catch (Exception ex) { error = ex.Message; }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        checkingUpdate = false;
                        if (r != null && Updater.IsNewer(r))
                        {
                            pendingUpdate = r;
                            if (!silent) OfferUpdate();
                        }
                        else if (!silent && error != null)
                            SetStatus("Impossible de vérifier les mises à jour : " + error,
                                      "Could not check for updates: " + error, Warn);
                        else if (!silent)
                            SetStatus("✔  Spectre Control est à jour (v" + Updater.Format(Updater.Current) + ").",
                                      "✔  Spectre Control is up to date (v" + Updater.Format(Updater.Current) + ").", Fg);
                        UpdateUpdateButton();
                    });
                }
                catch (InvalidOperationException) { }   // window closed meanwhile
            });
        }

        void OnUpdateClicked()
        {
            if (pendingUpdate != null) OfferUpdate();
            else CheckForUpdatesAsync(false);
        }

        void OfferUpdate()
        {
            ReleaseInfo r = pendingUpdate;
            // Not installed, or no exe attached to the release: let the user download it from the page
            if (!Installer.IsRunningInstalled() || r.DownloadUrl == null)
            {
                Process.Start(new ProcessStartInfo(r.PageUrl) { UseShellExecute = true });
                return;
            }
            if (MessageBox.Show(Lang.T(
                    "La version " + Updater.Format(r.Version) + " est disponible (tu as la " + Updater.Format(Updater.Current) + ").\n\n" +
                    "La télécharger et l'installer maintenant ? Tes profils et réglages sont conservés.",
                    "Version " + Updater.Format(r.Version) + " is available (you have " + Updater.Format(Updater.Current) + ").\n\n" +
                    "Download and install it now? Your profiles and settings are kept."),
                    Program.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
                return;

            updateButton.Enabled = false;
            SetStatus("Téléchargement de la version " + Updater.Format(r.Version) + "…",
                      "Downloading version " + Updater.Format(r.Version) + "…", Accent);
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string path = null, error = null;
                try { path = Updater.Download(r); }
                catch (Exception ex) { error = ex.Message; }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        updateButton.Enabled = true;
                        if (error != null)
                        {
                            SetStatus("Échec de la mise à jour : " + error, "Update failed: " + error, Warn);
                            return;
                        }
                        try
                        {
                            Updater.LaunchInstaller(path);
                            Close();   // the new version waits for this window to close, installs, then reopens
                        }
                        catch (Exception ex)
                        {
                            SetStatus("Échec de la mise à jour : " + ex.Message, "Update failed: " + ex.Message, Warn);
                        }
                    });
                }
                catch (InvalidOperationException) { }
            });
        }

        void OnKeepChanged()
        {
            settings.KeepAfterRestart = cbKeep.Checked;
            SaveQuietly();
            if (cbKeep.Checked)
                SetStatus("« " + settings.Current.Name + " » sera réappliqué à chaque démarrage et sortie de veille.",
                          "\"" + settings.Current.Name + "\" will be re-applied at every startup and wake from sleep.", Muted);
            else
                SetStatus("Au prochain redémarrage, les ventilateurs reviendront à la gestion HP.",
                          "At the next restart, fans will go back to HP's default control.", Muted);
        }

        void SaveQuietly()
        {
            try { settings.Save(); }
            catch (Exception ex) { SetStatus("Erreur : " + ex.Message, "Error: " + ex.Message, Warn); }
        }

        void DrawProfileItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using (SolidBrush back = new SolidBrush(selected ? AccentFill : Card))
                e.Graphics.FillRectangle(back, e.Bounds);
            string text = (e.Index == settings.Active ? "✔  " : "     ") + profileBox.Items[e.Index];
            TextRenderer.DrawText(e.Graphics, text, profileBox.Font, e.Bounds, selected ? Color.White : Fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
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
                trackValues[i].Text = mode == FanMode.Auto ? "Auto" : LevelText(tracks[i].Value, i, mode);
                trackValues[i].ForeColor = mode == FanMode.Auto ? Muted : Fg;
            }
        }

        // Real speed the fans will reach for a slider level. Fan 1 never drops below its firmware floor,
        // fan 2 can stop. In "same speed" mode, show both when they differ.
        string LevelText(int level, int slider, FanMode mode)
        {
            int fan1 = Math.Max(Settings.Fan1FloorRpm, level * 100);
            int fan2 = level * 100;
            if (mode == FanMode.Separate) return RpmText(slider == 0 ? fan1 : fan2);
            if (fanCount < 2 || fan1 == fan2) return RpmText(fan1);
            return fan1.ToString("N0", NumberCulture()) + " / " + RpmText(fan2);
        }

        static CultureInfo NumberCulture()
        {
            return CultureInfo.GetCultureInfo(Lang.T("fr-FR", "en-US"));
        }

        static string RpmText(int rpm)
        {
            return rpm.ToString("N0", NumberCulture()) + Lang.T(" tr/min", " RPM");
        }

        void Apply()
        {
            Profile p = SelectedProfile;
            SaveControlsTo(p);
            ApplyProfile(p);
        }

        // Makes the profile active (✔, re-applied at startup) and applies it to the fans and CPU now
        void ApplyProfile(Profile p)
        {
            try
            {
                settings.Active = settings.Profiles.IndexOf(p);
                settings.Save();
                profileBox.Invalidate();
                p.ApplyCpu();

                if (p.Mode != FanMode.Auto)
                {
                    p.ApplyFans();
                    manualActive = true;
                    ShowRestartLink(false);
                    SetStatus("✔  « " + p.Name + " » appliqué. La vitesse change dans les 20 secondes.",
                              "✔  \"" + p.Name + "\" applied. Fan speed changes within 20 seconds.", Fg);
                }
                else if (manualActive)
                {
                    ShowRestartLink(true);
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

        // WMI performance counters can take a few hundred ms: read them off the UI thread
        void SampleSensorsAsync()
        {
            if (sampling || !settings.ShowAdvice) return;
            sampling = true;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try { advisor.Sample(); } catch { }
                try { BeginInvoke((MethodInvoker)delegate { sampling = false; UpdateAdvice(); }); } catch { }
            });
        }

        void UpdateAdvice()
        {
            Advice a = advisor.Evaluate(Math.Max(lastRpm[0], lastRpm[1]));
            // Each state has its own symbol, so it never depends on telling colors apart
            Color c;
            string text, icon;
            switch (a)
            {
                case Advice.Hot:
                    // Nothing left to do: information, not a request, so not red
                    c = Warn;
                    icon = "";   // warning triangle
                    text = Lang.T("Chaud, ventilateurs déjà au max", "Hot, fans already at max");
                    break;
                case Advice.Raise:
                    bool hot = advisor.Throttling || advisor.TempC >= Advisor.HotC;
                    c = hot ? Danger : Warn;
                    icon = "";   // up arrow
                    text = hot ? Lang.T("Trop chaud : augmente la vitesse", "Too hot: raise the fan speed")
                               : Lang.T("Chaud : augmenter aiderait", "Warm: more speed would help");
                    break;
                case Advice.Lower:
                    c = Cool;
                    icon = "";   // down arrow
                    text = Lang.T("Au frais : tu peux baisser", "Cool: you can lower the speed");
                    break;
                case Advice.Ok:
                    c = Okay;
                    icon = "";   // check mark
                    text = Lang.T("Vitesse adaptée", "Fan speed is fine");
                    break;
                default:
                    c = Muted;
                    icon = "";   // unknown
                    text = Lang.T("Température indisponible", "Temperature unavailable");
                    break;
            }
            adviceIcon.Text = icon;
            adviceIcon.ForeColor = c;
            adviceText.Text = text;
            adviceText.ForeColor = a == Advice.Unknown ? Muted : Fg;
            adviceStats.Text = double.IsNaN(advisor.TempC) ? "" :
                Math.Round(advisor.TempC) + " °C · " + Math.Round(advisor.AvgLoad) + " %";
            string why = a == Advice.Hot
                ? Lang.T("Les ventilateurs ne peuvent pas aller plus vite. Le processeur se protège tout seul en ralentissant un peu : c'est normal sous forte charge.\n\n",
                         "The fans can't go any faster. The CPU protects itself by slowing down a little: that's normal under heavy load.\n\n")
                : "";
            string tip = why + Lang.T(
                "Basé sur la température du processeur, sa charge moyenne (30 s) et la vitesse actuelle des ventilateurs.",
                "Based on CPU temperature, its average load (30 s) and the current fan speed.");
            tips.SetToolTip(adviceText, tip);
            tips.SetToolTip(adviceStats, Lang.T("Température du processeur · charge moyenne", "CPU temperature · average load"));
        }

        void RefreshRpm()
        {
            SampleSensorsAsync();
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

        // Positions read after DPI scaling: the bar has the same margin on the left and right
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
                if (cbCpu.Checked) new Profile("", FanMode.Auto, 0, false).ApplyCpu();
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

        // Controls are created without a position: LayoutAll() places them

        RadioButton AddRadio()
        {
            RadioButton r = new RadioButton();
            Controls.Add(r);
            return r;
        }

        CheckBox AddCheckBox()
        {
            CheckBox c = new CheckBox();
            Controls.Add(c);
            return c;
        }

        Label AddLabel(string text, Font font, Color color)
        {
            Label l = MakeLabel(text, font, color);
            Controls.Add(l);
            return l;
        }

        static Label MakeLabel(string text, Font font, Color color)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = font;
            l.ForeColor = color;
            l.BackColor = Color.Transparent;
            return l;
        }

        Button MakeIconButton(string glyph)
        {
            Button b = MakeButton(glyph, Card);
            b.Font = new Font("Segoe MDL2 Assets", 10f);
            Controls.Add(b);
            return b;
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
