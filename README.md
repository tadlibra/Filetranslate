# 🐝 Beeslater - Công cụ dịch ngôn ngữ cho Minecraft Modpack

Tự động dịch toàn bộ mod trong modpack sang tiếng Việt chỉ với vài click.

---

## ✨ Tính năng

- ⚡ Dịch nhanh với đa luồng (nhiều mod song song)
- 🔧 Bảo toàn placeholder (%s, %d, {0}...) không bị dịch sai
- 📖 Dịch sách hướng dẫn Patchouli
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

> ⚠️ Bản dịch FTB Quests **không dùng Resource Pack** như mod thường — phải copy file thủ công vào thư mục config.

**Cách áp dụng bản dịch quest:**

1. Sau khi dịch xong, mở thư mục instance của bạn
2. Vào thư mục `ftbquests_translated/` (do Beeslater tạo ra)
3. **Copy toàn bộ** nội dung bên trong
4. Dán vào `config/ftbquests/quests/` — chọn **Replace** khi được hỏi
5. Khởi động lại Minecraft

> 💡 Thư mục instance thường nằm tại:
> - CurseForge: `C:\Users\<tên>\curseforge\minecraft\Instances\<tên modpack>`
> - Prism/MultiMC: `C:\Users\<tên>\AppData\Roaming\PrismLauncher\instances\<tên modpack>`

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