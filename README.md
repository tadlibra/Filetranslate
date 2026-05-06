# Minecraft Mod Translator

Tự động dịch ngôn ngữ cho toàn bộ mod trong modpack Minecraft.  
Hỗ trợ Google Translate (miễn phí) và Claude AI (chất lượng cao).

## Tính năng

- Dịch hàng loạt file `.jar` trong thư mục `mods/`
- Hỗ trợ 6 ngôn ngữ: Tiếng Việt, Trung, Nhật, Hàn, Thái, Indonesia
- Output ra Resource Pack dùng được ngay trong Minecraft

## Tải về & Chạy ngay (khuyến nghị)

1. Vào tab **[Releases](../../releases)** → tải `MC_Mod_Translator.exe`
2. Double-click để chạy — **không cần cài Python hay bất kỳ thứ gì thêm**

> **Cảnh báo SmartScreen của Windows:** Lần đầu chạy, Windows có thể hiện thông báo  
> _"Windows protected your PC"_. Đây là bình thường với mọi file `.exe` chưa có chứng chỉ.  
> Bấm **"More info" → "Run anyway"** để tiếp tục.

## Cách dùng

1. Mở app, chọn thư mục instance CurseForge (phải chứa thư mục `mods/`)
2. Chọn ngôn ngữ đích và dịch vụ dịch thuật
3. Nhấn **Bắt đầu dịch**
4. Khi xong: Minecraft → **Options → Resource Packs** → bật `VietNamese_Complete` lên trên cùng → đổi ngôn ngữ trong Settings

## Claude AI (tùy chọn)

Để dùng Claude AI thay Google Translate, set biến môi trường:
```
ANTHROPIC_API_KEY=sk-ant-...
```
App sẽ tự load key, không cần nhập tay.

## Tự build từ source

Yêu cầu **Python 3.10+** — chạy `build_exe.bat` để build ra `dist\MC_Mod_Translator.exe`.

## Yêu cầu hệ thống

- Windows 10/11
