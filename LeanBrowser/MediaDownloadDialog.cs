using System.Diagnostics;

namespace LeanBrowser;

/// <summary>Modeless progress window for a video or audio download.</summary>
internal sealed class MediaDownloadDialog : Form
{
    private readonly Action _cancel;
    private readonly SynchronizationContext _uiContext;
    private readonly int _uiThreadId;
    private readonly Label _title = new()
    {
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font(Theme.UiFont, 15f, FontStyle.Bold)
    };
    private readonly Label _source = new()
    {
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font(Theme.UiFont, 9f)
    };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font(Theme.UiFont, 10f, FontStyle.Bold)
    };
    private readonly Label _detail = new()
    {
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font(Theme.UiFont, 9f)
    };
    private readonly ProgressBar _progress = new()
    {
        Dock = DockStyle.Fill,
        Minimum = 0,
        Maximum = 100,
        Style = ProgressBarStyle.Marquee,
        MarqueeAnimationSpeed = 30
    };
    private readonly Button _cancelButton = CreateButton("Cancelar");
    private readonly Button _openFolderButton = CreateButton("Abrir pasta");
    private readonly Button _closeButton = CreateButton("Fechar");
    private bool _cancelRequested;
    private bool _finished;
    private string? _completedFilePath;

    internal MediaDownloadDialog(MediaDownloadKind kind, string sourceUrl, Action cancel)
    {
        _cancel = cancel ?? throw new ArgumentNullException(nameof(cancel));
        _uiThreadId = Environment.CurrentManagedThreadId;
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        Text = kind == MediaDownloadKind.Audio ? "Download de áudio" : "Download de vídeo";
        _title.Text = kind == MediaDownloadKind.Audio ? "Baixando áudio" : "Baixando vídeo";
        _source.Text = sourceUrl;
        _status.Text = "Preparando download...";
        _detail.Text = "Aguardando informações de progresso.";

        Font = new Font(Theme.UiFont, 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(500, 248);
        MinimumSize = Size;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8,
            Padding = new Padding(22, 16, 22, 16)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.Controls.Add(_title, 0, 0);
        layout.Controls.Add(_source, 0, 1);
        layout.Controls.Add(_status, 0, 3);
        layout.Controls.Add(_progress, 0, 4);
        layout.Controls.Add(_detail, 0, 5);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty
        };
        buttons.Controls.Add(_closeButton);
        buttons.Controls.Add(_openFolderButton);
        buttons.Controls.Add(_cancelButton);
        layout.Controls.Add(buttons, 0, 7);
        Controls.Add(layout);

        _cancelButton.Click += (_, _) => RequestCancel();
        _openFolderButton.Click += (_, _) => OpenCompletedFileFolder();
        _closeButton.Click += (_, _) => Close();
        _openFolderButton.Visible = false;
        _closeButton.Visible = false;
        CancelButton = _cancelButton;
        ApplyTheme();
    }

    internal void UpdateProgress(MediaDownloadProgress progress) => OnUiThread(() =>
    {
        if (_finished || _cancelRequested) return;

        _status.Text = string.IsNullOrWhiteSpace(progress.Message)
            ? "Baixando..."
            : progress.Message;
        if (progress.Percent is { } value && double.IsFinite(value))
        {
            var percent = (int)Math.Round(Math.Clamp(value, 0d, 100d));
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.MarqueeAnimationSpeed = 0;
            _progress.Value = percent;
            _detail.Text = progress.BytesReceived < 0
                ? $"{percent}% desta etapa concluídos"
                : progress.TotalBytes > 0
                    ? $"{percent}%  •  {FormatBytes(progress.BytesReceived)} de {FormatBytes(progress.TotalBytes)}"
                    : $"{percent}%  •  {FormatBytes(progress.BytesReceived)} recebidos";
        }
        else
        {
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.MarqueeAnimationSpeed = 30;
            _detail.Text = progress.BytesReceived > 0
                ? $"{FormatBytes(progress.BytesReceived)} recebidos"
                : "Calculando progresso...";
        }
    });

    internal void MarkCompleted(string filePath) => OnUiThread(() =>
    {
        if (_finished) return;
        _finished = true;
        _completedFilePath = filePath;
        _title.Text = "Download concluído";
        _status.Text = "Arquivo salvo com sucesso.";
        _detail.Text = Path.GetFileName(filePath);
        _progress.Style = ProgressBarStyle.Continuous;
        _progress.MarqueeAnimationSpeed = 0;
        _progress.Value = 100;
        _cancelButton.Visible = false;
        _openFolderButton.Visible = true;
        _closeButton.Visible = true;
        CancelButton = _closeButton;
        AcceptButton = _openFolderButton;
    });

    internal void MarkFailed(string message) => OnUiThread(() =>
    {
        if (_finished) return;
        _finished = true;
        _title.Text = "Download interrompido";
        _status.Text = "Não foi possível concluir o download.";
        _detail.Text = string.IsNullOrWhiteSpace(message) ? "Tente novamente." : message;
        _progress.Style = ProgressBarStyle.Continuous;
        _progress.MarqueeAnimationSpeed = 0;
        _cancelButton.Visible = false;
        _closeButton.Visible = true;
        CancelButton = _closeButton;
        AcceptButton = _closeButton;
    });

    internal void ApplyTheme() => OnUiThread(() =>
    {
        BackColor = Theme.Surface;
        ForeColor = Theme.Ink;
        _title.ForeColor = Theme.Ink;
        _source.ForeColor = Theme.InkMuted;
        _status.ForeColor = Theme.Ink;
        _detail.ForeColor = Theme.InkMuted;
        foreach (var button in new[] { _cancelButton, _openFolderButton, _closeButton })
        {
            button.BackColor = Theme.SurfaceHot;
            button.ForeColor = Theme.Ink;
        }
        Invalidate(true);
    });

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_finished) RequestCancel();
        base.OnFormClosing(e);
    }

    private void RequestCancel()
    {
        if (_finished || _cancelRequested) return;
        _cancelRequested = true;
        _cancelButton.Enabled = false;
        _status.Text = "Cancelando download...";
        try { _cancel(); }
        catch (Exception ex) { MarkFailed(ex.Message); }
    }

    private void OpenCompletedFileFolder()
    {
        if (_completedFilePath is not { Length: > 0 } filePath) return;
        try
        {
            var fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath))
            {
                MessageBox.Show(this, "O arquivo não está mais no local onde foi salvo.",
                    "Arquivo não encontrado", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var explorer = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            var start = new ProcessStartInfo(explorer) { UseShellExecute = false };
            start.ArgumentList.Add("/select,");
            start.ArgumentList.Add(fullPath);
            Process.Start(start);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
        {
            MessageBox.Show(this, "Não foi possível abrir a pasta do arquivo.",
                "Erro ao abrir pasta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnUiThread(Action action)
    {
        if (Environment.CurrentManagedThreadId == _uiThreadId)
        {
            if (!IsDisposed && !Disposing) action();
            return;
        }
        _uiContext.Post(_ =>
        {
            if (!IsDisposed && !Disposing) action();
        }, null);
    }

    private static Button CreateButton(string text)
    {
        var button = new Button
        {
            Text = text,
            Width = 104,
            Height = 32,
            Margin = new Padding(8, 3, 0, 0),
            FlatStyle = FlatStyle.Flat,
            Font = new Font(Theme.UiFont, 9f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private static string FormatBytes(long count)
    {
        count = Math.Max(0, count);
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)count;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return unit == 0 ? $"{count:N0} B" : $"{size:0.0} {units[unit]}";
    }
}
