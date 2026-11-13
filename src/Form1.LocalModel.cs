using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Filetranslate;

// Built-in free AI: llama.cpp's llama-server plus a Qwen3.5 GGUF model, downloaded once into
// %LOCALAPPDATA%\Beeslator\local-ai and run on 127.0.0.1 while the app is open. No key and no account;
// every file is pinned to a release/revision and checked against its SHA-256 before use.
public partial class Form1
{
    private sealed record LocalDownload(string Url, string Sha256, long Size, string FileName);
    private sealed record LocalModelPack(string Alias, string Label, LocalDownload File, long MinRamBytes);

    private static readonly LocalDownload LlamaServerZip = new(
        "https://github.com/ggml-org/llama.cpp/releases/download/b12340/llama-b12340-bin-win-vulkan-x64.zip",
        "a2c7a2bfaaf57b5357f84cf584603d088143a5be08c68cb623e6d9bb7db664e8", 38_101_437, "llama-b12340-bin-win-vulkan-x64.zip");

    private static readonly LocalModelPack[] LocalModelPacks =
    [
        new("qwen3.5-4b", "Qwen3.5 4B (2,7 GB, dịch tốt hơn)", new(
            "https://huggingface.co/unsloth/Qwen3.5-4B-GGUF/resolve/e87f176479d0855a907a41277aca2f8ee7a09523/Qwen3.5-4B-Q4_K_M.gguf",
            "00fe7986ff5f6b463e62455821146049db6f9313603938a70800d1fb69ef11a4", 2_740_937_888, "Qwen3.5-4B-Q4_K_M.gguf"), 12L << 30),
        new("qwen3.5-2b", "Qwen3.5 2B (1,3 GB, máy yếu)", new(
            "https://huggingface.co/unsloth/Qwen3.5-2B-GGUF/resolve/f6d5376be1edb4d416d56da11e5397a961aca8ae/Qwen3.5-2B-Q4_K_M.gguf",
            "aaf42c8b7c3cab2bf3d69c355048d4a0ee9973d48f16c731c0520ee914699223", 1_280_835_840, "Qwen3.5-2B-Q4_K_M.gguf"), 0),
    ];

    private static readonly string LocalAiRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Beeslator", "local-ai");
    private static string LlamaServerDir => Path.Combine(LocalAiRoot, "llama-b12340");
    private static string LlamaServerExe => Path.Combine(LlamaServerDir, "llama-server.exe");
    private static string LlamaServerMarker => Path.Combine(LlamaServerDir, ".installed");

    private Process? _localServer;
    private Uri? _localServerUri;

    private static LocalModelPack PickLocalModelPack(long totalRamBytes) =>
        LocalModelPacks.First(p => totalRamBytes >= p.MinRamBytes);

    private static LocalModelPack? FindLocalModelPack(string? alias) => LocalModelPacks.FirstOrDefault(p => p.Alias == alias);

    private static string LocalModelPath(LocalModelPack pack) => Path.Combine(LocalAiRoot, "models", pack.File.FileName);

    private static bool IsLocalModelInstalled(LocalModelPack pack) =>
        File.Exists(LlamaServerMarker) && new FileInfo(LocalModelPath(pack)) is { Exists: true } f && f.Length == pack.File.Size;

    private async Task InstallLocalModelAsync(LocalModelPack pack, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.Combine(LocalAiRoot, "models"));
        if (!File.Exists(LlamaServerMarker))
        {
            var zip = Path.Combine(LocalAiRoot, LlamaServerZip.FileName);
            await DownloadVerifiedAsync(LlamaServerZip, zip, "llama.cpp", ct);
            // Extract in place and mark completion: renaming a folder of fresh .exe/.dll files fails while
            // antivirus is still scanning them, and the marker makes an interrupted extraction start over.
            ZipFile.ExtractToDirectory(zip, LlamaServerDir, true);
            if (!File.Exists(LlamaServerExe)) throw new IOException("llama-server.exe missing from the llama.cpp archive.");
            await File.WriteAllTextAsync(LlamaServerMarker, LlamaServerZip.Sha256, ct);
            File.Delete(zip);
        }
        await DownloadVerifiedAsync(pack.File, LocalModelPath(pack), pack.Alias, ct);
    }

    // Resumable download into <target>.part; the file only gets its final name after the SHA-256 matches.
    private async Task DownloadVerifiedAsync(LocalDownload item, string target, string label, CancellationToken ct)
    {
        if (File.Exists(target) && new FileInfo(target).Length == item.Size) return;
        var part = target + ".part";
        var have = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (have > item.Size) { File.Delete(part); have = 0; }
        if (have < item.Size)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, item.Url);
            if (have > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(have, null);
            using var download = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            using var response = await download.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (have > 0 && response.StatusCode != HttpStatusCode.PartialContent) have = 0; // server ignored the range
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true);
            var buffer = new byte[1 << 20];
            var lastReport = DateTime.MinValue;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                have += read;
                if (DateTime.UtcNow - lastReport > TimeSpan.FromSeconds(2))
                {
                    lastReport = DateTime.UtcNow;
                    SetStatus($"Tải {label}: {have * 100 / item.Size}% ({have / 1_000_000} / {item.Size / 1_000_000} MB)");
                }
            }
        }
        SetStatus($"Kiểm tra {label}...");
        string hash;
        await using (var stream = File.OpenRead(part)) hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
        if (hash != item.Sha256)
        {
            File.Delete(part);
            throw new IOException($"{label}: SHA-256 không khớp, file tải về đã bị xóa. Hãy thử lại.");
        }
        File.Move(part, target, true);
    }

    // Starts llama-server once per app session on a free loopback port and waits until the model is loaded.
    private async Task<Uri> EnsureLocalServerAsync(LocalModelPack pack, CancellationToken ct)
    {
        if (_localServer is { HasExited: false } && _localServerUri is not null) return _localServerUri;
        if (!IsLocalModelInstalled(pack)) throw new InvalidOperationException("AI trên máy chưa được cài.");
        int port;
        using (var probe = new TcpListener(IPAddress.Loopback, 0)) { probe.Start(); port = ((IPEndPoint)probe.LocalEndpoint).Port; }
        var info = new ProcessStartInfo(LlamaServerExe)
        {
            WorkingDirectory = LlamaServerDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var arg in new[] { "-m", LocalModelPath(pack), "-a", pack.Alias, "--host", "127.0.0.1", "--port", port.ToString(),
                     "-c", "16384", "-np", "2", "--reasoning", "off", "--no-webui" })
            info.ArgumentList.Add(arg);
        var process = Process.Start(info) ?? throw new IOException("Không chạy được llama-server.");
        var tail = new System.Collections.Concurrent.ConcurrentQueue<string>();
        void Keep(string? line) { if (line is null) return; tail.Enqueue(line); while (tail.Count > 20) tail.TryDequeue(out _); }
        process.OutputDataReceived += (_, e) => Keep(e.Data);
        process.ErrorDataReceived += (_, e) => Keep(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _localServer = process;
        var uri = new Uri($"http://127.0.0.1:{port}/v1");
        using var health = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(5);
        SetStatus("Đang nạp AI trên máy...");
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (process.HasExited) throw new IOException("llama-server dừng khi nạp model: " + string.Join(" | ", tail.TakeLast(3)));
            try
            {
                using var response = await health.GetAsync($"http://127.0.0.1:{port}/health", ct);
                if (response.IsSuccessStatusCode) break;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { }
            if (DateTime.UtcNow > deadline) { StopLocalServer(); throw new TimeoutException("llama-server không sẵn sàng sau 5 phút."); }
            await Task.Delay(500, ct);
        }
        _localServerUri = uri;
        return uri;
    }

    private void StopLocalServer()
    {
        try { if (_localServer is { HasExited: false }) _localServer.Kill(true); }
        catch (InvalidOperationException) { }
        _localServer?.Dispose();
        _localServer = null;
        _localServerUri = null;
    }

    // A built-in config stores no URL: the server's port is chosen when it starts.
    private async Task PrepareBuiltInAsync(CancellationToken ct)
    {
        if (_aiConfig?.Provider != TranslatorProvider.BuiltIn) return;
        var pack = FindLocalModelPack(_aiConfig.Model) ?? throw new InvalidOperationException("Model AI trên máy không hợp lệ.");
        if (!IsLocalModelInstalled(pack))
        {
            Log($"Đang tải AI miễn phí chạy trên máy: {pack.Label}. Chỉ tải một lần.");
            await InstallLocalModelAsync(pack, ct);
        }
        var uri = await EnsureLocalServerAsync(pack, ct);
        _aiConfig = _aiConfig with { BaseUrl = uri };
        Log($"AI trên máy sẵn sàng: {pack.Label}.");
    }
}
