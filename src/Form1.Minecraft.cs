using System.Text;
using System.Text.Json;

namespace Filetranslate;

public partial class Form1
{
    private async Task WritePackMetaAsync(string instancePath, string outputPack, CancellationToken ct)
    {
        var version = TryReadMinecraftVersion(instancePath);
        var packFormat = DetectPackFormat(version);
        var mcmeta = new
        {
            pack = new
            {
                pack_format = packFormat,
                description = $"Auto translated by Beeslator{(version is null ? "" : $" for Minecraft {version}")}"
            }
        };
        await File.WriteAllTextAsync(Path.Combine(outputPack, "pack.mcmeta"), JsonSerializer.Serialize(mcmeta, JsonOptions(true)), Encoding.UTF8, ct);
        Log(version is null
            ? $"Không nhận diện được version Minecraft, dùng pack_format {packFormat}."
            : $"Nhận diện Minecraft {version}, dùng pack_format {packFormat}.");
    }

    private static string? TryReadMinecraftVersion(string instancePath)
    {
        foreach (var file in new[]
        {
            Path.Combine(instancePath, "minecraftinstance.json"),
            Path.Combine(instancePath, "manifest.json"),
            Path.Combine(instancePath, "mmc-pack.json")
        })
        {
            var version = TryReadMinecraftVersionFromJson(file);
            if (!string.IsNullOrWhiteSpace(version)) return version;
        }

        return null;
    }

    private static string? TryReadMinecraftVersionFromJson(string file)
    {
        if (!File.Exists(file)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;

            if (root.TryGetProperty("minecraft", out var minecraft)
                && minecraft.TryGetProperty("version", out var mcVersion))
            {
                return mcVersion.GetString();
            }

            if (root.TryGetProperty("baseModLoader", out var loader)
                && loader.TryGetProperty("minecraftVersion", out var loaderVersion))
            {
                return loaderVersion.GetString();
            }

            if (root.TryGetProperty("components", out var components) && components.ValueKind == JsonValueKind.Array)
            {
                foreach (var component in components.EnumerateArray())
                {
                    if (component.TryGetProperty("uid", out var uid)
                        && uid.GetString() == "net.minecraft"
                        && component.TryGetProperty("version", out var componentVersion))
                    {
                        return componentVersion.GetString();
                    }
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static int DetectPackFormat(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return 15;
        var parts = version.Split('.', '-', '_');
        if (parts.Length < 2 || !int.TryParse(parts[1], out var minor)) return 15;
        var patch = parts.Length > 2 && int.TryParse(parts[2], out var parsedPatch) ? parsedPatch : 0;

        return minor switch
        {
            <= 8 => 1,
            9 or 10 => 2,
            11 or 12 => 3,
            13 or 14 => 4,
            15 => 5,
            16 => patch >= 2 ? 6 : 5,
            17 => 7,
            18 => 8,
            19 => patch >= 4 ? 13 : 9,
            20 => patch switch
            {
                >= 5 => 32,
                >= 3 => 22,
                >= 2 => 18,
                _ => 15
            },
            21 => 34,
            _ => 34
        };
    }

    private void EnableResourcePack(string instancePath)
    {
        var optionsFile = Path.Combine(instancePath, "options.txt");
        if (!File.Exists(optionsFile)) return;

        var lines = File.ReadAllLines(optionsFile).ToList();
        var changed = false;
        for (var i = 0; i < lines.Count; i++)
        {
            if (!lines[i].StartsWith("resourcePacks:", StringComparison.Ordinal)) continue;
            var current = lines[i]["resourcePacks:".Length..].Trim();
            if (current.Contains($"\"{ResourcePackName}\"", StringComparison.Ordinal)) continue;

            lines[i] = current switch
            {
                "" or "[]" => $"resourcePacks:[\"{ResourcePackName}\"]",
                _ when current.EndsWith(']') => $"resourcePacks:{current[..^1].TrimEnd()},{Quote(ResourcePackName)}]",
                _ => $"resourcePacks:[{Quote(ResourcePackName)}]"
            };
            changed = true;
        }
        if (!lines.Any(l => l.StartsWith("resourcePacks:", StringComparison.Ordinal)))
        {
            lines.Add($"resourcePacks:[\"{ResourcePackName}\"]");
            changed = true;
        }
        if (changed)
        {
            File.WriteAllLines(optionsFile, lines, Encoding.UTF8);
            Log("Đã bật resource pack FileTranslate trong options.txt.");
        }
    }

    private async Task SetMinecraftLanguageAsync(string instancePath, string langFile, CancellationToken ct)
    {
        var optionsFile = Path.Combine(instancePath, "options.txt");
        if (!File.Exists(optionsFile))
        {
            await WriteAtomicAsync(optionsFile, $"lang:{langFile}\n", true, ct);
            Log($"Đã tạo options.txt và đặt ngôn ngữ {langFile}.");
            return;
        }

        var lines = File.ReadAllLines(optionsFile).ToList();
        var changed = false;
        for (var i = 0; i < lines.Count; i++)
        {
            if (!lines[i].StartsWith("lang:", StringComparison.Ordinal)) continue;
            if (lines[i].Equals($"lang:{langFile}", StringComparison.Ordinal)) return;
            lines[i] = $"lang:{langFile}";
            changed = true;
            break;
        }

        if (!lines.Any(l => l.StartsWith("lang:", StringComparison.Ordinal)))
        {
            lines.Add($"lang:{langFile}");
            changed = true;
        }

        if (changed)
        {
            await WriteAtomicAsync(optionsFile, string.Join("\n", lines) + "\n", true, ct);
            Log($"Đã đặt ngôn ngữ Minecraft sang {langFile}.");
        }
    }

    private static string Quote(string value) => $"\"{value}\"";
}
