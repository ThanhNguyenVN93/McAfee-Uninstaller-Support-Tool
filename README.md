# McAfee Uninstall Tool

Công cụ gỡ cài đặt sạch McAfee trên Windows 7 / 10 / 11.

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![.NET](https://img.shields.io/badge/.NET-4.5.2-purple)
![License](https://img.shields.io/badge/license-MIT-green)

## Tính năng

- **Bước 1 — Sao lưu:** Tự động tạo System Restore Point và xuất file `.reg` backup ra Desktop trước khi gỡ cài đặt
- **Bước 2 — Gỡ cài đặt:** Chạy `mc-update.exe /uninstall`; fallback Safe Mode nếu thất bại; đếm ngược restart tự động
- **Bước 3 — Quét dọn:** Deep scan toàn bộ thư mục, registry, services, driver `.sys`, scheduled tasks, WMI SecurityCenter, COM GUIDs; xóa với 3 lớp bảo vệ (direct → robocopy → MoveFileEx reboot)
- **Undo script:** Tự động tạo `McAfee_Undo_Restore.bat` trên Desktop để phục hồi Registry nếu cần

## Yêu cầu

- Windows 7 / 10 / 11
- .NET Framework 4.5.2 trở lên
- Chạy với quyền **Administrator**

## Build

```
MSBuild frm_mcafee_unin.csproj /p:Configuration=Release /t:Rebuild
```

Output: `bin\Release\McAfee Uninstaller Support Tool.exe`  
File `.exe` **self-contained** — tất cả ảnh và icon đã được embed bên trong.

## Cấu trúc project

```
Form1.cs              — Logic chính (wizard 3 bước)
Form1.Designer.cs     — UI layout, step indicator
FormDonate.cs         — Form ủng hộ tác giả (QR TCB + MoMo)
Program.cs            — Entry point
frm_mcafee_unin.csproj
app.manifest          — requireAdministrator
convertico-mcafee.ico — Icon (embedded)
donate.jpg            — Ảnh nút donate (embedded)
tcb.jpg               — QR Techcombank (embedded)
momo.jpg              — QR MoMo (embedded)
```

## Lưu ý

Công cụ này chỉ dành cho mục đích gỡ cài đặt McAfee hợp lệ trên máy tính cá nhân. Luôn chạy Bước 1 (sao lưu) trước khi thực hiện bất kỳ thao tác xóa nào.
