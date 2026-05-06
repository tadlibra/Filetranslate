# 🐝 Beeslater - Công cụ dịch ngôn ngữ cho Minecraft Modpack

Tự động dịch toàn bộ mod trong modpack sang tiếng Việt chỉ với vài click.

---

## ✨ Tính năng

- ⚡ Dịch nhanh với đa luồng (nhiều mod song song)
- 🔧 Bảo toàn placeholder (%s, %d, {0}...) không bị dịch sai
- 📖 Dịch sách hướng dẫn Patchouli
- 📋 Dịch FTB Quests — dùng hệ thống lang file native, tự đổi theo ngôn ngữ Minecraft
- ↷ Tự bỏ qua mod đã dịch rồi (tiếp tục từ chỗ dở)
- 🗑 Tùy chọn xóa sạch và dịch lại từ đầu
- 🌐 Hỗ trợ ngôn ngữ: Việt

---

## 📥 Cách dùng

1. Tải file `Beeslater.exe` bên dưới
2. Double-click chạy thẳng, không cần cài đặt
3. Chọn thư mục instance → nhấn **Bắt đầu dịch**
4. Khởi động Minecraft → đổi ngôn ngữ sang **Tiếng Việt**
5. Mod và quest tự hiển thị bản dịch — không cần làm gì thêm

> ⚠️ Nếu Windows hiện SmartScreen: nhấn **More info → Run anyway**

> 💡 Thư mục instance thường nằm tại:
> - CurseForge: `C:\Users\<tên>\curseforge\minecraft\Instances\<tên modpack>`
> - Prism/MultiMC: `C:\Users\<tên>\AppData\Roaming\PrismLauncher\instances\<tên modpack>`

---

## 🔄 Changelog

### v1.3
- 🔄 FTB Quests dùng hệ thống lang file native (`config/ftbquests/quests/lang/vi_vn.json`) — không ghi đè SNBT gốc
- ✅ Resource Pack tự động được bật trong `options.txt` — không cần bật tay trong game
- Đổi ngôn ngữ Minecraft → mod + quest đều tự đổi theo

### v1.2
- 📋 Thêm tính năng dịch FTB Quests (.snbt)
- Tự động dịch title, subtitle, description, text trong quest

### v1.1
- Fix lỗi placeholder bị dịch sai (%s thành %5...)
- Thêm tính năng dịch sách hướng dẫn Patchouli
- Thêm nút Dừng lại
- Thêm checkbox Xóa sạch dịch lại từ đầu