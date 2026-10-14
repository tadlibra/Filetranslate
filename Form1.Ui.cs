using System.Drawing.Drawing2D;

namespace Filetranslate;

public partial class Form1
{
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

        _resetBox.Text = "Dịch lại từ bản gốc (giữ backup)";
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
}
