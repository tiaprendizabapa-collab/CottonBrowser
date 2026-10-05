using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace LeanBrowser;

internal static class PageCapture
{
    public static async Task<byte[]> VisibleAsync(CoreWebView2 core)
    {
        using var stream = new MemoryStream(); await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream); return stream.ToArray();
    }
    public static async Task<byte[]> FullAsync(CoreWebView2 core)
    {
        using var metrics = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Page.getLayoutMetrics", "{}"));
        var content = metrics.RootElement.GetProperty("cssContentSize");
        var width = content.GetProperty("width").GetDouble(); var height = content.GetProperty("height").GetDouble();
        if (width <= 0 || height <= 0 || width * height > 40_000_000 || width > 16384 || height > 32768)
            throw new InvalidOperationException("A página é muito grande para uma única imagem. Capture a área visível ou uma seleção.");
        var options = JsonSerializer.Serialize(new { format = "png", captureBeyondViewport = true, fromSurface = true, clip = new { x = 0, y = 0, width, height, scale = 1 } });
        using var capture = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", options));
        return Convert.FromBase64String(capture.RootElement.GetProperty("data").GetString()!);
    }
}

internal sealed class CapturePreviewDialog : Form
{
    private readonly Bitmap _original;
    private Bitmap? _selection;
    private readonly PictureBox _preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(30, 30, 46) };
    private Point? _start;
    private Rectangle _rectangle;
    private readonly bool _canSelect;
    public CapturePreviewDialog(byte[] png, bool selectArea)
    {
        using var stream = new MemoryStream(png); using var image = Image.FromStream(stream); _original = new Bitmap(image);
        _canSelect = selectArea; Text = selectArea ? "Arraste para selecionar uma área" : "Captura de página";
        var workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        ClientSize = new Size(Math.Min(900, workArea.Width - 40), Math.Min(650, workArea.Height - 80));
        MinimumSize = new Size(520, 360); StartPosition = FormStartPosition.CenterParent;
        _preview.Image = _original;
        var tools = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, Padding = new Padding(4) };
        foreach (var label in new[] { "Salvar imagem…", "Copiar imagem", "Refazer seleção", "Fechar" })
        {
            var button = new Button { Text = label, AutoSize = true };
            button.Click += (_, _) =>
            {
                try
                {
                    if (label == "Fechar") { Close(); return; }
                    if (label == "Refazer seleção") { _preview.Image = _original; _selection?.Dispose(); _selection = null; _rectangle = Rectangle.Empty; _preview.Invalidate(); return; }
                    if (_canSelect && _selection is null) { MessageBox.Show(this, "Arraste sobre a imagem para selecionar a área."); return; }
                    var selected = _selection ?? _original;
                    if (label == "Copiar imagem") Clipboard.SetImage(selected);
                    else { using var save = new SaveFileDialog { Filter = "Imagem PNG|*.png", FileName = "CottonBrowser-Captura.png" }; if (save.ShowDialog(this) == DialogResult.OK) selected.Save(save.FileName, System.Drawing.Imaging.ImageFormat.Png); }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException) { UiDialogs.Error(this, ex); }
            };
            tools.Controls.Add(button);
        }
        _preview.MouseDown += (_, e) => { if (!_canSelect || _selection is not null || e.Button != MouseButtons.Left) return; _start = e.Location; _preview.Capture = true; };
        _preview.MouseMove += (_, e) => { if (_start is { } start) { _rectangle = Rectangle.FromLTRB(Math.Min(start.X, e.X), Math.Min(start.Y, e.Y), Math.Max(start.X, e.X), Math.Max(start.Y, e.Y)); _preview.Invalidate(); } };
        _preview.Paint += (_, e) => { if (_start is not null) { using var pen = new Pen(Color.Orange, 2); e.Graphics.DrawRectangle(pen, _rectangle); } };
        _preview.MouseUp += (_, _) =>
        {
            if (_start is null) return; _start = null; _preview.Capture = false;
            var scale = Math.Min((double)_preview.Width / _original.Width, (double)_preview.Height / _original.Height);
            var left = (_preview.Width - _original.Width * scale) / 2; var top = (_preview.Height - _original.Height * scale) / 2;
            var area = Rectangle.Intersect(new Rectangle(0, 0, _original.Width, _original.Height), new Rectangle((int)((_rectangle.X - left) / scale), (int)((_rectangle.Y - top) / scale), (int)(_rectangle.Width / scale), (int)(_rectangle.Height / scale)));
            if (area.Width > 1 && area.Height > 1) { _selection = _original.Clone(area, _original.PixelFormat); _preview.Image = _selection; }
            _preview.Invalidate();
        };
        Controls.Add(_preview); Controls.Add(tools);
    }
    protected override void Dispose(bool disposing) { if (disposing) { _preview.Image = null; _selection?.Dispose(); _original.Dispose(); } base.Dispose(disposing); }
}
