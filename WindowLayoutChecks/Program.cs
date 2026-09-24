using System.Drawing;
using LeanBrowser;

static void Check(Rectangle actual, Rectangle expected, string caseName)
{
    if (actual != expected)
        throw new Exception($"{caseName}: esperado {expected}, obtido {actual}");
}

var large = new Rectangle(0, 0, 1920, 1080);
var largeWork = new Rectangle(0, 0, 1920, 1032);
var small = new Rectangle(1920, 0, 1366, 768);
var smallWork = new Rectangle(1920, 0, 1366, 720);

Check(WindowLayout.MaximizedBounds(large, largeWork),
    new Rectangle(0, 0, 1920, 1032), "Monitor principal");
Check(WindowLayout.MaximizedBounds(small, smallWork),
    new Rectangle(0, 0, 1366, 720), "Monitor secundário menor");
Check(WindowLayout.MaximizedBounds(new Rectangle(-1366, 0, 1366, 768),
    new Rectangle(-1366, 40, 1366, 728)),
    new Rectangle(0, 40, 1366, 728), "Monitor à esquerda com barra superior");
Check(WindowLayout.FitNormal(new Rectangle(1920, 0, 1920, 1032), smallWork),
    smallWork, "Mover janela grande para tela pequena");
Check(WindowLayout.FitNormal(new Rectangle(2200, 100, 1200, 680), smallWork),
    new Rectangle(2086, 40, 1200, 680), "Recolocar janela parcialmente cortada");
Check(WindowLayout.FitNormal(new Rectangle(200, 100, 1200, 680), largeWork),
    new Rectangle(200, 100, 1200, 680), "Preservar janela que já cabe");
Check(WindowLayout.FitNormal(new Rectangle(0, 0, 1200, 780),
    new Rectangle(0, 0, 800, 552)),
    new Rectangle(0, 0, 800, 552), "Tela compacta de 800 por 600");
Check(WindowLayout.MaximizedBounds(new Rectangle(0, 0, 3840, 2160),
    new Rectangle(0, 0, 3840, 2112)),
    new Rectangle(0, 0, 3840, 2112), "Tela 4K");

Console.WriteLine("PASS: maximização e ajuste de janelas em monitores grandes, pequenos e deslocados.");
