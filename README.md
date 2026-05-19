# McAfee Uninstall Tool

> Công cụ gỡ cài đặt sạch McAfee trên Windows 7 / 10 / 11.  
> A clean McAfee removal tool for Windows 7 / 10 / 11.

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![.NET](https://img.shields.io/badge/.NET-4.5.2-purple)
![License](https://img.shields.io/badge/license-MIT-green)

---

## Tính năng / Features

| | Tiếng Việt | English |
|---|---|---|
| **Bước 1 / Step 1** | Tự động tạo System Restore Point và xuất file `.reg` backup ra Desktop | Automatically creates a System Restore Point and exports `.reg` backup files to the Desktop |
| **Bước 2 / Step 2** | Chạy `mc-update.exe /uninstall`; fallback Safe Mode nếu thất bại; đếm ngược restart tự động | Runs `mc-update.exe /uninstall`; falls back to Safe Mode cleanup on failure; auto-restarts with countdown |
| **Bước 3 / Step 3** | Deep scan thư mục, registry, services, driver `.sys`, scheduled tasks, WMI, COM GUIDs — xóa với 3 lớp (direct → robocopy → MoveFileEx reboot) | Deep scans folders, registry, services, `.sys` drivers, scheduled tasks, WMI, COM GUIDs — 3-layer deletion (direct → robocopy → MoveFileEx on reboot) |
| **Undo script** | Tự động tạo `McAfee_Undo_Restore.bat` trên Desktop để phục hồi Registry | Auto-generates `McAfee_Undo_Restore.bat` on the Desktop to restore the Registry if needed |

---

## Yêu cầu / Requirements

| Tiếng Việt | English |
|---|---|
| Windows 7 / 10 / 11 | Windows 7 / 10 / 11 |
| .NET Framework 4.5.2 trở lên | .NET Framework 4.5.2 or later |
| Chạy với quyền **Administrator** | Must run as **Administrator** |

---

## Build

```
MSBuild frm_mcafee_unin.csproj /p:Configuration=Release /t:Rebuild
```

**Output:** `bin\Release\McAfee Uninstaller Support Tool.exe`

> **VI:** File `.exe` hoàn toàn độc lập — tất cả ảnh và icon đã được embed bên trong, không cần copy file kèm theo.  
> **EN:** The `.exe` is fully self-contained — all images and icons are embedded; no extra files needed.

---

## Cấu trúc project / Project Structure

```
Form1.cs              — Logic chính / Main logic (3-step wizard)
Form1.Designer.cs     — UI layout, step indicator
FormDonate.cs         — Form ủng hộ tác giả / Donate form (QR TCB + MoMo)
Program.cs            — Entry point
frm_mcafee_unin.csproj
app.manifest          — requireAdministrator
convertico-mcafee.ico — Icon (embedded)
donate.jpg            — Ảnh nút donate / Donate button image (embedded)
tcb.jpg               — QR Techcombank (embedded)
momo.jpg              — QR MoMo (embedded)
```

---

## Lưu ý / Disclaimer

> **VI:** Công cụ này chỉ dành cho mục đích gỡ cài đặt McAfee hợp lệ trên máy tính cá nhân. Luôn chạy Bước 1 (sao lưu) trước khi thực hiện bất kỳ thao tác xóa nào.  
> **EN:** This tool is intended solely for legitimate McAfee removal on personal computers. Always run Step 1 (backup) before performing any deletion.
