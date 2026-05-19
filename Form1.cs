using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace frm_mcafee_unin
{
    public partial class Form1 : Form
    {
        private const string MCAFEE_DIR    = @"C:\Program Files\McAfee\wps\1.39.160.1";
        private const string UNINSTALL_EXE = @"C:\Program Files\McAfee\wps\1.39.160.1\mc-update.exe";
        private const string REG_TOOL      = @"HKEY_LOCAL_MACHINE\SOFTWARE\McAfeeCleanupTool";
        private const string REG_PENDING   = "PendingStep3";   // flag: cần chạy bước 3 sau restart
        private const string REG_RESTARTED = "HasRestarted";   // flag: đã restart rồi, không restart nữa

        private static readonly string[] MCAFEE_DIRS = {
            @"C:\Program Files\McAfee",
            @"C:\Program Files\Common Files\McAfee",
            @"C:\ProgramData\McAfee",
        };

        private static readonly string[] MCAFEE_SERVICES = {
            "McAfeeFramework", "mfefire", "mfevtp", "mcshield",
            "mfehidk", "mfenlfk", "mfewfpk", "McAPExe", "ModuleCoreService"
        };

        private readonly List<string> _foundFolders = new List<string>();
        private readonly List<string> _foundRegKeys = new List<string>();

        private System.Windows.Forms.Timer _restartTimer;
        private int _restartCountdown;

        private Process _uninstallProcess;
        private CancellationTokenSource _abortCts;
        private string _customUninstallExe;   // null = dùng đường dẫn mặc định

        // Telegram webhook để gửi log khi có lỗi Safe Mode (để trống = không gửi)
        private const string TELEGRAM_WEBHOOK = "";

        // ── P/Invoke: MoveFileEx — xóa file bị khóa vào lần boot kế tiếp ────
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool MoveFileEx(string lpExistingFileName,
                                              string lpNewFileName,
                                              uint   dwFlags);
        private const uint MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;

        // ── P/Invoke: NtSuspendProcess — đóng băng toàn bộ watchdog network ─
        [DllImport("ntdll.dll")]
        private static extern int NtSuspendProcess(IntPtr processHandle);

        // Đặt true khi MoveFileEx được gọi → hiển thị cảnh báo reboot ở cuối Bước 3
        private bool _hasPendingDeletes;

        // Buffer log toàn bộ phiên để ghi ra file khi kết thúc Bước 3
        private readonly StringBuilder _sessionLog = new StringBuilder();

        public Form1()
        {
            InitializeComponent();
            SetupCustomUI();
            // Win7 mặc định TLS 1.0 — ép TLS 1.2 để tránh lỗi kết nối nếu cần
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; } catch { }
            this.Shown += Form1_Shown;
        }

        private async void Form1_Shown(object sender, EventArgs e)
        {
            // Hiển thị SHA256 của chính tool để người dùng có thể xác minh tính toàn vẹn
            try
            {
                using (var sha = SHA256.Create())
                using (var fs  = File.OpenRead(Application.ExecutablePath))
                {
                    byte[] hash = sha.ComputeHash(fs);
                    Log("SHA256: " + BitConverter.ToString(hash).Replace("-", "").ToLower());
                }
            }
            catch { }

            // Dọn backup files cũ > 7 ngày (chạy ngầm, không block UI)
#pragma warning disable CS4014
            Task.Run(() => CleanupOldBackupFiles(TimeSpan.FromDays(7)));
#pragma warning restore CS4014

            // Kiểm tra xem có đang ở trạng thái sau restart không
            if (IsPendingStep3())
            {
                ClearPendingStep3();
                GoToStep(2);
                Log("Khởi động lại hoàn tất — đang bắt đầu Bước 3...");
                await Task.Delay(800);
                BtnScanLeftovers_Click(null, EventArgs.Empty);
                return;
            }

            // Kiểm tra McAfee rồi tự động chạy Bước 1
            bool found = DetectMcAfee();
            if (!found)
            {
                Log("Không tìm thấy McAfee — chuyển sang quét dọn.");
                GoToStep(2);
                await Task.Delay(400);
                BtnScanLeftovers_Click(null, EventArgs.Empty);
                return;
            }

            Log("Phát hiện McAfee. Đang tự động tạo điểm sao lưu...");
            await RunStep1();
        }

        // ─────────────────────────────────────────────────────────────────────
        // BƯỚC 1: SAO LƯU (tự động)
        // ─────────────────────────────────────────────────────────────────────
        private async Task RunStep1()
        {
            btnCreateRestorePoint.Enabled = false;
            btnNext.Enabled               = false;

            bool rpOk = await Task.Run(() => CreateSystemRestorePoint());
            await Task.Run(() => ExportRegistryBackup());

            string msg = rpOk
                ? "✔ System Restore Point đã tạo. File .reg backup (nếu có) đã xuất ra Desktop."
                : "⚠ Không tạo được Restore Point — System Restore có thể đang tắt. Vẫn tiếp tục được.";

            Log(msg);
            lblBackupStatus.Text      = msg;
            lblBackupStatus.ForeColor = rpOk
                ? System.Drawing.Color.FromArgb(50, 200, 120)
                : System.Drawing.Color.FromArgb(255, 160, 50);

            btnNext.Enabled = true;
            // Tự động chuyển sang Bước 2 sau 1 giây
            await Task.Delay(1200);
            GoToStep(1);
        }

        private async void BtnCreateRestorePoint_Click(object sender, EventArgs e)
        {
            await RunStep1();
        }

        private static void EnsureVssRunning()
        {
            try
            {
                var svc = new ServiceController("VSS");
                if (svc.Status != ServiceControllerStatus.Running)
                {
                    svc.Start();
                    svc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                }
            }
            catch { }
        }

        private bool CreateSystemRestorePoint()
        {
            try
            {
                EnsureVssRunning();
                RunCmd("powershell", "-ExecutionPolicy Bypass -NonInteractive -Command \"Enable-ComputerRestore -Drive 'C:\\'\"");
                var scope    = new ManagementScope(@"\\localhost\root\default");
                var cls      = new ManagementClass(scope, new ManagementPath("SystemRestore"), null);
                var inParams = cls.GetMethodParameters("CreateRestorePoint");
                inParams["Description"]      = "McAfee Uninstall Tool Backup";
                inParams["RestorePointType"] = 0;
                inParams["EventType"]        = 100;
                var result = cls.InvokeMethod("CreateRestorePoint", inParams, null);
                return (uint)result["ReturnValue"] == 0;
            }
            catch { return false; }
        }

        private static readonly string[] MCAFEE_REG_EXPORT_PATHS = {
            @"HKLM\SOFTWARE\McAfee",
            @"HKLM\SOFTWARE\WOW6432Node\McAfee",
            @"HKLM\SOFTWARE\McAfee.NTS",
            @"HKLM\SYSTEM\CurrentControlSet\Services\mfefire",
        };

        private bool ExportRegistryBackup()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            // Kiểm tra dung lượng ổ C còn trống (tối thiểu 10 MB)
            try
            {
                var drive = new DriveInfo("C");
                if (drive.AvailableFreeSpace < 10L * 1024 * 1024)
                {
                    Log("⚠ Ổ C còn ít hơn 10 MB trống — bỏ qua xuất file .reg backup.");
                    return false;
                }
            }
            catch { }

            foreach (var regPath in MCAFEE_REG_EXPORT_PATHS)
            {
                // Kiểm tra key tồn tại trước khi export
                string subKey = regPath.Substring(5); // bỏ "HKLM\"
                bool exists = false;
                try
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(subKey))
                        exists = k != null;
                }
                catch { }

                if (!exists) continue;

                string safeName = SafeRegName(regPath);
                string dest = Path.Combine(desktop, $"McAfee_Backup_{safeName}.reg");
                bool ok = RunCmd("reg", $"export \"{regPath}\" \"{dest}\" /y");
                Log(ok ? $"  ✔ Đã xuất: {dest}" : $"  ✘ Không xuất được: {regPath}");
            }

            return true;
        }

        // ─────────────────────────────────────────────────────────────────────
        // BƯỚC 2: GỠ CÀI ĐẶT
        // ─────────────────────────────────────────────────────────────────────
        private async void BtnRunUninstall_Click(object sender, EventArgs e)
        {
            btnRunUninstall.Enabled = false;
            btnNext.Enabled         = false;
            btnAbort.Enabled        = true;
            _abortCts               = new CancellationTokenSource();

            string exePath = _customUninstallExe ?? UNINSTALL_EXE;
            if (File.Exists(exePath))
            {
                // Set flag TRƯỚC khi chạy — đề phòng McAfee tự restart mà không qua countdown của tool
                SetPendingStep3WithRunOnce();

                Log("Tìm thấy mc-update.exe — đang chạy /uninstall...");
                bool ok = await Task.Run(() => RunMcUpdateUninstall(exePath, _abortCts.Token));
                btnAbort.Enabled = false;

                if (_abortCts.IsCancellationRequested)
                {
                    ClearPendingStep3();
                    Log("⚠ Đã hủy bỏ gỡ cài đặt.");
                    lblUninstallStatus.Text      = "Đã hủy bỏ.";
                    lblUninstallStatus.ForeColor = System.Drawing.Color.FromArgb(255, 160, 50);
                    btnRunUninstall.Enabled = true;
                    btnNext.Enabled         = true;
                    return;
                }

                if (ok)
                {
                    Log("✔ Gỡ cài đặt hoàn tất.");
                    lblUninstallStatus.Text      = "✔ Gỡ cài đặt thành công.";
                    lblUninstallStatus.ForeColor = System.Drawing.Color.FromArgb(50, 200, 120);
                    PromptRestart();
                }
                else
                {
                    ClearPendingStep3();
                    Log("⚠ mc-update thất bại — chuyển sang phương án Safe Mode.");
                    OfferSafeModeCleanup();
                }
            }
            else
            {
                btnAbort.Enabled = false;
                Log("Không tìm thấy mc-update.exe — chuyển sang phương án Safe Mode.");
                OfferSafeModeCleanup();
            }
        }

        private void BtnBrowseUninstall_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog
            {
                Title  = "Chọn mc-update.exe (hoặc trình gỡ McAfee bất kỳ)",
                Filter = "Executable|*.exe",
            })
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _customUninstallExe = dlg.FileName;
                    lblUninstallStatus.Text = "Đường dẫn: " + _customUninstallExe;
                    Log("Đường dẫn tuỳ chỉnh: " + _customUninstallExe);
                }
            }
        }

        private void BtnAbort_Click(object sender, EventArgs e)
        {
            _abortCts?.Cancel();
            btnAbort.Enabled = false;
            Log("Đang hủy bỏ tiến trình gỡ cài đặt...");
            try { _uninstallProcess?.Kill(); } catch { }
        }

        private static void SetPendingStep3WithRunOnce()
        {
            Registry.SetValue(REG_TOOL, REG_PENDING, "1");
            string appPath = Application.ExecutablePath;
            Registry.SetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
                "McAfeeCleanupTool", $"\"{appPath}\"");
        }

        private bool RunMcUpdateUninstall(string exePath = null, CancellationToken ct = default(CancellationToken))
        {
            if (exePath == null) exePath = UNINSTALL_EXE;
            try
            {
                _uninstallProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName        = exePath,
                        Arguments       = "/uninstall /silent",
                        Verb            = "runas",
                        UseShellExecute = true
                    }
                };
                _uninstallProcess.Start();
                // Kiểm tra abort mỗi giây trong khi chờ (tối đa 5 phút)
                for (int i = 0; i < 300; i++)
                {
                    if (ct.IsCancellationRequested) return false;
                    if (_uninstallProcess.WaitForExit(1000)) break;
                }
                return !ct.IsCancellationRequested && _uninstallProcess.ExitCode == 0;
            }
            catch
            {
                if (ct.IsCancellationRequested) return false;
                try
                {
                    _uninstallProcess = Process.Start(new ProcessStartInfo
                    {
                        FileName        = exePath,
                        Arguments       = "/uninstall",
                        Verb            = "runas",
                        UseShellExecute = true
                    });
                    for (int i = 0; i < 600; i++)
                    {
                        if (ct.IsCancellationRequested) return false;
                        if (_uninstallProcess.WaitForExit(1000)) break;
                    }
                    return !ct.IsCancellationRequested && _uninstallProcess.ExitCode == 0;
                }
                catch { return false; }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // RESTART: đếm ngược 15 giây
        // ─────────────────────────────────────────────────────────────────────
        private void PromptRestart()
        {
            // Flag đã được set trước khi chạy uninstall, chỉ cần đảm bảo vẫn còn đó
            SetPendingStep3WithRunOnce();

            _restartCountdown = 15;
            UpdateRestartLabel();
            btnNext.Enabled = false;

            _restartTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _restartTimer.Tick += RestartTimer_Tick;
            _restartTimer.Start();
        }

        private void RestartTimer_Tick(object sender, EventArgs e)
        {
            _restartCountdown--;
            UpdateRestartLabel();

            if (_restartCountdown <= 0)
            {
                _restartTimer.Stop();
                Registry.SetValue(REG_TOOL, REG_RESTARTED, "1");
                RunCmd("shutdown", "/r /t 0");
            }
        }

        private void UpdateRestartLabel()
        {
            string msg = _restartCountdown > 0
                ? $"✔ Gỡ cài đặt xong. Máy sẽ khởi động lại sau {_restartCountdown}s để hoàn tất.\n(Tool sẽ tự mở lại và thực hiện Bước 3)"
                : "Đang khởi động lại...";

            if (lblUninstallStatus.InvokeRequired)
                lblUninstallStatus.Invoke(new Action(() => { lblUninstallStatus.Text = msg; }));
            else
                lblUninstallStatus.Text = msg;

            Log($"Khởi động lại sau {_restartCountdown}s...");
        }

        // ─────────────────────────────────────────────────────────────────────
        // SAFE MODE FALLBACK
        // ─────────────────────────────────────────────────────────────────────
        private void OfferSafeModeCleanup()
        {
            var dlg = MessageBox.Show(
                "mc-update.exe không hoạt động được.\n\n" +
                "Tool sẽ lên lịch dọn dẹp tự động trong Safe Mode (1 lần duy nhất),\n" +
                "sau đó khởi động lại máy.\n\nBạn có muốn tiếp tục không?",
                "Phương án Safe Mode", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (dlg == DialogResult.Yes)
                ScheduleSafeModeCleanup();
            else
            {
                btnRunUninstall.Enabled = true;
                btnNext.Enabled         = true;
                Log("Bỏ qua Safe Mode — tiếp tục sang Bước 3.");
            }
        }

        private void ScheduleSafeModeCleanup()
        {
            try
            {
                Log("Đang thiết lập Safe Mode boot...");

                string scriptPath = Path.Combine(@"C:\Windows\Temp", "mcafee_safemode_clean.ps1");
                File.WriteAllText(scriptPath, BuildSafeModeScript(scriptPath), System.Text.Encoding.UTF8);

                // Dùng RunOnce với prefix * — cách DUY NHẤT đáng tin cậy trong Safe Mode
                // Task Scheduler không chạy trong safeboot minimal
                string psCmd = $"powershell.exe -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"";
                Registry.SetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
                    "*McAfeeCleanupSafeMode", psCmd);

                // Đánh dấu cần chạy bước 3 sau khi boot về normal
                Registry.SetValue(REG_TOOL, REG_PENDING,   "1");
                Registry.SetValue(REG_TOOL, REG_RESTARTED, "1");

                // Bật Safe Mode (minimal — không cần network)
                RunCmd("bcdedit", "/set {current} safeboot minimal");

                Log("Đã thiết lập. Máy sẽ khởi động lại vào Safe Mode...");
                MessageBox.Show(
                    "Đã thiết lập xong!\n\n" +
                    "Máy sẽ boot vào Safe Mode, tự dọn sạch McAfee,\n" +
                    "rồi khởi động lại bình thường và tự mở Bước 3.",
                    "Sẵn sàng", MessageBoxButtons.OK, MessageBoxIcon.Information);

                RunCmd("shutdown", "/r /t 5");
            }
            catch (Exception ex)
            {
                Log($"Lỗi: {ex.Message}");
                MessageBox.Show("Không thể thiết lập Safe Mode.\nHãy chắc chắn chạy tool với quyền Administrator.",
                    "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                btnRunUninstall.Enabled = true;
                btnNext.Enabled         = true;
            }
        }

        private string BuildSafeModeScript(string selfPath)
        {
            string appPath  = Application.ExecutablePath;
            string logPath  = @"C:\Windows\Temp\mcafee_safemode_clean.log";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# McAfee Safe Mode Cleanup — auto-generated");

            // Helper ghi log
            sb.AppendLine($"$log = '{logPath}'");
            sb.AppendLine("function Write-Log($msg) { \"$(Get-Date -Format 'HH:mm:ss')  $msg\" | Tee-Object -FilePath $log -Append | Out-Null }");
            sb.AppendLine("Write-Log 'Safe Mode cleanup started'");

            // Kill tiến trình McAfee còn sống trong Safe Mode
            foreach (var pn in MCAFEE_PROCESSES)
                sb.AppendLine($"Stop-Process -Name '{pn}' -Force -ErrorAction SilentlyContinue; Write-Log 'Kill process: {pn}'");

            // Xóa thư mục McAfee
            foreach (var dir in MCAFEE_DIRS)
            {
                sb.AppendLine($"Write-Log 'TakeOwn: {dir}'");
                sb.AppendLine($"takeown /F '{dir}' /R /D Y 2>$null");
                sb.AppendLine($"icacls '{dir}' /grant Administrators:F /T /C /Q 2>$null");
                sb.AppendLine($"Remove-Item -Path '{dir}' -Recurse -Force -ErrorAction SilentlyContinue");
                sb.AppendLine($"if (Test-Path '{dir}') {{ Write-Log 'FAILED to delete: {dir}' }} else {{ Write-Log 'Deleted: {dir}' }}");
            }

            // Xóa services
            foreach (var svc in MCAFEE_SERVICES)
            {
                sb.AppendLine($"sc.exe delete '{svc}' 2>$null");
                sb.AppendLine($"Write-Log 'Deleted service: {svc}'");
            }

            // Khôi phục boot bình thường
            sb.AppendLine("Write-Log 'Restoring normal boot'");
            sb.AppendLine("bcdedit /deletevalue '{current}' safeboot");

            // Đăng ký app mở lại sau khi boot về normal
            sb.AppendLine($"New-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\RunOnce' " +
                          $"-Name 'McAfeeCleanupTool' -Value '\"{appPath}\"' -PropertyType String -Force | Out-Null");
            sb.AppendLine("Write-Log 'Registered app for RunOnce on next normal boot'");

            // Xóa entry RunOnce Safe Mode của chính mình
            sb.AppendLine("Remove-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\RunOnce' " +
                          "-Name '*McAfeeCleanupSafeMode' -ErrorAction SilentlyContinue");

            sb.AppendLine("Write-Log 'Cleanup complete — restarting'");

            // Xóa script (log vẫn giữ lại để hậu kiểm)
            sb.AppendLine($"Remove-Item -Path '{selfPath}' -Force -ErrorAction SilentlyContinue");

            sb.AppendLine("Restart-Computer -Force");

            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────────────────────
        // BƯỚC 3: QUÉT DỌN
        // ─────────────────────────────────────────────────────────────────────
        private async void BtnScanLeftovers_Click(object sender, EventArgs e)
        {
            btnScanLeftovers.Enabled  = false;
            btnDeleteSelected.Enabled = false;
            treeLeftovers.Nodes.Clear();
            _foundFolders.Clear();
            _foundRegKeys.Clear();
            Log("Đang quét toàn bộ hệ thống...");

            await Task.Run(() => ScanSystem());
            PopulateTree();
            btnScanLeftovers.Enabled = true;

            if (_foundFolders.Count == 0 && _foundRegKeys.Count == 0)
            {
                Log("✔ Hệ thống sạch hoàn toàn.");
                MessageBox.Show(
                    "Hệ thống đã sạch hoàn toàn!\nKhông tìm thấy bất kỳ dấu vết McAfee nào.",
                    "Hoàn tất", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.Close();
            }
            else
            {
                Log($"Tìm thấy {_foundFolders.Count} thư mục, {_foundRegKeys.Count} registry key.");
                btnDeleteSelected.Enabled = true;
            }
        }

        private void ScanSystem()
        {
            // ── Thư mục ──────────────────────────────────────────────────────
            string[] scanRoots = {
                @"C:\Program Files",
                @"C:\Program Files (x86)",
                @"C:\Program Files\Common Files",
                @"C:\ProgramData",
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            };
            foreach (var root in scanRoots)
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    foreach (var dir in Directory.GetDirectories(root, "*", System.IO.SearchOption.TopDirectoryOnly))
                    {
                        string name = Path.GetFileName(dir).ToLowerInvariant();
                        if (name.Contains("mcafee") || name.Contains("mfe") || name.Contains("mcshield"))
                            _foundFolders.Add(dir);
                    }
                }
                catch { }
            }

            // ── Registry: các key gốc McAfee ─────────────────────────────────
            ScanRegistryHive(Registry.LocalMachine, @"SOFTWARE\McAfee");
            ScanRegistryHive(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\McAfee");
            ScanRegistryHive(Registry.CurrentUser,  @"SOFTWARE\McAfee");

            // ── Registry: NativeMessagingHosts (Chrome / Edge) ────────────────
            ScanChildrenForMcAfee(Registry.LocalMachine, @"SOFTWARE\Google\Chrome\NativeMessagingHosts");
            ScanChildrenForMcAfee(Registry.LocalMachine, @"SOFTWARE\Microsoft\Edge\NativeMessagingHosts");
            ScanChildrenForMcAfee(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Google\Chrome\NativeMessagingHosts");
            ScanChildrenForMcAfee(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Edge\NativeMessagingHosts");

            // ── Registry: Uninstall entries ────────────────────────────────────
            ScanUninstallForMcAfee(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            ScanUninstallForMcAfee(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");

            // ── Registry: Services ─────────────────────────────────────────────
            ScanRegistryServicesForMcAfee();

            // ── Driver files (.sys) ────────────────────────────────────────────
            ScanDriversForMcAfee();

            // ── Scheduled Tasks ────────────────────────────────────────────────
            ScanScheduledTasksForMcAfee();

            // ── WMI SecurityCenter namespace ───────────────────────────────────
            CleanWmiSecurityCenter();

            // ── HKCR COM GUIDs ────────────────────────────────────────────────
            ScanHkcrGuidsForMcAfee();

            // ── SharedDLLs reference counts ───────────────────────────────────
            CleanSharedDllsForMcAfee();
        }

        private void ScanChildrenForMcAfee(RegistryKey hive, string parentPath)
        {
            try
            {
                using (var parent = hive.OpenSubKey(parentPath))
                {
                    if (parent == null) return;
                    foreach (var child in parent.GetSubKeyNames())
                    {
                        string cl = child.ToLowerInvariant();
                        if (cl.Contains("mcafee") || cl.Contains("webadvisor") || cl.Contains("siteadvisor"))
                            _foundRegKeys.Add(hive.Name + "\\" + parentPath + "\\" + child);
                    }
                }
            }
            catch { }
        }

        private void ScanUninstallForMcAfee(RegistryKey hive, string parentPath)
        {
            try
            {
                using (var parent = hive.OpenSubKey(parentPath))
                {
                    if (parent == null) return;
                    foreach (var child in parent.GetSubKeyNames())
                    {
                        try
                        {
                            using (var sub = parent.OpenSubKey(child))
                            {
                                string name = sub?.GetValue("DisplayName")?.ToString() ?? "";
                                string pub  = sub?.GetValue("Publisher")?.ToString() ?? "";
                                if (name.ToLowerInvariant().Contains("mcafee") ||
                                    pub.ToLowerInvariant().Contains("mcafee"))
                                    _foundRegKeys.Add(hive.Name + "\\" + parentPath + "\\" + child);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private void ScanHkcrGuidsForMcAfee()
        {
            // McAfee đăng ký COM object và shell extension dưới HKCR\CLSID
            // Nếu còn sót sẽ gây "Orphaned context menu" — chuột phải vẫn hiện menu McAfee
            try
            {
                using (var clsid = Registry.ClassesRoot.OpenSubKey("CLSID"))
                {
                    if (clsid == null) return;
                    foreach (var guid in clsid.GetSubKeyNames())
                    {
                        try
                        {
                            using (var k = clsid.OpenSubKey(guid))
                            {
                                // Kiểm tra InprocServer32 trỏ vào thư mục McAfee
                                string server = k?.OpenSubKey("InprocServer32")?.GetValue(null)?.ToString() ?? "";
                                string defVal = k?.GetValue(null)?.ToString() ?? "";
                                bool isMcAfee = server.ToLowerInvariant().Contains("mcafee")
                                             || defVal.ToLowerInvariant().Contains("mcafee");
                                if (isMcAfee)
                                    _foundRegKeys.Add(@"HKEY_CLASSES_ROOT\CLSID\" + guid);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private void CleanSharedDllsForMcAfee()
        {
            const string sharedDllsPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs";
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(sharedDllsPath, true))
                {
                    if (key == null) return;
                    var toRemove = new List<string>();
                    foreach (var valName in key.GetValueNames())
                    {
                        if (valName.ToLowerInvariant().Contains("mcafee"))
                            toRemove.Add(valName);
                    }
                    foreach (var v in toRemove)
                    {
                        key.DeleteValue(v, false);
                        Log("  ✔ Đã xóa SharedDLL entry: " + v);
                    }
                }
            }
            catch { }
        }

        private void ScanDriversForMcAfee()
        {
            string driversDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers");
            if (!Directory.Exists(driversDir)) return;
            try
            {
                foreach (var f in Directory.GetFiles(driversDir, "mfe*.sys"))
                    _foundFolders.Add(f);   // dùng _foundFolders để hiển thị cùng nhóm "Thư mục/File"
                foreach (var f in Directory.GetFiles(driversDir, "mcafee*.sys"))
                    _foundFolders.Add(f);
            }
            catch { }
        }

        private void ScanScheduledTasksForMcAfee()
        {
            string tasksDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Tasks");
            if (!Directory.Exists(tasksDir)) return;
            try
            {
                foreach (var f in Directory.GetFiles(tasksDir, "*", System.IO.SearchOption.AllDirectories))
                {
                    string name = Path.GetFileName(f).ToLowerInvariant();
                    if (name.Contains("mcafee") || name.Contains("mfe") || name.Contains("webadvisor"))
                        _foundFolders.Add(f);
                }
                // Quét thêm thư mục con có tên McAfee trong Tasks
                foreach (var d in Directory.GetDirectories(tasksDir, "*", System.IO.SearchOption.TopDirectoryOnly))
                {
                    if (Path.GetFileName(d).ToLowerInvariant().Contains("mcafee"))
                        _foundFolders.Add(d);
                }
            }
            catch { }
        }

        private void CleanWmiSecurityCenter()
        {
            // Xóa đăng ký AV product McAfee khỏi WMI SecurityCenter2
            // để Windows nhận diện đúng trạng thái bảo vệ và bật lại Defender
            string[] namespaces = { @"root\SecurityCenter", @"root\SecurityCenter2" };
            foreach (var ns in namespaces)
            {
                try
                {
                    var connOpts = new ConnectionOptions { Timeout = TimeSpan.FromSeconds(10) };
                    var scope    = new ManagementScope("\\\\.\\" + ns, connOpts);
                    scope.Connect();
                    var enumOpts = new EnumerationOptions
                    {
                        Timeout          = TimeSpan.FromSeconds(10),
                        ReturnImmediately = false
                    };
                    var query = new ObjectQuery("SELECT * FROM AntiVirusProduct");
                    using (var searcher = new ManagementObjectSearcher(scope, query, enumOpts))
                    using (var results  = searcher.Get())
                    {
                        foreach (ManagementObject obj in results)
                        {
                            string dispName = obj["displayName"]?.ToString() ?? "";
                            if (dispName.ToLowerInvariant().Contains("mcafee"))
                            {
                                obj.Delete();
                                Log("  ✔ Đã xóa WMI AV entry: " + dispName + " (" + ns + ")");
                            }
                        }
                    }
                }
                catch { }
            }
        }

        private void ScanRegistryHive(RegistryKey hive, string subKey)
        {
            try
            {
                using (var key = hive.OpenSubKey(subKey))
                    if (key != null) _foundRegKeys.Add(hive.Name + "\\" + subKey);
            }
            catch { }
        }

        private void ScanRegistryServicesForMcAfee()
        {
            try
            {
                using (var svcKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services"))
                {
                    if (svcKey == null) return;
                    foreach (var name in svcKey.GetSubKeyNames())
                    {
                        string n = name.ToLowerInvariant();
                        if (n.StartsWith("mfe") || n.StartsWith("mcafee") || n.StartsWith("mcshield"))
                            _foundRegKeys.Add(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\" + name);
                    }
                }
            }
            catch { }
        }

        private void PopulateTree()
        {
            treeLeftovers.CheckBoxes = true;
            treeLeftovers.Nodes.Clear();

            if (_foundFolders.Count > 0)
            {
                var node = new TreeNode($"Thư mục ({_foundFolders.Count})");
                foreach (var f in _foundFolders)
                    node.Nodes.Add(new TreeNode(f) { Checked = true, Tag = "folder" });
                node.Expand();
                treeLeftovers.Nodes.Add(node);
            }

            if (_foundRegKeys.Count > 0)
            {
                var node = new TreeNode($"Registry ({_foundRegKeys.Count})");
                foreach (var r in _foundRegKeys)
                    node.Nodes.Add(new TreeNode(r) { Checked = true, Tag = "reg" });
                node.Expand();
                treeLeftovers.Nodes.Add(node);
            }
        }

        private async void BtnDeleteSelected_Click(object sender, EventArgs e)
        {
            // Kiểm tra tính toàn vẹn file backup trước khi xóa
            ValidateBackupFiles();

            if (MessageBox.Show(
                    "Xóa tất cả mục đã tích chọn?\nHành động này không thể hoàn tác.",
                    "Xác nhận", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            btnDeleteSelected.Enabled = false;
            btnScanLeftovers.Enabled  = false;
            _hasPendingDeletes = false;
            Log("Đang kill tiến trình McAfee...");

            // Thu thập danh sách trên UI thread trước khi vào background — tránh cross-thread access
            var items = new List<Tuple<string, bool>>();
            foreach (TreeNode root in treeLeftovers.Nodes)
                foreach (TreeNode child in root.Nodes)
                    if (child.Checked)
                        items.Add(Tuple.Create(child.Text, child.Tag?.ToString() == "folder"));

            int deleted = 0, failed = 0;
            long freedBytes = 0;
            await Task.Run(() =>
            {
                long diskBefore = 0;
                try { diskBefore = new DriveInfo("C").AvailableFreeSpace; } catch { }

                KillMcAfeeProcesses();

                foreach (var item in items)
                {
                    string target   = item.Item1;
                    bool   isFolder = item.Item2;
                    bool   ok       = isFolder ? DeleteFolder(target) : DeleteRegistryKey(target);

                    Log(ok
                        ? $"  ✔ Đã xóa {(isFolder ? "thư mục" : "key")}: {target}"
                        : $"  ✘ Thất bại {(isFolder ? "thư mục" : "key")}: {target}");

                    if (ok) deleted++; else failed++;
                }

                foreach (var svc in MCAFEE_SERVICES)
                {
                    bool ok = RunCmd("sc", "delete \"" + svc + "\"");
                    if (ok) Log("  ✔ Đã xóa service: " + svc);
                }

                // Dọn WMI SecurityCenter trước khi bật Defender để Windows nhận diện đúng
                CleanWmiSecurityCenter();

                RunCmd("powershell",
                    "-ExecutionPolicy Bypass -NonInteractive -Command \"Set-MpPreference -DisableRealtimeMonitoring $false;" +
                    "Start-Service WinDefend -ErrorAction SilentlyContinue\"");

                try { freedBytes = new DriveInfo("C").AvailableFreeSpace - diskBefore; } catch { }
            });

            GenerateUndoScript();
            FlushLogToFile();
            string freedStr = freedBytes > 0
                ? string.Format(" | Giải phóng (ước tính): {0:0.0} MB", freedBytes / 1048576.0)
                : "";
            Log("✔ Xóa xong: " + deleted + " thành công, " + failed + " thất bại." + freedStr + " — Defender đã bật.");
            TrySendLog("McAfee Cleanup: deleted=" + deleted + " failed=" + failed + freedStr);

            string pendingNote = _hasPendingDeletes
                ? "\n\n⚠ Một số tệp đang bị khóa bởi kernel và sẽ tự động xóa sau khi bạn KHỞI ĐỘNG LẠI máy."
                : "";
            MessageBox.Show(
                "Hoàn tất!\n✔ Thành công: " + deleted + "\n✘ Thất bại: " + failed + freedStr +
                "\n\nWindows Defender đã được bật lại.\nScript phục hồi đã tạo trên Desktop.\nLog chi tiết: C:\\Windows\\Temp\\McAfeeCleanup_Final.log" + pendingNote,
                "Hoàn tất", MessageBoxButtons.OK,
                _hasPendingDeletes ? MessageBoxIcon.Warning : MessageBoxIcon.Information);

            // Quét lại để xác nhận
            await Task.Run(() => ScanSystem());
            PopulateTree();
            if (_foundFolders.Count == 0 && _foundRegKeys.Count == 0)
            {
                treeLeftovers.Nodes.Clear();
                treeLeftovers.Nodes.Add(new TreeNode("✔  Hệ thống sạch hoàn toàn.")
                {
                    ForeColor = System.Drawing.Color.FromArgb(50, 200, 120)
                });
                Log("✔ Hệ thống sạch hoàn toàn.");
            }

            btnScanLeftovers.Enabled = true;
            btnNext.Enabled          = true;
        }

        // Tạo script .bat trên Desktop để "đảo ngược" các xóa Registry
        private void GenerateUndoScript()
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string script  = Path.Combine(desktop, "McAfee_Undo_Restore.bat");
                var sb = new StringBuilder();
                sb.AppendLine("@echo off");
                sb.AppendLine(":: Script phục hồi Registry McAfee (auto-generated bởi McAfee Uninstall Tool)");
                sb.AppendLine(":: Chạy với quyền Administrator nếu cần");
                // Reimport tất cả .reg đã export ở Bước 1
                foreach (var regPath in MCAFEE_REG_EXPORT_PATHS)
                {
                    string regFile = Path.Combine(desktop, "McAfee_Backup_" + SafeRegName(regPath) + ".reg");
                    sb.AppendLine("if exist \"" + regFile + "\" reg import \"" + regFile + "\"");
                }
                sb.AppendLine("echo Phuc hoi hoan tat. Khoi dong lai may de ap dung.");
                sb.AppendLine("pause");
                File.WriteAllText(script, sb.ToString(), Encoding.Default);
                Log("  ✔ Đã tạo script phục hồi: " + script);
            }
            catch { }
        }

        // Kiểm tra các file .reg backup có nội dung hợp lệ (> 0 KB)
        private void ValidateBackupFiles()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var bad = new List<string>();
            foreach (var regPath in MCAFEE_REG_EXPORT_PATHS)
            {
                string f = Path.Combine(desktop, "McAfee_Backup_" + SafeRegName(regPath) + ".reg");
                if (File.Exists(f) && new FileInfo(f).Length == 0)
                    bad.Add(Path.GetFileName(f));
            }
            if (bad.Count > 0)
                Log("⚠ File backup có kích thước 0 KB (có thể bị lỗi khi export): " + string.Join(", ", bad));
        }

        // Xóa file backup + undo script cũ hơn maxAge
        private void CleanupOldBackupFiles(TimeSpan maxAge)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            DateTime cutoff = DateTime.Now - maxAge;
            // .reg backup files
            foreach (var regPath in MCAFEE_REG_EXPORT_PATHS)
            {
                string f = Path.Combine(desktop, "McAfee_Backup_" + SafeRegName(regPath) + ".reg");
                try { if (File.Exists(f) && File.GetLastWriteTime(f) < cutoff) File.Delete(f); }
                catch { }
            }
            // Undo script
            string bat = Path.Combine(desktop, "McAfee_Undo_Restore.bat");
            try { if (File.Exists(bat) && File.GetLastWriteTime(bat) < cutoff) File.Delete(bat); }
            catch { }
        }

        // Xóa ngay lập tức theo yêu cầu người dùng
        private void BtnCleanupTempFiles_Click(object sender, EventArgs e)
        {
            CleanupOldBackupFiles(TimeSpan.Zero);
            Log("✔ Đã dọn sạch file backup và undo script trên Desktop.");
            MessageBox.Show("Đã xóa tất cả file .reg backup và script phục hồi trên Desktop.",
                "Dọn dẹp xong", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // Quét ServiceDll trong registry để tìm service ẩn của svchost host McAfee DLL
        private void StopOrphanedSvcHostServices()
        {
            try
            {
                using (var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services"))
                {
                    if (services == null) return;
                    foreach (var svcName in services.GetSubKeyNames())
                    {
                        try
                        {
                            using (var param = services.OpenSubKey(svcName + @"\Parameters"))
                            {
                                string dll = param?.GetValue("ServiceDll")?.ToString() ?? "";
                                if (!dll.ToLowerInvariant().Contains("mcafee")) continue;
                                RunCmd("sc", "stop \"" + svcName + "\"");
                                Log("  ✔ Đã stop svchost service với McAfee DLL: " + svcName);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private void TrySendLog(string logContent)
        {
            if (string.IsNullOrEmpty(TELEGRAM_WEBHOOK)) return;
            try
            {
                using (var wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.ContentType] = "application/json";
                    string json = "{\"text\": \"" + logContent.Replace("\"", "'").Replace("\n", "\\n") + "\"}";
                    wc.UploadString(TELEGRAM_WEBHOOK, "POST", json);
                }
            }
            catch { }
        }

        // ─────────────────────────────────────────────────────────────────────
        // ĐIỀU HƯỚNG WIZARD
        // ─────────────────────────────────────────────────────────────────────
        private void GoToStep(int index)
        {
            tabControl.SelectedIndex = index;
            progressBarAll.Value     = index * 50;
            UpdateStepIndicator(index);

            if (index == tabControl.TabCount - 1)
                btnNext.Text = "Hoàn tất  ✔";

            // Bước 2: phải nhấn nút gỡ và hoàn tất mới được Tiếp tục
            if (index == 1)
                btnNext.Enabled = false;
        }

        private void BtnNext_Click(object sender, EventArgs e)
        {
            if (tabControl.SelectedIndex < tabControl.TabCount - 1)
            {
                GoToStep(tabControl.SelectedIndex + 1);
            }
            else
            {
                MessageBox.Show("Hệ thống đã được dọn sạch hoàn toàn!", "Hoàn tất",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.Close();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────────────────────
        private bool DetectMcAfee() =>
            Directory.Exists(MCAFEE_DIR)
            || File.Exists(UNINSTALL_EXE)
            || RegistryKeyExists(@"SOFTWARE\McAfee")
            || RegistryKeyExists(@"SOFTWARE\WOW6432Node\McAfee");

        private static bool IsPendingStep3()
        {
            try { return Registry.GetValue(REG_TOOL, REG_PENDING, null)?.ToString() == "1"; }
            catch { return false; }
        }

        private static void ClearPendingStep3()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\McAfeeCleanupTool", true))
                    key?.DeleteValue(REG_PENDING, false);
            }
            catch { }
        }

        private void Log(string msg)
        {
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg;
            _sessionLog.AppendLine(line);
            if (txtLog.InvokeRequired)
                txtLog.Invoke(new Action(() => { txtLog.AppendText(line + "\n"); txtLog.ScrollToCaret(); }));
            else
            {
                txtLog.AppendText(line + "\n");
                txtLog.ScrollToCaret();
            }
        }

        private void FlushLogToFile()
        {
            try
            {
                string logPath = @"C:\Windows\Temp\McAfeeCleanup_Final.log";
                File.AppendAllText(logPath, _sessionLog.ToString(), Encoding.UTF8);
                Log("  ✔ Log đã lưu tại: " + logPath);
            }
            catch { }
        }

        private static readonly string[] MCAFEE_PROCESSES = {
            "McSvHost", "mccs_sc", "mcshield", "mfemms", "mfefire",
            "McAPExe", "mcuicnt", "ModuleCoreService", "McCSP", "mfevtps"
        };

        private void KillMcAfeeProcesses()
        {
            // Phase 1: Suspend toàn bộ — đóng băng watchdog network trước khi kill
            // (nếu kill từng tiến trình một, tiến trình còn lại có thể hồi sinh tiến trình vừa kill)
            var allProcs = new List<Process>();
            foreach (var name in MCAFEE_PROCESSES)
            {
                try
                {
                    foreach (var p in Process.GetProcessesByName(name))
                    {
                        try { NtSuspendProcess(p.Handle); } catch { }
                        allProcs.Add(p);
                    }
                }
                catch { }
            }

            // Phase 2: Kill toàn bộ sau khi đã đóng băng
            foreach (var p in allProcs)
            {
                try
                {
                    string pname = p.ProcessName;
                    int    pid   = p.Id;
                    p.Kill();
                    p.WaitForExit(5000);
                    Log("  ✔ Đã kill: " + pname + ".exe (PID " + pid + ")");
                }
                catch { }
            }

            // Phase 3: Dừng svchost service đang host McAfee DLL (orphaned processes)
            // — PHẢI chạy TRƯỚC taskkill để Windows không auto-restart service khi thấy PID chết
            StopOrphanedSvcHostServices();

            // Chờ 1.5 giây để SCM (Service Control Manager) xử lý lệnh stop
            // trước khi taskkill /T đóng tiến trình svchost
            Thread.Sleep(1500);

            // Fallback: taskkill đảm bảo
            RunCmd("taskkill", "/F /IM McSvHost.exe /T");
            RunCmd("taskkill", "/F /IM mccs_sc.exe /T");
        }

        private static bool RunCmd(string exe, string args)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName       = exe,
                    Arguments      = args,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                });
                p.WaitForExit(30000);
                return p.ExitCode == 0;
            }
            catch { return false; }
        }

        private static void TakeOwnershipFolder(string path)
        {
            RunCmd("takeown", $"/F \"{path}\" /R /D Y");
            RunCmd("icacls",  $"\"{path}\" /grant Administrators:F /T /C /Q");
        }

        private static void TakeOwnershipRegKey(string fullPath)
        {
            string sub = fullPath.StartsWith("HKEY_LOCAL_MACHINE\\")
                ? fullPath.Substring(19) : null;
            if (sub == null) return;

            RunCmd("powershell",
                $"-ExecutionPolicy Bypass -NonInteractive -Command \"" +
                $"$k = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey('{sub}'," +
                $"[Microsoft.Win32.RegistryKeyPermissionCheck]::ReadWriteSubTree," +
                $"[System.Security.AccessControl.RegistryRights]::TakeOwnership);" +
                $"if($k){{$a=$k.GetAccessControl([System.Security.AccessControl.AccessControlSections]::None);" +
                $"$a.SetOwner([System.Security.Principal.NTAccount]'Administrators');$k.SetAccessControl($a);" +
                $"$r=New-Object System.Security.AccessControl.RegistryAccessRule('Administrators','FullControl','Allow');" +
                $"$a2=$k.GetAccessControl();$a2.SetAccessRule($r);$k.SetAccessControl($a2)}}\"");
        }

        private bool DeleteFolder(string path)
        {
            // Nếu là file driver .sys — thử unload qua fltmc trước khi xóa
            bool isSysFile = path.EndsWith(".sys", StringComparison.OrdinalIgnoreCase)
                          && File.Exists(path);
            if (isSysFile)
            {
                string driverName = Path.GetFileNameWithoutExtension(path);
                RunCmd("fltmc", "unload " + driverName);      // mini-filter
                RunCmd("sc", "stop \"" + driverName + "\"");   // legacy driver service
            }

            TakeOwnershipFolder(path);

            // Lớp 1: xóa trực tiếp
            try
            {
                if (isSysFile) File.Delete(path);
                else           Directory.Delete(path, true);
                return true;
            }
            catch (Exception ex)
            {
                int    w32  = Marshal.GetLastWin32Error();
                string hint = w32 == 5
                    ? " → Gợi ý: McAfee Self-Protection có thể đang chặn. Dùng phương án Safe Mode để xóa triệt để."
                    : "";
                Log("  ⚠ Delete lần 1 [" + w32 + " " + new System.ComponentModel.Win32Exception(w32).Message + "]" + hint + ": " + ex.Message);
            }

            // Lớp 2: Robocopy mirror (chỉ áp dụng cho thư mục)
            if (!isSysFile)
            {
                try
                {
                    string empty = Path.Combine(Path.GetTempPath(), "empty_" + Guid.NewGuid());
                    Directory.CreateDirectory(empty);
                    RunCmd("robocopy", "\"" + empty + "\" \"" + path + "\" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP");
                    Directory.Delete(path, true);
                    Directory.Delete(empty, true);
                    return true;
                }
                catch { }
            }

            // Lớp 3 (last resort): MoveFileEx — xếp hàng xóa vào lần boot kế tiếp
            // QUAN TRỌNG: file con phải được đánh dấu TRƯỚC thư mục cha
            // để Windows thực hiện đúng thứ tự xóa khi reboot
            if (!isSysFile && Directory.Exists(path))
            {
                // Đánh dấu file con từ sâu nhất lên trước
                try
                {
                    var childFiles = Directory.GetFiles(path, "*", System.IO.SearchOption.AllDirectories)
                                              .OrderByDescending(f => f.Length);
                    foreach (var f in childFiles)
                        if (!IsAlreadyPendingDelete(f) && MoveFileEx(f, null, MOVEFILE_DELAY_UNTIL_REBOOT))
                            _hasPendingDeletes = true;

                    var childDirs = Directory.GetDirectories(path, "*", System.IO.SearchOption.AllDirectories)
                                             .OrderByDescending(d => d.Length);
                    foreach (var d in childDirs)
                        if (!IsAlreadyPendingDelete(d) && MoveFileEx(d, null, MOVEFILE_DELAY_UNTIL_REBOOT))
                            _hasPendingDeletes = true;
                }
                catch { }
            }

            if (IsAlreadyPendingDelete(path))
            {
                Log("  ℹ Đã trong hàng chờ xóa khi reboot: " + path);
                _hasPendingDeletes = true;
                return true;
            }
            bool scheduled = MoveFileEx(path, null, MOVEFILE_DELAY_UNTIL_REBOOT);
            if (scheduled)
            {
                _hasPendingDeletes = true;
                Log("  ⏳ Sẽ xóa khi reboot: " + path);
            }
            else
            {
                int w32 = Marshal.GetLastWin32Error();
                Log("  ✘ MoveFileEx thất bại [" + w32 + " " + new System.ComponentModel.Win32Exception(w32).Message + "]: " + path);
            }
            return scheduled;
        }

        private static bool DeleteRegistryKey(string fullPath)
        {
            TakeOwnershipRegKey(fullPath);
            try
            {
                RegistryKey hive;
                string sub;
                if (fullPath.StartsWith("HKEY_LOCAL_MACHINE\\"))
                { hive = Registry.LocalMachine; sub = fullPath.Substring(19); }
                else if (fullPath.StartsWith("HKEY_CURRENT_USER\\"))
                { hive = Registry.CurrentUser;  sub = fullPath.Substring(18); }
                else return false;

                hive.DeleteSubKeyTree(sub, false);
                return true;
            }
            catch { return false; }
        }

        // Kiểm tra file đã có trong PendingFileRenameOperations chưa để tránh ghi trùng
        private static bool IsAlreadyPendingDelete(string path)
        {
            try
            {
                const string pendingKey = @"SYSTEM\CurrentControlSet\Control\Session Manager";
                using (var key = Registry.LocalMachine.OpenSubKey(pendingKey))
                {
                    var values = key?.GetValue("PendingFileRenameOperations") as string[];
                    if (values == null) return false;
                    // Windows lưu dạng \??\C:\path — kiểm tra cả hai dạng
                    string normalized = @"\??\" + path;
                    foreach (var v in values)
                        if (string.Equals(v, path, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(v, normalized, StringComparison.OrdinalIgnoreCase))
                            return true;
                }
            }
            catch { }
            return false;
        }

        private static bool RegistryKeyExists(string subKey)
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(subKey))
                    return k != null;
            }
            catch { return false; }
        }

        private static string SafeRegName(string regPath) =>
            regPath.Replace(@"\", "_").Replace(":", "");

        internal static System.Drawing.Icon LoadEmbeddedIcon(string name)
        {
            var asm    = System.Reflection.Assembly.GetExecutingAssembly();
            var stream = asm.GetManifestResourceStream("frm_mcafee_unin." + name);
            return stream != null ? new System.Drawing.Icon(stream) : null;
        }

        internal static System.Drawing.Image LoadEmbeddedImage(string name)
        {
            var asm    = System.Reflection.Assembly.GetExecutingAssembly();
            var stream = asm.GetManifestResourceStream("frm_mcafee_unin." + name);
            return stream != null ? System.Drawing.Image.FromStream(stream) : null;
        }
    }
}
