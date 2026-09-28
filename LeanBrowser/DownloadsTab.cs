namespace LeanBrowser;

/// <summary>Aba de histórico que também exibe o progresso das transferências atuais.</summary>
public sealed class DownloadsTab : TabPage
{
    private readonly DownloadHistoryStore _store;
    private readonly Dictionary<Guid, DownloadRow> _rows = new();
    private readonly Label _title = new() { AutoSize = true, Font = new Font(Theme.UiFont, 22f, FontStyle.Bold) };
    private readonly Label _subtitle = new() { AutoSize = true, Font = new Font(Theme.UiFont, 10f) };
    private readonly Label _empty = new()
    {
        AutoSize = true,
        Text = "Nenhum download iniciado ainda.",
        Font = new Font(Theme.UiFont, 11f),
        Margin = new Padding(4, 8, 0, 0)
    };
    private readonly FlowLayoutPanel _list = new()
    {
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
        _list.Controls.Add(_empty);
        _list.ClientSizeChanged += (_, _) => SizeRows();
        Controls.Add(_list);
        Controls.Add(_subtitle);
        Controls.Add(_title);
        Resize += (_, _) => LayoutContent();
        LayoutContent();
        ApplyTheme();
        RefreshEntries();
    }

    public void RefreshEntries(IReadOnlyList<DownloadEntry>? snapshot = null)
    {
        if (IsDisposed) return;
        var entries = (snapshot ?? _store.Snapshot())
            .OrderByDescending(entry => entry.StartedAt)
            .ThenBy(entry => entry.Id)
            .ToArray();
        var ids = entries.Select(entry => entry.Id).ToHashSet();
        var scroll = _list.AutoScrollPosition;
        // Keep the same visible item in place when a newer download is inserted above it.
        var anchor = scroll.Y < 0
            ? _rows.Values.Where(row => row.Bottom > 0).OrderBy(row => row.Top).FirstOrDefault()
            : null;
        var anchorTop = anchor?.Top ?? 0;
        _list.SuspendLayout();
        try
        {
            foreach (var id in _rows.Keys.Where(id => !ids.Contains(id)).ToArray())
            {
                var row = _rows[id];
                _rows.Remove(id);
                _list.Controls.Remove(row);
                row.Dispose();
            }
            _empty.Visible = entries.Length == 0;
            for (var index = 0; index < entries.Length; index++)
            {
                var entry = entries[index];
                if (!_rows.TryGetValue(entry.Id, out var row))
                {
                    row = new DownloadRow(entry);
                    _rows.Add(entry.Id, row);
                    _list.Controls.Add(row);
                }
                else row.UpdateEntry(entry);
                if (_list.Controls.GetChildIndex(row) != index)
                    _list.Controls.SetChildIndex(row, index);
            }
            SizeRows();
        }
        finally { _list.ResumeLayout(true); }
        SizeRows();
        var desiredY = anchor is { IsDisposed: false }
            ? -_list.AutoScrollPosition.Y + anchor.Top - anchorTop
            : -scroll.Y;
        _list.AutoScrollPosition = new Point(0, Math.Max(0, desiredY));
    }

    public void ApplyTheme()
    {
        BackColor = Theme.Chrome;
        _list.BackColor = Theme.Chrome;
        _title.ForeColor = Theme.Ink;
        _subtitle.ForeColor = Theme.InkMuted;
        _empty.BackColor = Theme.Chrome;
        _empty.ForeColor = Theme.InkMuted;
        foreach (var row in _rows.Values) row.ApplyTheme();
        Invalidate(true);
    }

    private void LayoutContent()
    {
        var width = Math.Max(0, ClientSize.Width - Padding.Horizontal);
        _title.Location = new Point(Padding.Left, Padding.Top);
        _subtitle.Location = new Point(Padding.Left, Padding.Top + 42);
        _subtitle.MaximumSize = new Size(width, 0);
        var listTop = _subtitle.Bottom + 6;
        _list.Bounds = new Rectangle(Padding.Left, listTop, width,
            Math.Max(0, ClientSize.Height - listTop - Padding.Bottom));
        SizeRows();
    }

    private void SizeRows()
    {
        var width = Math.Max(0, _list.ClientSize.Width - _list.Padding.Horizontal - 8);
        foreach (var row in _rows.Values)
            if (row.Width != width) row.Width = width;
    }

    private sealed class DownloadRow : Panel
    {
        private readonly Label _fileName = new() { AutoEllipsis = true, AutoSize = false, Font = new Font(Theme.UiFont, 10f, FontStyle.Bold) };
        private readonly Label _source = new() { AutoEllipsis = true, AutoSize = false, Font = new Font(Theme.UiFont, 8.5f) };
        private readonly Label _status = new() { AutoEllipsis = true, AutoSize = false, TextAlign = ContentAlignment.MiddleRight, Font = new Font(Theme.UiFont, 9f, FontStyle.Bold) };
        private readonly ProgressBar _progress = new() { Style = ProgressBarStyle.Continuous, Minimum = 0, Maximum = 100 };
        private DownloadEntry _entry;

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
            UpdateStatusColor();
            _progress.BackColor = Theme.SurfaceHot;
            LayoutRow();
            Invalidate(true);
        }

        public void UpdateEntry(DownloadEntry entry)
        {
            if (_entry == entry) return;
            _entry = entry;
            UpdateValues();
            UpdateStatusColor();
        }

        private void UpdateStatusColor() => _status.ForeColor =
            _entry.Status == DownloadStatus.Interrupted ? Color.FromArgb(0xB4, 0x23, 0x18) : Theme.Shield;

        private void UpdateValues()
        {
            _fileName.Text = _entry.FileName;
            _source.Text = _entry.SourceUrl;
            var percent = GetPercent(_entry);
            var indeterminate = percent < 0 && _entry.Status == DownloadStatus.InProgress;
            var style = indeterminate ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            if (_progress.Style != style) _progress.Style = style;
            _progress.MarqueeAnimationSpeed = indeterminate ? 30 : 0;
            _progress.Value = Math.Max(0, percent);
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
            var width = Math.Max(0, ClientSize.Width - Padding.Horizontal);
            var compact = width < 520;
            var statusWidth = compact ? width : Math.Min(290, width / 2);
            _fileName.Location = new Point(Padding.Left, Padding.Top);
            _fileName.Size = new Size(compact ? width : Math.Max(0, width - statusWidth - 12), 22);
            _status.Location = compact
                ? new Point(Padding.Left, Padding.Top + 46)
                : new Point(Padding.Left + width - statusWidth, Padding.Top);
            _status.Size = new Size(statusWidth, 22);
            _status.TextAlign = compact ? ContentAlignment.MiddleLeft : ContentAlignment.MiddleRight;
            _source.Location = new Point(Padding.Left, Padding.Top + 24);
            _source.Size = new Size(width, 18);
            _progress.Location = new Point(Padding.Left, Padding.Top + (compact ? 73 : 49));
            _progress.Size = new Size(width, 14);
            Height = compact ? 116 : 92;
        }

        private static int GetPercent(DownloadEntry entry)
        {
            if (entry.Status == DownloadStatus.Completed) return 100;
            if (entry.TotalBytes <= 0) return -1;
            return (int)Math.Clamp(entry.BytesReceived * 100.0 / entry.TotalBytes, 0, 100);
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
