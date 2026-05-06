# Minecraft Mod Translator

Tự động dịch ngôn ngữ cho toàn bộ mod trong modpack Minecraft.  
Hỗ trợ Google Translate (miễn phí) và Claude AI (chất lượng cao).

## Tính năng

- Dịch hàng loạt file `.jar` trong thư mục `mods/`
- Hỗ trợ 6 ngôn ngữ: Tiếng Việt, Trung, Nhật, Hàn, Thái, Indonesia
- Output ra Resource Pack dùng được ngay trong Minecraft

## Cách dùng

### 1. Tải về
Bấm **Code → Download ZIP**, giải nén ra thư mục bất kỳ.

### 2. Build file .exe
Chạy file `build_exe.bat` — tự động cài thư viện và build ra `dist\MC_Mod_Translator.exe`.  
Yêu cầu: **Python 3.10+** ([tải tại python.org](https://www.python.org/downloads/))

### 3. Chạy app
Mở `dist\MC_Mod_Translator.exe`, chọn thư mục instance, chọn ngôn ngữ, nhấn **Bắt đầu dịch**.

### 4. Cài Resource Pack
1. Mở Minecraft → **Options → Resource Packs**
2. Bật `VietNamese_Complete` lên trên cùng
3. Đổi ngôn ngữ trong **Settings**

## Claude AI (tùy chọn)

Để dùng Claude AI thay Google Translate, set biến môi trường:
```
ANTHROPIC_API_KEY=sk-ant-...
```
App sẽ tự load key, không cần nhập tay.

## Yêu cầu hệ thống

- Windows 10/11
- Python 3.10+ (chỉ cần khi build)
