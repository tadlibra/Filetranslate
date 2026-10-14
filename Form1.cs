using System.Diagnostics;
using System.Net.Http;
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

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(120) };
    private readonly Regex _mcIdRegex = new(@"^[a-z0-9_\-]+:[a-z0-9_/\-.]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly Regex _hexRegex = new(@"^[0-9A-Fa-f]{6,8}$", RegexOptions.Compiled);
    private readonly Regex _letterRegex = new(@"\p{L}", RegexOptions.Compiled);
    private readonly Regex _colorRegex = new(@"(?:&|§)[0-9a-fklmnorA-FKLMNOR]", RegexOptions.Compiled);
    private readonly Regex _formatTokenRegex = new(@"(%(?:\d+\$)?[-#+ 0,(<]*\d*(?:\.\d+)?[bcdeEufFgGosSxXaAhn%])|(\{[A-Za-z0-9_.:-]+\})|(\$\{[^}]+\})|(<[^>\r\n]{1,80}>)", RegexOptions.Compiled);
    private readonly Regex _pathLikeRegex = new(@"^[a-z0-9_\-./]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private CancellationTokenSource? _cts;
    private SemaphoreSlim _translateLimiter = new(5);
    private Stopwatch? _stopwatch;
    private int _totalJobs;
    private int _doneJobs;
    private int _errorCount;
    private int _aiUseLogged;
    private int _aiFallbackLogged;
    private AiTranslatorConfig? _aiConfig;
    private bool _resetTranslations;
    private readonly System.Collections.Concurrent.ConcurrentQueue<CoverageItem> _coverage = new();

    private static readonly string[] SourceLangFiles =
    [
        "en_us.json", "en_us.lang", "en_gb.json", "en_gb.lang",
        "zh_cn.json", "zh_cn.lang", "zh_tw.json", "zh_tw.lang",
        "ko_kr.json", "ko_kr.lang", "ja_jp.json", "ja_jp.lang"
    ];

    private static readonly HashSet<string> JsonTextFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "title", "subtitle", "text", "description", "desc",
        "tooltip", "summary", "header", "footer", "body", "content",
        "contents", "landing_text", "message", "caption", "link_text"
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
}
