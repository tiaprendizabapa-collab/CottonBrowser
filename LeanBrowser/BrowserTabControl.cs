using System.Drawing.Drawing2D;

namespace LeanBrowser;

/// <summary>Faixa horizontal de abas, separada do conteúdo WebView2.</summary>
public sealed class BrowserTabControl : TabControl
{
    private const string TabDragFormat = "CottonBrowser.Tab";
    private readonly Dictionary<TabPage, TabHeader> _headers = new();
    private readonly Dictionary<BrowserTab, Action> _organizationHandlers = new();
    private TabPage? _reorderingTab;
    private bool _updatingOrganization;
    private readonly Button _searchTabsButton = new()
    {
        Text = "", AccessibleName = "Pesquisar abas", Size = new Size(32, 36),
        Margin = new Padding(1, 4, 1, 0), FlatStyle = FlatStyle.Flat,
        BackColor = Theme.Chrome, ForeColor = Theme.Ink, Cursor = Cursors.Hand,
        UseVisualStyleBackColor = false
    };
    private readonly Button _newTabButton = new()
    {
        Text = "", AccessibleName = "Nova aba", Size = new Size(36, 36),
        Margin = new Padding(4, 4, 6, 0), FlatStyle = FlatStyle.Flat,
        BackColor = Theme.Chrome, ForeColor = Theme.Ink, Cursor = Cursors.Hand,
        Font = new Font(Theme.UiFont, 15f, FontStyle.Regular),
        TextAlign = ContentAlignment.MiddleCenter, UseVisualStyleBackColor = false
    };
    private readonly Label _brandLabel = new()
    {
        Text = "", AutoSize = false, Height = 38, Width = 36,
        TextAlign = ContentAlignment.MiddleCenter, ImageAlign = ContentAlignment.MiddleCenter,
        Padding = new Padding(0),
        BackColor = Theme.Chrome
    };
    private Image? _brandIcon;
    private readonly ToolTip _toolTip = new();
    public FlowLayoutPanel HeaderStrip { get; } = new()
    {
        Height = 50, WrapContents = false, FlowDirection = FlowDirection.LeftToRight,
        AutoScroll = true, AllowDrop = true, BackColor = Theme.Chrome, Padding = new Padding(4, 3, 8, 3)
    };
    public int BrowserTabCount => TabPages.OfType<BrowserTab>().Count();
    public event Action<BrowserTab>? CloseRequested;
    public event Action<TabPage>? AuxiliaryCloseRequested;
    public event Action? NewTabRequested;
    public event Action? BrandClicked;
    public event Action<BrowserTab>? TabDraggedOutside;
    public event Action? OrganizationChanged;

    public void SearchTabs()
    {
        using var dialog = new TabSearchDialog(this);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK || dialog.SelectedPage is not { } page
            || !TabPages.Contains(page)) return;
        SelectedTab = page;
        if (page is BrowserTab tab) tab.Web.Focus();
        else page.Focus();
    }

    /// <summary>Atualiza título, estado de áudio e suspensão sem recriar a página.</summary>
    public void RefreshTab(BrowserTab tab)
    {
        if (!_headers.TryGetValue(tab, out var header)) return;
        header.RefreshMetadata();
        UpdateToolTip(tab, header);
    }

    /// <summary>Mantém as abas fixadas no início e os membros de cada grupo juntos.</summary>
    public void RestoreOrganizationOrder()
    {
        var source = TabPages.Cast<TabPage>().ToArray();
        var ordered = source.Where(page => page is BrowserTab { IsPinned: true }).ToList();
        var included = new HashSet<TabPage>(ordered);
        foreach (var page in source)
        {
            if (!included.Add(page)) continue;
            ordered.Add(page);
            if (page is not BrowserTab { IsPinned: false } tab || string.IsNullOrWhiteSpace(tab.GroupName)) continue;
            foreach (var member in source.OfType<BrowserTab>().Where(candidate => !candidate.IsPinned
                && candidate.IsPrivate == tab.IsPrivate
                && string.Equals(candidate.GroupName, tab.GroupName, StringComparison.Ordinal)))
            {
                if (included.Add(member)) ordered.Add(member);
            }
        }
        if (source.SequenceEqual(ordered)) return;
        var selected = SelectedTab;
        HeaderStrip.SuspendLayout();
        try
        {
            for (var index = 0; index < ordered.Count; index++)
            {
                var page = ordered[index];
                if (TabPages[index] == page) continue;
                _reorderingTab = page;
                TabPages.Remove(page);
                TabPages.Insert(index, page);
            }
            if (selected is not null) SelectedTab = selected;
            SyncHeaderOrder();
        }
        finally
        {
            _reorderingTab = null;
            HeaderStrip.ResumeLayout();
        }
        OnSelectedIndexChanged(EventArgs.Empty);
    }

    private void SyncHeaderOrder()
    {
        for (var index = 0; index < TabPages.Count; index++)
            HeaderStrip.Controls.SetChildIndex(_headers[TabPages[index]], index + 1);
        HeaderStrip.Controls.SetChildIndex(_searchTabsButton, HeaderStrip.Controls.Count - 2);
        HeaderStrip.Controls.SetChildIndex(_newTabButton, HeaderStrip.Controls.Count - 1);
    }

    private void OnOrganizationChanged(BrowserTab tab)
    {
        RefreshTab(tab);
        if (_updatingOrganization) return;
        RestoreOrganizationOrder();
        OrganizationChanged?.Invoke();
    }

    private void UpdateToolTip(TabPage page, TabHeader header)
    {
        var status = page is BrowserTab tab ? string.Join(" • ", new[]
        {
            tab.IsPinned ? "Fixada" : null,
            tab.GroupName,
            tab.IsPrivate ? "Anônima" : null,
            tab.IsSuspended ? "Suspensa para economizar memória" : null,
            header.AudioMuted ? "Áudio silenciado" : header.AudioPlaying ? "Reproduzindo áudio" : null
        }.Where(value => !string.IsNullOrWhiteSpace(value))) : "";
        _toolTip.SetToolTip(header, $"{page.Text}{(status.Length > 0 ? "\n" + status : "")}\n"
            + "Arraste para mudar a ordem ou abrir em outra janela • Clique direito para organizar");
        header.AccessibleName = page.Text;
        header.AccessibleDescription = status;
    }

    // Recebe a propriedade da imagem; o cabeçalho anterior é libertado.
    public void SetFavicon(TabPage tab, Image? favicon)
    {
        if (_headers.TryGetValue(tab, out var header)) header.SetFavicon(favicon);
        else favicon?.Dispose();
    }

    public void ApplyTheme()
    {
        HeaderStrip.BackColor = Theme.Chrome;
        _brandLabel.BackColor = Theme.Chrome;
        _newTabButton.BackColor = Theme.Chrome;
        _newTabButton.ForeColor = Theme.Ink;
        _newTabButton.FlatAppearance.MouseOverBackColor = Theme.SurfaceHot;
        _searchTabsButton.BackColor = Theme.Chrome;
        _searchTabsButton.ForeColor = Theme.Ink;
        _searchTabsButton.FlatAppearance.MouseOverBackColor = Theme.SurfaceHot;
        HeaderStrip.Invalidate(true);
        foreach (var header in _headers.Values) header.Invalidate();
    }

    public BrowserTabControl()
    {
        // O TabControl mantém as páginas; HeaderStrip exibe as abas acima do conteúdo.
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(1, 1);
        Appearance = TabAppearance.FlatButtons;
        _newTabButton.FlatAppearance.BorderSize = 0;
        _newTabButton.FlatAppearance.MouseOverBackColor = Theme.SurfaceHot;
        _newTabButton.Paint += (_, e) =>
        {
            var center = new Point(_newTabButton.Width / 2, _newTabButton.Height / 2);
            var arm = Math.Max(5, _newTabButton.LogicalToDeviceUnits(6));
            using var pen = new Pen(Theme.Ink, Math.Max(1.4f, _newTabButton.DeviceDpi / 72f));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawLine(pen, center.X - arm, center.Y, center.X + arm, center.Y);
            e.Graphics.DrawLine(pen, center.X, center.Y - arm, center.X, center.Y + arm);
        };
        _newTabButton.Click += (_, _) => NewTabRequested?.Invoke();
        _searchTabsButton.FlatAppearance.BorderSize = 0;
        _searchTabsButton.FlatAppearance.MouseOverBackColor = Theme.SurfaceHot;
        _searchTabsButton.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var size = _searchTabsButton.LogicalToDeviceUnits(10);
            var left = (_searchTabsButton.Width - size) / 2 - _searchTabsButton.LogicalToDeviceUnits(2);
            var top = (_searchTabsButton.Height - size) / 2 - _searchTabsButton.LogicalToDeviceUnits(2);
            using var pen = new Pen(Theme.Ink, Math.Max(1.5f, _searchTabsButton.DeviceDpi / 64f));
            e.Graphics.DrawEllipse(pen, left, top, size, size);
            e.Graphics.DrawLine(pen, left + size - 1, top + size - 1,
                left + size + _searchTabsButton.LogicalToDeviceUnits(5), top + size + _searchTabsButton.LogicalToDeviceUnits(5));
        };
        _searchTabsButton.Click += (_, _) => SearchTabs();
        _toolTip.SetToolTip(_searchTabsButton, "Pesquisar abas (Ctrl+Shift+E)");
        _toolTip.SetToolTip(_newTabButton, "Nova aba (Ctrl+T)");
        HeaderStrip.Controls.Add(_brandLabel);
        HeaderStrip.Controls.Add(_searchTabsButton);
        HeaderStrip.Controls.Add(_newTabButton);
        HeaderStrip.Paint += PaintHeaderEdge;
        HeaderStrip.DragEnter += AcceptTabDrag;
        HeaderStrip.DragOver += AcceptTabDrag;
        _brandLabel.Cursor = Cursors.Hand;
        _brandLabel.Click += (_, _) => BrandClicked?.Invoke();
        _toolTip.SetToolTip(_brandLabel, "CottonBrowser — clique 5 vezes rapidamente");
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon is not null)
            {
                _brandIcon = new Bitmap(24, 24);
                using var graphics = Graphics.FromImage(_brandIcon);
                graphics.DrawIcon(icon, new Rectangle(0, 0, 24, 24));
                _brandLabel.Image = _brandIcon;
            }
        }
        catch { }
    }

    private static void AcceptTabDrag(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(TabDragFormat) == true
            ? DragDropEffects.Move : DragDropEffects.None;
    }

    private void CompleteTabDrag(TabPage tab, Point screenPosition)
    {
        if (TabPages.IndexOf(tab) < 0) return;

        // Soltar abaixo da faixa de abas (inclusive sobre a página) abre outra janela.
        if (!HeaderStrip.RectangleToScreen(HeaderStrip.ClientRectangle).Contains(screenPosition))
        {
            if (tab is BrowserTab browserTab) TabDraggedOutside?.Invoke(browserTab);
            return;
        }

        var targetIndex = 0;
        foreach (TabPage other in TabPages)
        {
            if (other == tab) continue;
            var bounds = _headers[other].RectangleToScreen(_headers[other].ClientRectangle);
            if (screenPosition.X < bounds.Left + bounds.Width / 2) break;
            targetIndex++;
        }
        if (targetIndex == TabPages.IndexOf(tab)) return;

        HeaderStrip.SuspendLayout();
        _reorderingTab = tab;
        try
        {
            TabPages.Remove(tab);
            TabPages.Insert(targetIndex, tab);
            SelectedTab = tab;
            SyncHeaderOrder();
        }
        finally
        {
            _reorderingTab = null;
            HeaderStrip.ResumeLayout();
        }
        RestoreOrganizationOrder();
        OnSelectedIndexChanged(EventArgs.Empty);
        OrganizationChanged?.Invoke();
    }

    private static void PaintHeaderEdge(object? sender, PaintEventArgs e)
    {
        if (sender is not Control control || control.ClientSize.Height < 3 || control.ClientSize.Width < 1) return;
        var edge = new Rectangle(0, control.ClientSize.Height - 3, control.ClientSize.Width, 3);
        using var brush = new LinearGradientBrush(edge,
            Color.FromArgb(0, Color.Black), Color.FromArgb(Theme.IsDark ? 48 : 28, Color.Black),
            LinearGradientMode.Vertical);
        e.Graphics.FillRectangle(brush, edge);
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control is not TabPage tab) return;
        if (tab == _reorderingTab) return;
        var header = new TabHeader(this, tab);
        _headers.Add(tab, header);
        HeaderStrip.Controls.Add(header);
        if (tab is BrowserTab browserTab)
        {
            Action changed = () => OnOrganizationChanged(browserTab);
            _organizationHandlers.Add(browserTab, changed);
            browserTab.OrganizationChanged += changed;
        }
        SyncHeaderOrder();
        tab.TextChanged += OnTabTextChanged;
        OnTabTextChanged(tab, EventArgs.Empty);
        RestoreOrganizationOrder();
    }

    protected override void OnControlRemoved(ControlEventArgs e)
    {
        base.OnControlRemoved(e);
        if (e.Control is not TabPage tab) return;
        if (tab == _reorderingTab) return;
        if (tab is BrowserTab browserTab && _organizationHandlers.Remove(browserTab, out var changed))
            browserTab.OrganizationChanged -= changed;
        tab.TextChanged -= OnTabTextChanged;
        if (_headers.Remove(tab, out var header)) header.Dispose();
    }

    private void OnTabTextChanged(object? sender, EventArgs e)
    {
        if (sender is TabPage tab && _headers.TryGetValue(tab, out var header))
        {
            UpdateToolTip(tab, header);
            header.Invalidate();
        }
    }

    private void ShowTabMenu(TabPage page, Control header, Point location)
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false, BackColor = Theme.Surface, ForeColor = Theme.Ink };
        if (page is BrowserTab tab)
        {
            menu.Items.Add(tab.IsPinned ? "Desafixar aba" : "Fixar aba", null, (_, _) => tab.IsPinned = !tab.IsPinned);
            menu.Items.Add("Novo grupo...", null, (_, _) => EditGroup(tab, editExisting: false));
            var groups = TabPages.OfType<BrowserTab>().Where(candidate => candidate.IsPrivate == tab.IsPrivate
                    && !string.IsNullOrWhiteSpace(candidate.GroupName))
                .GroupBy(candidate => candidate.GroupName!, StringComparer.Ordinal).ToArray();
            if (groups.Length > 0)
            {
                var move = new ToolStripMenuItem("Mover para grupo");
                foreach (var group in groups)
                {
                    var groupName = group.Key;
                    var color = group.First().GroupColorArgb;
                    var item = new ToolStripMenuItem(groupName.Replace("&", "&&")) { Checked = tab.GroupName == groupName };
                    item.Click += (_, _) => ApplyOrganization(() => { tab.GroupName = groupName; tab.GroupColorArgb = color; });
                    move.DropDownItems.Add(item);
                }
                menu.Items.Add(move);
            }
            if (!string.IsNullOrWhiteSpace(tab.GroupName))
            {
                menu.Items.Add("Editar grupo...", null, (_, _) => EditGroup(tab, editExisting: true));
                menu.Items.Add("Remover aba do grupo", null, (_, _) => ApplyOrganization(() =>
                {
                    tab.GroupName = null;
                    tab.GroupColorArgb = 0;
                }));
                menu.Items.Add("Desagrupar todas as abas", null, (_, _) =>
                {
                    var members = GroupMembers(tab);
                    ApplyOrganization(() =>
                    {
                        foreach (var member in members) { member.GroupName = null; member.GroupColorArgb = 0; }
                    });
                });
            }
            if (TryGetAudio(tab, out _, out var muted))
                menu.Items.Add(muted ? "Ativar som da aba" : "Silenciar aba", null, (_, _) => ToggleMute(tab));
            menu.Items.Add(new ToolStripSeparator());
        }
        menu.Items.Add("Pesquisar abas...", null, (_, _) => SearchTabs());
        menu.Items.Add("Fechar aba", null, (_, _) =>
        {
            if (page is BrowserTab browserTab) CloseRequested?.Invoke(browserTab);
            else AuxiliaryCloseRequested?.Invoke(page);
        });
        menu.Closed += (_, _) => menu.Dispose();
        menu.Show(header, location);
    }

    private BrowserTab[] GroupMembers(BrowserTab tab) => TabPages.OfType<BrowserTab>()
        .Where(candidate => candidate.IsPrivate == tab.IsPrivate && candidate.GroupName == tab.GroupName).ToArray();

    private void EditGroup(BrowserTab tab, bool editExisting)
    {
        using var dialog = new TabGroupDialog(editExisting ? tab.GroupName : null,
            editExisting ? tab.GroupColorArgb : 0);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK || tab.IsDisposed) return;
        var members = editExisting ? GroupMembers(tab) : [tab];
        // Uma renomeação para um grupo existente reúne os membros sob a mesma cor.
        var existing = TabPages.OfType<BrowserTab>().FirstOrDefault(candidate => candidate.IsPrivate == tab.IsPrivate
            && !members.Contains(candidate) && candidate.GroupName == dialog.GroupName);
        var color = existing?.GroupColorArgb ?? dialog.GroupColorArgb;
        ApplyOrganization(() =>
        {
            foreach (var member in members) { member.GroupName = dialog.GroupName; member.GroupColorArgb = color; }
        });
    }

    private void ApplyOrganization(Action update)
    {
        _updatingOrganization = true;
        try { update(); }
        finally { _updatingOrganization = false; }
        RestoreOrganizationOrder();
        OrganizationChanged?.Invoke();
    }

    private static bool TryGetAudio(BrowserTab tab, out bool playing, out bool muted)
    {
        playing = muted = false;
        if (tab.IsDisposed || tab.Web.IsDisposed || tab.Web.CoreWebView2 is not { } core) return false;
        try { playing = core.IsDocumentPlayingAudio; muted = core.IsMuted; return true; }
        catch (InvalidOperationException) { return false; }
        catch (System.Runtime.InteropServices.COMException) { return false; }
    }

    private void ToggleMute(BrowserTab tab)
    {
        if (!TryGetAudio(tab, out _, out var muted)) return;
        try { tab.Web.CoreWebView2.IsMuted = !muted; RefreshTab(tab); }
        catch (InvalidOperationException) { }
        catch (System.Runtime.InteropServices.COMException) { }
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        if (_reorderingTab is not null) return;
        base.OnSelectedIndexChanged(e);
        _toolTip.SetToolTip(_newTabButton, SelectedTab is BrowserTab { IsPrivate: true }
            ? "Nova guia anônima (Ctrl+T)" : "Nova aba (Ctrl+T)");
        foreach (var header in _headers.Values) header.Invalidate();
        if (SelectedTab is { } tab && _headers.TryGetValue(tab, out var selected))
            HeaderStrip.ScrollControlIntoView(TabPages.IndexOf(tab) == TabCount - 1 ? _newTabButton : selected);
    }

    protected override void WndProc(ref Message m)
    {
        // TCM_ADJUSTRECT: as páginas ocupam também a área do cabeçalho nativo,
        // pois o cabeçalho visível está em HeaderStrip.
        if (m.Msg == 0x1328 && !DesignMode)
        {
            m.Result = (IntPtr)1;
            return;
        }
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
            HeaderStrip.Dispose();
            _brandIcon?.Dispose();
            foreach (var subscription in _organizationHandlers)
                subscription.Key.OrganizationChanged -= subscription.Value;
            _organizationHandlers.Clear();
        }
        base.Dispose(disposing);
    }

    private sealed class TabHeader : Control
    {
        private readonly BrowserTabControl _owner;
        private readonly TabPage _tab;
        private Image? _favicon;
        private bool _hoverClose;
        private bool _hoverAudio;
        private bool _dragCandidate;
        private bool _dragCanceled;
        private Point _dragOrigin;
        private bool IsPinned => _tab is BrowserTab { IsPinned: true };
        public bool AudioPlaying { get; private set; }
        public bool AudioMuted { get; private set; }
        private bool HasAudioControl => AudioPlaying || AudioMuted;
        private Rectangle CloseBounds => IsPinned ? Rectangle.Empty : new(Width - LogicalToDeviceUnits(30),
            (Height - LogicalToDeviceUnits(24)) / 2, LogicalToDeviceUnits(24), LogicalToDeviceUnits(24));
        private Rectangle AudioBounds => !HasAudioControl ? Rectangle.Empty : new(
            IsPinned ? Width - LogicalToDeviceUnits(26) : CloseBounds.Left - LogicalToDeviceUnits(24),
            (Height - LogicalToDeviceUnits(24)) / 2, LogicalToDeviceUnits(24), LogicalToDeviceUnits(24));

        public TabHeader(BrowserTabControl owner, TabPage tab)
        {
            _owner = owner;
            _tab = tab;
            Size = new Size(214, 38);
            Margin = new Padding(0, 3, 6, 2);
            AccessibleName = tab.Text;
            AccessibleRole = AccessibleRole.PageTab;
            TabStop = true;
            AllowDrop = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            RefreshMetadata();
        }

        public void RefreshMetadata()
        {
            AudioPlaying = AudioMuted = false;
            if (_tab is BrowserTab tab && TryGetAudio(tab, out var playing, out var muted))
            { AudioPlaying = playing; AudioMuted = muted; }
            Width = LogicalToDeviceUnits(IsPinned ? (HasAudioControl ? 74 : 48) : 228);
            Cursor = Cursors.Hand;
            Invalidate();
        }

        protected override void OnDragEnter(DragEventArgs drgevent)
        {
            base.OnDragEnter(drgevent);
            AcceptTabDrag(this, drgevent);
        }

        protected override void OnDragOver(DragEventArgs drgevent)
        {
            base.OnDragOver(drgevent);
            AcceptTabDrag(this, drgevent);
        }

        protected override void OnQueryContinueDrag(QueryContinueDragEventArgs qcdevent)
        {
            if (qcdevent.EscapePressed) _dragCanceled = true;
            base.OnQueryContinueDrag(qcdevent);
        }

        protected override void OnGiveFeedback(GiveFeedbackEventArgs gfbevent)
        {
            gfbevent.UseDefaultCursors = false;
            System.Windows.Forms.Cursor.Current = Cursors.SizeAll;
            base.OnGiveFeedback(gfbevent);
        }

        public void SetFavicon(Image? favicon)
        {
            var previous = _favicon;
            _favicon = favicon;
            previous?.Dispose();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var selected = _owner.SelectedTab == _tab;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            e.Graphics.Clear(Theme.Chrome);
            var panel = new Rectangle(0, 0, Width - 1, Height - 1);
            using var panelPath = Draw.RoundedRect(panel, LogicalToDeviceUnits(11));
            using var panelBrush = new SolidBrush(selected ? Theme.Surface : Theme.SurfaceHot);
            e.Graphics.FillPath(panelBrush, panelPath);
            var iconSize = LogicalToDeviceUnits(16);
            var iconLeft = IsPinned ? LogicalToDeviceUnits(16) : LogicalToDeviceUnits(12);
            var titleLeft = iconLeft + iconSize + LogicalToDeviceUnits(8);
            if (_favicon is not null)
            {
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.DrawImage(_favicon,
                    new Rectangle(iconLeft, (Height - iconSize) / 2, iconSize, iconSize));
            }
            else if (IsPinned)
            {
                using var fallback = new Pen(Theme.InkMuted, Math.Max(1.3f, DeviceDpi / 80f));
                e.Graphics.DrawEllipse(fallback, iconLeft, (Height - iconSize) / 2, iconSize, iconSize);
                e.Graphics.DrawLine(fallback, iconLeft, Height / 2, iconLeft + iconSize, Height / 2);
                e.Graphics.DrawEllipse(fallback, iconLeft + iconSize / 3, (Height - iconSize) / 2, iconSize / 3, iconSize);
            }
            var grouped = _tab is BrowserTab browserTab && !string.IsNullOrWhiteSpace(browserTab.GroupName);
            var titleRight = HasAudioControl ? AudioBounds.Left : CloseBounds.Left;
            var title = new Rectangle(titleLeft, grouped ? LogicalToDeviceUnits(1) : 0,
                Math.Max(0, titleRight - titleLeft - LogicalToDeviceUnits(4)), grouped ? Height * 2 / 3 : Height);
            if (!IsPinned)
                TextRenderer.DrawText(e.Graphics, _tab.Text, Font, title, Theme.Ink,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (grouped && _tab is BrowserTab groupTab)
            {
                var color = groupTab.GroupColorArgb == 0 ? Theme.Accent : Color.FromArgb(groupTab.GroupColorArgb);
                using var groupAccent = new SolidBrush(color);
                e.Graphics.FillRectangle(groupAccent, LogicalToDeviceUnits(6), Height - LogicalToDeviceUnits(4),
                    Width - LogicalToDeviceUnits(12), LogicalToDeviceUnits(3));
                if (!IsPinned)
                {
                    var groupBounds = new Rectangle(titleLeft, Height * 2 / 3 - LogicalToDeviceUnits(3), title.Width, Height / 3);
                    using var groupFont = new Font(Font.FontFamily, Math.Max(7.5f, Font.Size - 1.5f), FontStyle.Regular);
                    TextRenderer.DrawText(e.Graphics, groupTab.GroupName, groupFont, groupBounds, Theme.InkMuted,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                }
            }
            using var border = new Pen(Theme.Divider, 1f);
            e.Graphics.DrawPath(border, panelPath);
            if (selected)
            {
                using var accent = new SolidBrush(Theme.Accent);
                e.Graphics.FillRectangle(accent, LogicalToDeviceUnits(12), 1,
                    Math.Max(0, Width - LogicalToDeviceUnits(24)), LogicalToDeviceUnits(3));
            }
            if (_tab is BrowserTab { IsSuspended: true })
            {
                using var sleepFont = new Font(Font.FontFamily, 7.5f, FontStyle.Bold);
                TextRenderer.DrawText(e.Graphics, "z", sleepFont, new Rectangle(iconLeft + LogicalToDeviceUnits(10),
                    LogicalToDeviceUnits(2), LogicalToDeviceUnits(12), LogicalToDeviceUnits(14)), Theme.InkMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            if (HasAudioControl) PaintAudio(e.Graphics);
            if (!IsPinned)
            {
                var close = CloseBounds;
                if (_hoverClose)
                {
                    using var halo = new SolidBrush(Theme.SurfaceHot);
                    e.Graphics.FillEllipse(halo, close);
                }
                using var pen = new Pen(_hoverClose ? Theme.Ink : Theme.InkMuted,
                    Math.Max(1.6f, DeviceDpi / 60f));
                var inset = close.Width / 3;
                e.Graphics.DrawLine(pen, close.Left + inset, close.Top + inset, close.Right - inset, close.Bottom - inset);
                e.Graphics.DrawLine(pen, close.Right - inset, close.Top + inset, close.Left + inset, close.Bottom - inset);
            }
            if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, IsPinned ? panel : title);
        }

        private void PaintAudio(Graphics graphics)
        {
            var bounds = AudioBounds;
            if (_hoverAudio)
            {
                using var halo = new SolidBrush(Theme.SurfaceHot);
                graphics.FillEllipse(halo, bounds);
            }
            var unit = Math.Max(1, LogicalToDeviceUnits(1));
            var x = bounds.Left + 5 * unit;
            var y = bounds.Top + bounds.Height / 2;
            using var pen = new Pen(AudioMuted ? Theme.InkMuted : Theme.Accent, Math.Max(1.4f, DeviceDpi / 72f));
            var speaker = new[] { new Point(x, y - 3 * unit), new Point(x + 3 * unit, y - 3 * unit),
                new Point(x + 7 * unit, y - 6 * unit), new Point(x + 7 * unit, y + 6 * unit),
                new Point(x + 3 * unit, y + 3 * unit), new Point(x, y + 3 * unit) };
            graphics.DrawPolygon(pen, speaker);
            if (AudioMuted)
            {
                graphics.DrawLine(pen, x + 10 * unit, y - 3 * unit, x + 15 * unit, y + 3 * unit);
                graphics.DrawLine(pen, x + 15 * unit, y - 3 * unit, x + 10 * unit, y + 3 * unit);
            }
            else graphics.DrawArc(pen, x + 4 * unit, y - 6 * unit, 10 * unit, 12 * unit, -60, 120);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragCandidate && (Control.MouseButtons & MouseButtons.Left) != 0)
            {
                var dragSize = SystemInformation.DragSize;
                var dragBounds = new Rectangle(_dragOrigin.X - dragSize.Width / 2,
                    _dragOrigin.Y - dragSize.Height / 2, dragSize.Width, dragSize.Height);
                if (!dragBounds.Contains(System.Windows.Forms.Cursor.Position))
                {
                    _dragCandidate = false;
                    _dragCanceled = false;
                    var data = new DataObject(TabDragFormat, "tab");
                    DoDragDrop(data, DragDropEffects.Move);
                    if (!_dragCanceled)
                        _owner.CompleteTabDrag(_tab, System.Windows.Forms.Cursor.Position);
                    return;
                }
            }
            var hover = CloseBounds.Contains(e.Location);
            var audioHover = AudioBounds.Contains(e.Location);
            if (_hoverClose == hover && _hoverAudio == audioHover) return;
            _hoverClose = hover;
            _hoverAudio = audioHover;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverClose = false;
            _hoverAudio = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Middle)
            {
                RequestClose();
                return;
            }
            if (e.Button == MouseButtons.Right)
            {
                _owner.ShowTabMenu(_tab, this, e.Location);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (AudioBounds.Contains(e.Location) && _tab is BrowserTab audioTab)
            {
                _owner.ToggleMute(audioTab);
                return;
            }
            if (CloseBounds.Contains(e.Location))
            {
                RequestClose();
                return;
            }

            _owner.SelectedTab = _tab;
            _dragOrigin = System.Windows.Forms.Cursor.Position;
            _dragCandidate = true;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragCandidate = false;
            base.OnMouseUp(e);
        }

        private void RequestClose()
        {
            if (_tab is BrowserTab tab) _owner.CloseRequested?.Invoke(tab);
            else _owner.AuxiliaryCloseRequested?.Invoke(_tab);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode is Keys.Enter or Keys.Space)
            {
                _owner.SelectedTab = _tab;
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Apps || (e.KeyCode == Keys.F10 && e.Shift))
            {
                _owner.ShowTabMenu(_tab, this, new Point(0, Height));
                e.Handled = e.SuppressKeyPress = true;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _favicon?.Dispose();
            base.Dispose(disposing);
        }
    }
}

