using System.Drawing.Drawing2D;

namespace Filetranslate;

public partial class Form1
{
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

    private void BrowseFolder()
    {
        if (_folderDialog.ShowDialog(this) == DialogResult.OK)
        {
            _pathBox.Text = _folderDialog.SelectedPath;
            Log($"Đã chọn: {_folderDialog.SelectedPath}");
        }
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
}
