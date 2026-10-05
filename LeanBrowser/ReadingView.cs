using System.Text.Json;

namespace LeanBrowser;

internal sealed class ReadingView : UserControl
{
    private readonly RichTextBox _text = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, DetectUrls = false, AccessibleName = "Texto do modo de leitura" };
    private readonly FlowLayoutPanel _tools = new() { Dock = DockStyle.Top, Height = 48 };
    private float _fontSize = 16;
    private bool _dark;
    private readonly string _originalText;
    private bool _wideSpacing;
    private Font? _readingFont;
    public event Action? ExitRequested;
    public ReadingView(string title, string content)
    {
        Dock = DockStyle.Fill; Padding = new Padding(28); _dark = Theme.IsDark;
        _originalText = title + "\n\n" + content; _text.Text = _originalText;
        foreach (var label in new[] { "A−", "A+", "Espaçamento", "Fundo claro / escuro", "Voltar à página" })
        {
            var button = new Button { Text = label, AutoSize = true, FlatStyle = FlatStyle.Flat, AccessibleName = label };
            button.Click += (_, _) =>
            {
                if (label == "A−") _fontSize = Math.Max(10, _fontSize - 2);
                else if (label == "A+") _fontSize = Math.Min(32, _fontSize + 2);
                else if (label == "Fundo claro / escuro") _dark = !_dark;
                else if (label == "Voltar à página") { ExitRequested?.Invoke(); return; }
                else { _wideSpacing = !_wideSpacing; _text.Text = _wideSpacing ? _originalText.Replace("\n", "\n\n") : _originalText; }
                ApplyColors();
            };
            _tools.Controls.Add(button);
        }
        _tools.AutoSize = true; _tools.WrapContents = true; Controls.Add(_text); Controls.Add(_tools); ApplyColors();
    }
    private void ApplyColors()
    {
        BackColor = _text.BackColor = _tools.BackColor = _dark ? Color.FromArgb(30, 30, 46) : Color.FromArgb(250, 247, 240);
        _text.ForeColor = _dark ? Color.FromArgb(205, 214, 244) : Color.FromArgb(40, 43, 50);
        var previous = _readingFont; _readingFont = new Font("Segoe UI", _fontSize); _text.Font = _readingFont; previous?.Dispose();
        foreach (Control control in _tools.Controls) { control.BackColor = BackColor; control.ForeColor = _text.ForeColor; }
    }
    protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) _readingFont?.Dispose(); }
    internal const string ExtractionScript = """
        (() => {
          const root = document.querySelector('article') || document.querySelector('main') || document.body;
          if (!root) return null;
          const copy = root.cloneNode(true);
          copy.querySelectorAll('script,style,nav,header,footer,aside,form,button,iframe,[role="navigation"],[hidden]').forEach(n => n.remove());
          // textContent is used on a detached clone; block boundaries remain readable.
          copy.querySelectorAll('p,h1,h2,h3,h4,li,blockquote,section,div,br').forEach(n => { n.appendChild(document.createTextNode('\n\n')); });
          const text = (copy.textContent || '').replace(/[\t ]+/g,' ').replace(/\n\s*\n\s*\n/g,'\n\n').trim().slice(0,500000);
          return {title: document.title, text};
        })()
        """;
}
