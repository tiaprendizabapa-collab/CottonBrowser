using System.Diagnostics;
using Microsoft.Web.WebView2.Core;

namespace LeanBrowser;

internal sealed class PerformancePanel : Form
{
    private readonly Func<CoreWebView2Environment?> _environment;
    private readonly Func<BrowserTab[]> _tabs;
    private readonly Label _summary = new() { Dock = DockStyle.Top, Height = 70, Padding = new Padding(12), AutoEllipsis = true };
    private readonly ListView _list = new() { Dock = DockStyle.Fill, FullRowSelect = true, MultiSelect = false, View = View.Details, HideSelection = false };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
    private readonly Dictionary<int, TimeSpan> _cpu = new();
    private long _sampleAt;
    private bool _working;
    public PerformancePanel(Func<CoreWebView2Environment?> environment, Func<BrowserTab[]> tabs,
        Func<BrowserTab, Task<bool>> suspend, Action<BrowserTab> resume, Action<BrowserTab> close)
    {
        _environment = environment; _tabs = tabs;
        Text = "Desempenho · CottonBrowser"; ClientSize = new Size(760, 470); MinimumSize = new Size(500, 330);
        StartPosition = FormStartPosition.CenterParent; BackColor = Theme.Chrome; ForeColor = Theme.Ink; Font = new Font(Theme.UiFont, 10);
        _list.Columns.Add("Aba", 310); _list.Columns.Add("Estado", 130); _list.Columns.Add("Site", 270);
        _list.BackColor = Theme.Surface; _list.ForeColor = Theme.Ink;
        var tools = FeatureUi.Tools(); tools.Dock = DockStyle.Bottom;
        var pause = FeatureUi.Button("Suspender selecionada", () => {});
        pause.Click += async (_, _) =>
        {
            if (_working || Selected() is not { } tab) return;
            _working = true; pause.Enabled = false;
            try
            {
                if (!await suspend(tab) && !IsDisposed) MessageBox.Show(this,
                    "Esta aba está ativa, reproduz mídia, tem alterações pendentes ou está protegida pela economia de memória. Selecione uma aba inativa.");
                if (!IsDisposed) RefreshData();
            }
            finally { _working = false; if (!IsDisposed) pause.Enabled = true; }
        };
        tools.Controls.Add(pause); tools.Controls.Add(FeatureUi.Button("Retomar", () => { if (Selected() is { } tab) { resume(tab); RefreshData(); } }));
        tools.Controls.Add(FeatureUi.Button("Fechar aba", () => { if (Selected() is { } tab) { close(tab); if (!IsDisposed) RefreshData(); } }));
        tools.Controls.Add(FeatureUi.Note("Processos compartilhados: memória e CPU são mostradas para o navegador, sem atribuir valores imprecisos a cada aba."));
        FeatureUi.WrapNotes(tools);
        Controls.Add(_list); Controls.Add(_summary); Controls.Add(tools);
        _timer.Tick += (_, _) => RefreshData(); Shown += (_, _) => { RefreshData(); _timer.Start(); };
        FormClosed += (_, _) => _timer.Dispose();
    }
    private BrowserTab? Selected() => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as BrowserTab : null;
    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose(); base.Dispose(disposing);
    }
    internal void RefreshData()
    {
        var pids = new HashSet<int> { Environment.ProcessId };
        try { foreach (var info in _environment()?.GetProcessInfos() ?? []) pids.Add((int)info.ProcessId); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        long memory = 0; double cpuDelta = 0; var nextCpu = new Dictionary<int, TimeSpan>();
        foreach (var pid in pids)
        {
            try
            {
                using var process = Process.GetProcessById(pid); memory += process.WorkingSet64; var current = process.TotalProcessorTime;
                nextCpu[pid] = current; if (_cpu.TryGetValue(pid, out var previous)) cpuDelta += Math.Max(0, (current - previous).TotalMilliseconds);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
        var now = Environment.TickCount64; var elapsed = now - _sampleAt;
        var usage = _sampleAt == 0 || elapsed <= 0 ? "calculando…" : $"{Math.Clamp(cpuDelta / elapsed / Environment.ProcessorCount * 100, 0, 100):F1}%";
        _sampleAt = now; _cpu.Clear(); foreach (var pair in nextCpu) _cpu.Add(pair.Key, pair.Value);
        var tabs = _tabs().Where(t => !t.IsDisposed).ToArray();
        _summary.Text = $"Memória: {memory / 1024d / 1024:F0} MB     CPU: {usage}     Processos: {nextCpu.Count}\nAbas desta janela: {tabs.Length} · Em repouso: {tabs.Count(t => t.IsSuspended)}";
        var selected = Selected(); _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (var tab in tabs)
            {
                string state;
                try
                {
                    var core = tab.Web.CoreWebView2;
                    state = tab.IsFloatingVideo ? "Vídeo flutuante" : tab.IsSuspended ? "Em repouso" : tab.Loading ? "Carregando" : core?.IsDocumentPlayingAudio == true ? "Reproduzindo áudio" : "Ativa";
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { state = "Indisponível"; }
                var host = Uri.TryCreate(tab.LastKnownUrl, UriKind.Absolute, out var uri) ? uri.Host : "";
                var row = new ListViewItem([tab.IsPrivate ? "Aba anônima" : tab.Text, state, tab.IsPrivate ? "Privado" : host]) { Tag = tab };
                _list.Items.Add(row); if (tab == selected) row.Selected = true;
            }
        }
        finally { _list.EndUpdate(); }
    }
}
