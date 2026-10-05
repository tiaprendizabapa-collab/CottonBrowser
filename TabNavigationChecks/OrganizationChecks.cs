using System.Reflection;
using LeanBrowser;
using CottonBrowser.Shared;

internal static class OrganizationChecks
{
    public static void Run()
    {
        using var tabs = new BrowserTabControl();
        var allowlist = new SiteAllowlist(new SiteExceptionStore(Path.Combine(Path.GetTempPath(),
            "CottonOrganizationChecks-" + Guid.NewGuid().ToString("N") + ".json")));
        var alpha = new BrowserTab(allowlist) { Text = "Alpha" };
        var settings = new TabPage("Configurações");
        var beta = new BrowserTab(allowlist) { Text = "Beta" };
        var anonymous = new BrowserTab(allowlist, isPrivate: true) { Text = "Anônima" };
        var gamma = new BrowserTab(allowlist) { Text = "Gamma" };
        tabs.TabPages.AddRange([alpha, settings, beta, anonymous, gamma]);
        _ = tabs.Handle;
        tabs.SelectedTab = settings;
        var initialHeader = HeaderFor(tabs, beta);
        var initialWidth = initialHeader.Width;
        var changes = 0;
        tabs.OrganizationChanged += () => changes++;

        beta.IsPinned = true;
        Check(tabs.TabPages[0] == beta && tabs.SelectedTab == settings,
            "Fixar uma aba deve colocá-la no início sem selecionar outra página.");
        Check(HeaderFor(tabs, beta) == initialHeader && initialHeader.Width < initialWidth / 2,
            "A aba fixada deve usar o mesmo cabeçalho, em tamanho compacto.");
        gamma.GroupName = "Trabalho";
        alpha.GroupName = "Trabalho";
        alpha.GroupColorArgb = Color.CornflowerBlue.ToArgb();
        gamma.GroupColorArgb = alpha.GroupColorArgb;
        Check(tabs.TabPages.Cast<TabPage>().SequenceEqual([beta, alpha, gamma, settings, anonymous]),
            "Os membros do grupo devem ficar juntos, depois das abas fixadas.");
        anonymous.GroupName = "Trabalho";
        Check(tabs.TabPages.IndexOf(anonymous) > tabs.TabPages.IndexOf(settings),
            "Grupos com o mesmo nome em modos normal e anônimo devem permanecer separados.");
        Check(tabs.SelectedTab == settings && changes >= 5,
            "Organizar abas deve preservar a seleção e notificar a persistência.");
        Check(HeaderFor(tabs, alpha).AccessibleDescription?.Contains("Trabalho") == true,
            "O grupo deve também aparecer na descrição acessível da aba.");
        var headerOrder = tabs.HeaderStrip.Controls.Cast<Control>().Where(control => control.AccessibleRole == AccessibleRole.PageTab).ToArray();
        Check(headerOrder.SequenceEqual(tabs.TabPages.Cast<TabPage>().Select(page => HeaderFor(tabs, page))),
            "A ordem visual dos cabeçalhos deve acompanhar as páginas após fixar e agrupar.");
        beta.IsPinned = false;
        Check(initialHeader.Width == initialWidth,
            "Desafixar deve devolver a largura normal sem recriar o WebView.");
        alpha.IsPinned = true;
        Check(tabs.TabPages[0] == alpha, "Fixar um membro do grupo deve respeitar a área de abas fixadas.");
        alpha.IsPinned = false;
        var order = tabs.TabPages.Cast<TabPage>().ToArray();
        tabs.RestoreOrganizationOrder();
        Check(order.SequenceEqual(tabs.TabPages.Cast<TabPage>()),
            "Normalizar novamente uma ordem válida não deve alterá-la.");
        Check(tabs.SelectedTab == settings && tabs.TabPages.OfType<BrowserTab>().All(tab => tab.Web.CoreWebView2 is null),
            "Organizar abas não deve navegar, criar perfis ou trocar a página selecionada.");
        CheckSearch(tabs, alpha);
        if (Environment.GetEnvironmentVariable("COTTON_TABS_PREVIEW") == "1") CapturePreviews(tabs);
        tabs.TabPages.Remove(beta);
        initialHeader = HeaderFor(tabs, gamma);
        beta.IsPinned = true;
        Check(!initialHeader.IsDisposed, "Remover uma aba deve retirar a assinatura dos seus eventos.");
        beta.Dispose();
        Console.WriteLine("PASS: abas fixadas compactas, grupos, isolamento anônimo, seleção e busca de abas.");
    }

    private static void CheckSearch(BrowserTabControl tabs, BrowserTab alpha)
    {
        var type = typeof(BrowserTabControl).Assembly.GetType("LeanBrowser.TabSearchDialog")!;
        using var dialog = (Form)Activator.CreateInstance(type, tabs)!;
        var search = (TextBox)type.GetField("_search", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
        var results = (ListBox)type.GetField("_results", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
        search.Text = "alpha";
        Check(results.Items.Count == 1 && type.GetProperty("SelectedPage")!.GetValue(dialog) == alpha,
            "A busca deve encontrar o título sem diferenciar maiúsculas e selecionar a aba correta.");
        search.Text = "trabalho";
        Check(results.Items.Count == 3, "A busca deve encontrar o nome do grupo, incluindo abas anônimas da própria janela.");
        alpha.NavigateOrQueue("https://alpha.example.test/account");
        search.Text = "alpha.example.test";
        Check(results.Items.Count == 1 && type.GetProperty("SelectedPage")!.GetValue(dialog) == alpha,
            "A busca deve encontrar o endereço de uma aba cuja navegação ainda está na fila.");
        search.Text = "sem resultado";
        Check(results.Items.Count == 0, "Uma busca sem correspondência não deve selecionar outra aba.");
    }

    private static Control HeaderFor(BrowserTabControl tabs, TabPage page) => tabs.HeaderStrip.Controls.Cast<Control>()
        .Single(control => control.AccessibleRole == AccessibleRole.PageTab && control.AccessibleName == page.Text);

    private static void CapturePreviews(BrowserTabControl tabs)
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "LeanBrowser", "LeanBrowser.csproj"))) root = root.Parent;
        var directory = Path.Combine(root!.FullName, ".tools");
        Directory.CreateDirectory(directory);
        var pinned = tabs.TabPages.OfType<BrowserTab>().Single(tab => tab.Text == "Beta");
        pinned.IsPinned = true;
        tabs.SelectedTab = tabs.TabPages.OfType<BrowserTab>().Single(tab => tab.Text == "Alpha");
        tabs.HeaderStrip.Size = new Size(1100, 65);
        _ = tabs.HeaderStrip.Handle;
        tabs.HeaderStrip.PerformLayout();
        SavePreview(tabs.HeaderStrip, Path.Combine(directory, "tabs-preview.png"));
        var assembly = typeof(BrowserTabControl).Assembly;
        using var search = (Form)Activator.CreateInstance(assembly.GetType("LeanBrowser.TabSearchDialog")!, tabs)!;
        _ = search.Handle;
        assembly.GetType("LeanBrowser.TabSearchDialog")!.GetMethod("Filter", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(search, null);
        search.PerformLayout();
        SavePreview(search, Path.Combine(directory, "tab-search-preview.png"));
        using var group = (Form)Activator.CreateInstance(assembly.GetType("LeanBrowser.TabGroupDialog")!, "Trabalho", Color.CornflowerBlue.ToArgb())!;
        _ = group.Handle;
        group.PerformLayout();
        SavePreview(group, Path.Combine(directory, "tab-group-preview.png"));
    }

    private static void SavePreview(Control control, string path)
    {
        if (control is Form form)
        {
            form.Opacity = 0;
            form.Show();
            Application.DoEvents();
        }
        using var bitmap = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        if (control is Form previewForm) previewForm.Hide();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
