using System.Drawing.Imaging;
using System.Reflection;
using LeanBrowser;

public static class MenuChecks
{
    public static void Run()
    {
        var originalTheme = Theme.IsDark;
        try
        {
            foreach (var dark in new[] { true, false })
            {
                Theme.SetDark(dark);
                CheckMenu(dark);
            }
            Console.WriteLine("PASS: menu em áreas normal/curta, submenus, zoom, estados e previews claro/escuro.");
        }
        finally { Theme.SetDark(originalTheme); }
    }

    private static void CheckMenu(bool dark)
    {
        // Usa somente dados fictícios e controles; não cria BrowserForm ou WebView2.
        using var menu = new BrowserOverflowMenu();
        var zoomFactor = 1.25;
        var fullscreen = false;
        var changes = new List<int>();
        BrowserMenuZoom? zoom = null;
        zoom = new BrowserMenuZoom(direction =>
        {
            changes.Add(direction);
            zoomFactor = direction == 0 ? 1 : Math.Round(zoomFactor + direction * 0.1, 2);
            zoom!.SetState(zoomFactor, true, fullscreen);
        }, () =>
        {
            fullscreen = !fullscreen;
            zoom!.SetState(zoomFactor, true, fullscreen);
        });
        var history = Item("Histórico recente", "\uE81C");
        history.DropDownItems.Add(Item("Página de exemplo", "\uE81C"));
        var favorites = Item("Favoritos", "\uE734");
        favorites.DropDownItems.Add(Item("Adicionar esta página", "\uE734", "Ctrl+D"));
        favorites.DropDownItems.Add(Item("Gerenciar favoritos", "\uE8F1"));
        var protection = Item("Proteção", "\uEA18");
        protection.DropDownItems.Add(Item("Bloquear anúncios", "\uE73E", "Ctrl+Shift+A"));
        protection.DropDownItems.Add(new BrowserMenuItem("Proteção ativa", "\uE946") { Enabled = false });
        menu.Items.AddRange([
            new BrowserMenuItem("Atualização disponível", "\uE895")
            {
                IsBanner = true, Detail = "Versão de demonstração"
            },
            Item("Nova guia", "\uE710", "Ctrl+T"),
            Item("Nova guia anônima", "\uE727", "Ctrl+Shift+N"),
            new ToolStripSeparator(),
            new BrowserMenuItem("Perfil")
            {
                IsProfile = true, Avatar = "P", Detail = "Perfil de exemplo · Modo padrão"
            },
            history, Item("Downloads", "\uE896", "Ctrl+J"), favorites, protection,
            Item("Central do navegador", "\uE80F"), new ToolStripSeparator(), zoom,
            new ToolStripSeparator(), Item("Imprimir…", "\uE749", "Ctrl+P"),
            Item("Localizar na página…", "\uE721", "Ctrl+F"), new ToolStripSeparator(),
            Item("Configurações", "\uE713"), Item("Verificar atualizações", "\uE895"),
            Item("Sair", "\uE8BB")
        ]);
        zoom.SetState(zoomFactor, true, fullscreen);
        _ = menu.Handle;
        menu.ApplyTheme();

        ConstrainAndCheck(menu, new Size(900, 800));
        var normalSize = menu.Size;
        Check(normalSize.Width >= 300 && normalSize.Height > 400,
            "O menu completo deve ter largura legível e acomodar suas opções.");
        foreach (var parent in new[] { history, favorites, protection })
        {
            Check(parent.DropDown is BrowserOverflowMenu, "Submenus devem usar o mesmo menu do navegador.");
            var child = (BrowserOverflowMenu)parent.DropDown;
            _ = child.Handle;
            ConstrainAndCheck(child, new Size(480, 300));
        }

        var buttons = zoom.Control.Controls.Cast<Control>().OrderBy(button => button.TabIndex).ToArray();
        Check(buttons.Length == 4 && buttons.All(button => button.Enabled),
            "O zoom de uma página pronta deve disponibilizar os quatro botões.");
        Check(buttons[1].Text == "125%", "O menu deve exibir o zoom atual da página.");
        Click(buttons[0]);
        Check(zoomFactor == 1.15 && buttons[1].Text == "115%", "Reduzir deve atualizar o valor exibido.");
        Click(buttons[2]);
        Check(zoomFactor == 1.25 && buttons[1].Text == "125%", "Aumentar deve atualizar o valor exibido.");
        Click(buttons[1]);
        Check(zoomFactor == 1 && buttons[1].Text == "100%", "O percentual deve restaurar 100%.");
        Check(changes.SequenceEqual(new[] { -1, 1, 0 }), "Os três comandos de zoom devem usar seus callbacks corretos.");
        Click(buttons[3]);
        Check(fullscreen && buttons[3].AccessibleName == "Sair da tela cheia",
            "Tela cheia deve mudar o estado e sua descrição acessível.");
        Click(buttons[3]);
        Check(!fullscreen && buttons[3].AccessibleName == "Entrar em tela cheia",
            "Sair da tela cheia deve restaurar o comando original.");

        zoom.SetState(1.5, false, false);
        Check(buttons.Take(3).All(button => !button.Enabled) && buttons[3].Enabled,
            "Sem página pronta, somente os comandos de zoom devem ficar desabilitados.");
        zoom.SetState(1.25, true, false);
        ConstrainAndCheck(menu, new Size(480, 300));
        Check(menu.Height < normalSize.Height, "A área curta deve limitar a altura do menu.");
        Check(menu.Items.Cast<ToolStripItem>().Sum(item => item.Height) > menu.Height,
            "O cenário curto deve exercitar um menu com conteúdo maior que a área visível.");
        Check(buttons[1].Text == "125%" && buttons.Take(3).All(button => button.Enabled),
            "Adaptar o menu à janela não deve perder o zoom ou a disponibilidade dos comandos.");
        CheckMouseWheel(menu, zoom);

        ConstrainAndCheck(menu, new Size(900, 800));
        Check(menu.Size == normalSize, "Voltar à área normal deve recuperar o tamanho original do menu.");
        Check(zoom.Control.Controls.Cast<Control>().OrderBy(button => button.TabIndex).SequenceEqual(buttons),
            "Redimensionar e atualizar o menu deve preservar os controles de zoom.");
        Capture(menu, dark);
            if (Environment.GetEnvironmentVariable("COTTON_MENU_LIVE_CAPTURE") == "1")
            {
                CheckLiveControls(menu);
                CheckLiveWheel();
            }
    }

    private static BrowserMenuItem Item(string text, string glyph, string shortcut = "") => new(text, glyph, shortcut);

    private static void ConstrainAndCheck(BrowserOverflowMenu menu, Size available)
    {
        menu.ConstrainTo(available, menu.DeviceDpi);
        menu.PerformLayout();
        Check(menu.Width > 0 && menu.Height > 0 && menu.Width <= available.Width && menu.Height <= available.Height,
            $"O tamanho efetivo do menu ({menu.Width}x{menu.Height}) deve caber na área {available.Width}x{available.Height}.");
    }

    private static void Click(Control button) =>
        typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(button, [EventArgs.Empty]);

    private static void CheckMouseWheel(BrowserOverflowMenu menu, BrowserMenuZoom zoom)
    {
        var top = menu.Items[0].Bounds.Top;
        var controls = zoom.Control.Controls.Cast<Control>().ToArray();
        var selected = menu.Items.Cast<ToolStripItem>().FirstOrDefault(item => item.Selected);
        var clicks = 0;
        foreach (ToolStripItem item in menu.Items) item.Click += (_, _) => clicks++;
        void Wheel(int delta) => typeof(BrowserOverflowMenu).GetMethod("OnMouseWheel",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(menu,
            [new HandledMouseEventArgs(MouseButtons.None, 0, 100, 100, delta)]);
        Wheel(120);
        Check(menu.Items[0].Bounds.Top == top, "Rolar para cima no início não pode ultrapassar o limite.");
        Wheel(-60); Check(menu.Items[0].Bounds.Top == top, "Movimentos parciais devem acumular sem saltar opções.");
        Wheel(-60);
        if (SystemInformation.MouseWheelScrollLines != 0)
        {
            Check(menu.Items[0].Bounds.Top < top, "A roda para baixo deve mover o conteúdo do menu.");
            for (var i = 0; i < 100; i++) Wheel(-120);
            var bottom = menu.Items[0].Bounds.Top; Wheel(-120);
            Check(menu.Items[0].Bounds.Top == bottom, "A roda deve parar no fim do menu.");
            Wheel(120); Check(menu.Items[0].Bounds.Top > bottom, "A roda para cima deve voltar às opções anteriores.");
            for (var i = 0; i < 100; i++) Wheel(120);
            Check(menu.Items[0].Bounds.Top == top, "A roda deve retornar ao início sem deixar espaço vazio.");
        }
        Check(clicks == 0 && zoom.Control.Controls.Cast<Control>().SequenceEqual(controls)
            && controls[1].Text == "125%", "Rolar não pode ativar opções, mudar o zoom ou recriar os botões.");
        Check(menu.Items.Cast<ToolStripItem>().FirstOrDefault(item => item.Selected) == selected,
            "Rolar não pode trocar a opção selecionada.");
    }

    private static void Capture(BrowserOverflowMenu menu, bool dark)
    {
        DirectoryInfo? root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "LeanBrowser", "LeanBrowser.csproj")))
            root = root.Parent;
        if (root is null) throw new DirectoryNotFoundException("Não foi possível localizar a pasta do projeto para os previews.");
        var directory = Path.Combine(root.FullName, ".tools");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, dark ? "menu-preview-dark.png" : "menu-preview-light.png");
        using var preview = new Bitmap(menu.Width, menu.Height);
        menu.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size));
        var zoom = menu.Items.OfType<BrowserMenuZoom>().Single();
        using (var zoomPreview = new Bitmap(zoom.Control.Width, zoom.Control.Height))
        {
            zoom.Control.DrawToBitmap(zoomPreview, new Rectangle(Point.Empty, zoomPreview.Size));
            using (var row = Graphics.FromImage(zoomPreview))
            foreach (Control button in zoom.Control.Controls)
            {
                using var buttonPreview = new Bitmap(button.Width, button.Height);
                button.DrawToBitmap(buttonPreview, new Rectangle(Point.Empty, buttonPreview.Size));
                row.DrawImageUnscaled(buttonPreview, button.Location);
            }
            using var canvas = Graphics.FromImage(preview);
            canvas.DrawImageUnscaled(zoomPreview, zoom.Bounds.Location);
        }
        preview.Save(path, ImageFormat.Png);
        var colors = new HashSet<int>();
        for (var y = 0; y < preview.Height; y += 3)
        for (var x = 0; x < preview.Width; x += 3)
            colors.Add(preview.GetPixel(x, y).ToArgb());
        Check(colors.Count > 16, "DrawToBitmap não renderizou conteúdo suficiente; o preview precisa de inspeção visual.");
        Console.WriteLine($"PREVIEW: {path}");
    }

    private static void CheckLiveControls(BrowserOverflowMenu menu)
    {
        // ToolStripControlHost hospeda botões em HWNDs filhos; DrawToBitmap não
        // os inclui. A abertura verifica que eles ficam visíveis e posicionados.
        var screen = Screen.PrimaryScreen!.WorkingArea;
        menu.Show(new Point(screen.Left + 24, screen.Top + 24));
        try
        {
            Application.DoEvents();
            var zoom = menu.Items.OfType<BrowserMenuZoom>().Single();
            var buttons = zoom.Control.Controls.Cast<Control>().ToArray();
            Console.WriteLine($"LIVE: menu={menu.Visible}, host={zoom.Visible}, row={zoom.Control.Visible}, buttons={string.Join(',', buttons.Select(button => button.Visible))}, bounds={zoom.Bounds}");
            Check(menu.Visible && zoom.Visible && zoom.Control.Visible && buttons.All(button => button.Visible),
                "Os controles de zoom devem aparecer no menu aberto.");
            Check(buttons.All(button => zoom.Control.ClientRectangle.Contains(button.Bounds)),
                "Os botões de zoom devem permanecer dentro da linha.");
        }
        finally { menu.Close(); }
    }

    private static void CheckLiveWheel()
    {
        using var menu = new BrowserOverflowMenu();
        var commands = 0;
        var zoom = new BrowserMenuZoom(_ => commands++, () => commands++);
        zoom.SetState(1.25, true, false); menu.Items.Add(zoom);
        for (var i = 0; i < 30; i++) menu.Items.Add(Item("Opção " + i, "\uE946"));
        ConstrainAndCheck(menu, new Size(480, 300));
        var screen = Screen.PrimaryScreen!.WorkingArea;
        menu.Show(new Point(screen.Left + 24, screen.Top + 24));
        try
        {
            Application.DoEvents();
            var button = zoom.Control.Controls.Cast<Control>().Single(control => control.Text == "125%");
            Check(button.Visible, "O botão de zoom da primeira linha deve estar visível.");
            var point = button.PointToScreen(new Point(button.Width / 2, button.Height / 2));
            Message Wheel(Point position, IntPtr target) => Message.Create(target, 0x020A,
                new IntPtr(unchecked((int)((uint)(ushort)-120 << 16))),
                new IntPtr(unchecked((int)((uint)(ushort)position.X | ((uint)(ushort)position.Y << 16)))));
            var top = menu.Items[0].Bounds.Top;
            var message = Wheel(point, button.Handle);
            Check(Application.FilterMessage(ref message), "A roda sobre um botão hospedado deve ser capturada pelo menu aberto.");
            if (SystemInformation.MouseWheelScrollLines != 0)
                Check(menu.Items[0].Bounds.Top < top, "A roda sobre o botão de zoom deve rolar o menu.");
            Check(commands == 0 && button.Text == "125%" && menu.Visible,
                "Rolar sobre o zoom não pode mudar o valor ou fechar o menu.");
            var outside = Wheel(new Point(menu.Right + 50, menu.Bottom + 50), button.Handle);
            var scrolled = menu.Items[0].Bounds.Top;
            Check(!Application.FilterMessage(ref outside) && menu.Items[0].Bounds.Top == scrolled,
                "A roda fora do menu deve continuar disponível para a página.");
            menu.Close();
            message = Wheel(point, IntPtr.Zero);
            Check(!Application.FilterMessage(ref message), "Fechar o menu deve remover seu filtro da roda do mouse.");
        }
        finally { menu.Close(); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
