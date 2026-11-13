using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Filetranslate;

// Built-in free AI: llama.cpp's llama-server plus a Qwen3.5 GGUF model, downloaded once into
// %LOCALAPPDATA%\Beeslator\local-ai and run on 127.0.0.1 while the app is open. No key and no account;
// every file is pinned to a release/revision and checked against its SHA-256 before use, so a mirror
// can serve the bytes without being trusted.
public partial class Form1
{
    private sealed record LocalDownload(string[] Urls, string Sha256, long Size, string FileName);
    private sealed record LocalModelPack(string Alias, string Label, LocalDownload File, long MinRamBytes);

    private const long MinBuiltInRamBytes = 6L << 30;

    private static readonly LocalDownload LlamaServerZipX64 = new(
        ["https://github.com/ggml-org/llama.cpp/releases/download/b12340/llama-b12340-bin-win-vulkan-x64.zip"],
        "a2c7a2bfaaf57b5357f84cf584603d088143a5be08c68cb623e6d9bb7db664e8", 38_101_437, "llama-b12340-bin-win-vulkan-x64.zip");

    private static readonly LocalDownload LlamaServerZipArm64 = new(
        ["https://github.com/ggml-org/llama.cpp/releases/download/b12340/llama-b12340-bin-win-vulkan-arm64.zip"],
        "6020834a4fd9de1608349c9105d4a6d25efc7a42a7e634e69331f9d884a78414", 26_038_125, "llama-b12340-bin-win-vulkan-arm64.zip");

    private static string[] HuggingFaceUrls(string path) =>
        ["https://huggingface.co/" + path, "https://hf-mirror.com/" + path];

    private static readonly LocalModelPack[] LocalModelPacks =
    [
        new("qwen3.5-4b", "Qwen3.5 4B (2,7 GB, dịch tốt hơn)", new(
            HuggingFaceUrls("unsloth/Qwen3.5-4B-GGUF/resolve/e87f176479d0855a907a41277aca2f8ee7a09523/Qwen3.5-4B-Q4_K_M.gguf"),
            "00fe7986ff5f6b463e62455821146049db6f9313603938a70800d1fb69ef11a4", 2_740_937_888, "Qwen3.5-4B-Q4_K_M.gguf"), 12L << 30),
        new("qwen3.5-2b", "Qwen3.5 2B (1,3 GB, máy yếu)", new(
            HuggingFaceUrls("unsloth/Qwen3.5-2B-GGUF/resolve/f6d5376be1edb4d416d56da11e5397a961aca8ae/Qwen3.5-2B-Q4_K_M.gguf"),
            "aaf42c8b7c3cab2bf3d69c355048d4a0ee9973d48f16c731c0520ee914699223", 1_280_835_840, "Qwen3.5-2B-Q4_K_M.gguf"), 0),
    ];

    private static readonly string LocalAiRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Beeslator", "local-ai");

    // The OS architecture, not the process: an x64 build of this app runs emulated on ARM64 Windows,
    // but llama-server should run natively there.
    private static bool IsArm64Os => RuntimeInformation.OSArchitecture == Architecture.Arm64;
    private static LocalDownload LlamaServerZip => IsArm64Os ? LlamaServerZipArm64 : LlamaServerZipX64;
    private static string LlamaServerDir => Path.Combine(LocalAiRoot, IsArm64Os ? "llama-b12340-arm64" : "llama-b12340");
    private static string LlamaServerExe => Path.Combine(LlamaServerDir, "llama-server.exe");
    private static string LlamaServerMarker => Path.Combine(LlamaServerDir, ".installed");

    private Process? _localServer;
    private Uri? _localServerUri;

    private static long TotalRamBytes => (long)GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

    private static LocalModelPack PickLocalModelPack(long totalRamBytes) =>
        LocalModelPacks.First(p => totalRamBytes >= p.MinRamBytes);

    private static LocalModelPack? FindLocalModelPack(string? alias) => LocalModelPacks.FirstOrDefault(p => p.Alias == alias);

    private static string LocalModelPath(LocalModelPack pack) => Path.Combine(LocalAiRoot, "models", pack.File.FileName);

    private static bool IsLocalModelInstalled(LocalModelPack pack) =>
        File.Exists(LlamaServerMarker) && new FileInfo(LocalModelPath(pack)) is { Exists: true } f && f.Length == pack.File.Size;

    // Null when this PC can run the built-in AI, otherwise a reason the user can act on.
    private static string? LocalAiBlocker(LocalModelPack pack, long totalRam, long freeDisk, Architecture os)
    {
        if (os is not (Architecture.X64 or Architecture.Arm64))
            return "AI trên máy cần Windows 64-bit (x64 hoặc ARM64).";
        if (totalRam < MinBuiltInRamBytes)
            return $"Máy có {totalRam / (1L << 30)} GB RAM, AI trên máy cần ít nhất {MinBuiltInRamBytes >> 30} GB. Hãy dùng một dịch vụ có key miễn phí (Groq, Gemini) trong CẤU HÌNH AI.";
        var needed = (IsLocalModelInstalled(pack) ? 0 : pack.File.Size) + LlamaServerZipX64.Size * 3 + (512L << 20);
        if (freeDisk < needed)
            return $"Ổ đĩa chứa {LocalAiRoot} còn {freeDisk / 1_000_000_000.0:0.0} GB trống, cần khoảng {needed / 1_000_000_000.0:0.0} GB.";
        return null;
    }

    private static long FreeDiskBytes()
    {
        try { return new DriveInfo(Path.GetPathRoot(LocalAiRoot)!).AvailableFreeSpace; }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { return long.MaxValue; }
    }

    private async Task InstallLocalModelAsync(LocalModelPack pack, CancellationToken ct)
    {
        if (LocalAiBlocker(pack, TotalRamBytes, FreeDiskBytes(), RuntimeInformation.OSArchitecture) is { } blocker)
            throw new InvalidOperationException(blocker);
        Directory.CreateDirectory(Path.Combine(LocalAiRoot, "models"));
        await EnsureVcRuntimeAsync(ct);
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
    // Each URL is tried in turn, so a blocked host falls through to the next mirror.
    private async Task DownloadVerifiedAsync(LocalDownload item, string target, string label, CancellationToken ct)
    {
        if (File.Exists(target) && new FileInfo(target).Length == item.Size) return;
        Exception? last = null;
        foreach (var url in item.Urls)
        {
            try
            {
                await DownloadFromAsync(url, item, target, label, ct);
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                last = ex;
                Log($"Tải {label} từ {new Uri(url).Host} lỗi: {ex.Message}");
            }
        }
        throw new IOException($"Không tải được {label}. Kiểm tra mạng (GitHub/Hugging Face có bị chặn không) rồi thử lại.", last);
    }

    private async Task DownloadFromAsync(string url, LocalDownload item, string target, string label, CancellationToken ct)
    {
        var part = target + ".part";
        var have = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (have > item.Size) { File.Delete(part); have = 0; }
        if (have < item.Size)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
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
            throw new IOException($"{label}: SHA-256 không khớp, file tải về đã bị xóa.");
        }
        File.Move(part, target, true);
    }

    // llama.cpp's Windows builds link the Visual C++ runtime dynamically; a freshly installed Windows may lack it.
    private static readonly string[] VcRuntimeFiles = ["vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll"];

    private static bool HasVcRuntime() => VcRuntimeFiles.All(f => File.Exists(Path.Combine(Environment.SystemDirectory, f)));

    private async Task EnsureVcRuntimeAsync(CancellationToken ct)
    {
        if (HasVcRuntime()) return;
        Log("Máy chưa có Microsoft Visual C++ Runtime: tải bản chính thức từ Microsoft và cài (Windows sẽ hỏi quyền).");
        var url = IsArm64Os ? "https://aka.ms/vs/17/release/vc_redist.arm64.exe" : "https://aka.ms/vs/17/release/vc_redist.x64.exe";
        var installer = Path.Combine(LocalAiRoot, IsArm64Os ? "vc_redist.arm64.exe" : "vc_redist.x64.exe");
        Directory.CreateDirectory(LocalAiRoot);
        using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
        await using (var input = await client.GetStreamAsync(url, ct))
        await using (var output = File.Create(installer))
            await input.CopyToAsync(output, ct);
        // The download URL always serves the latest build, so trust comes from Microsoft's Authenticode signature.
        if (!IsSignedByMicrosoft(installer))
        {
            File.Delete(installer);
            throw new IOException("File cài Visual C++ Runtime không có chữ ký hợp lệ của Microsoft, đã xóa.");
        }
        using (var setup = Process.Start(new ProcessStartInfo(installer, "/install /passive /norestart") { UseShellExecute = true, Verb = "runas" })
            ?? throw new IOException("Không chạy được file cài Visual C++ Runtime."))
            await setup.WaitForExitAsync(ct);
        File.Delete(installer);
        if (!HasVcRuntime()) throw new IOException("Chưa cài được Microsoft Visual C++ Runtime. Hãy cài thủ công từ https://aka.ms/vs/17/release/vc_redist.x64.exe rồi thử lại.");
    }

    private static bool IsSignedByMicrosoft(string path)
    {
        if (!WinTrust.Verify(path)) return false;
#pragma warning disable SYSLIB0057 // only the signer subject is read; WinVerifyTrust above validated the chain and file hash
        using var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
        return signer.GetNameInfo(X509NameType.SimpleName, false) == "Microsoft Corporation"
            || signer.Subject.Contains("O=Microsoft Corporation", StringComparison.Ordinal);
    }

    private static class WinTrust
    {
        private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FileInfo
        {
            public uint cbStruct;
            public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Data
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref Data data);

        public static bool Verify(string path)
        {
            var file = new FileInfo { cbStruct = (uint)Marshal.SizeOf<FileInfo>(), pcwszFilePath = path };
            var filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
            try
            {
                Marshal.StructureToPtr(file, filePtr, false);
                var data = new Data
                {
                    cbStruct = (uint)Marshal.SizeOf<Data>(),
                    dwUIChoice = 2,        // WTD_UI_NONE
                    fdwRevocationChecks = 0,
                    dwUnionChoice = 1,     // WTD_CHOICE_FILE
                    pFile = filePtr,
                    dwStateAction = 0,
                    dwProvFlags = 0x1000,  // WTD_CACHE_ONLY_URL_RETRIEVAL
                };
                var action = GenericVerifyV2;
                return WinVerifyTrust(IntPtr.Zero, ref action, ref data) == 0;
            }
            finally
            {
                Marshal.DestroyStructure<FileInfo>(filePtr);
                Marshal.FreeHGlobal(filePtr);
            }
        }
    }

    // Starts llama-server once per app session on a free loopback port and waits until the model is loaded.
    // If the GPU backend fails (old driver, no Vulkan, too little VRAM) the server is restarted on the CPU.
    private async Task<Uri> EnsureLocalServerAsync(LocalModelPack pack, CancellationToken ct)
    {
        if (_localServer is { HasExited: false } && _localServerUri is not null) return _localServerUri;
        if (!IsLocalModelInstalled(pack)) throw new InvalidOperationException("AI trên máy chưa được cài.");
        try { return await StartLocalServerAsync(pack, cpuOnly: false, ct); }
        catch (Exception ex) when (ex is IOException or TimeoutException && !ct.IsCancellationRequested)
        {
            Log($"AI trên máy không chạy được bằng GPU ({ex.Message}); chuyển sang CPU.");
            return await StartLocalServerAsync(pack, cpuOnly: true, ct);
        }
    }

    private async Task<Uri> StartLocalServerAsync(LocalModelPack pack, bool cpuOnly, CancellationToken ct)
    {
        StopLocalServer();
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
        if (cpuOnly) { info.ArgumentList.Add("-dev"); info.ArgumentList.Add("none"); }
        var process = Process.Start(info) ?? throw new IOException("Không chạy được llama-server.");
        var tail = new System.Collections.Concurrent.ConcurrentQueue<string>();
        void Keep(string? line) { if (line is null) return; tail.Enqueue(line); while (tail.Count > 20) tail.TryDequeue(out _); }
        process.OutputDataReceived += (_, e) => Keep(e.Data);
        process.ErrorDataReceived += (_, e) => Keep(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _localServer = process;
        using var health = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(cpuOnly ? 10 : 5);
        SetStatus(cpuOnly ? "Đang nạp AI trên máy (CPU)..." : "Đang nạp AI trên máy...");
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                await Task.Delay(200, CancellationToken.None); // let the last stderr lines arrive
                throw new IOException("llama-server dừng khi nạp model (mã " + process.ExitCode + "): " + string.Join(" | ", tail.TakeLast(3)));
            }
            try
            {
                using var response = await health.GetAsync($"http://127.0.0.1:{port}/health", ct);
                if (response.IsSuccessStatusCode) break;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { }
            if (DateTime.UtcNow > deadline) { StopLocalServer(); throw new TimeoutException("llama-server không sẵn sàng kịp."); }
            await Task.Delay(500, ct);
        }
        _localServerUri = new Uri($"http://127.0.0.1:{port}/v1");
        return _localServerUri;
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
