namespace LeanBrowser;

/// <summary>Aba de histórico que também exibe o progresso das transferências atuais.</summary>
public sealed class DownloadsTab : TabPage
{
    private readonly DownloadHistoryStore _store;
    private readonly Label _title = new() { AutoSize = true, Font = new Font(Theme.UiFont, 22f, FontStyle.Bold) };
    private readonly Label _subtitle = new() { AutoSize = true, Font = new Font(Theme.UiFont, 10f) };
    private readonly FlowLayoutPanel _list = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        Padding = new Padding(0, 18, 0, 24)
    };

    public DownloadsTab(DownloadHistoryStore store) : base("Downloads")
    {
        _store = store;
        Padding = new Padding(48, 30, 48, 0);
        _title.Text = "Downloads";
        _subtitle.Text = "Acompanhe as transferências e consulte o histórico local.";
        Controls.Add(_list);
        Controls.Add(_subtitle);
        Controls.Add(_title);
        Resize += (_, _) => LayoutContent();
        LayoutContent();
        ApplyTheme();
        RefreshEntries();
    }

    public void RefreshEntries()
    {
        if (IsDisposed) return;
        _list.SuspendLayout();
        try
        {
            foreach (Control child in _list.Controls) child.Dispose();
            _list.Controls.Clear();

            var entries = _store.Snapshot();
            if (entries.Count == 0)
            {
                var empty = new Label
                {
                    AutoSize = true,
                    Text = "Nenhum download iniciado ainda.",
                    Font = new Font(Theme.UiFont, 11f),
                    Margin = new Padding(4, 8, 0, 0)
                };
                _list.Controls.Add(empty);
            }
            else
            {
                foreach (var entry in entries)
                    _list.Controls.Add(new DownloadRow(entry));
            }
        }
        finally { _list.ResumeLayout(true); }
        ApplyTheme();
    }

    public void ApplyTheme()
    {
        BackColor = Theme.Chrome;
        _list.BackColor = Theme.Chrome;
        _title.ForeColor = Theme.Ink;
        _subtitle.ForeColor = Theme.InkMuted;
        foreach (Control child in _list.Controls)
        {
            child.BackColor = Theme.Surface;
            child.ForeColor = Theme.Ink;
            if (child is DownloadRow row) row.ApplyTheme();
        }
        Invalidate(true);
    }

    private void LayoutContent()
    {
        _title.Location = new Point(0, 0);
        _subtitle.Location = new Point(0, 38);
        _list.Location = new Point(0, 76);
        _list.Size = new Size(Math.Max(320, ClientSize.Width), Math.Max(120, ClientSize.Height - 76));
        foreach (Control child in _list.Controls) child.Width = Math.Max(280, _list.ClientSize.Width - _list.Padding.Horizontal - 8);
    }

    private sealed class DownloadRow : Panel
    {
        private readonly Label _fileName = new() { AutoEllipsis = true, AutoSize = false, Font = new Font(Theme.UiFont, 10f, FontStyle.Bold) };
        private readonly Label _source = new() { AutoEllipsis = true, AutoSize = false, Font = new Font(Theme.UiFont, 8.5f) };
        private readonly Label _status = new() { AutoEllipsis = true, AutoSize = false, TextAlign = ContentAlignment.MiddleRight, Font = new Font(Theme.UiFont, 9f, FontStyle.Bold) };
        private readonly ProgressBar _progress = new() { Style = ProgressBarStyle.Continuous, Minimum = 0, Maximum = 100 };
        private readonly DownloadEntry _entry;

        public DownloadRow(DownloadEntry entry)
        {
            _entry = entry;
            Height = 92;
            Margin = new Padding(0, 0, 0, 10);
            Padding = new Padding(18, 12, 18, 12);
            Controls.Add(_fileName);
            Controls.Add(_source);
            Controls.Add(_status);
            Controls.Add(_progress);
            Layout += (_, _) => LayoutRow();
            UpdateValues();
            ApplyTheme();
        }

        public void ApplyTheme()
        {
            BackColor = Theme.Surface;
            _fileName.ForeColor = Theme.Ink;
            _source.ForeColor = Theme.InkMuted;
            _status.ForeColor = _entry.Status == DownloadStatus.Interrupted ? Color.FromArgb(0xB4, 0x23, 0x18) : Theme.Shield;
            _progress.BackColor = Theme.SurfaceHot;
            LayoutRow();
            Invalidate(true);
        }

        private void UpdateValues()
        {
            _fileName.Text = _entry.FileName;
            _source.Text = _entry.SourceUrl;
            var percent = GetPercent(_entry);
            _progress.Value = percent;
            _status.Text = _entry.Status switch
            {
                DownloadStatus.Completed => $"Concluído  •  {FormatBytes(_entry.BytesReceived)}",
                DownloadStatus.Interrupted => $"Interrompido  •  {_entry.Detail ?? "falha desconhecida"}",
                _ when percent >= 0 => $"{percent}%  •  {FormatBytes(_entry.BytesReceived)} de {FormatBytes(_entry.TotalBytes)}",
                _ => $"Baixando  •  {FormatBytes(_entry.BytesReceived)}"
            };
        }

        private void LayoutRow()
        {
            var width = Math.Max(220, ClientSize.Width - Padding.Horizontal);
            _fileName.Location = new Point(Padding.Left, Padding.Top);
            _fileName.Size = new Size(Math.Max(100, width - 180), 22);
            _status.Location = new Point(Math.Max(Padding.Left, width - 165), Padding.Top);
            _status.Size = new Size(165, 22);
            _source.Location = new Point(Padding.Left, Padding.Top + 24);
            _source.Size = new Size(width, 18);
            _progress.Location = new Point(Padding.Left, Padding.Top + 49);
            _progress.Size = new Size(width, 14);
        }

        private static int GetPercent(DownloadEntry entry)
        {
            if (entry.Status == DownloadStatus.Completed) return 100;
            if (entry.TotalBytes <= 0) return 0;
            return (int)Math.Clamp(entry.BytesReceived * 100L / entry.TotalBytes, 0, 100);
        }

        private static string FormatBytes(long value)
        {
            if (value < 0) return "tamanho desconhecido";
            string[] units = ["B", "KB", "MB", "GB", "TB"];
            var size = (double)value;
            var unit = 0;
            while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
            return unit == 0 ? $"{value:N0} B" : $"{size:0.0} {units[unit]}";
        }
    }
}
