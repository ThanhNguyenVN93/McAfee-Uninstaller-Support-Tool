using System;
using System.Drawing;
using System.Windows.Forms;

namespace frm_mcafee_unin
{
    partial class Form1
    {
        private TabControl tabControl;
        private TabPage tabBackup, tabUninstall, tabScan;
        private Button btnNext;
        private ProgressBar progressBarAll;
        private RichTextBox txtLog;

        private Button btnCreateRestorePoint;
        private Button btnCleanupTempFiles;
        private Label lblBackupStatus;

        private Button btnRunUninstall;
        private Button btnAbort;
        private Button btnBrowseUninstall;
        private Label lblUninstallStatus;

        private Button btnScanLeftovers;
        private Button btnDeleteSelected;
        private TreeView treeLeftovers;

        // Labels cần update khi đổi ngôn ngữ
        private Label lblHeaderSub;
        private Label lblTab1Desc, lblTab2Desc, lblTab3Desc;
        private Button btnLang;

        private static readonly Color BG_DARK    = Color.FromArgb(18, 18, 28);
        private static readonly Color BG_PANEL   = Color.FromArgb(28, 28, 42);
        private static readonly Color BG_TAB     = Color.FromArgb(32, 32, 48);
        private static readonly Color ACCENT     = Color.FromArgb(82, 130, 255);
        private static readonly Color ACCENT2    = Color.FromArgb(50, 200, 120);
        private static readonly Color ACCENT3    = Color.FromArgb(255, 160, 50);
        private static readonly Color TEXT_MAIN  = Color.FromArgb(220, 225, 240);
        private static readonly Color TEXT_DIM   = Color.FromArgb(120, 130, 150);

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
            lblHeaderSub = new Label
            {
                Text      = S.T("Gỡ cài đặt sạch McAfee — Windows 7 / 10 / 11",
                                 "Clean McAfee removal — Windows 7 / 10 / 11"),
                Font      = new Font("Segoe UI", 8.5f),
                ForeColor = TEXT_DIM,
                Location  = new Point(22, 38),
                AutoSize  = true
            };

            // Nút chuyển ngôn ngữ
            btnLang = new Button
            {
                Text      = S.Current == S.Lang.VI ? "EN" : "VI",
                Location  = new Point(578, 20),
                Size      = new Size(46, 24),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 65),
                ForeColor = TEXT_DIM,
                Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor    = Cursors.Hand,
            };
            btnLang.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 100);
            btnLang.FlatAppearance.BorderSize  = 1;
            btnLang.Click += (s, ev) =>
            {
                S.Toggle();
                ApplyLanguage();
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
            ttDonate.SetToolTip(picDonate, S.T("Ủng hộ tác giả ☕", "Support the author ☕"));
            picDonate.Click += (s, ev) => new FormDonate().ShowDialog(this);

            pnlHeader.Controls.AddRange(new Control[] { lblTitle, lblHeaderSub, btnLang, picDonate });
            this.Controls.Add(pnlHeader);

            // ── Step Indicator ────────────────────────────────────────────────
            pnlSteps = new Panel { Location = new Point(0, 64), Size = new Size(700, 48), BackColor = Color.FromArgb(22, 22, 34) };
            string[] stepNames = S.Current == S.Lang.VI
                ? new[] { "1  Sao lưu", "2  Gỡ cài đặt", "3  Quét dọn" }
                : new[] { "1  Backup",  "2  Uninstall",   "3  Cleanup" };
            Color[] stepClrs = { ACCENT, ACCENT2, ACCENT3 };
            _stepColors = stepClrs;
            int stepW = 700 / 3;
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

            // ── Tab 1: Sao lưu / Backup ───────────────────────────────────────
            lblTab1Desc = new Label
            {
                Text      = S.T("Tạo điểm khôi phục hệ thống và sao lưu Registry trước khi gỡ cài đặt.",
                                  "Create a system restore point and back up the Registry before uninstalling."),
                Font      = new Font("Segoe UI", 9f),
                ForeColor = TEXT_DIM,
                Location  = new Point(20, 18),
                Size      = new Size(630, 18),
            };
            btnCreateRestorePoint = StyleBtn(new Button
            {
                Text     = S.T("  Tạo System Restore Point", "  Create System Restore Point"),
                Location = new Point(20, 48), Size = new Size(240, 40)
            }, ACCENT);
            btnCleanupTempFiles = StyleBtn(new Button
            {
                Text     = S.T("  Dọn file tạm", "  Clean Temp Files"),
                Location = new Point(272, 48), Size = new Size(140, 40)
            }, Color.FromArgb(70, 60, 90));
            lblBackupStatus = new Label
            {
                Text      = S.T("ℹ  Nhấn nút để tạo restore point và xuất file .reg backup ra Desktop.",
                                  "ℹ  Click the button to create a restore point and export .reg backup to Desktop."),
                Font      = new Font("Segoe UI", 8.5f),
                ForeColor = TEXT_DIM,
                Location  = new Point(20, 104),
                Size      = new Size(640, 40),
            };
            tabBackup.Controls.AddRange(new Control[] { lblTab1Desc, btnCreateRestorePoint, btnCleanupTempFiles, lblBackupStatus });

            // ── Tab 2: Gỡ cài đặt / Uninstall ────────────────────────────────
            lblTab2Desc = new Label
            {
                Text      = S.T("Chạy tiến trình gỡ cài đặt chính thức của McAfee.",
                                  "Run the official McAfee uninstall process."),
                Font      = new Font("Segoe UI", 9f),
                ForeColor = TEXT_DIM,
                Location  = new Point(20, 18),
                Size      = new Size(630, 18),
            };
            btnRunUninstall = StyleBtn(new Button
            {
                Text     = S.T("  Chạy mc-update /uninstall", "  Run mc-update /uninstall"),
                Location = new Point(20, 48), Size = new Size(240, 40)
            }, ACCENT2);
            btnAbort = StyleBtn(new Button
            {
                Text     = S.T("  Hủy bỏ", "  Abort"),
                Location = new Point(272, 48), Size = new Size(110, 40), Enabled = false
            }, Color.FromArgb(160, 60, 60));
            btnBrowseUninstall = StyleBtn(new Button
            {
                Text     = S.T("  Chọn file...", "  Browse..."),
                Location = new Point(394, 48), Size = new Size(120, 40)
            }, Color.FromArgb(60, 60, 80));
            lblUninstallStatus = new Label
            {
                Text      = S.T("Đường dẫn:  C:\\Program Files\\McAfee\\wps\\1.39.160.1\\mc-update.exe  /uninstall",
                                  "Path:  C:\\Program Files\\McAfee\\wps\\1.39.160.1\\mc-update.exe  /uninstall"),
                Font      = new Font("Segoe UI", 8.5f),
                ForeColor = TEXT_DIM,
                Location  = new Point(20, 104),
                Size      = new Size(640, 40),
            };
            tabUninstall.Controls.AddRange(new Control[] { lblTab2Desc, btnRunUninstall, btnAbort, btnBrowseUninstall, lblUninstallStatus });

            // ── Tab 3: Quét dọn / Cleanup ─────────────────────────────────────
            lblTab3Desc = new Label
            {
                Text      = S.T("Quét sâu toàn bộ thư mục và Registry để tìm dấu vết McAfee còn sót.",
                                  "Deep scan all folders and Registry for leftover McAfee traces."),
                Font      = new Font("Segoe UI", 9f),
                ForeColor = TEXT_DIM,
                Location  = new Point(20, 18),
                Size      = new Size(630, 18),
            };
            btnScanLeftovers  = StyleBtn(new Button
            {
                Text     = "  Deep Scan",
                Location = new Point(20, 48), Size = new Size(155, 36)
            }, ACCENT3);
            btnDeleteSelected = StyleBtn(new Button
            {
                Text     = S.T("  Xóa mục đã chọn", "  Delete Selected"),
                Location = new Point(186, 48), Size = new Size(180, 36), Enabled = false
            }, Color.FromArgb(200, 55, 55));
            treeLeftovers = new TreeView
            {
                Location    = new Point(20, 96),
                Size        = new Size(648, 196),
                BackColor   = Color.FromArgb(20, 20, 32),
                ForeColor   = TEXT_MAIN,
                Font        = new Font("Segoe UI", 8.5f),
                BorderStyle = BorderStyle.None,
            };
            tabScan.Controls.AddRange(new Control[] { lblTab3Desc, btnScanLeftovers, btnDeleteSelected, treeLeftovers });

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
                Location    = new Point(20, 22),
                Size        = new Size(430, 36),
                ReadOnly    = true,
                BackColor   = Color.FromArgb(14, 14, 22),
                ForeColor   = ACCENT2,
                Font        = new Font("Consolas", 8.5f),
                BorderStyle = BorderStyle.None,
                ScrollBars  = RichTextBoxScrollBars.None
            };

            btnNext = StyleBtn(new Button
            {
                Text     = S.T("Tiếp tục  ▶", "Next  ▶"),
                Location = new Point(578, 22), Size = new Size(106, 36)
            }, ACCENT);

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

        internal void ApplyLanguage()
        {
            btnLang.Text = S.Current == S.Lang.VI ? "EN" : "VI";

            lblHeaderSub.Text = S.T("Gỡ cài đặt sạch McAfee — Windows 7 / 10 / 11",
                                     "Clean McAfee removal — Windows 7 / 10 / 11");

            string[] stepNames = S.Current == S.Lang.VI
                ? new[] { "1  Sao lưu", "2  Gỡ cài đặt", "3  Quét dọn" }
                : new[] { "1  Backup",  "2  Uninstall",   "3  Cleanup" };
            for (int i = 0; i < stepLabels.Length; i++)
                stepLabels[i].Text = stepNames[i];

            // Tab 1
            lblTab1Desc.Text           = S.T("Tạo điểm khôi phục hệ thống và sao lưu Registry trước khi gỡ cài đặt.",
                                              "Create a system restore point and back up the Registry before uninstalling.");
            btnCreateRestorePoint.Text = S.T("  Tạo System Restore Point", "  Create System Restore Point");
            btnCleanupTempFiles.Text   = S.T("  Dọn file tạm", "  Clean Temp Files");

            // Tab 2
            lblTab2Desc.Text       = S.T("Chạy tiến trình gỡ cài đặt chính thức của McAfee.",
                                          "Run the official McAfee uninstall process.");
            btnRunUninstall.Text   = S.T("  Chạy mc-update /uninstall", "  Run mc-update /uninstall");
            btnAbort.Text          = S.T("  Hủy bỏ", "  Abort");
            btnBrowseUninstall.Text = S.T("  Chọn file...", "  Browse...");

            // Tab 3
            lblTab3Desc.Text       = S.T("Quét sâu toàn bộ thư mục và Registry để tìm dấu vết McAfee còn sót.",
                                          "Deep scan all folders and Registry for leftover McAfee traces.");
            btnDeleteSelected.Text = S.T("  Xóa mục đã chọn", "  Delete Selected");

            // Footer — giữ text "Hoàn tất" nếu đang ở bước cuối
            if (tabControl != null)
                btnNext.Text = tabControl.SelectedIndex == tabControl.TabCount - 1
                    ? S.T("Hoàn tất  ✔", "Done  ✔")
                    : S.T("Tiếp tục  ▶", "Next  ▶");
        }

        private Color[] _stepColors;

        internal void UpdateStepIndicator(int activeIndex)
        {
            for (int i = 0; i < stepLabels.Length; i++)
            {
                var pnl = (Panel)stepLabels[i].Tag;
                if (i == activeIndex)
                {
                    pnl.BackColor           = _stepColors[i];
                    stepLabels[i].ForeColor = Color.White;
                }
                else if (i < activeIndex)
                {
                    pnl.BackColor           = Color.FromArgb(35, 55, 35);
                    stepLabels[i].ForeColor = Color.FromArgb(90, 180, 90);
                }
                else
                {
                    pnl.BackColor           = Color.FromArgb(45, 45, 65);
                    stepLabels[i].ForeColor = TEXT_DIM;
                }
            }
        }

        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code
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
