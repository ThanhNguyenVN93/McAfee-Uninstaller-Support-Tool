using System;
using System.Drawing;
using System.Windows.Forms;

namespace frm_mcafee_unin
{
    public class FormDonate : Form
    {
        public FormDonate()
        {
            this.Text            = "Ủng hộ tác giả ☕";
            this.Size            = new Size(520, 400);
            this.MinimumSize     = this.Size;
            this.MaximumSize     = this.Size;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox     = false;
            this.MinimizeBox     = false;
            this.StartPosition   = FormStartPosition.CenterParent;
            this.BackColor       = Color.FromArgb(22, 22, 34);

            try { var ico = Form1.LoadEmbeddedIcon("convertico-mcafee.ico"); if (ico != null) this.Icon = ico; }
            catch { }

            var lblTitle = new Label
            {
                Text      = "Cảm ơn bạn đã sử dụng McAfee Uninstall Tool!",
                Font      = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(220, 225, 240),
                Location  = new Point(20, 16),
                Size      = new Size(470, 22),
                TextAlign = ContentAlignment.MiddleCenter,
            };

            var lblSub = new Label
            {
                Text      = "Nếu tool hữu ích, bạn có thể ủng hộ tác giả qua:",
                Font      = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(120, 130, 150),
                Location  = new Point(20, 42),
                Size      = new Size(470, 18),
                TextAlign = ContentAlignment.MiddleCenter,
            };

            // ── TCB QR ──────────────────────────────────────────────────────
            var pnlTcb = MakeQrPanel("Techcombank (TCB)", "tcb.jpg", new Point(30, 72));

            // ── MoMo QR ─────────────────────────────────────────────────────
            var pnlMomo = MakeQrPanel("Ví MoMo", "momo.jpg", new Point(270, 72));

            // ── Close button ─────────────────────────────────────────────────
            var btnClose = new Button
            {
                Text      = "Đóng",
                Location  = new Point(200, 334),
                Size      = new Size(110, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(50, 50, 70),
                ForeColor = Color.White,
                Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor    = Cursors.Hand,
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s, e) => this.Close();

            this.Controls.AddRange(new Control[] { lblTitle, lblSub, pnlTcb, pnlMomo, btnClose });
        }

        private Panel MakeQrPanel(string label, string imgFile, Point location)
        {
            var pnl = new Panel
            {
                Location  = location,
                Size      = new Size(200, 250),
                BackColor = Color.FromArgb(30, 30, 46),
            };

            var lbl = new Label
            {
                Text      = label,
                Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(180, 190, 220),
                Location  = new Point(0, 8),
                Size      = new Size(200, 20),
                TextAlign = ContentAlignment.MiddleCenter,
            };

            var pic = new PictureBox
            {
                Location = new Point(20, 34),
                Size     = new Size(160, 200),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.White,
            };
            try
            {
                var img = Form1.LoadEmbeddedImage(imgFile);
                if (img != null) pic.Image = img;
                else pic.BackColor = Color.FromArgb(40, 40, 60);
            }
            catch { pic.BackColor = Color.FromArgb(40, 40, 60); }

            pnl.Controls.AddRange(new Control[] { lbl, pic });
            return pnl;
        }

    }
}
