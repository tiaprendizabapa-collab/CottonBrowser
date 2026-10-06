# Abas, favoritos e senhas

Implementação para C# / .NET 8 / WinForms / WebView2, integrada em BrowserForm.

## Classes e integração

- `BrowserTab` contém um WebView2, um AdBlocker e o estado de carregamento. Cada aba tem histórico independente.
- `BrowserTabManager.CreateAsync(url)` cria e inicializa a aba no ambiente compartilhado; `CloseActive()` remove e libera seus controles; `SelectNext(direction)` alterna a seleção.
- `BrowserForm.InitializeWebViewAsync()` cria um único CoreWebView2Environment com perfil persistente. O evento `Ready` chama `ConfigureTab`, que aplica configurações, conecta os eventos e instala o bloqueador antes da primeira navegação.
- `RefreshActiveTab()` reflete apenas a aba selecionada na barra de endereço, no título, no indicador e nos botões. Eventos de abas em segundo plano não alteram a barra da aba ativa.
- `BookmarkStore.Load()`, `Add(url, title)` e `Remove(url)` persistem os favoritos em JSON. O menu Favoritos captura `CoreWebView2.Source` e `DocumentTitle`; cada link abre uma aba.
- `BookmarkManagerView` integra pastas, edição, movimento, ordenação e importação/exportação Netscape HTML às configurações. Pastas vazias persistem em `bookmarks.json.folders.json`; favoritos anteriores, sem `Folder`, permanecem na raiz.
- `BrowserFeatures` integra leitura, tradução, capturas, abas verticais e divisão. `ActiveBrowserTab` acompanha o foco entre os dois WebViews existentes; sair da divisão devolve cada controle à sua aba sem recriar o documento.
- `ReadingView` extrai texto de uma cópia do artigo no DOM e oferece ajustes locais, sem substituir o documento original. `PageCapture` captura PNG via WebView2/CDP, com limites de dimensões e seleção na prévia.
- `BrowserPaths` seleciona o diretório do perfil antes de inicializar preferências e sessões. `BrowserProfileStore` mantém o cadastro e abre o executável com `--profile <id>`. Cada perfil tem mutex e ambiente WebView2 próprios.
- `NewTabCustomizationView` salva preferências de fundo, visibilidade e atalhos. O host envia um evento para a página interna exata; o JavaScript monta links com `textContent` e valida HTTP/HTTPS, sem expor novas operações da ponte a páginas externas.
- `PasswordManager.Configure(core)` habilita o gerenciador de senhas nativo. `ClearSavedPasswordsAsync(core)` apaga as senhas do perfil compartilhado.

Fechar a última aba encerra a janela. Links HTTP/HTTPS que solicitam uma nova janela por ação do usuário abrem uma aba; pop-ups não solicitados são descartados. Esta implementação abre a URL, mas não preserva `window.opener` ou janelas auxiliares criadas via script; fluxos OAuth que dependem disso precisam de integração com `NewWindowRequested.NewWindow` e deferrals.

## Uso

| Comando | Ação |
|---|---|
| Ctrl+T / + | Abrir nova aba com a barra de pesquisa pronta para digitar |
| Ctrl+W / × na guia | Fechar a aba selecionada pelo teclado ou a guia clicada |
| Ctrl+Tab / Ctrl+Shift+Tab | Próxima / anterior |
| Ctrl+D | Adicionar ou atualizar favorito da página |
| Ctrl+J | Abrir o gestor de downloads |
| Ctrl++ / Ctrl+- / Ctrl+0 | Aumentar, diminuir ou repor o zoom do site; salvo por domínio no perfil, inclusive após reiniciar |
| Lupa / menu de três pontos | Redefinir o zoom para 100% |
| Favoritos | Adicionar, remover a página atual ou abrir links salvos |
| Configurações → Histórico | Consultar, pesquisar e apagar visitas dentro das configurações, sem abrir outra aba |
| Menu de três pontos → Histórico / Ctrl+H | Abrir diretamente a seção Histórico na aba de configurações, reutilizando-a se já estiver aberta |
| Apagar senhas salvas | Apagar todas as senhas do perfil, após confirmação |

Os atalhos existentes permanecem disponíveis. Ctrl+Shift+A afeta o bloqueador da aba atual.
Ao digitar um endereço conhecido, a barra completa o domínio e destaca a parte sugerida; o X da lista remove essa entrada do histórico. O menu de três pontos fecha ao clicar fora dele.

## Persistência

- Favoritos: `%LOCALAPPDATA%\LeanBrowser\bookmarks.json`.
- Histórico de visitas e sugestões da barra: `%LOCALAPPDATA%\LeanBrowser\history.json` (até 10.000 visitas).
- Sessão de abas normais: `%LOCALAPPDATA%\LeanBrowser\session.json` (URLs, títulos e organização; sem formulários nem abas anônimas).
- Preferências de inicialização, memória, buscador e zoom por domínio: `%LOCALAPPDATA%\LeanBrowser\preferences.json`. Na primeira abertura, valores do antigo `site-zoom.json` são importados; o arquivo antigo só é removido após salvar as preferências com sucesso.
- Perfil WebView2, incluindo dados de login: `%LOCALAPPDATA%\LeanBrowser\WebView2`.
- Preferências dos sites (cookies persistentes, localStorage e IndexedDB) permanecem no perfil WebView2 e voltam automaticamente na próxima visita. Permissões de câmera, microfone, localização e notificações são lembradas por origem após decisão explícita; abas anônimas não as persistem. Cookies de sessão e dados temporários seguem a validade definida pelo site.

Esses caminhos pertencem ao perfil padrão, preservado para compatibilidade.
Perfis novos usam `%LOCALAPPDATA%\LeanBrowser\Profiles\<id>` para dados,
preferências, tema e WebView2; `browser-profiles.json` na raiz armazena os nomes,
com mutex durante alterações para coordenar os processos dos perfis.
O perfil padrão mantém o tema no diretório roaming usado anteriormente.

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
.\.tools\dotnet\dotnet.exe run --project .\SessionChecks\SessionChecks.csproj
.\.tools\dotnet\dotnet.exe run --project .\TabNavigationChecks\TabNavigationChecks.csproj
.\.tools\dotnet\dotnet.exe run --project .\NavigationHistoryChecks\NavigationHistoryChecks.csproj
.\.tools\dotnet\dotnet.exe run --project .\TabMemoryChecks\TabMemoryChecks.csproj
.\.tools\dotnet\dotnet.exe run --project .\BrowserFeatureChecks\BrowserFeatureChecks.csproj
.\.tools\dotnet\dotnet.exe run --project .\BrowserFeatureChecks\BrowserFeatureChecks.csproj -- --webview
```

Os testes verificam persistência, Unicode, atualização sem duplicação, remoção, rejeição de esquemas perigosos/credenciais em URL e preservação de arquivo corrompido.

`BrowserFeatureChecks` também verifica importação/exportação com subpastas e
pastas vazias, conflitos de edição, diretórios e preferências isolados por perfil,
layout das novas seções em temas claro/escuro, abas verticais e restauração dos
controles após a divisão. Com `--webview`, usa perfis temporários e o runtime
real para testar extração de leitura, captura inteira, isolamento de cookies e
personalização da nova guia sem interpretar títulos de atalhos como HTML.

Verificação manual pendente em uma sessão gráfica:

1. Abrir duas páginas, alternar, navegar para trás em uma e fechar a outra; conferir isolamento de histórico, título, URL e contador.
2. Salvar um favorito, reiniciar, abrir pelo menu e remover.
3. Em um site HTTPS de teste, usar credenciais fictícias, aceitar o diálogo nativo de salvamento, reiniciar e verificar a sugestão/preenchimento; uma origem diferente não deve receber essa credencial.
4. Apagar as senhas, abrir uma página nova do mesmo site e verificar a ausência da sugestão. Campos e sessões previamente abertos não são limpos.
5. Minimizar/restaurar, abrir/fechar rapidamente abas durante inicialização e conferir que eventos de abas em segundo plano não alteram a aba ativa.

