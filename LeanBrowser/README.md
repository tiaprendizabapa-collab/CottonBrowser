# LeanBrowser

Atualização pelo próprio navegador e publicação de novas versões: [UPDATES.md](../UPDATES.md).

Navegador minimalista construído sobre o motor web **nativo do sistema**
(Microsoft Edge WebView2), sem Chromium embutido.

Shell em C# / .NET 8 / WinForms. Bloqueio de anúncios feito por **interceptação
de rede no motor**, não por injeção de script.

---

## Compilar e rodar

Na pasta principal (acima desta), execute `run.cmd` para abrir o navegador.
Se o executável ainda não existir, ele será compilado automaticamente.
Para recompilar após alterar o código, execute `build.cmd`.
Os scripts funcionam independentemente da pasta atual do terminal.

Nesta máquina, o SDK .NET 8 foi preparado em `..\.tools\dotnet` e é
detectado automaticamente pelo `build.cmd`. Em outra máquina, instale o SDK
conforme abaixo. Ter apenas o runtime do .NET não permite compilar.

**Pré-requisitos**

| Item | Como obter |
|---|---|
| .NET 8 SDK | `winget install Microsoft.DotNet.SDK.8` |
| WebView2 Runtime | Já presente no Windows 11 e na maioria dos Win10. Senão: `winget install Microsoft.EdgeWebView2Runtime` |

**Build**

```cmd
cd LeanBrowser
build.cmd
```

Ou manualmente:

```cmd
dotnet restore
dotnet publish -c Release -r win-x64 -o .\dist
.\dist\CottonBrowser.exe
```

Para iterar durante o desenvolvimento: `dotnet run`.

O binário final fica em `dist\CottonBrowser.exe` (~2 MB, *framework-dependent*:
reaproveita o .NET já instalado em vez de duplicar ~70 MB de runtime por app).

---

## Atalhos

| Tecla | Ação |
|---|---|
| `Ctrl+L` / `Alt+D` | Focar a barra de endereços |
| `F5` / `Ctrl+R` | Recarregar |
| `Esc` | Parar carregamento |
| `Alt+←` / `Alt+→` | Voltar / Avançar |
| `Alt+Home` | Página inicial |
| `Ctrl+Shift+A` | Ligar/desligar o bloqueador |

---

## Barra de endereços

| Você digita | Resultado |
|---|---|
| `https://exemplo.com/a` | usado como está |
| `exemplo.com` | → `https://exemplo.com` |
| `localhost:3000` | → `http://localhost:3000` (dev server raramente tem TLS) |
| `//cdn.exemplo.com` | → `https://cdn.exemplo.com` |
| `como fazer pão` | → busca no Google |
| `receita bolo` | → busca (sem ponto de domínio) |

---

## Bloqueador de anúncios

A lista fica embutida no executável, mas é sobrescrevível sem recompilar:

```
%APPDATA%\LeanBrowser\blocklist.txt
```

O app exporta a lista padrão para esse caminho no primeiro início. Um domínio
por linha; `#` comenta. Formato *hosts file* (`0.0.0.0 dominio.com`) também é
aceito, então você pode colar trechos de listas públicas.

O contador de bloqueios da página aparece no escudo, à esquerda da URL.

### Easter egg do algodão

Clique cinco vezes rapidamente no ícone de algodão no canto esquerdo da faixa
de abas para reproduzir o jumpscare do Foxy. O vídeo fica embutido no
executável, o áudio começa junto e o fundo verde é removido em tempo real no
canvas. O áudio usa um ganho alto, compressor e distorção leve para o efeito
cômico. Pressione `Esc` ou clique no vídeo para fechar.

**Aviso de compatibilidade:** `googletagmanager.com` está na lista. Alguns
sites usam o GTM para carregar funcionalidade legítima e podem quebrar
parcialmente. Se isso acontecer, remova a linha do `blocklist.txt` do
`%APPDATA%`.

---

## Por que esta arquitetura consome pouco

### 1. Zero Chromium embutido

Electron/CEF **empacotam** um Chromium inteiro: ~150 MB em disco e, na prática,
uma árvore de processos dedicada por aplicação. Duas apps Electron abertas =
dois Chromium completos na RAM.

O WebView2 usa o runtime **Evergreen** já instalado no Windows: binários
compartilhados, page-cache do SO compartilhado, e os processos do motor podem
ser reaproveitados entre hosts. O que este projeto adiciona é apenas o shell
.NET — poucos MB.

### 2. Bloqueio no motor, não na página

A abordagem comum (uBlock-like em WebExtension, ou injetar JS na página) exige:

- carregar e parsear centenas de KB de regras **dentro de cada renderer**;
- rodar JavaScript em cada documento e iframe;
- varrer o DOM com `MutationObserver` durante toda a vida da página.

Isso é CPU no caminho quente, multiplicada por frame.

Aqui os padrões são registrados como filtros do próprio motor via
`AddWebResourceRequestedFilter`. Consequências:

- o casamento de padrão roda em **código nativo, no processo do browser**,
  antes de qualquer marshalling COM;
- requisições legítimas **nunca saem do motor** — o código gerenciado nem é
  acordado;
- o handler em C# só é invocado para requisições que já são anúncio, e o
  corpo dele é uma atribuição de resposta 403.

Ou seja: o custo total do bloqueio é proporcional ao número de anúncios
**bloqueados**, não ao número de requisições da página.

> **A armadilha que evitamos:** registrar `AddWebResourceRequestedFilter("*", All)`
> e decidir no C#. Isso parece equivalente, mas força todas as 200–500
> requisições de um site moderno a cruzarem a fronteira COM/IPC, virarem
> objetos .NET e voltarem — segurando a thread de UI no processo. É a diferença
> entre um pedágio em cada pacote e um filtro na entrada.

Efeito colateral bom: cada requisição barrada é uma conexão TCP + handshake TLS
+ parse + execução de JS de terceiro que **não acontece**. O bloqueador quase
sempre economiza mais CPU do que gasta.

### 3. Consolidação de processos

`--process-per-site` faz todos os frames de uma mesma origem compartilharem um
renderer, em vez de um por aba/iframe. Junto com `--renderer-process-limit=4`,
`msWebOOUI` e `msPdfOOUI` desligados (menos dois processos de UI), a árvore de
processos fica bem menor que a de um Chrome comum.

### 4. Subsistemas desligados

Cada `--disable-*` remove código que alocaria memória residente e timers que
acordariam a CPU em background: component updater, domain reliability, sync,
Cast/MediaRouter, optimization hints, autofill server, crash reporter. Nenhum
deles serve a um navegador minimalista.

### 5. Suspensão quando minimizado

Ao minimizar, o app chama `MemoryUsageTargetLevel = Low` e `TrySuspendAsync()`:
os timers do renderer congelam e boa parte do heap volta para o SO. Uma aba
pesada cai de centenas de MB para poucas dezenas enquanto está fora de vista.

### 6. GC e UI do host

- GC workstation **não concorrente**: elimina a thread de background do GC. O
  shell quase não aloca (a página vive no processo do motor), então o GC
  concorrente só custaria uma thread ociosa e heap extra.
- `InvariantGlobalization`: não carrega ICU (~30 MB de dados).
- Toolbar desenhada à mão (`GraphicsPath` + `OptimizedDoubleBuffer`) em vez de
  `TableLayoutPanel` + `ButtonRenderer`. Repinta em foco/hover/resize, nunca
  por frame.
- O contador de bloqueios **não** atualiza a UI a cada requisição barrada — é
  lido uma única vez, em `NavigationCompleted`. Caso contrário seriam dezenas
  de invalidações por página.

### 7. O que *não* fizemos de propósito

- **Não** desligamos a GPU. Sem aceleração, a composição volta para a CPU e o
  consumo **sobe**. "Leve" não é sinônimo de "renderização por software".
- **Não** desligamos o SmartScreen (`IsReputationCheckingRequired`).
  Economizaria uma ida à rede por navegação, mas não vale o risco.
- **Não** usamos `--memory-pressure-off`. Ele *impede* o Chromium de devolver
  memória sob pressão — o oposto do objetivo.

---

## Por que WebView2/C# e não Tauri

Tauri (Rust) tem shell mais enxuto que .NET — uns 20–30 MB a menos de runtime.
Mas o custo dominante nos dois casos é **a árvore de processos do motor web**,
que é idêntica. A diferença real está no requisito de bloqueio:

| Plataforma | Interceptação de rede disponível |
|---|---|
| Windows / WebView2 | `AddWebResourceRequestedFilter` — API de primeira classe |
| macOS / WKWebView | `WKContentRuleList` — JSON compilado, mecanismo totalmente diferente |
| Linux / WebKitGTK | `WebKitUserContentFilterStore`, ou uma extensão `.so` separada |

Uma solução Tauri multiplataforma exigiria **três** implementações distintas do
bloqueador, sob `#[cfg(target_os)]`. Para um projeto onde o bloqueio no motor é
requisito central, o caminho Windows-first entrega o comportamento correto sem
três caminhos de código divergentes.

O shell aqui é pequeno e isolado: portar para Tauri depois significa reescrever
`BrowserForm` e `UI`, mantendo `UrlHelper` e a `blocklist` como estão.

---

## Ideia de evolução

Para escalar de ~60 domínios para listas grandes (EasyList tem ~80 mil regras),
a estratégia de filtros por padrão deixa de escalar: o motor faria N
comparações por requisição. O caminho nesse ponto é o DevTools Protocol:

```csharp
await core.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
await core.CallDevToolsProtocolMethodAsync("Network.setBlockedURLs", json);
```

O bloqueio passa a ser resolvido inteiramente dentro do processo do browser,
com estrutura de dados própria — ao custo de ligar o domínio `Network` do CDP,
que adiciona instrumentação no renderer. Vale medir antes de trocar.

## Abas, favoritos e senhas

Consulte [FEATURES.md](FEATURES.md) para classes, integração, atalhos, persistência, segurança e roteiro de validação.

---

## Monitoramento corporativo/parental opcional

O cliente agora registra navegação concluída, aba ativa e termos de pesquisa
em uma fila limitada executada fora da thread da interface. Por privacidade,
nenhum dado é transmitido por padrão: o envio só é ativado quando as duas
variáveis abaixo existem.

```powershell
$env:COTTON_MONITORING_ENDPOINT = "https://servidor.exemplo/api/telemetry/navigation"
$env:COTTON_MONITORING_TOKEN = "token-de-ingestao"
```

O servidor opcional está em `../MonitoringServer`. Ele usa ASP.NET Core e
SignalR, exige token de ingestão para receber eventos e token Admin para os
relatórios (`/api/admin/reports/top-sites`, `/api/admin/reports/searches`) e o
hub `/hubs/navigation`. O armazenamento de teste é limitado a 50 mil eventos
em memória; `../MonitoringServer/schema.sql` traz o modelo PostgreSQL
particionado para produção.

```powershell
$env:COTTON_INGEST_TOKEN = "token-de-ingestao-longo-e-aleatorio"
$env:COTTON_ADMIN_TOKEN = "token-de-admin-longo-e-aleatorio"
dotnet run --project ..\MonitoringServer
```

Use HTTPS, tokens aleatórios e uma política de retenção compatível com a
legislação e com o consentimento dos usuários monitorados. O RBAC do navegador
continua ocultando recursos avançados para perfis sem role Admin; a API do
servidor também valida a role no token, independentemente da interface.

