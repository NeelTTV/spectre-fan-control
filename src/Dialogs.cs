using System;
using System.Drawing;
using System.Windows.Forms;

namespace SpectreControl
{
    static class Dialogs
    {
        // Small dark text prompt used to name or rename a profile. Returns null if cancelled.
        // Laid out by hand from the screen DPI: WinForms auto-scaling squashed the text box at 200 %.
        public static string AskName(IWin32Window owner, string title, string label, string initial)
        {
            float scale;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
            Func<int, int> S = v => (int)Math.Round(v * scale);

            using (Form f = new Form())
            using (Font font = new Font("Segoe UI", 10f))
            using (TextBox box = new TextBox())
            using (Button ok = new Button())
            using (Button cancel = new Button())
            {
                f.AutoScaleMode = AutoScaleMode.None;
                f.Font = font;
                f.Text = title;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = f.MinimizeBox = false;
                f.ShowInTaskbar = false;
                f.StartPosition = FormStartPosition.CenterParent;
                f.BackColor = MainForm.Bg;
                f.ForeColor = MainForm.Fg;

                Label l = new Label();
                l.Text = label;
                l.AutoSize = true;
                l.Location = new Point(S(16), S(16));
                f.Controls.Add(l);

                box.Font = font;
                box.Text = initial;
                box.MaxLength = Settings.MaxNameLength;
                box.BackColor = MainForm.Card;
                box.ForeColor = MainForm.Fg;
                box.BorderStyle = BorderStyle.FixedSingle;
                box.Location = new Point(S(16), S(44));
                box.Width = S(328);   // height comes from the font
                f.Controls.Add(box);

                int buttonsTop = box.Bottom + S(18);
                ok.Text = "OK";
                ok.DialogResult = DialogResult.OK;
                ok.Bounds = new Rectangle(S(152), buttonsTop, S(92), S(32));
                cancel.Text = Lang.T("Annuler", "Cancel");
                cancel.DialogResult = DialogResult.Cancel;
                cancel.Bounds = new Rectangle(S(252), buttonsTop, S(92), S(32));
                foreach (Button b in new Button[] { ok, cancel })
                {
                    b.Font = font;
                    b.FlatStyle = FlatStyle.Flat;
                    b.FlatAppearance.BorderSize = 0;
                    b.BackColor = b == ok ? MainForm.AccentFill : MainForm.Card;
                    b.ForeColor = b == ok ? Color.White : MainForm.Fg;
                    f.Controls.Add(b);
                }
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                f.ClientSize = new Size(S(360), ok.Bottom + S(16));

                f.Shown += delegate { box.Focus(); box.SelectAll(); };
                return f.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
            }
        }
    }
}
