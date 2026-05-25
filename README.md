# Beeslater - Minecraft Mod Translator

Ung dung desktop Windows de dich file lang trong modpack Minecraft sang tieng Viet.

## Cong nghe

- C# / .NET 8 WinForms
- Khong con phu thuoc Python

## Tinh nang

- Dich da luong cho nhieu mod `.jar`
- Ho tro AI provider rieng cua tung nguoi dung:
  - Gemini API
  - OpenAI-compatible endpoint
  - Anthropic/Claudible-compatible endpoint
  - Ollama local/offline
  - Google Translate fallback
- Ho tro nguon `en_us`, `zh_cn`, `zh_tw`, `ja_jp`, `ko_kr`
- Co che ghi truc tiep `vi_vn.json` vao modpack de doi Language trong game la ap dung, khong bat buoc resource pack
- Backup jar truoc khi inject: `*.jar.beeslator.bak`
- Dich FTB Quests sang:
  - `config/ftbquests/quests/lang/vi_vn.json`
  - `resourcepacks/FileTranslate/assets/ftbquests/lang/vi_vn.json`
- Tu dong them `"FileTranslate"` vao `options.txt`
- Nut dung giua qua trinh
- Tuy chon xoa sach ban dich cu va dich lai

## Cau hinh AI

Trong app bam `Cau hinh AI`, chon provider va nhap key/model cua rieng ban.
Config duoc luu local tai:

`%APPDATA%\Beeslator\translator.config.json`

Khong commit API key vao repo.

Provider goi y:

- `Gemini`: de dung cho nguoi dung pho thong vi co free tier trong Google AI Studio tuy quota hien tai.
- `OpenAiCompatible`: dung cho OpenAI, OpenRouter, LM Studio hoac endpoint tuong thich `/v1/chat/completions`.
- `AnthropicCompatible`: dung cho Claude/Claudible endpoint tuong thich `/v1/messages`.
- `Ollama`: mien phi/offline neu nguoi dung cai Ollama va co model local, mac dinh `http://localhost:11434`.
- `Google`: fallback khong can key, chat luong thap hon AI.

## Build

Yeu cau:

- .NET SDK 8+

Lenh:

```bat
build.bat
```

Hoac truc tiep:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

File EXE:

`bin\Release\net8.0-windows\win-x64\publish\Beeslater.exe`
