using System;
using System.Drawing;
using System.Windows.Forms;

namespace frm_mcafee_unin
{
    partial class Form1
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        /// 
        // Khai báo các thành phần giao diện
        private TabControl tabControl;
        private TabPage tabBackup, tabUninstall, tabScan;
        private Button btnNext;
        private ProgressBar progressBarAll;
        private RichTextBox txtLog;

        // Controls cho Chức năng 1
        private Button btnCreateRestorePoint;
        private Button btnCleanupTempFiles;
        private Label lblBackupStatus;

        // Controls cho Chức năng 2
        private Button btnRunUninstall;
        private Button btnAbort;
        private Button btnBrowseUninstall;
        private Label lblUninstallStatus;

        // Controls cho Chức năng 3
        private Button btnScanLeftovers;
        private Button btnDeleteSelected;
        private TreeView treeLeftovers;

        // Màu sắc chủ đạo
        private static readonly Color BG_DARK    = Color.FromArgb(18, 18, 28);
        private static readonly Color BG_PANEL   = Color.FromArgb(28, 28, 42);
        private static readonly Color BG_TAB     = Color.FromArgb(32, 32, 48);
        private static readonly Color ACCENT     = Color.FromArgb(82, 130, 255);
        private static readonly Color ACCENT2    = Color.FromArgb(50, 200, 120);
        private static readonly Color ACCENT3    = Color.FromArgb(255, 160, 50);
        private static readonly Color TEXT_MAIN  = Color.FromArgb(220, 225, 240);
        private static readonly Color TEXT_DIM   = Color.FromArgb(120, 130, 150);
        private static readonly Color BTN_HOVER  = Color.FromArgb(60, 100, 220);

        private Label[] stepLabels = new Label[3];
        private Panel   pnlHeader, pnlFooter, pnlSteps;
        private PictureBox picDonate;

        private Button StyleBtn(Button b, Color bg)
        {
            b.FlatStyle  = FlatStyle.Flat;
            b.FlatAppearance.BorderSize  = 0;
            b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(bg, 0.15f);
            b.BackColor  = bg;
            b.ForeColor  = Color.White;
            b.Font       = new Font("Segoe UI", 9f, FontStyle.Bold);
            b.Cursor     = Cursors.Hand;
            return b;
        }

        private void SetupCustomUI()
        {
            // ── Form ──────────────────────────────────────────────────────────
            this.Text            = "McAfee Uninstall Tool  v1.0";
            try { var ico = Form1.LoadEmbeddedIcon("convertico-mcafee.ico"); if (ico != null) this.Icon = ico; }
            catch { }
            this.Size            = new Size(700, 540);
            this.MinimumSize     = this.Size;
            this.MaximumSize     = this.Size;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox     = false;
            this.StartPosition   = FormStartPosition.CenterScreen;
            this.BackColor       = BG_DARK;

            // ── Header ────────────────────────────────────────────────────────
            pnlHeader = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = BG_PANEL };
            var lblTitle = new Label
            {
                Text      = "McAfee Uninstall Tool",
                Font      = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = TEXT_MAIN,
                Location  = new Point(20, 10),
                AutoSize  = true
            };
            var lblSub = new Label
            {
                Text      = "Gỡ cài đặt sạch McAfee — Windows 7 / 10 / 11",
                Font      = new Font("Segoe UI", 8.5f),
                ForeColor = TEXT_DIM,
                Location  = new Point(22, 38),
                AutoSize  = true
            };
            picDonate = new PictureBox
            {
                Location  = new Point(636, 8),
                Size      = new Size(48, 48),
                SizeMode  = PictureBoxSizeMode.Zoom,
                Cursor    = Cursors.Hand,
                BackColor = Color.Transparent,
            };
            try { picDonate.Image = Form1.LoadEmbeddedImage("donate.jpg"); }
            catch { }
            var ttDonate = new ToolTip();
            ttDonate.SetToolTip(picDonate, "Ủng hộ tác giả ☕");
            picDonate.Click += (s, ev) => new FormDonate().ShowDialog(this);

            pnlHeader.Controls.AddRange(new Control[] { lblTitle, lblSub, picDonate });
            this.Controls.Add(pnlHeader);

            // ── Step Indicator ────────────────────────────────────────────────
            pnlSteps = new Panel { Location = new Point(0, 64), Size = new Size(700, 48), BackColor = Color.FromArgb(22, 22, 34) };
            string[] stepNames = { "1  Sao lưu", "2  Gỡ cài đặt", "3  Quét dọn" };
            Color[]  stepClrs  = { ACCENT, ACCENT2, ACCENT3 };
            _stepColors = stepClrs;
            int stepW = 700 / 3; // 233px mỗi bước, lấp đầy toàn bộ chiều ngang
            for (int i = 0; i < 3; i++)
            {
                var pnlStep = new Panel
                {
                    Location  = new Point(i * stepW, 0),
                    Size      = new Size(i == 2 ? 700 - stepW * 2 : stepW, 48),
                    BackColor = (i == 0) ? stepClrs[i] : Color.FromArgb(38, 38, 56)
                };
                stepLabels[i] = new Label
                {
                    Text      = stepNames[i],
                    Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
                    ForeColor = (i == 0) ? Color.White : TEXT_DIM,
                    Dock      = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter
                };
                pnlStep.Controls.Add(stepLabels[i]);
                pnlSteps.Controls.Add(pnlStep);
                stepLabels[i].Tag = pnlStep;
            }
            this.Controls.Add(pnlSteps);

            // ── TabControl ────────────────────────────────────────────────────
            tabControl = new TabControl
            {
                Location   = new Point(0, 112),
                Size       = new Size(700, 320),
                Appearance = TabAppearance.Buttons,
                ItemSize   = new Size(0, 1),
                SizeMode   = TabSizeMode.Fixed,
                Padding    = new Point(0, 0)
            };
            tabBackup    = new TabPage { BackColor = BG_TAB, Padding = new Padding(20, 16, 20, 16) };
            tabUninstall = new TabPage { BackColor = BG_TAB, Padding = new Padding(20, 16, 20, 16) };
            tabScan      = new TabPage { BackColor = BG_TAB, Padding = new Padding(20, 16, 20, 16) };
            tabControl.TabPages.AddRange(new[] { tabBackup, tabUninstall, tabScan });
            this.Controls.Add(tabControl);

            // ── Tab 1: Sao lưu ───────────────────────────────────────────────
            var lbl1 = new Label
            {
                Text      = "Tạo điểm khôi phục hệ thống và sao lưu Registry trước khi gỡ cài đặt.",
                Font      = new Font("Segoe UI", 9f),
                ForeColor = TEXT_DIM,
                Location  = new Point(20, 18),
                Size      = new Size(630, 18),
            };
            btnCreateRestorePoint = StyleBtn(new Button { Text = "  Tạo System Restore Point", Location = new Point(20, 48), Size = new Size(240, 40) }, ACCENT);
            btnCleanupTempFiles   = StyleBtn(new Button { Text = "  Dọn file tạm", Location = new Point(272, 48), Size = new Size(140, 40) }, Color.FromArgb(70, 60, 90));
            lblBackupStatus = new Label
            {
                Text      = "ℹ  Nhấn nút để tạo restore point và xuất file .reg backup ra Desktop.",
                Font      = new Font("Segoe UI", 8.5f),
                ForeColor = TEXT_DIM,
                Location  = new Point(20, 104),
                Size      = new Size(640, 40),
            };
            tabBackup.Controls.AddRange(new Control[] { lbl1, btnCreateRestorePoint, btnCleanupTempFiles, lblBackupStatus });

            // ── Tab 2: Gỡ cài đặt ────────────────────────────────────────────
            var lbl2 = new Label
            {
                Text      = "Chạy tiến trình gỡ cài đặt chính thức của McAfee.",
                Font      = new Font("Segoe UI", 9f),
                ForeColor = TEXT_DIM,
                Location  = new Point(20, 18),
                Size      = new Size(630, 18),
            };
            btnRunUninstall = StyleBtn(new Button { Text = "  Chạy mc-update /uninstall", Location = new Point(20, 48), Size = new Size(240, 40) }, ACCENT2);
            btnAbort = StyleBtn(new Button { Text = "  Hủy bỏ", Location = new Point(272, 48), Size = new Size(110, 40), Enabled = false }, Color.FromArgb(160, 60, 60));
            btnBrowseUninstall = StyleBtn(new Button { Text = "  Chọn file...", Location = new Point(394, 48), Size = new Size(120, 40) }, Color.FromArgb(60, 60, 80));
            lblUninstallStatus = new Label
            {
                Text      = "Đường dẫn:  C:\\Program Files\\McAfee\\wps\\1.39.160.1\\mc-update.exe  /uninstall",
                Font      = new Font("Segoe UI", 8.5f),
                ForeColor = TEXT_DIM,
                Location  = new Point(20, 104),
                Size      = new Size(640, 40),
            };
            tabUninstall.Controls.AddRange(new Control[] { lbl2, btnRunUninstall, btnAbort, btnBrowseUninstall, lblUninstallStatus });

            // ── Tab 3: Quét dọn ───────────────────────────────────────────────
            var lbl3 = new Label
            {
                Text      = "Quét sâu toàn bộ thư mục và Registry để tìm dấu vết McAfee còn sót.",
                Font      = new Font("Segoe UI", 9f),
                ForeColor = TEXT_DIM,
                Location  = new Point(20, 18),
                Size      = new Size(630, 18),
            };
            btnScanLeftovers  = StyleBtn(new Button { Text = "  Deep Scan", Location = new Point(20, 48), Size = new Size(155, 36) }, ACCENT3);
            btnDeleteSelected = StyleBtn(new Button { Text = "  Xóa mục đã chọn", Location = new Point(186, 48), Size = new Size(180, 36), Enabled = false }, Color.FromArgb(200, 55, 55));
            treeLeftovers = new TreeView
            {
                Location    = new Point(20, 96),
                Size        = new Size(648, 196),
                BackColor   = Color.FromArgb(20, 20, 32),
                ForeColor   = TEXT_MAIN,
                Font        = new Font("Segoe UI", 8.5f),
                BorderStyle = BorderStyle.None,
            };
            tabScan.Controls.AddRange(new Control[] { lbl3, btnScanLeftovers, btnDeleteSelected, treeLeftovers });

            // ── Footer ────────────────────────────────────────────────────────
            pnlFooter = new Panel { Location = new Point(0, 432), Size = new Size(700, 68), BackColor = BG_PANEL };

            progressBarAll = new ProgressBar
            {
                Location = new Point(20, 8),
                Size     = new Size(656, 6),
                Style    = ProgressBarStyle.Continuous,
                Maximum  = 100,
                Value    = 0
            };

            txtLog = new RichTextBox
            {
                Location  = new Point(20, 22),
                Size      = new Size(430, 36),
                ReadOnly  = true,
                BackColor = Color.FromArgb(14, 14, 22),
                ForeColor = ACCENT2,
                Font      = new Font("Consolas", 8.5f),
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.None
            };

            btnNext = StyleBtn(new Button { Text = "Tiếp tục  ▶", Location = new Point(578, 22), Size = new Size(106, 36) }, ACCENT);

            pnlFooter.Controls.AddRange(new Control[] { progressBarAll, txtLog, btnNext });
            this.Controls.Add(pnlFooter);

            btnNext.Click               += BtnNext_Click;
            btnCreateRestorePoint.Click += BtnCreateRestorePoint_Click;
            btnCleanupTempFiles.Click   += BtnCleanupTempFiles_Click;
            btnRunUninstall.Click       += BtnRunUninstall_Click;
            btnAbort.Click              += BtnAbort_Click;
            btnBrowseUninstall.Click    += BtnBrowseUninstall_Click;
            btnScanLeftovers.Click      += BtnScanLeftovers_Click;
            btnDeleteSelected.Click     += BtnDeleteSelected_Click;
        }

        private Color[] _stepColors;

        internal void UpdateStepIndicator(int activeIndex)
        {
            for (int i = 0; i < stepLabels.Length; i++)
            {
                var pnl = (Panel)stepLabels[i].Tag;
                if (i == activeIndex)
                {
                    pnl.BackColor          = _stepColors[i];
                    stepLabels[i].ForeColor = Color.White;
                }
                else if (i < activeIndex)
                {
                    pnl.BackColor          = Color.FromArgb(35, 55, 35);
                    stepLabels[i].ForeColor = Color.FromArgb(90, 180, 90);
                }
                else
                {
                    pnl.BackColor          = Color.FromArgb(45, 45, 65);
                    stepLabels[i].ForeColor = TEXT_DIM;
                }
            }
        }

        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Name = "Form1";
            this.Text = "Form1";
            this.ResumeLayout(false);
        }

        #endregion
    }
}

