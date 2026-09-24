# Atualizações do CottonBrowser

## Instalação no menu Iniciar

Execute `build-installer.cmd` na pasta principal para gerar
`CottonInstaller/dist/CottonBrowserSetup.exe`. Ao abrir esse instalador e clicar
em **Instalar**, o navegador é copiado para
`%LOCALAPPDATA%\Programs\CottonBrowser` e registrado no menu Iniciar e em
**Aplicativos instalados** do Windows, sem exigir permissões de administrador.
O atalho aparece em **Todos os aplicativos** e na pesquisa do Iniciar. Para
colocá-lo em **Fixado**, procure por CottonBrowser, clique com o botão direito
e escolha **Fixar em Iniciar**.

O instalador preserva o perfil de navegação em AppData. O pacote de atualização
continua sendo `CottonBrowser-win-x64.zip`; a Release também oferece
`CottonBrowserSetup.exe` para novas instalações. Ambos são gerados pelo mesmo
script. O navegador e o atualizador requerem .NET 8 Desktop Runtime e WebView2.

O navegador verifica em segundo plano a última Release estável do repositório
`tiaprendizabapa-collab/CottonBrowser` ao abrir e a cada três horas. Uma nova
versão aparece no menu de três pontos, com um ponto colorido no botão. Também é
possível usar **Verificar atualizações** no mesmo menu. A instalação só começa
depois da confirmação do usuário.

O pacote é baixado por HTTPS, limitado a 250 MB e conferido contra o SHA-256
informado pela API do GitHub. O atualizador espera o navegador fechar, faz um
backup dos arquivos substituídos, instala o novo executável e os arquivos da
Central e reinicia o programa. Perfil WebView2, histórico, favoritos e outras
preferências ficam em AppData e não são substituídos.

## Publicar uma versão

1. Mescle as alterações na `main` e escolha uma versão maior que a atual,
   começando por `v1.0.1`.
2. Crie e envie uma tag dessa versão: `git tag v1.0.1` e
   `git push origin v1.0.1`.
3. O workflow de GitHub Actions compila o navegador e o atualizador, cria
   `CottonBrowser-win-x64.zip` e `CottonBrowserSetup.exe` e publica a Release. O navegador só verá a
   atualização depois que o pacote estiver publicado.

As próximas versões seguem o mesmo formato de tag, por exemplo `v1.1.0`.
Commits ou PRs, sozinhos, não são distribuídos automaticamente. A primeira
instalação desta funcionalidade precisa ser feita manualmente; versões antigas
não possuem o código para consultar a API. O pacote requer .NET 8 Desktop
Runtime e WebView2 Runtime no Windows x64. Instalações em pastas sem permissão
de escrita, como `Program Files`, precisam de um instalador com elevação.
