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
script. O instalador, o navegador e o atualizador incluem o runtime .NET 8;
o WebView2 Runtime ainda precisa estar disponível no Windows.

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

## Publicar atualizações

Ao mesclar alterações na `main`, o GitHub Actions compila o navegador e o
atualizador, cria `CottonBrowser-win-x64.zip` e `CottonBrowserSetup.exe` e
publica uma Release estável automaticamente. A versão recebe o próximo número
de patch acima da Release mais recente. Não é preciso criar uma tag nem executar
comandos no PowerShell para distribuir atualizações normais.

O navegador verifica a Release publicada ao abrir e a cada três horas. Ele só
oferece a instalação depois que a compilação do GitHub Actions termina e os
arquivos estão disponíveis. Para publicar uma versão manualmente, ainda é possível
enviar uma tag `vMAJOR.MINOR.PATCH` maior que a versão publicada.

A primeira instalação do recurso de atualização precisa ser manual em versões
antigas que não consultam a API do GitHub. O pacote requer WebView2 Runtime no
Windows x64. Instalações em pastas sem permissão de escrita, como `Program
Files`, precisam de um instalador com elevação.
