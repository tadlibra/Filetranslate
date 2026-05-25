# Beeslater - Công cụ dịch modpack Minecraft

Beeslater là ứng dụng Windows giúp dịch modpack Minecraft sang tiếng Việt. App có thể ghi trực tiếp file `vi_vn.json` vào modpack, nên khi vào game chỉ cần đổi Language sang Tiếng Việt là bản dịch được áp dụng.

## Dành Cho Người Dùng

Người dùng bình thường không cần cài .NET SDK và không cần build source.

1. Tải `Beeslater.exe` từ trang Release.
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
- Dịch FTB Quests qua `config/ftbquests/quests/lang/vi_vn.json`.
- Tự đặt `lang:vi_vn` trong `options.txt`.
- Có nút dừng giữa quá trình.
- Có tùy chọn xóa bản dịch cũ và dịch lại từ đầu.

## Cấu Hình AI

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
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

File build nằm tại:

```text
bin\Release\net8.0-windows\win-x64\publish\Beeslater.exe
```
