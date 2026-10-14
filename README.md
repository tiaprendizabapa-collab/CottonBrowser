# CottonBrowser

Navegador para Windows com uma edição Android e um servidor opcional de monitoramento.

## Estrutura

| Pasta | Conteúdo |
|---|---|
| `src/LeanBrowser` | Navegador Windows e seus recursos. |
| `src/CottonInstaller` | Instalador Windows. |
| `src/CottonUpdater` | Atualizador Windows. |
| `src/CottonMobile` | Aplicativo Android, incluindo os testes instrumentados do Gradle. |
| `src/MonitoringServer` | Servidor e painel de monitoramento opcional. |
| `src/Shared` | Código compartilhado pelo navegador e pelo servidor. |
| `tests/` | Projetos de verificação .NET e verificações Android independentes. |
| `scripts/` | Comandos de compilação, execução e geração dos filtros Android. |
| `.github/` | Automação de testes e publicação. |
| `.tools/` | Ferramentas e pré-requisitos locais, ignorados pelo Git. |

## Compilar e executar no Windows

Na raiz do repositório, com o SDK .NET 8 instalado:

```cmd
scripts\build.cmd
scripts\run.cmd
```

Também é possível abrir os arquivos `.cmd` diretamente na pasta `scripts`.
O navegador compilado fica em `src/LeanBrowser/dist`.

Para gerar o instalador:

```cmd
scripts\build-installer.cmd
```

O resultado fica em `src/CottonInstaller/dist/CottonBrowserSetup.exe`.
O arquivo `.cmd` chama o script PowerShell de mesmo nome. A geração do instalador
pode baixar o pré-requisito WebView2 e restaurar pacotes do NuGet.

## Verificações

Execute os projetos a partir da raiz, por exemplo:

```cmd
dotnet run --project tests/BookmarkChecks
dotnet run --project tests/BrowserUpdateChecks
dotnet run --project tests/MonitoringServerChecks
```

Com Node.js instalado, as verificações dos filtros e scripts Android são:

```cmd
node scripts/build-filter-assets.js --check
node tests/CottonMobile/page_protection_check.js
node tests/CottonMobile/youtube_protection_check.js
```

Mais informações: [navegador Windows](src/LeanBrowser/README.md),
[Android](src/CottonMobile/README.md), [monitoramento](src/MonitoringServer/README.md)
e [instalação e atualizações](UPDATES.md).
