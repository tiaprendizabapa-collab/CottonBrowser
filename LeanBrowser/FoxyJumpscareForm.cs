using System.Reflection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LeanBrowser;

/// <summary>
/// Reproduz o vídeo do easter egg em um canvas com chroma key. O verde é
/// removido por pixel durante a reprodução, sem alterar a página aberta.
/// </summary>
public sealed class FoxyJumpscareForm : Form
{
    private const string ResourceName = "LeanBrowser.Assets.foxy-jumpscare.mp4";
    private const string AudioResourceName = "LeanBrowser.Assets.foxy-jumpscare.mp3";
    private readonly CoreWebView2Environment _environment;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer _timeout = new() { Interval = 30_000 };

    public FoxyJumpscareForm(CoreWebView2Environment environment)
    {
        _environment = environment;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Black;
        KeyPreview = true;
        MinimumSize = new Size(480, 300);
        Controls.Add(_web);

        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        Shown += async (_, _) => await StartAsync();
        _timeout.Tick += (_, _) => Close();
        FormClosed += (_, _) => _timeout.Dispose();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (Owner is { IsDisposed: false } owner) Bounds = owner.Bounds;
        else Bounds = Screen.FromControl(this).WorkingArea;
        _timeout.Start();
    }

    private async Task StartAsync()
    {
        try
        {
            // O vídeo nasce de um clique no shell, mas isso não é considerado
            // gesto do usuário pelo WebView2 criado depois. Um perfil isolado
            // com autoplay explícito garante que o áudio seja reproduzido sem
            // alterar a política das páginas normais do CottonBrowser.
            var playerData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LeanBrowser", "FoxyWebView2");
            CoreWebView2Environment playerEnvironment;
            try
            {
                Directory.CreateDirectory(playerData);
                var options = new CoreWebView2EnvironmentOptions(
                    "--autoplay-policy=no-user-gesture-required");
                playerEnvironment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null, userDataFolder: playerData, options: options);
            }
            catch
            {
                playerEnvironment = _environment;
            }

            await _web.EnsureCoreWebView2Async(playerEnvironment);
            if (IsDisposed) return;
            var core = _web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.WebMessageReceived += (_, e) =>
            {
                var message = e.TryGetWebMessageAsString();
                if (message is "ended" or "close") BeginInvoke(new Action(Close));
            };

            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
                ?? throw new FileNotFoundException("Vídeo do easter egg ausente.");
            using var audioStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(AudioResourceName)
                ?? throw new FileNotFoundException("Áudio do easter egg ausente.");
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            using var audioMemory = new MemoryStream();
            audioStream.CopyTo(audioMemory);
            core.NavigateToString(BuildHtml(
                Convert.ToBase64String(memory.ToArray()),
                Convert.ToBase64String(audioMemory.ToArray())));
        }
        catch { Close(); }
    }

    private static string BuildHtml(string videoBase64, string audioBase64) => """
<!doctype html>
<html><head><meta charset="utf-8"><style>
html,body { margin:0; width:100%; height:100%; overflow:hidden; background:transparent; }
canvas { display:block; width:100vw; height:100vh; object-fit:contain; background:transparent; }
video { display:none; }
</style></head><body>
<video id="source" autoplay muted playsinline></video>
<audio id="sound" preload="auto"></audio><canvas id="stage"></canvas>
<script>
const video = document.getElementById('source');
const sound = document.getElementById('sound');
const canvas = document.getElementById('stage');
const ctx = canvas.getContext('2d', { willReadFrequently:true });
video.src = 'data:video/mp4;base64,__VIDEO_DATA__';
sound.src = 'data:audio/mpeg;base64,__AUDIO_DATA__';
let audioContext;
function startLoudAudio() {
  try {
    audioContext = new (window.AudioContext || window.webkitAudioContext)();
    const source = audioContext.createMediaElementSource(sound);
    const shaper = audioContext.createWaveShaper();
    const curve = new Float32Array(44100);
    const drive = 90;
    for (let i = 0; i < curve.length; i++) {
      const x = i * 2 / curve.length - 1;
      curve[i] = ((3 + drive) * x * 20 * Math.PI / 180) /
        (Math.PI + drive * Math.abs(x));
    }
    shaper.curve = curve;
    shaper.oversample = '4x';
    const gain = audioContext.createGain();
    gain.gain.value = 3.0;
    const compressor = audioContext.createDynamicsCompressor();
    compressor.threshold.value = -22;
    compressor.knee.value = 8;
    compressor.ratio.value = 12;
    compressor.attack.value = 0.002;
    compressor.release.value = 0.12;
    source.connect(shaper).connect(gain).connect(compressor).connect(audioContext.destination);
    sound.volume = 1;
    return audioContext.resume().then(() => sound.play());
  } catch (_) {
    sound.volume = 1;
    return sound.play();
  }
}
video.addEventListener('loadedmetadata', () => {
  canvas.width = video.videoWidth || 1280;
  canvas.height = video.videoHeight || 720;
  video.play().catch(() => window.chrome?.webview?.postMessage('close'));
  startLoudAudio().catch(() => {});
  requestAnimationFrame(render);
});
video.addEventListener('ended', () => window.chrome?.webview?.postMessage('ended'));
function render() {
  if (video.readyState >= 2) {
    ctx.drawImage(video, 0, 0, canvas.width, canvas.height);
    const frame = ctx.getImageData(0, 0, canvas.width, canvas.height);
    const pixels = frame.data;
    for (let i = 0; i < pixels.length; i += 4) {
      const r = pixels[i], g = pixels[i + 1], b = pixels[i + 2];
      const strongestOther = Math.max(r, b);
      const greenExcess = g - strongestOther;
      if (g > 65 && greenExcess > 18) {
        const edge = Math.min(255, Math.max(0, (greenExcess - 18) * 18));
        pixels[i + 3] = Math.max(0, pixels[i + 3] - edge);
      }
    }
    ctx.putImageData(frame, 0, 0);
  }
  requestAnimationFrame(render);
}
document.addEventListener('keydown', e => { if (e.key === 'Escape') window.chrome?.webview?.postMessage('close'); });
document.addEventListener('click', () => window.chrome?.webview?.postMessage('close'));
</script></body></html>
""".Replace("__VIDEO_DATA__", videoBase64, StringComparison.Ordinal)
     .Replace("__AUDIO_DATA__", audioBase64, StringComparison.Ordinal);
}
