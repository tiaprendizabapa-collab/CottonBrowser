namespace LeanBrowser;

public sealed record BrowserShortcut(string Id, string Label, Keys Default);
public static class ShortcutCatalog
{
    public static readonly BrowserShortcut[] Commands =
    [
        new("history", "Histórico", Keys.Control | Keys.H),
        new("downloads", "Downloads", Keys.Control | Keys.J),
        new("reader", "Modo de leitura", Keys.F9),
        new("split", "Tela dividida", Keys.Control | Keys.Shift | Keys.S),
        new("mute", "Silenciar aba", Keys.Control | Keys.Shift | Keys.M),
        new("settings", "Configurações", Keys.Control | Keys.Oemcomma),
        new("workspaces", "Espaços de trabalho", Keys.Control | Keys.Shift | Keys.W),
        new("reading", "Lista de leitura", Keys.Control | Keys.Shift | Keys.L),
        new("video", "Vídeo flutuante", Keys.Control | Keys.Shift | Keys.Y),
        new("performance", "Painel de desempenho", Keys.Control | Keys.Shift | Keys.P)
    ];
    private static readonly Keys[] Reserved =
    [
        Keys.Control | Keys.T, Keys.Control | Keys.W, Keys.Control | Keys.N, Keys.Control | Keys.L,
        Keys.Control | Keys.R, Keys.Control | Keys.D, Keys.Control | Keys.F, Keys.Control | Keys.P,
        Keys.Control | Keys.A, Keys.Control | Keys.C, Keys.Control | Keys.V, Keys.Control | Keys.X,
        Keys.Control | Keys.Z, Keys.Control | Keys.Y, Keys.Control | Keys.Shift | Keys.Z,
        Keys.Control | Keys.D0, Keys.Control | Keys.NumPad0, Keys.Control | Keys.Oemplus, Keys.Control | Keys.OemMinus,
        Keys.Control | Keys.Add, Keys.Control | Keys.Subtract, Keys.Alt | Keys.F4, Keys.Alt | Keys.Tab, Keys.Alt | Keys.Space,
        Keys.Control | Keys.Tab, Keys.Control | Keys.Shift | Keys.Tab,
        Keys.Control | Keys.Shift | Keys.T, Keys.Control | Keys.Shift | Keys.N, Keys.Control | Keys.Shift | Keys.E,
        Keys.Control | Keys.Shift | Keys.I, Keys.Control | Keys.Shift | Keys.A,
        Keys.F5, Keys.F11, Keys.F12, Keys.Escape, Keys.Alt | Keys.Left, Keys.Alt | Keys.Right, Keys.Alt | Keys.Home, Keys.Alt | Keys.D
    ];
    public static Keys Get(string id, BrowserPreferences preferences) => preferences.Shortcuts.TryGetValue(id, out var key)
        ? (Keys)key : Commands.Single(c => c.Id == id).Default;
    public static string Display(Keys key) => new KeysConverter().ConvertToString(key)?.Replace("Control", "Ctrl").Replace("Oemcomma", ",") ?? "";
    public static bool IsAvailable(Keys key) => Enum.IsDefined(key & Keys.KeyCode) && (key & Keys.KeyCode) != Keys.None
        && (key & Keys.KeyCode) is not (Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin or Keys.Tab)
        && ((key & Keys.Control) != 0 || (key & Keys.Alt) != 0 || (key & Keys.KeyCode) is >= Keys.F1 and <= Keys.F24)
        && !Reserved.Contains(key) && (key & ~(Keys.KeyCode | Keys.Control | Keys.Shift | Keys.Alt)) == 0;
    public static Dictionary<string, int> Normalize(Dictionary<string, int>? source)
    {
        var result = new Dictionary<string, int>(); var used = new HashSet<Keys>();
        foreach (var command in Commands)
        {
            var key = source?.TryGetValue(command.Id, out var value) == true ? (Keys)value : command.Default;
            // Defaults of other commands stay reserved so an override never disables another action.
            if (!IsAvailable(key) || Commands.Any(c => c.Id != command.Id && c.Default == key) || !used.Add(key)) key = command.Default;
            used.Add(key);
            if (key != command.Default) result[command.Id] = (int)key;
        }
        return result;
    }
}
