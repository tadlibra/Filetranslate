using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Filetranslate;

public partial class Form1 : Form
{
    private const string ResourcePackName = "FileTranslate";
    private static readonly string ClaudeSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude",
        "settings.json");
    private static readonly string UserTranslatorConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Beeslator",
        "translator.config.json");
    private static readonly string LocalTranslatorConfigPath = Path.Combine(AppContext.BaseDirectory, "translator.config.json");

    private readonly TextBox _pathBox = new();
    private readonly ComboBox _langBox = new();
    private readonly CheckBox _resetBox = new();
    private readonly Button _settingsButton = new();
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();
    private readonly ProgressBar _progressBar = new();
    private readonly Label _countLabel = new();
    private readonly Label _percentLabel = new();
    private readonly Label _statusLabel = new();
    private readonly Label _timerLabel = new();
    private readonly TextBox _logBox = new();
    private readonly FolderBrowserDialog _folderDialog = new();
    private readonly System.Windows.Forms.Timer _uiTimer = new();

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly Regex _snbtSingleRegex = new(@"\b(title|subtitle):\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);
    private readonly Regex _snbtArrayStartRegex = new(@"\b(description|text):\s*\[", RegexOptions.Compiled);
    private readonly Regex _arrayStringRegex = new(@"""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);
    private readonly Regex _mcIdRegex = new(@"^[a-z0-9_\-]+:[a-z0-9_/\-.]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly Regex _hexRegex = new(@"^[0-9A-Fa-f]{6,8}$", RegexOptions.Compiled);
    private readonly Regex _letterRegex = new(@"\p{L}", RegexOptions.Compiled);
    private readonly Regex _colorRegex = new(@"(?:&|§)[0-9a-fklmnorA-FKLMNOR]", RegexOptions.Compiled);
    private readonly Regex _formatTokenRegex = new(@"(%(?:\d+\$)?[-#+ 0,(<]*\d*(?:\.\d+)?[bcdeEufFgGosSxXaAhn%])|(\{[A-Za-z0-9_.:-]+\})|(\$\{[^}]+\})|(<[^>\r\n]{1,80}>)", RegexOptions.Compiled);
    private readonly Regex _pathLikeRegex = new(@"^[a-z0-9_\-./]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private CancellationTokenSource? _cts;
    private SemaphoreSlim _translateLimiter = new(6);
    private Stopwatch? _stopwatch;
    private int _totalJobs;
    private int _doneJobs;
    private int _errorCount;
    private AiTranslatorConfig? _aiConfig;

    private static readonly string[] SourceLangFiles =
    [
        "en_us.json", "en_us.lang",
        "zh_cn.json", "zh_tw.json",
        "ko_kr.json", "ja_jp.json"
    ];

    private static readonly HashSet<string> JsonTextFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "title", "subtitle", "text", "description", "desc",
        "tooltip", "summary", "header", "footer", "body", "content",
        "contents", "landing_text", "book_texture", "message"
    };

    private static readonly Dictionary<string, (string Code, string File)> Languages = new()
    {
        ["Tiếng Việt"] = ("vi", "vi_vn"),
    };

    public Form1()
    {
        InitializeComponent();
        BuildUi();
    }

    private void BuildUi()
    {
        Text = "Beeslator";
        ClientSize = new Size(900, 720);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(18, 16, 10);
        ForeColor = Color.FromArgb(248, 230, 170);
        Font = new Font("Segoe UI", 10);

        try { Icon = new Icon("icon.ico"); } catch { }

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(24, 20, 24, 24),
            BackColor = Color.FromArgb(18, 16, 10),
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 398));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(body);

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 90,
            BackColor = Color.FromArgb(35, 28, 11),
        };
        Controls.Add(header);

        header.Controls.Add(CreateBeeLogo());

        var title = new Label
        {
            Text = "Beeslator",
            ForeColor = Color.FromArgb(255, 207, 64),
            Font = new Font("Segoe UI Semibold", 24),
            Location = new Point(84, 14),
            AutoSize = true,
        };
        header.Controls.Add(title);

        var subtitle = new Label
        {
            Text = "Dịch modpack Minecraft sâu hơn, an toàn hơn.",
            ForeColor = Color.FromArgb(205, 181, 104),
            Font = new Font("Segoe UI", 10),
            Location = new Point(87, 54),
            AutoSize = true,
        };
        header.Controls.Add(subtitle);

        var left = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 0, 18, 0),
        };
        body.Controls.Add(left, 0, 0);

        var right = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(8, 0, 0, 0),
        };
        body.Controls.Add(right, 1, 0);

        left.Controls.Add(SectionLabel("Thư mục modpack", 372));
        var pathCard = CardPanel(372, 68);
        _pathBox.SetBounds(12, 17, 244, 32);
        _pathBox.BorderStyle = BorderStyle.FixedSingle;
        _pathBox.BackColor = Color.FromArgb(30, 26, 16);
        _pathBox.ForeColor = Color.WhiteSmoke;
        var browseButton = new Button
        {
            Text = "Chọn",
            Bounds = new Rectangle(266, 16, 92, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(246, 190, 48),
            ForeColor = Color.FromArgb(32, 25, 8),
            Font = new Font("Segoe UI Semibold", 10),
        };
        browseButton.FlatAppearance.BorderSize = 0;
        browseButton.Click += (_, _) => BrowseFolder();
        pathCard.Controls.Add(_pathBox);
        pathCard.Controls.Add(browseButton);
        left.Controls.Add(pathCard);

        left.Controls.Add(SectionLabel("Ngôn ngữ dịch", 372));
        _langBox.Width = 372;
        _langBox.Height = 34;
        _langBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _langBox.BackColor = Color.FromArgb(30, 26, 16);
        _langBox.ForeColor = Color.WhiteSmoke;
        _langBox.FlatStyle = FlatStyle.Flat;
        _langBox.Items.AddRange(Languages.Keys.Cast<object>().ToArray());
        _langBox.SelectedIndex = 0;
        _langBox.Margin = new Padding(0, 0, 0, 10);
        left.Controls.Add(_langBox);

        _resetBox.Text = "Xóa bản dịch cũ và dịch lại từ đầu";
        _resetBox.ForeColor = Color.FromArgb(255, 205, 86);
        _resetBox.Width = 372;
        _resetBox.Margin = new Padding(0, 8, 0, 8);
        left.Controls.Add(_resetBox);

        ApplyActionButtonStyle(_settingsButton, Color.FromArgb(55, 45, 22), "CẤU HÌNH AI", 38, Color.FromArgb(255, 207, 64));
        _settingsButton.Width = 372;
        _settingsButton.Font = new Font("Segoe UI Semibold", 11, FontStyle.Bold);
        _settingsButton.Click += (_, _) => OpenTranslatorSettings();
        left.Controls.Add(_settingsButton);

        ApplyActionButtonStyle(_startButton, Color.FromArgb(246, 190, 48), "BẮT ĐẦU DỊCH", 48, Color.FromArgb(28, 22, 7));
        _startButton.Width = 372;
        _startButton.Click += async (_, _) => await StartTranslateAsync();
        left.Controls.Add(_startButton);

        ApplyActionButtonStyle(_stopButton, Color.FromArgb(116, 47, 31), "DỪNG LẠI", 42, Color.White);
        _stopButton.Width = 372;
        _stopButton.Enabled = false;
        _stopButton.Click += (_, _) => StopTranslate();
        left.Controls.Add(_stopButton);

        right.Controls.Add(SectionLabel("Tiến trình", 430));
        var progressCard = CardPanel(430, 94);
        _countLabel.Text = "0 / 0 việc";
        _countLabel.ForeColor = Color.FromArgb(255, 207, 64);
        _countLabel.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        _countLabel.Location = new Point(14, 14);
        _countLabel.AutoSize = true;
        _percentLabel.Text = "0%";
        _percentLabel.ForeColor = Color.FromArgb(255, 207, 64);
        _percentLabel.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        _percentLabel.Location = new Point(370, 14);
        _percentLabel.Size = new Size(44, 24);
        _percentLabel.TextAlign = ContentAlignment.TopRight;
        _progressBar.SetBounds(14, 52, 402, 18);
        _progressBar.Minimum = 0;
        _progressBar.Maximum = 100;
        progressCard.Controls.Add(_countLabel);
        progressCard.Controls.Add(_percentLabel);
        progressCard.Controls.Add(_progressBar);
        right.Controls.Add(progressCard);

        var statusRow = new Panel { Width = 430, Height = 30, BackColor = Color.Transparent, Margin = new Padding(0, 2, 0, 8) };
        _statusLabel.Text = "Sẵn sàng...";
        _statusLabel.ForeColor = Color.FromArgb(162, 142, 76);
        _statusLabel.Location = new Point(0, 3);
        _statusLabel.AutoSize = true;
        _timerLabel.Text = "";
        _timerLabel.ForeColor = Color.FromArgb(162, 142, 76);
        _timerLabel.Location = new Point(330, 3);
        _timerLabel.Size = new Size(100, 24);
        _timerLabel.TextAlign = ContentAlignment.TopRight;
        statusRow.Controls.Add(_statusLabel);
        statusRow.Controls.Add(_timerLabel);
        right.Controls.Add(statusRow);

        right.Controls.Add(SectionLabel("Log", 430));
        var logCard = CardPanel(430, 424);
        _logBox.Multiline = true;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.BorderStyle = BorderStyle.None;
        _logBox.BackColor = Color.FromArgb(14, 12, 8);
        _logBox.ForeColor = Color.FromArgb(245, 229, 176);
        _logBox.Font = new Font("Consolas", 11);
        _logBox.SetBounds(14, 14, 402, 396);
        logCard.Controls.Add(_logBox);
        right.Controls.Add(logCard);

        _uiTimer.Interval = 1000;
        _uiTimer.Tick += (_, _) =>
        {
            if (_stopwatch is null) return;
            var t = _stopwatch.Elapsed;
            _timerLabel.Text = $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
        };

        _aiConfig = LoadAiTranslatorConfig();
        Log("App sẵn sàng.");
        Log(_aiConfig is null
            ? "AI chưa cấu hình, sẽ fallback Google Translate."
            : $"AI translator: {_aiConfig.Provider} / {_aiConfig.Model} @ {_aiConfig.BaseUrl.Host}");
    }

    private async Task StartTranslateAsync()
    {
        var instancePath = _pathBox.Text.Trim();
        var modsPath = Path.Combine(instancePath, "mods");
        if (string.IsNullOrWhiteSpace(instancePath) || !Directory.Exists(modsPath))
        {
            Log("Không tìm thấy thư mục mods/.");
            return;
        }

        _cts = new CancellationTokenSource();
        _translateLimiter.Dispose();
        _translateLimiter = new SemaphoreSlim(5);

        _startButton.Enabled = false;
        _stopButton.Enabled = true;
        _logBox.Clear();
        _errorCount = 0;
        _doneJobs = 0;
        _totalJobs = 0;
        _stopwatch = Stopwatch.StartNew();
        _uiTimer.Start();
        SetStatus("Đang dịch...");

        try
        {
            await RunTranslateAsync(instancePath, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            Log("Đã dừng.");
            SetStatus("Đã dừng");
        }
        catch (Exception ex)
        {
            Log($"Lỗi: {ex.Message}");
            SetStatus("Lỗi");
        }
        finally
        {
            _uiTimer.Stop();
            _stopwatch?.Stop();
            _startButton.Enabled = true;
            _stopButton.Enabled = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private void StopTranslate() => _cts?.Cancel();

    private async Task RunTranslateAsync(string instancePath, CancellationToken ct)
    {
        var (langCode, langFile) = Languages[_langBox.Text];
        var outputPack = Path.Combine(instancePath, ".beeslator-staging");
        const int batchSize = 50;

        if (Directory.Exists(outputPack))
        {
            Directory.Delete(outputPack, true);
            Log("Đã xóa bản dịch cũ.");
        }

        Directory.CreateDirectory(outputPack);
        Log("Chế độ inject trực tiếp: app sẽ backup rồi ghi lang vào jar/assets.");

        var jarFiles = Directory.GetFiles(Path.Combine(instancePath, "mods"), "*.jar");
        var looseLangFiles = FindLooseLangFiles(instancePath).ToArray();
        var loosePatchouliFiles = FindLoosePatchouliFiles(instancePath).ToArray();

        _totalJobs = jarFiles.Length + looseLangFiles.Length + loosePatchouliFiles.Length + 1;
        UpdateProgress();

        Log($"Tìm thấy {jarFiles.Length} mod jar, {looseLangFiles.Length} lang rời, {loosePatchouliFiles.Length} file Patchouli rời.");

        var jarTasks = jarFiles.Select(j => RunJobAsync(
            () => ProcessJarAsync(j, outputPack, langCode, langFile, batchSize, ct),
            ct)).ToArray();
        await Task.WhenAll(jarTasks);

        var looseLangTasks = looseLangFiles.Select(f => RunJobAsync(
            () => ProcessLooseLangFileAsync(f, outputPack, langCode, langFile, batchSize, ct),
            ct)).ToArray();
        await Task.WhenAll(looseLangTasks);

        var loosePatchouliTasks = loosePatchouliFiles.Select(f => RunJobAsync(
            () => ProcessLoosePatchouliFileAsync(f, outputPack, langCode, langFile, batchSize, ct),
            ct)).ToArray();
        await Task.WhenAll(loosePatchouliTasks);

        await RunJobAsync(() => TranslateFtbQuestsAsync(instancePath, outputPack, langCode, langFile, batchSize, ct), ct);
        SetMinecraftLanguage(instancePath, langFile);

        var elapsed = _stopwatch?.Elapsed ?? TimeSpan.Zero;
        Log(_errorCount > 0
            ? $"Hoàn tất có {_errorCount} lỗi. Thời gian: {(int)elapsed.TotalMinutes}p {elapsed.Seconds}s"
            : $"Hoàn tất. Thời gian: {(int)elapsed.TotalMinutes}p {elapsed.Seconds}s");
        SetStatus(_errorCount > 0 ? $"Hoàn tất - {_errorCount} lỗi" : "Hoàn tất!");
    }

    private async Task RunJobAsync(Func<Task<JobResult>> job, CancellationToken ct)
    {
        var result = await job();
        Interlocked.Add(ref _errorCount, result.Errors);
        var done = Interlocked.Increment(ref _doneJobs);
        UpdateProgress();

        if (result.Messages.Count == 0) return;
        Log($"[{done}/{_totalJobs}] {result.Name}");
        foreach (var msg in result.Messages) Log($" {msg}");
    }

    private async Task<JobResult> ProcessJarAsync(
        string jarPath, string outputPack, string langCode, string langFile, int batchSize, CancellationToken ct)
    {
        await _translateLimiter.WaitAsync(ct);
        try
        {
            var modName = Path.GetFileNameWithoutExtension(jarPath);
            var messages = new List<string>();
            var errors = 0;
            var stagedFiles = new List<string>();

            using (var fs = File.OpenRead(jarPath))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                var langEntries = PickBestLangEntries(zip);
                foreach (var (ns, sourceEntry) in langEntries)
                {
                    ct.ThrowIfCancellationRequested();
                    var result = await TranslateLangEntryAsync(sourceEntry.FullName, async () =>
                    {
                        using var sr = new StreamReader(sourceEntry.Open(), Encoding.UTF8);
                        return await sr.ReadToEndAsync(ct);
                    }, outputPack, ns, langCode, langFile, batchSize, ct);

                    errors += result.Errors;
                    messages.AddRange(result.Messages);
                    stagedFiles.Add(Path.Combine(outputPack, "assets", ns, "lang", $"{langFile}.json"));
                }

                var patchouliEntries = zip.Entries
                    .Where(IsPatchouliBookEntry)
                    .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                foreach (var entry in patchouliEntries)
                {
                    ct.ThrowIfCancellationRequested();
                    var result = await TranslatePatchouliJsonAsync(entry.FullName, async () =>
                    {
                        using var sr = new StreamReader(entry.Open(), Encoding.UTF8);
                        return await sr.ReadToEndAsync(ct);
                    }, outputPack, langCode, langFile, batchSize, ct);

                    errors += result.Errors;
                    messages.AddRange(result.Messages);
                    var staged = GetPatchouliOutputPath(entry.FullName, outputPack, langFile);
                    if (staged is not null) stagedFiles.Add(staged);
                }
            }

            var injected = InjectFilesIntoJar(jarPath, outputPack, stagedFiles, ct);
            if (injected > 0) messages.Add($"Đã ghi trực tiếp {injected} file vào jar.");

            if (messages.Count == 0) messages.Add("Không có lang/Patchouli cần dịch.");
            return new JobResult(modName, messages, errors);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new JobResult(Path.GetFileNameWithoutExtension(jarPath), [$"Lỗi: {ex.Message}"], 1);
        }
        finally
        {
            _translateLimiter.Release();
        }
    }

    private async Task<JobResult> ProcessLooseLangFileAsync(
        string sourcePath, string outputPack, string langCode, string langFile, int batchSize, CancellationToken ct)
    {
        await _translateLimiter.WaitAsync(ct);
        try
        {
            var parts = NormalizePath(sourcePath).Split('/');
            var langIndex = Array.FindIndex(parts, p => p.Equals("lang", StringComparison.OrdinalIgnoreCase));
            if (langIndex < 2) return new JobResult(Path.GetFileName(sourcePath), [], 0);

            var ns = parts[langIndex - 1];
            var result = await TranslateLangEntryAsync(sourcePath, () => File.ReadAllTextAsync(sourcePath, ct), outputPack, ns, langCode, langFile, batchSize, ct);

            var staged = Path.Combine(outputPack, "assets", ns, "lang", $"{langFile}.json");
            var target = Path.Combine(Path.GetDirectoryName(sourcePath)!, $"{langFile}.json");
            if (File.Exists(staged))
            {
                File.Copy(staged, target, true);
                result.Messages.Add($"Đã ghi trực tiếp: {target}");
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new JobResult(Path.GetFileName(sourcePath), [$"Lỗi: {ex.Message}"], 1);
        }
        finally
        {
            _translateLimiter.Release();
        }
    }

    private async Task<JobResult> ProcessLoosePatchouliFileAsync(
        string sourcePath, string outputPack, string langCode, string langFile, int batchSize, CancellationToken ct)
    {
        await _translateLimiter.WaitAsync(ct);
        try
        {
            var result = await TranslatePatchouliJsonAsync(sourcePath, () => File.ReadAllTextAsync(sourcePath, ct), outputPack, langCode, langFile, batchSize, ct);

            var staged = GetPatchouliOutputPath(sourcePath, outputPack, langFile);
            if (staged is not null && File.Exists(staged))
            {
                var target = ReplaceLanguageSegment(sourcePath, "en_us", langFile);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(staged, target, true);
                result.Messages.Add($"Đã ghi trực tiếp: {target}");
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new JobResult(Path.GetFileName(sourcePath), [$"Lỗi: {ex.Message}"], 1);
        }
        finally
        {
            _translateLimiter.Release();
        }
    }

    private async Task<JobResult> TranslateLangEntryAsync(
        string sourceName,
        Func<Task<string>> readContent,
        string outputPack,
        string ns,
        string langCode,
        string langFile,
        int batchSize,
        CancellationToken ct)
    {
        var messages = new List<string>();
        var errors = 0;
        var outFile = Path.Combine(outputPack, "assets", ns, "lang", $"{langFile}.json");
        var sourceMap = sourceName.EndsWith(".lang", StringComparison.OrdinalIgnoreCase)
            ? ParseLegacyLang(await readContent())
            : ParseLangJson(await readContent());

        if (sourceMap.Count == 0) return new JobResult(Path.GetFileName(sourceName), [], 0);

        var translated = File.Exists(outFile) && !_resetBox.Checked
            ? LoadExistingJsonMap(outFile)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        var missing = sourceMap
            .Where(x => !translated.ContainsKey(x.Key) && IsTranslatable(x.Value))
            .ToArray();

        if (missing.Length == 0)
        {
            messages.Add($"{ns}: đã có đủ {translated.Count} key, bỏ qua.");
            return new JobResult(Path.GetFileName(sourceName), messages, 0);
        }

        foreach (var batch in Chunk(missing, batchSize))
        {
            ct.ThrowIfCancellationRequested();
            var values = batch.Select(x => x.Value).ToList();
            var translatedBatch = await TranslateBatchAsync(values, langCode, DetectSourceLanguage(values), ct);
            errors += translatedBatch.Errors;
            for (var i = 0; i < batch.Length; i++)
            {
                translated[batch[i].Key] = translatedBatch.Values.ElementAtOrDefault(i) ?? batch[i].Value;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
        await File.WriteAllTextAsync(outFile, SerializeJsonMap(translated), Encoding.UTF8, ct);
        messages.Add($"{ns}: dịch thêm {missing.Length} key, tổng {translated.Count} key.");

        return new JobResult(Path.GetFileName(sourceName), messages, errors);
    }

    private async Task<JobResult> TranslatePatchouliJsonAsync(
        string sourceName,
        Func<Task<string>> readContent,
        string outputPack,
        string langCode,
        string langFile,
        int batchSize,
        CancellationToken ct)
    {
        var rel = NormalizePath(sourceName);
        var match = Regex.Match(rel, @"assets/([^/]+)/patchouli_books/([^/]+)/en_us/(.+\.json)$", RegexOptions.IgnoreCase);
        if (!match.Success) return new JobResult(Path.GetFileName(sourceName), [], 0);

        var ns = match.Groups[1].Value;
        var book = match.Groups[2].Value;
        var targetRel = match.Groups[3].Value;
        var outFile = Path.Combine(outputPack, "assets", ns, "patchouli_books", book, langFile, targetRel.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(outFile) && !_resetBox.Checked)
        {
            return new JobResult(Path.GetFileName(sourceName), [$"Patchouli {ns}/{book}: đã có, bỏ qua."], 0);
        }

        var node = JsonNode.Parse(await readContent());
        if (node is null) return new JobResult(Path.GetFileName(sourceName), [], 0);

        var strings = new List<JsonStringRef>();
        CollectJsonStrings(node, null, strings);
        var targets = strings.Where(x => IsPatchouliTextField(x.PropertyName, x.Value)).ToArray();
        if (targets.Length == 0) return new JobResult(Path.GetFileName(sourceName), [], 0);

        var errors = 0;
        foreach (var batch in Chunk(targets, batchSize))
        {
            ct.ThrowIfCancellationRequested();
            var values = batch.Select(x => x.Value).ToList();
            var translatedBatch = await TranslateBatchAsync(values, langCode, DetectSourceLanguage(values), ct);
            errors += translatedBatch.Errors;
            for (var i = 0; i < batch.Length; i++)
            {
                batch[i].Set(translatedBatch.Values.ElementAtOrDefault(i) ?? batch[i].Value);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
        await File.WriteAllTextAsync(outFile, node.ToJsonString(JsonOptions(true)), Encoding.UTF8, ct);
        return new JobResult(Path.GetFileName(sourceName), [$"Patchouli {ns}/{book}: dịch {targets.Length} chuỗi."], errors);
    }

    private async Task<JobResult> TranslateFtbQuestsAsync(string instancePath, string outputPack, string langCode, string langFile, int batchSize, CancellationToken ct)
    {
        var questDir = Path.Combine(instancePath, "config", "ftbquests", "quests");
        if (!Directory.Exists(questDir)) return new JobResult("FTB Quests", ["Không có FTB Quests."], 0);

        var snbtFiles = Directory.GetFiles(questDir, "*.snbt", SearchOption.AllDirectories);
        if (snbtFiles.Length == 0) return new JobResult("FTB Quests", ["Không có file .snbt."], 0);

        var allEntries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in snbtFiles)
        {
            ct.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(file, ct);
            var rel = Path.GetRelativePath(questDir, file).Replace('\\', '/');
            rel = Regex.Replace(rel, @"\.snbt$", "", RegexOptions.IgnoreCase).Replace("/", ".");
            foreach (var kv in ExtractSnbtStrings(content, rel)) allEntries[kv.Key] = kv.Value;
        }

        if (allEntries.Count == 0) return new JobResult("FTB Quests", ["Không tìm thấy chuỗi cần dịch."], 0);

        var out1 = Path.Combine(questDir, "lang", $"{langFile}.json");
        var translated = File.Exists(out1) && !_resetBox.Checked
            ? LoadExistingJsonMap(out1)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        var missing = allEntries.Where(x => !translated.ContainsKey(x.Key)).ToArray();
        var errors = 0;
        foreach (var batch in Chunk(missing, Math.Max(100, batchSize)))
        {
            ct.ThrowIfCancellationRequested();
            var values = batch.Select(x => x.Value).ToList();
            var translatedBatch = await TranslateBatchAsync(values, langCode, DetectSourceLanguage(values), ct);
            errors += translatedBatch.Errors;
            for (var i = 0; i < batch.Length; i++)
            {
                translated[batch[i].Key] = translatedBatch.Values.ElementAtOrDefault(i) ?? batch[i].Value;
            }
        }

        var json = SerializeJsonMap(translated);
        Directory.CreateDirectory(Path.GetDirectoryName(out1)!);
        await File.WriteAllTextAsync(out1, json, Encoding.UTF8, ct);

        return new JobResult("FTB Quests", [$"Dịch thêm {missing.Length} chuỗi, tổng {translated.Count} chuỗi."], errors);
    }

    private Dictionary<string, string> ExtractSnbtStrings(string content, string prefix)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        var count = 0;
        string Key(string field) => $"{prefix}.{field}.{Interlocked.Increment(ref count)}";

        var contentNoTasks = Regex.Replace(content, @"tasks:\s*\[[\s\S]*?\]\s*\}", "tasks:[]};");
        foreach (Match m in _snbtSingleRegex.Matches(contentNoTasks))
        {
            var field = m.Groups[1].Value;
            var value = UnescapeSnbtString(m.Groups[2].Value);
            if (IsTranslatable(value)) entries[Key(field)] = value;
        }

        foreach (Match m in _snbtArrayStartRegex.Matches(content))
        {
            var field = m.Groups[1].Value;
            var start = m.Index + m.Length - 1;
            var end = FindMatchingBracket(content, start);
            if (end <= start) continue;

            var arr = content[(start + 1)..end];
            foreach (Match sm in _arrayStringRegex.Matches(arr))
            {
                var value = UnescapeSnbtString(sm.Groups[1].Value);
                if (IsTranslatable(value)) entries[Key(field)] = value;
            }
        }

        return entries;
    }

    private int FindMatchingBracket(string content, int start)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < content.Length; i++)
        {
            var ch = content[i];
            if (inString)
            {
                escaped = ch == '\\' && !escaped;
                if (ch == '"' && !escaped) inString = false;
                if (ch != '\\') escaped = false;
                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }
            if (ch == '[') depth++;
            else if (ch == ']')
            {
                depth--;
                if (depth == 0) return i;
            }
        }

        return -1;
    }

    private bool IsTranslatable(string value)
    {
        var stripped = value.Trim();
        if (string.IsNullOrEmpty(stripped)) return false;
        if (_mcIdRegex.IsMatch(stripped)) return false;
        if (_hexRegex.IsMatch(stripped)) return false;
        if (_pathLikeRegex.IsMatch(stripped) && stripped.Contains('/')) return false;
        if (stripped.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || stripped.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
        var plain = _colorRegex.Replace(stripped, "");
        plain = _formatTokenRegex.Replace(plain, "");
        return _letterRegex.IsMatch(plain);
    }

    private static Dictionary<string, string> ParseLegacyLang(string raw)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in raw.Split('\n'))
        {
            var t = line.Trim();
            if (string.IsNullOrEmpty(t) || t.StartsWith("#", StringComparison.Ordinal)) continue;
            var idx = t.IndexOf('=');
            if (idx <= 0) continue;
            map[t[..idx].Trim()] = t[(idx + 1)..].Trim();
        }
        return map;
    }

    private static Dictionary<string, string> ParseLangJson(string raw)
    {
        return JsonSerializer.Deserialize<Dictionary<string, string>>(raw) ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static Dictionary<string, string> LoadExistingJsonMap(string file)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private async Task<TranslateResult> TranslateBatchAsync(List<string> texts, string target, string source, CancellationToken ct)
    {
        if (texts.Count == 0) return new TranslateResult([], 0);

        if (_aiConfig is not null)
        {
            try
            {
                return await TranslateBatchWithAiAsync(texts, target, source, _aiConfig, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                if (!_aiConfig.FallbackToGoogle) throw;
                var fallback = await TranslateBatchWithGoogleAsync(texts, target, source, ct);
                return fallback with { Errors = fallback.Errors + texts.Count };
            }
        }

        return await TranslateBatchWithGoogleAsync(texts, target, source, ct);
    }

    private async Task<TranslateResult> TranslateBatchWithAiAsync(
        List<string> texts,
        string target,
        string source,
        AiTranslatorConfig config,
        CancellationToken ct)
    {
        var protectedTexts = new List<string>(texts.Count);
        var tokenMaps = new List<Dictionary<string, string>>(texts.Count);
        foreach (var text in texts)
        {
            protectedTexts.Add(ProtectTokens(text, out var tokens));
            tokenMaps.Add(tokens);
        }

        var prompt = BuildAiTranslatePrompt(protectedTexts, target, source);
        var translated = config.Provider switch
        {
            TranslatorProvider.AnthropicCompatible => await TranslateWithAnthropicCompatibleAsync(prompt, config, ct),
            TranslatorProvider.OpenAiCompatible => await TranslateWithOpenAiCompatibleAsync(prompt, config, ct),
            TranslatorProvider.Gemini => await TranslateWithGeminiAsync(prompt, config, ct),
            TranslatorProvider.Ollama => await TranslateWithOllamaAsync(prompt, config, ct),
            _ => throw new InvalidOperationException("Unsupported AI provider.")
        };

        if (string.IsNullOrWhiteSpace(translated)) throw new InvalidOperationException("AI response is empty.");

        var values = ParseJsonStringArray(translated);
        if (values.Count != texts.Count) throw new InvalidOperationException("AI response item count mismatch.");

        for (var i = 0; i < values.Count; i++)
        {
            values[i] = RestoreTokens(values[i], tokenMaps[i]);
        }

        return new TranslateResult(values, 0);
    }

    private async Task<string?> TranslateWithAnthropicCompatibleAsync(string prompt, AiTranslatorConfig config, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(config.BaseUrl, "/v1/messages"));
        request.Headers.TryAddWithoutValidation("x-api-key", config.ApiKey);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {config.ApiKey}");
        request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        request.Content = JsonContent.Create(new
        {
            model = config.Model,
            max_tokens = Math.Clamp(prompt.Length / 2 + 1024, 1024, 8192),
            temperature = 0,
            system = "You are a professional Minecraft modpack localization engine. Return valid JSON only.",
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = prompt
                }
            }
        });

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var responseJson = await response.Content.ReadAsStringAsync(ct);
        return ParseAnthropicText(responseJson);
    }

    private async Task<string?> TranslateWithOpenAiCompatibleAsync(string prompt, AiTranslatorConfig config, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(config.BaseUrl, "/v1/chat/completions"));
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {config.ApiKey}");
        }

        request.Content = JsonContent.Create(new
        {
            model = config.Model,
            temperature = 0,
            messages = new[]
            {
                new { role = "system", content = "You are a professional Minecraft modpack localization engine. Return valid JSON only." },
                new { role = "user", content = prompt }
            }
        });

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return ParseOpenAiText(json);
    }

    private async Task<string?> TranslateWithGeminiAsync(string prompt, AiTranslatorConfig config, CancellationToken ct)
    {
        var model = string.IsNullOrWhiteSpace(config.Model) ? "gemini-2.5-flash" : config.Model;
        var url = new Uri(config.BaseUrl, $"/v1beta/models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(config.ApiKey)}");
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = JsonContent.Create(new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0
            }
        });

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return ParseGeminiText(json);
    }

    private async Task<string?> TranslateWithOllamaAsync(string prompt, AiTranslatorConfig config, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(config.BaseUrl, "/api/chat"));
        request.Content = JsonContent.Create(new
        {
            model = config.Model,
            stream = false,
            messages = new[]
            {
                new { role = "system", content = "You are a professional Minecraft modpack localization engine. Return valid JSON only." },
                new { role = "user", content = prompt }
            }
        });

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return ParseOllamaText(json);
    }

    private async Task<TranslateResult> TranslateBatchWithGoogleAsync(List<string> texts, string target, string source, CancellationToken ct)
    {
        var outList = new List<string>(texts.Count);
        var errors = 0;
        foreach (var text in texts)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var protectedText = ProtectTokens(text, out var tokens);
                var q = Uri.EscapeDataString(protectedText);
                var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={source}&tl={target}&dt=t&q={q}";
                var json = await _httpClient.GetStringAsync(url, ct);
                var translated = ParseGoogleTranslate(json);
                outList.Add(translated is null ? text : RestoreTokens(translated, tokens));
                if (translated is null) errors++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                outList.Add(text);
                errors++;
            }
        }

        return new TranslateResult(outList, errors);
    }

    private static string BuildAiTranslatePrompt(List<string> texts, string target, string source)
    {
        var inputJson = JsonSerializer.Serialize(texts, JsonOptions(false));
        return $"""
Translate this Minecraft modpack localization batch from {source} to {target}.

Rules:
- Return only a valid JSON string array, with exactly {texts.Count} items, same order as input.
- Translate naturally for Minecraft/modpack UI, quests, items, guide books, and tooltips.
- Preserve every protected token exactly, including ZX0XZ style tokens.
- Preserve Minecraft color/format codes, placeholders, commands, item ids, URLs, file paths, and JSON-like fragments.
- Do not add explanations, markdown, comments, numbering, or extra keys.
- If a string should not be translated, return it unchanged.

Input JSON array:
{inputJson}
""";
    }

    private static string? ParseAnthropicText(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return null;

        var sb = new StringBuilder();
        foreach (var item in content.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var type)
                && type.GetString() == "text"
                && item.TryGetProperty("text", out var text))
            {
                sb.Append(text.GetString());
            }
        }

        return sb.ToString();
    }

    private static string? ParseOpenAiText(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array) return null;
        var first = choices.EnumerateArray().FirstOrDefault();
        if (first.ValueKind == JsonValueKind.Undefined) return null;
        return first.TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var content)
            ? content.GetString()
            : null;
    }

    private static string? ParseGeminiText(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array) return null;
        var first = candidates.EnumerateArray().FirstOrDefault();
        if (first.ValueKind == JsonValueKind.Undefined
            || !first.TryGetProperty("content", out var content)
            || !content.TryGetProperty("parts", out var parts)
            || parts.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var sb = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var text)) sb.Append(text.GetString());
        }
        return sb.ToString();
    }

    private static string? ParseOllamaText(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var content)
            ? content.GetString()
            : null;
    }

    private static List<string> ParseJsonStringArray(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            trimmed = Regex.Replace(trimmed, @"^```(?:json)?\s*", "", RegexOptions.IgnoreCase);
            trimmed = Regex.Replace(trimmed, @"\s*```$", "");
        }

        var start = trimmed.IndexOf('[');
        var end = trimmed.LastIndexOf(']');
        if (start >= 0 && end > start) trimmed = trimmed[start..(end + 1)];

        return JsonSerializer.Deserialize<List<string>>(trimmed) ?? [];
    }

    private static AiTranslatorConfig? LoadAiTranslatorConfig()
    {
        return LoadUserTranslatorConfig()
            ?? LoadClaudeSettingsConfig();
    }

    private static AiTranslatorConfig? LoadUserTranslatorConfig()
    {
        foreach (var file in new[] { UserTranslatorConfigPath, LocalTranslatorConfigPath })
        {
            if (!File.Exists(file)) continue;
            try
            {
                var config = JsonSerializer.Deserialize<TranslatorSettingsFile>(File.ReadAllText(file));
                if (config is null || config.Provider == TranslatorProvider.Google) return null;
                var normalized = NormalizeTranslatorConfig(config);
                if (normalized is not null) return normalized;
            }
            catch
            {
                continue;
            }
        }

        return null;
    }

    private static TranslatorSettingsFile LoadEditableTranslatorSettings()
    {
        foreach (var file in new[] { UserTranslatorConfigPath, LocalTranslatorConfigPath })
        {
            if (!File.Exists(file)) continue;
            try
            {
                return JsonSerializer.Deserialize<TranslatorSettingsFile>(File.ReadAllText(file)) ?? new TranslatorSettingsFile();
            }
            catch
            {
                break;
            }
        }

        var claude = LoadClaudeSettingsConfig();
        if (claude is not null)
        {
            return new TranslatorSettingsFile
            {
                Provider = claude.Provider,
                BaseUrl = claude.BaseUrl.ToString().TrimEnd('/'),
                Model = claude.Model,
                ApiKey = claude.ApiKey,
                FallbackToGoogle = claude.FallbackToGoogle
            };
        }

        return new TranslatorSettingsFile
        {
            Provider = TranslatorProvider.Gemini,
            BaseUrl = DefaultBaseUrl(TranslatorProvider.Gemini),
            Model = DefaultModel(TranslatorProvider.Gemini),
            FallbackToGoogle = true
        };
    }

    private static void SaveTranslatorSettings(TranslatorSettingsFile settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(UserTranslatorConfigPath)!);
        File.WriteAllText(UserTranslatorConfigPath, JsonSerializer.Serialize(settings, JsonOptions(true)), Encoding.UTF8);
    }

    private static AiTranslatorConfig? NormalizeTranslatorConfig(TranslatorSettingsFile config)
    {
        var baseUrl = string.IsNullOrWhiteSpace(config.BaseUrl)
            ? DefaultBaseUrl(config.Provider)
            : config.BaseUrl;
        var model = string.IsNullOrWhiteSpace(config.Model)
            ? DefaultModel(config.Provider)
            : config.Model;

        if (string.IsNullOrWhiteSpace(baseUrl)
            || string.IsNullOrWhiteSpace(model)
            || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (config.Provider is TranslatorProvider.Gemini or TranslatorProvider.AnthropicCompatible
            && string.IsNullOrWhiteSpace(config.ApiKey))
        {
            return null;
        }

        return new AiTranslatorConfig(config.Provider, uri, config.ApiKey ?? "", model, config.FallbackToGoogle);
    }

    private static AiTranslatorConfig? LoadClaudeSettingsConfig()
    {
        if (!File.Exists(ClaudeSettingsPath)) return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(ClaudeSettingsPath));
            if (!doc.RootElement.TryGetProperty("env", out var env)) return null;

            var baseUrl = ReadString(env, "ANTHROPIC_BASE_URL");
            var apiKey = ReadString(env, "ANTHROPIC_AUTH_TOKEN")
                ?? ReadString(env, "ANTHROPIC_API_KEY")
                ?? ReadString(env, "CLAUDIBLE_API_KEY");
            var model = ReadString(env, "ANTHROPIC_DEFAULT_SONNET_MODEL")
                ?? ReadString(env, "ANTHROPIC_DEFAULT_HAIKU_MODEL")
                ?? ReadString(env, "ANTHROPIC_DEFAULT_OPUS_MODEL");

            if (doc.RootElement.TryGetProperty("model", out var modelAlias))
            {
                model = modelAlias.GetString() switch
                {
                    "haiku" => ReadString(env, "ANTHROPIC_DEFAULT_HAIKU_MODEL") ?? model,
                    "opus" => ReadString(env, "ANTHROPIC_DEFAULT_OPUS_MODEL") ?? model,
                    "sonnet" => ReadString(env, "ANTHROPIC_DEFAULT_SONNET_MODEL") ?? model,
                    _ => model
                };
            }

            if (string.IsNullOrWhiteSpace(baseUrl)
                || string.IsNullOrWhiteSpace(apiKey)
                || string.IsNullOrWhiteSpace(model)
                || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            {
                return null;
            }

            return new AiTranslatorConfig(TranslatorProvider.AnthropicCompatible, uri, apiKey, model, true);
        }
        catch
        {
            return null;
        }
    }

    private static string DefaultBaseUrl(TranslatorProvider provider) => provider switch
    {
        TranslatorProvider.Gemini => "https://generativelanguage.googleapis.com",
        TranslatorProvider.OpenAiCompatible => "https://api.openai.com",
        TranslatorProvider.AnthropicCompatible => "https://api.anthropic.com",
        TranslatorProvider.Ollama => "http://localhost:11434",
        _ => ""
    };

    private static string DefaultModel(TranslatorProvider provider) => provider switch
    {
        TranslatorProvider.Gemini => "gemini-2.5-flash",
        TranslatorProvider.OpenAiCompatible => "gpt-4o-mini",
        TranslatorProvider.AnthropicCompatible => "claude-3-5-haiku-latest",
        TranslatorProvider.Ollama => "qwen2.5:7b",
        _ => ""
    };

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) ? value.GetString() : null;
    }

    private string ProtectTokens(string text, out Dictionary<string, string> tokens)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var index = 0;
        var protectedText = _formatTokenRegex.Replace(text, match =>
        {
            var key = $"ZX{index++}XZ";
            map[key] = match.Value;
            return key;
        });
        tokens = map;
        return protectedText;
    }

    private static string RestoreTokens(string text, Dictionary<string, string> tokens)
    {
        foreach (var (key, value) in tokens)
        {
            text = text.Replace(key, value, StringComparison.OrdinalIgnoreCase);
        }
        return text;
    }

    private static string? ParseGoogleTranslate(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) return null;
        var first = root[0];
        if (first.ValueKind != JsonValueKind.Array) return null;
        var sb = new StringBuilder();
        foreach (var seg in first.EnumerateArray())
        {
            if (seg.ValueKind == JsonValueKind.Array && seg.GetArrayLength() > 0) sb.Append(seg[0].GetString());
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    private static string DetectSourceLanguage(IEnumerable<string> values)
    {
        var sample = string.Join(' ', values.Take(20));
        if (Regex.IsMatch(sample, @"[\u4E00-\u9FFF\u3400-\u4DBF]")) return "zh-CN";
        if (Regex.IsMatch(sample, @"[\u3040-\u30FF]")) return "ja";
        if (Regex.IsMatch(sample, @"[\uAC00-\uD7AF]")) return "ko";
        return "en";
    }

    private static IEnumerable<T[]> Chunk<T>(IReadOnlyList<T> values, int size)
    {
        for (var i = 0; i < values.Count; i += size)
        {
            var len = Math.Min(size, values.Count - i);
            var arr = new T[len];
            for (var j = 0; j < len; j++) arr[j] = values[i + j];
            yield return arr;
        }
    }

    private Dictionary<string, ZipArchiveEntry> PickBestLangEntries(ZipArchive zip)
    {
        var byNamespace = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in SourceLangFiles)
        {
            foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith($"lang/{source}", StringComparison.OrdinalIgnoreCase)))
            {
                var parts = entry.FullName.Split('/');
                var assetsIndex = Array.FindIndex(parts, p => p.Equals("assets", StringComparison.OrdinalIgnoreCase));
                if (assetsIndex < 0 || parts.Length <= assetsIndex + 2) continue;
                var ns = parts[assetsIndex + 1];
                if (!byNamespace.ContainsKey(ns)) byNamespace[ns] = entry;
            }
        }
        return byNamespace;
    }

    private IEnumerable<string> FindLooseLangFiles(string instancePath)
    {
        foreach (var root in GetLooseAssetRoots(instancePath))
        {
            if (!Directory.Exists(root)) continue;
            foreach (var source in SourceLangFiles)
            {
                foreach (var file in Directory.GetFiles(root, source, SearchOption.AllDirectories))
                {
                    if (NormalizePath(file).Contains($"/{ResourcePackName}/", StringComparison.OrdinalIgnoreCase)) continue;
                    yield return file;
                }
            }
        }
    }

    private IEnumerable<string> FindLoosePatchouliFiles(string instancePath)
    {
        foreach (var root in GetLooseAssetRoots(instancePath))
        {
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
            {
                if (IsPatchouliBookPath(file)) yield return file;
            }
        }
    }

    private static IEnumerable<string> GetLooseAssetRoots(string instancePath)
    {
        yield return Path.Combine(instancePath, "kubejs", "assets");
        yield return Path.Combine(instancePath, "openloader", "resources", "assets");

        var resourcePacks = Path.Combine(instancePath, "resourcepacks");
        if (!Directory.Exists(resourcePacks)) yield break;
        foreach (var pack in Directory.GetDirectories(resourcePacks))
        {
            if (Path.GetFileName(pack).Equals(ResourcePackName, StringComparison.OrdinalIgnoreCase)) continue;
            yield return Path.Combine(pack, "assets");
        }
    }

    private static bool IsPatchouliBookEntry(ZipArchiveEntry entry) => IsPatchouliBookPath(entry.FullName);

    private static bool IsPatchouliBookPath(string path)
    {
        var normalized = NormalizePath(path);
        return normalized.Contains("/patchouli_books/", StringComparison.OrdinalIgnoreCase)
            && normalized.Contains("/en_us/", StringComparison.OrdinalIgnoreCase)
            && normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetPatchouliOutputPath(string sourceName, string outputPack, string langFile)
    {
        var rel = NormalizePath(sourceName);
        var match = Regex.Match(rel, @"assets/([^/]+)/patchouli_books/([^/]+)/en_us/(.+\.json)$", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var ns = match.Groups[1].Value;
        var book = match.Groups[2].Value;
        var targetRel = match.Groups[3].Value;
        return Path.Combine(outputPack, "assets", ns, "patchouli_books", book, langFile, targetRel.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string ReplaceLanguageSegment(string path, string sourceLang, string targetLang)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Equals(sourceLang, StringComparison.OrdinalIgnoreCase)) parts[i] = targetLang;
        }
        return Path.Combine(parts);
    }

    private int InjectFilesIntoJar(string jarPath, string stagingRoot, IEnumerable<string> stagedFiles, CancellationToken ct)
    {
        var files = stagedFiles
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0) return 0;

        BackupJar(jarPath);
        using var fs = new FileStream(jarPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Update);

        var injected = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var entryName = NormalizePath(Path.GetRelativePath(stagingRoot, file));
            if (!entryName.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)) continue;

            zip.GetEntry(entryName)?.Delete();
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using var input = File.OpenRead(file);
            using var output = entry.Open();
            input.CopyTo(output);
            injected++;
        }

        return injected;
    }

    private static void BackupJar(string jarPath)
    {
        var backup = jarPath + ".beeslator.bak";
        if (!File.Exists(backup)) File.Copy(jarPath, backup);
    }

    private bool IsPatchouliTextField(string? propertyName, string value)
    {
        if (!IsTranslatable(value)) return false;
        return propertyName is null || JsonTextFields.Contains(propertyName);
    }

    private void CollectJsonStrings(JsonNode node, string? propertyName, List<JsonStringRef> strings)
    {
        if (node is JsonObject obj)
        {
            foreach (var kv in obj.ToArray())
            {
                if (kv.Value is null) continue;
                CollectJsonStrings(kv.Value, kv.Key, strings);
            }
            return;
        }

        if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not null) CollectJsonStrings(item, propertyName, strings);
            }
            return;
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            strings.Add(new JsonStringRef(value, propertyName, text));
        }
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static string SerializeJsonMap(Dictionary<string, string> map)
    {
        return JsonSerializer.Serialize(map.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value), JsonOptions(true));
    }

    private static JsonSerializerOptions JsonOptions(bool indented) => new()
    {
        WriteIndented = indented,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string UnescapeSnbtString(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<string>($"\"{value}\"") ?? value;
        }
        catch
        {
            return value;
        }
    }

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

    private void SetMinecraftLanguage(string instancePath, string langFile)
    {
        var optionsFile = Path.Combine(instancePath, "options.txt");
        if (!File.Exists(optionsFile))
        {
            File.WriteAllLines(optionsFile, [$"lang:{langFile}"], Encoding.UTF8);
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
            File.WriteAllLines(optionsFile, lines, Encoding.UTF8);
            Log($"Đã đặt ngôn ngữ Minecraft sang {langFile}.");
        }
    }

    private static string Quote(string value) => $"\"{value}\"";

    private void BrowseFolder()
    {
        if (_folderDialog.ShowDialog(this) == DialogResult.OK)
        {
            _pathBox.Text = _folderDialog.SelectedPath;
            Log($"Đã chọn: {_folderDialog.SelectedPath}");
        }
    }

    private void OpenTranslatorSettings()
    {
        var current = LoadEditableTranslatorSettings();
        using var form = new Form
        {
            Text = "Cấu hình AI",
            ClientSize = new Size(520, 330),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Color.FromArgb(18, 16, 10),
            ForeColor = Color.FromArgb(248, 230, 170),
            Font = new Font("Segoe UI", 10)
        };

        var providerBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(160, 24),
            Width = 320
        };
        providerBox.Items.AddRange(Enum.GetNames<TranslatorProvider>().Cast<object>().ToArray());
        providerBox.SelectedItem = current.Provider.ToString();

        var baseUrlBox = SettingsTextBox(current.BaseUrl, 160, 70, false);
        var modelBox = SettingsTextBox(current.Model, 160, 116, false);
        var apiKeyBox = SettingsTextBox(current.ApiKey, 160, 162, true);
        var fallbackBox = new CheckBox
        {
            Text = "Fallback Google Translate khi AI lỗi",
            Checked = current.FallbackToGoogle,
            Location = new Point(160, 205),
            Width = 320,
            ForeColor = Color.FromArgb(255, 205, 86)
        };

        void UpdateDefaults()
        {
            if (!Enum.TryParse<TranslatorProvider>(providerBox.Text, out var provider)) return;
            if (string.IsNullOrWhiteSpace(baseUrlBox.Text) || baseUrlBox.Text == current.BaseUrl)
            {
                baseUrlBox.Text = DefaultBaseUrl(provider);
            }
            if (string.IsNullOrWhiteSpace(modelBox.Text) || modelBox.Text == current.Model)
            {
                modelBox.Text = DefaultModel(provider);
            }
            apiKeyBox.Enabled = provider is not TranslatorProvider.Google and not TranslatorProvider.Ollama;
            baseUrlBox.Enabled = provider is not TranslatorProvider.Google;
            modelBox.Enabled = provider is not TranslatorProvider.Google;
        }

        providerBox.SelectedIndexChanged += (_, _) => UpdateDefaults();

        form.Controls.Add(SettingsLabel("Provider", 24));
        form.Controls.Add(providerBox);
        form.Controls.Add(SettingsLabel("Base URL", 70));
        form.Controls.Add(baseUrlBox);
        form.Controls.Add(SettingsLabel("Model", 116));
        form.Controls.Add(modelBox);
        form.Controls.Add(SettingsLabel("API key", 162));
        form.Controls.Add(apiKeyBox);
        form.Controls.Add(fallbackBox);

        var saveButton = new Button
        {
            Text = "Lưu",
            Location = new Point(285, 260),
            Size = new Size(92, 36),
            DialogResult = DialogResult.OK,
            BackColor = Color.FromArgb(246, 190, 48),
            ForeColor = Color.FromArgb(28, 22, 7),
            FlatStyle = FlatStyle.Flat
        };
        saveButton.FlatAppearance.BorderSize = 0;
        var cancelButton = new Button
        {
            Text = "Hủy",
            Location = new Point(388, 260),
            Size = new Size(92, 36),
            DialogResult = DialogResult.Cancel,
            BackColor = Color.FromArgb(55, 45, 22),
            ForeColor = Color.FromArgb(255, 207, 64),
            FlatStyle = FlatStyle.Flat
        };
        cancelButton.FlatAppearance.BorderSize = 0;
        form.Controls.Add(saveButton);
        form.Controls.Add(cancelButton);
        form.AcceptButton = saveButton;
        form.CancelButton = cancelButton;

        UpdateDefaults();
        if (form.ShowDialog(this) != DialogResult.OK) return;
        if (!Enum.TryParse<TranslatorProvider>(providerBox.Text, out var selectedProvider)) return;

        var settings = new TranslatorSettingsFile
        {
            Provider = selectedProvider,
            BaseUrl = baseUrlBox.Text.Trim(),
            Model = modelBox.Text.Trim(),
            ApiKey = apiKeyBox.Text.Trim(),
            FallbackToGoogle = fallbackBox.Checked
        };
        SaveTranslatorSettings(settings);
        _aiConfig = LoadAiTranslatorConfig();
        Log(_aiConfig is null
            ? "Đã lưu cấu hình: dùng Google Translate fallback."
            : $"Đã lưu cấu hình AI: {_aiConfig.Provider} / {_aiConfig.Model} @ {_aiConfig.BaseUrl.Host}");
    }

    private static Label SettingsLabel(string text, int y) => new()
    {
        Text = text,
        Location = new Point(28, y + 4),
        Width = 120,
        ForeColor = Color.FromArgb(255, 207, 64)
    };

    private static TextBox SettingsTextBox(string? text, int x, int y, bool password) => new()
    {
        Text = text ?? "",
        Location = new Point(x, y),
        Width = 320,
        BackColor = Color.FromArgb(30, 26, 16),
        ForeColor = Color.WhiteSmoke,
        BorderStyle = BorderStyle.FixedSingle,
        UseSystemPasswordChar = password
    };

    private void UpdateProgress()
    {
        if (InvokeRequired) { Invoke(UpdateProgress); return; }
        var pct = _totalJobs == 0 ? 0 : (int)Math.Round(_doneJobs * 100.0 / _totalJobs);
        _progressBar.Value = Math.Clamp(pct, 0, 100);
        _countLabel.Text = $"{_doneJobs} / {_totalJobs} việc";
        _percentLabel.Text = $"{pct}%";
    }

    private void SetStatus(string status)
    {
        if (InvokeRequired) { Invoke(() => SetStatus(status)); return; }
        _statusLabel.Text = status;
    }

    private void Log(string message)
    {
        if (InvokeRequired) { Invoke(() => Log(message)); return; }
        _logBox.AppendText(message + Environment.NewLine);
    }

    private Label SectionLabel(string text, int width) => new()
    {
        Text = text,
        Width = width,
        Height = 28,
        Font = new Font("Segoe UI Semibold", 11, FontStyle.Bold),
        ForeColor = Color.FromArgb(255, 207, 64),
        Margin = new Padding(0, 6, 0, 6),
    };

    private static Panel CreateBeeLogo()
    {
        var logo = new Panel
        {
            Location = new Point(24, 22),
            Size = new Size(46, 46),
            BackColor = Color.FromArgb(246, 190, 48),
        };

        logo.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var wingBrush = new SolidBrush(Color.FromArgb(232, 239, 228));
            using var wingPen = new Pen(Color.FromArgb(35, 28, 11), 1.3f);
            e.Graphics.FillEllipse(wingBrush, 8, 7, 15, 14);
            e.Graphics.FillEllipse(wingBrush, 22, 7, 15, 14);
            e.Graphics.DrawEllipse(wingPen, 8, 7, 15, 14);
            e.Graphics.DrawEllipse(wingPen, 22, 7, 15, 14);

            using var bodyBrush = new SolidBrush(Color.FromArgb(35, 28, 11));
            e.Graphics.FillEllipse(bodyBrush, 11, 16, 24, 18);

            using var stripeBrush = new SolidBrush(Color.FromArgb(246, 190, 48));
            e.Graphics.FillRectangle(stripeBrush, 17, 17, 4, 17);
            e.Graphics.FillRectangle(stripeBrush, 25, 17, 4, 17);

            using var eyeBrush = new SolidBrush(Color.White);
            e.Graphics.FillEllipse(eyeBrush, 28, 19, 4, 4);

            using var stingerBrush = new SolidBrush(Color.FromArgb(35, 28, 11));
            var stinger = new[] { new Point(10, 25), new Point(4, 22), new Point(4, 28) };
            e.Graphics.FillPolygon(stingerBrush, stinger);
        };

        return logo;
    }

    private static Panel CardPanel(int width, int height)
    {
        var p = new Panel
        {
            Width = width,
            Height = height,
            BackColor = Color.FromArgb(25, 21, 12),
            Margin = new Padding(0, 0, 0, 10),
        };
        p.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(84, 65, 20), 1f);
            using var gp = RoundedRect(new Rectangle(0, 0, p.Width - 1, p.Height - 1), 8);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawPath(pen, gp);
        };
        return p;
    }

    private static void ApplyActionButtonStyle(Button btn, Color bg, string text, int height, Color fg)
    {
        btn.Text = text;
        btn.Height = height;
        btn.BackColor = bg;
        btn.ForeColor = fg;
        btn.FlatStyle = FlatStyle.Flat;
        btn.FlatAppearance.BorderSize = 0;
        btn.Font = new Font("Segoe UI Semibold", 13, FontStyle.Bold);
        btn.Margin = new Padding(0, 4, 0, 4);
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed record JobResult(string Name, List<string> Messages, int Errors);
    private sealed record TranslateResult(List<string> Values, int Errors);
    private sealed record AiTranslatorConfig(TranslatorProvider Provider, Uri BaseUrl, string ApiKey, string Model, bool FallbackToGoogle);

    private enum TranslatorProvider
    {
        Google,
        Gemini,
        OpenAiCompatible,
        AnthropicCompatible,
        Ollama
    }

    private sealed class TranslatorSettingsFile
    {
        public TranslatorProvider Provider { get; set; } = TranslatorProvider.Gemini;
        public string? BaseUrl { get; set; }
        public string? ApiKey { get; set; }
        public string? Model { get; set; }
        public bool FallbackToGoogle { get; set; } = true;
    }

    private sealed class JsonStringRef(JsonValue node, string? propertyName, string value)
    {
        public string? PropertyName { get; } = propertyName;
        public string Value { get; private set; } = value;

        public void Set(string value)
        {
            Value = value;
            node.ReplaceWith(JsonValue.Create(value)!);
        }
    }
}
