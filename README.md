# 🐝 Beeslater - Công cụ dịch ngôn ngữ cho Minecraft Modpack

Tự động dịch toàn bộ mod trong modpack sang tiếng Việt chỉ với vài click.

---

## ✨ Tính năng

- ⚡ Dịch nhanh với đa luồng (nhiều mod song song)
- 🔧 Bảo toàn placeholder (%s, %d, {0}...) không bị dịch sai
- 📋 Dịch FTB Quests (.snbt) — title, subtitle, description, text
- ↷ Tự bỏ qua mod đã dịch rồi (tiếp tục từ chỗ dở)
- 🗑 Tùy chọn xóa sạch và dịch lại từ đầu
- 🌐 Hỗ trợ ngôn ngữ: Việt

---

## 📥 Cách dùng

1. Tải file `Beeslater.exe` bên dưới
2. Double-click chạy thẳng, không cần cài đặt
3. Chọn thư mục modpack → nhấn **Bắt đầu dịch**
4. Vào Minecraft → Options → Resource Packs → bật **FileTranslate** lên trên cùng
5. Đổi ngôn ngữ trong Settings

> ⚠️ Nếu Windows hiện SmartScreen: nhấn **More info → Run anyway**

---

## 📋 FTB Quests

Bản dịch quest được lưu tại thư mục `ftbquests_translated/` trong instance (không ghi đè bản gốc).

Để áp dụng:
1. Mở thư mục `ftbquests_translated/`
2. Copy toàn bộ nội dung vào `config/ftbquests/quests/`
3. Ghi đè file gốc khi được hỏi

---

## 🔄 Changelog

### v1.2
- 📋 Thêm tính năng dịch FTB Quests (.snbt)
- Tự động dịch title, subtitle, description, text trong quest
- Bản dịch lưu tại `ftbquests_translated/` (không ghi đè bản gốc)

### v1.1
- Fix lỗi placeholder bị dịch sai (%s thành %5...)
- Thêm tính năng dịch sách hướng dẫn Patchouli
- Thêm nút Dừng lại
- Thêm checkbox Xóa sạch dịch lại từ đầu
