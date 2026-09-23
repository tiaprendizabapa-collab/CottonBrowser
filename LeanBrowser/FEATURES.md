# Abas, favoritos e senhas

Implementação para C# / .NET 8 / WinForms / WebView2, integrada em BrowserForm.

## Classes e integração

- `BrowserTab` contém um WebView2, um AdBlocker e o estado de carregamento. Cada aba tem histórico independente.
- `BrowserTabManager.CreateAsync(url)` cria e inicializa a aba no ambiente compartilhado; `CloseActive()` remove e libera seus controles; `SelectNext(direction)` alterna a seleção.
- `BrowserForm.InitializeWebViewAsync()` cria um único CoreWebView2Environment com perfil persistente. O evento `Ready` chama `ConfigureTab`, que aplica configurações, conecta os eventos e instala o bloqueador antes da primeira navegação.
- `RefreshActiveTab()` reflete apenas a aba selecionada na barra de endereço, no título, no indicador e nos botões. Eventos de abas em segundo plano não alteram a barra da aba ativa.
- `BookmarkStore.Load()`, `Add(url, title)` e `Remove(url)` persistem os favoritos em JSON. O menu Favoritos captura `CoreWebView2.Source` e `DocumentTitle`; cada link abre uma aba.
- `PasswordManager.Configure(core)` habilita o gerenciador de senhas nativo. `ClearSavedPasswordsAsync(core)` apaga as senhas do perfil compartilhado.

Fechar a última aba encerra a janela. Links HTTP/HTTPS que solicitam uma nova janela por ação do usuário abrem uma aba; pop-ups não solicitados são descartados. Esta implementação abre a URL, mas não preserva `window.opener` ou janelas auxiliares criadas via script; fluxos OAuth que dependem disso precisam de integração com `NewWindowRequested.NewWindow` e deferrals.

## Uso

| Comando | Ação |
|---|---|
| Ctrl+T / + | Abrir a página inicial em outra aba |
| Ctrl+W / × na guia | Fechar a aba selecionada pelo teclado ou a guia clicada |
| Ctrl+Tab / Ctrl+Shift+Tab | Próxima / anterior |
| Ctrl+D | Adicionar ou atualizar favorito da página |
| Ctrl+J | Abrir o gestor de downloads |
| Ctrl++ / Ctrl+- / Ctrl+0 | Aumentar, diminuir ou repor o zoom da aba |
| Favoritos | Adicionar, remover a página atual ou abrir links salvos |
| Apagar senhas salvas | Apagar todas as senhas do perfil, após confirmação |

Os atalhos existentes permanecem disponíveis. Ctrl+Shift+A afeta o bloqueador da aba atual.

## Persistência

- Favoritos: `%LOCALAPPDATA%\LeanBrowser\bookmarks.json`.
- Histórico para sugestões da barra: `%LOCALAPPDATA%\LeanBrowser\history.json` (até 200 URLs).
- Perfil WebView2, incluindo dados de login: `%LOCALAPPDATA%\LeanBrowser\WebView2`.

O JSON aceita somente HTTP/HTTPS sem usuário/senha embutidos na URL. Usa substituição por arquivo temporário no mesmo diretório; falhas de leitura ou JSON inválido são informadas e não sobrescrevem os dados existentes. URLs iguais atualizam o título. A classe é destinada à thread da UI de uma instância do app; múltiplas instâncias escrevendo simultaneamente exigem mutex ou SQLite transacional. Títulos e URLs dos favoritos não são criptografados e podem conter informações sensíveis, incluindo parâmetros de consulta.

## Segurança das senhas

A detecção de formulários, o diálogo Salvar/Atualizar, a seleção de credenciais e o preenchimento ficam a cargo do WebView2. Não há ponte JavaScript/C# que receba senhas, script de captura de teclado, chave fixa no executável ou senha em JSON. O consentimento para armazenar é solicitado pelo runtime.

O gerenciador do motor Edge usa criptografia local AES com proteção de chave pelo Windows (DPAPI). A implementação e sua evolução pertencem ao runtime: o aplicativo usa as APIs públicas, sem abrir ou modificar o banco interno. Isso protege dados em repouso, mas não protege contra malware executando como o usuário nem contra scripts maliciosos na própria página que recebe o preenchimento. Não há promessa de senha mestra ou exigência de Windows Hello nesta integração.

A associação de credenciais aos sites e as heurísticas de formulários são responsabilidade do runtime; não usamos comparação caseira por prefixo, domínio ou caminho de URL. Não se deve prometer preenchimento universal: formulários customizados, múltiplas contas, políticas corporativas e versões do runtime podem exigir seleção/interação ou impedir salvar. Use HTTPS para logins; esta implementação não impõe um bloqueio global de navegação HTTP e não modifica a política nativa de preenchimento.

`IsPasswordAutosaveEnabled = false` impede novos salvamentos, mas NÃO impede preenchimento de senhas já armazenadas. Por isso, o menu oferece exclusão explícita via `PasswordAutosave`. A exclusão não limpa campos já preenchidos, cookies ou sessões autenticadas; sair de uma conta é uma ação separada.

O perfil fica na conta local do Windows e não é o perfil pessoal do Edge. Não distribua a pasta WebView2 junto do aplicativo, não registre credenciais em logs e mantenha o runtime Evergreen atualizado. SmartScreen permanece habilitado e a flag que desativava a detecção de phishing foi removida.

Referências oficiais:
- [Salvar e preencher senhas no WebView2](https://learn.microsoft.com/en-us/microsoft-edge/webview2/reference/win32/icorewebview2settings4)
- [Proteção das senhas no motor Edge](https://learn.microsoft.com/en-us/deployedge/microsoft-edge-security-password-manager-security)
- [Perfil e remoção de dados WebView2](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/user-data-folder)

## Validação

Execute, na pasta `LeanBrowser`:

```powershell
.\.tools\dotnet\dotnet.exe build .\LeanBrowser\LeanBrowser.csproj --no-restore
.\.tools\dotnet\dotnet.exe run --project .\BookmarkChecks\BookmarkChecks.csproj
```

Os testes verificam persistência, Unicode, atualização sem duplicação, remoção, rejeição de esquemas perigosos/credenciais em URL e preservação de arquivo corrompido.

Verificação manual pendente em uma sessão gráfica:

1. Abrir duas páginas, alternar, navegar para trás em uma e fechar a outra; conferir isolamento de histórico, título, URL e contador.
2. Salvar um favorito, reiniciar, abrir pelo menu e remover.
3. Em um site HTTPS de teste, usar credenciais fictícias, aceitar o diálogo nativo de salvamento, reiniciar e verificar a sugestão/preenchimento; uma origem diferente não deve receber essa credencial.
4. Apagar as senhas, abrir uma página nova do mesmo site e verificar a ausência da sugestão. Campos e sessões previamente abertos não são limpos.
5. Minimizar/restaurar, abrir/fechar rapidamente abas durante inicialização e conferir que eventos de abas em segundo plano não alteram a aba ativa.

