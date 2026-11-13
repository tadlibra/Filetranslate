# Beeslater - Công cụ dịch modpack Minecraft

Beeslater là ứng dụng Windows giúp dịch modpack Minecraft sang tiếng Việt. App có thể ghi trực tiếp file `vi_vn.json` vào modpack, nên khi vào game chỉ cần đổi Language sang Tiếng Việt là bản dịch được áp dụng.

## Dành Cho Người Dùng

Người dùng bình thường không cần cài .NET SDK và không cần build source.

1. Tải `Beeslater.zip` từ trang Release (máy Windows ARM như Snapdragon: tải `Beeslater-arm64.zip`). Chạy trên Windows 10/11 64-bit, không cần cài .NET.
2. Mở app.
3. Chọn thư mục instance modpack.
4. Cấu hình AI nếu muốn dùng Gemini, Claude/Claudible, OpenAI-compatible hoặc Ollama.
5. Bấm `BẮT ĐẦU DỊCH`.
6. Vào Minecraft, chọn ngôn ngữ `Tiếng Việt`.

Nếu Windows SmartScreen cảnh báo, chọn `More info` rồi `Run anyway`.

## Tính Năng

- Dịch nhiều mod `.jar` trong modpack.
- Dùng AI theo cấu hình riêng của từng người:
  - Gemini API
  - OpenAI-compatible endpoint
  - Anthropic/Claudible-compatible endpoint
  - Ollama local/offline
  - Google Translate fallback
- Hỗ trợ nguồn `en_us`, `zh_cn`, `zh_tw`, `ja_jp`, `ko_kr`.
- Ghi trực tiếp `vi_vn.json` vào modpack, không bắt buộc bật resource pack.
- Backup jar trước khi inject: `*.jar.beeslator.bak`.
- Dịch Patchouli books.
- Dịch sổ tay Immersive Engineering, sách Citadel (Alex's Mobs...) và trang GuideME (AE2, ExtendedAE); giữ nguyên bản dịch chính thức nếu mod đã có.
- Chuỗi dịch lỗi được giữ tiếng Anh, bấm dịch lại để dịch tiếp phần còn thiếu.
- Dịch FTB Quests qua `config/ftbquests/quests/lang/vi_vn.json`.
- Tự đặt `lang:vi_vn` trong `options.txt`.
- Có nút dừng giữa quá trình.
- Có tùy chọn xóa bản dịch cũ và dịch lại từ đầu.

## Cấu Hình AI

**AI miễn phí có sẵn (không cần key):** lần đầu bấm `BẮT ĐẦU DỊCH` mà chưa cấu hình AI, app hỏi có cài AI chạy trên máy không. Chọn Yes: app tải [llama.cpp](https://github.com/ggml-org/llama.cpp) (38 MB) và model Qwen3.5 4B (2,7 GB; máy dưới 12 GB RAM dùng bản 2B 1,3 GB) vào `%LOCALAPPDATA%\Beeslator\local-ai`, kiểm tra SHA-256, rồi dịch offline, không giới hạn. Tốc độ tùy GPU: card rời nhanh, GPU tích hợp (Intel Iris Xe) chỉ khoảng 10 token/giây nên modpack lớn có thể mất nhiều giờ; dịch dở thì bấm lại để dịch tiếp.

Yêu cầu của AI trên máy: Windows 64-bit (x64 hoặc ARM64), RAM từ 6 GB, ổ đĩa trống 1,5-3 GB. Máy thiếu Microsoft Visual C++ Runtime thì app tự tải bản chính thức (kiểm tra chữ ký Microsoft) và cài, Windows sẽ hỏi quyền. GPU không chạy được (driver cũ, không có Vulkan) thì tự chuyển sang CPU. Hugging Face bị chặn thì tải qua mirror, file vẫn được kiểm tra SHA-256. Đo trên laptop Intel Iris Xe 16 GB: bản 4B khoảng 3 giây/chuỗi, dịch đúng phần lớn nhưng vẫn có lỗi nghĩa với từ hiếm; card rời nhanh hơn nhiều.

Trong `CẤU HÌNH AI` có mục `Mẫu miễn phí` để chọn nhanh: Built-in, Ollama, Qwen (DashScope), Groq, OpenRouter `:free`, Gemini, Pollinations. Các dịch vụ online cần bạn tự tạo key miễn phí; app không gắn sẵn key dùng chung.

Trong app bấm `CẤU HÌNH AI`, chọn provider rồi nhập API key/model của bạn.

Config được lưu local trên máy người dùng:

```text
%APPDATA%\Beeslator\translator.config.json
```

Không commit API key vào repo.

Gợi ý provider:

- `Gemini`: phù hợp cho người dùng phổ thông vì Google AI Studio có free tier tùy quota hiện tại.
- `OpenAiCompatible`: dùng cho OpenAI, OpenRouter, LM Studio hoặc endpoint tương thích `/v1/chat/completions`.
- `AnthropicCompatible`: dùng cho Claude/Claudible endpoint tương thích `/v1/messages`.
- `Ollama`: miễn phí/offline nếu người dùng cài Ollama và có model local, mặc định `http://localhost:11434`.
- `Google`: fallback không cần key, chất lượng thấp hơn AI.

## Dành Cho Developer

Chỉ developer muốn sửa source hoặc tự build mới cần cài:

- .NET SDK 8+

Build:

```bat
build.bat
```

Hoặc:

```powershell
dotnet publish src/Filetranslate.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

File build nằm tại:

```text
src\bin\Release\net8.0-windows\win-x64\publish\Beeslater.exe
```

Chạy regression test (CI cũng chạy lệnh này mỗi lần push):

```powershell
dotnet run --project tests/Beeslator.Regression
```

Push tag `v*` (ví dụ `v2.1`) thì GitHub Actions tự build và tạo Release kèm `Beeslater.zip`.
