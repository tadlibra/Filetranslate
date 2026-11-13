namespace Filetranslate;

public partial class Form1
{
    private void OpenTranslatorSettings()
    {
        var current = LoadEditableTranslatorSettings();
        using var form = new Form
        {
            Text = "Cấu hình AI",
            ClientSize = new Size(520, 430),
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
            Location = new Point(160, 124),
            Width = 320
        };
        providerBox.Items.AddRange(Enum.GetNames<TranslatorProvider>().Cast<object>().ToArray());
        providerBox.SelectedItem = current.Provider.ToString();

        var baseUrlBox = SettingsTextBox(current.BaseUrl, 160, 170, false);
        var modelBox = SettingsTextBox(current.Model, 160, 216, false);
        var apiKeyBox = SettingsTextBox(current.ApiKey, 160, 262, true);
        var fallbackBox = new CheckBox
        {
            Text = "Fallback Google Translate khi AI lỗi",
            Checked = current.FallbackToGoogle,
            Location = new Point(160, 305),
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
            apiKeyBox.Enabled = provider is not TranslatorProvider.Google and not TranslatorProvider.Ollama and not TranslatorProvider.BuiltIn;
            baseUrlBox.Enabled = provider is not TranslatorProvider.Google and not TranslatorProvider.BuiltIn;
            modelBox.Enabled = provider is not TranslatorProvider.Google;
        }

        providerBox.SelectedIndexChanged += (_, _) => UpdateDefaults();

        // Presets only fill the fields; each user signs up for their own free key (a shared key would be abused and revoked).
        var presetBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(160, 24), Width = 320 };
        presetBox.Items.Add("(tự nhập)");
        presetBox.Items.AddRange(ProviderPresets.Select(p => (object)p.Name).ToArray());
        presetBox.SelectedIndex = 0;
        var presetNote = new Label { Location = new Point(160, 56), Size = new Size(320, 40), ForeColor = Color.FromArgb(200, 190, 150), Font = new Font("Segoe UI", 8.5f) };
        var keyLink = new LinkLabel { Text = "Lấy key / tải về", Location = new Point(160, 96), AutoSize = true, Visible = false, LinkColor = Color.FromArgb(255, 205, 86) };
        keyLink.LinkClicked += (_, _) =>
        {
            if (keyLink.Tag is string url) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        };
        presetBox.SelectedIndexChanged += (_, _) =>
        {
            if (presetBox.SelectedIndex <= 0) { presetNote.Text = ""; keyLink.Visible = false; return; }
            var preset = ProviderPresets[presetBox.SelectedIndex - 1];
            providerBox.SelectedItem = preset.Provider.ToString();
            baseUrlBox.Text = preset.Provider == TranslatorProvider.BuiltIn ? DefaultBaseUrl(preset.Provider) : preset.BaseUrl;
            modelBox.Text = preset.Provider == TranslatorProvider.BuiltIn ? DefaultModel(preset.Provider) : preset.Model;
            presetNote.Text = preset.Note;
            keyLink.Tag = preset.KeyUrl;
            keyLink.Visible = preset.KeyUrl is not null;
        };
        form.Controls.Add(SettingsLabel("Mẫu miễn phí", 24));
        form.Controls.Add(presetBox);
        form.Controls.Add(presetNote);
        form.Controls.Add(keyLink);

        form.Controls.Add(SettingsLabel("Provider", 124));
        form.Controls.Add(providerBox);
        form.Controls.Add(SettingsLabel("Base URL", 170));
        form.Controls.Add(baseUrlBox);
        form.Controls.Add(SettingsLabel("Model", 216));
        form.Controls.Add(modelBox);
        form.Controls.Add(SettingsLabel("API key", 262));
        form.Controls.Add(apiKeyBox);
        form.Controls.Add(fallbackBox);

        var saveButton = new Button
        {
            Text = "Lưu",
            Location = new Point(285, 360),
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
            Location = new Point(388, 360),
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
        StopLocalServer();
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
}
