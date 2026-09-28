using LeanBrowser;

internal static class DownloadChecks
{
    public static void Run()
    {
        var tempRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CottonDownloadChecks"));
        var directory = Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "downloads.json");
        try
        {
            var store = new DownloadHistoryStore(path);
            var start = DateTimeOffset.UtcNow;
            var old = Entry("older.zip", start.AddMinutes(-1));
            var recent = Entry("recent.zip", start);
            store.Upsert(old);
            store.Upsert(recent);
            var persisted = File.ReadAllText(path);
            var progressing = old with { BytesReceived = 8192 };
            store.Upsert(progressing, persist: false);
            Check(store.Snapshot().First().Id == recent.Id, "Progresso não deve reordenar o histórico.");
            Check(store.Snapshot().Single(item => item.Id == old.Id).BytesReceived == 8192,
                "Bytes atuais devem estar disponíveis sem gravação em disco.");
            Check(File.ReadAllText(path) == persisted, "Atualização em memória não deve gravar em disco.");

            using var tabs = new BrowserTabControl { Size = new Size(900, 600) };
            var downloads = new DownloadsTab(store);
            tabs.TabPages.Add(downloads);
            _ = tabs.Handle;
            _ = downloads.Handle;
            tabs.PerformLayout();
            downloads.PerformLayout();
            var list = downloads.Controls.OfType<FlowLayoutPanel>().Single();
            _ = list.Handle;
            list.PerformLayout();
            var row = list.Controls.OfType<Panel>().Single(panel => panel.Controls.OfType<Label>()
                .Any(label => label.Text == "older.zip"));
            Check(row.Width > 600 && row.Right <= list.ClientSize.Width,
                "Cartões devem ocupar a largura disponível desde a primeira atualização.");
            Check(list.Top >= downloads.Controls.OfType<Label>().Max(label => label.Bottom),
                "A lista não deve cobrir o título e o subtítulo.");
            Check(row.Controls.OfType<ProgressBar>().Single().Style == ProgressBarStyle.Marquee,
                "Download sem tamanho total deve mostrar progresso indeterminado.");
            Check(row.Controls.OfType<Label>().Any(label => label.Text.StartsWith("Baixando")),
                "Download sem tamanho total deve indicar os bytes recebidos.");
            store.Upsert(progressing with { BytesReceived = 16384 }, persist: false);
            downloads.RefreshEntries();
            Check(list.Controls.Contains(row) && !row.IsDisposed,
                "Atualizar o progresso deve reutilizar o cartão existente.");
            Check(row.Controls.OfType<Label>().Any(label => label.Text.Contains("16")),
                "A interface deve receber os bytes atualizados.");

            tabs.Size = new Size(420, 600);
            tabs.PerformLayout();
            downloads.PerformLayout();
            list.PerformLayout();
            Check(row.Height == 116 && row.Right <= list.ClientSize.Width,
                "Janela estreita deve acomodar o progresso sem sobrepor o nome.");

            var completed = progressing with { Status = DownloadStatus.Completed, FinishedAt = DateTimeOffset.UtcNow };
            store.Upsert(completed);
            downloads.RefreshEntries();
            Check(row.Controls.OfType<ProgressBar>().Single() is { Style: ProgressBarStyle.Continuous, Value: 100 },
                "Ao terminar, o progresso deve sair do modo indeterminado.");
            var reloaded = new DownloadHistoryStore(path).Snapshot();
            Check(reloaded.Single(item => item.Id == old.Id).Status == DownloadStatus.Completed,
                "Conclusão deve persistir no histórico.");
            Check(reloaded.Single(item => item.Id == recent.Id).Status == DownloadStatus.Interrupted,
                "Download inacabado deve ser marcado como interrompido ao reabrir.");

            for (var index = 0; index < 20; index++)
                store.Upsert(Entry($"arquivo-{index}.zip", start.AddMinutes(-index - 2)), persist: false);
            downloads.RefreshEntries();
            list.PerformLayout();
            list.AutoScrollPosition = new Point(0, 400);
            var scrollBefore = list.AutoScrollPosition.Y;
            Check(scrollBefore < 0, "O cenário deve permitir testar a rolagem.");
            downloads.RefreshEntries();
            Check(list.AutoScrollPosition.Y == scrollBefore, "Atualizar a lista deve preservar a rolagem.");
            var anchor = list.Controls.OfType<Panel>().Where(panel => panel.Bottom > 0)
                .OrderBy(panel => panel.Top).First();
            var anchorTop = anchor.Top;
            store.Upsert(Entry("novo.zip", start.AddMinutes(1)), persist: false);
            downloads.RefreshEntries();
            Check(anchor.Top == anchorTop, "Inserir um novo download deve preservar o cartão visível.");
            Console.WriteLine("PASS: downloads, largura responsiva, cartões persistentes, progresso indeterminado e histórico.");
        }
        finally
        {
            var fullPath = Path.GetFullPath(directory);
            if (fullPath.StartsWith(tempRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(fullPath)) Directory.Delete(fullPath, recursive: true);
        }
    }

    private static DownloadEntry Entry(string name, DateTimeOffset start) => new(
        Guid.NewGuid(), name, "https://example.com/" + name, Path.Combine(Path.GetTempPath(), name),
        0, -1, DownloadStatus.InProgress, null, start, null);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
