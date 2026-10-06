# CottonBrowser

Navegador para Windows desenvolvido em C# / .NET 8 / WinForms, com o motor
Microsoft Edge WebView2. Inclui abas, favoritos, downloads, perfis de navegação
e proteção de anúncios por extensão, filtros de rede e scripts de página.

## Instalar em outro computador

Envie somente `CottonBrowserSetup.exe`. O instalador contém o navegador, o
atualizador, o runtime .NET 8 e o instalador offline do Microsoft Edge WebView2
Runtime para Windows x64. Quando necessário, o setup instala o WebView2 sem
baixar componentes adicionais durante a instalação.

Feche o navegador antes de executar o setup. A instalação fica em
`%LOCALAPPDATA%\Programs\CottonBrowser`, cria um atalho no menu Iniciar e
preserva os dados de navegação em AppData.

Consulte [UPDATES.md](../UPDATES.md) para instalação e publicação de versões.
A edição Android tem [instruções próprias](../CottonMobile/README.md).

## Compilar e rodar

Na pasta principal do repositório, execute `run.cmd`. Ele compila quando o
executável ainda não existe ou os arquivos de origem mudaram. Use `build.cmd`
para recompilar manualmente. Os scripts encontram a pasta do projeto mesmo
quando iniciados de outro diretório.

Nas alterações do dia a dia, atualize o executável existente com `build.cmd`,
sem gerar um setup a cada mudança. Se estiver usando a cópia instalada no
Windows, consulte a seção **Atualizar a instalação local durante o
desenvolvimento** em [UPDATES.md](../UPDATES.md).

Para compilar, é necessário o **SDK .NET 8**. O script detecta o SDK local em
`.tools\dotnet`, quando disponível, ou utiliza o `dotnet` instalado no Windows.
O primeiro restore pode precisar de acesso ao NuGet.

```cmd
build.cmd
run.cmd
```

Os arquivos publicados ficam em `LeanBrowser\dist`: `CottonBrowser.exe`,
`CottonUpdater.exe` e `Assets\Bridge`. O runtime .NET acompanha a publicação;
o WebView2 precisa estar instalado para executar essa versão diretamente.
Mantenha esses arquivos juntos. Para distribuir o aplicativo em um único
arquivo, use o setup.

Para executar durante o desenvolvimento, na pasta principal:

```cmd
dotnet run --project LeanBrowser\LeanBrowser.csproj
```

Para gerar o instalador:

```cmd
build-installer.cmd
```

O resultado fica em `CottonInstaller\dist\CottonBrowserSetup.exe`. A geração
do setup baixa o instalador offline do WebView2 quando ele ainda não está
disponível em `.tools\prerequisites`.

Gere o setup somente quando precisar distribuir ou instalar o navegador.
Mantenha o nome fixo `CottonBrowserSetup.exe`, substituindo o arquivo anterior
em vez de acumular cópias com a versão no nome.

## Atalhos

| Tecla | Ação |
|---|---|
| `Ctrl+L` / `Alt+D` | Focar a barra de endereços |
| `Ctrl+T` | Abrir nova aba |
| `Ctrl+Shift+T` | Reabrir a última aba normal fechada |
| `Ctrl+Shift+E` | Pesquisar nas abas abertas |
| `Ctrl+H` | Abrir a seção Histórico nas configurações |
| `Ctrl+W` | Fechar a aba selecionada |
| `Ctrl+Tab` / `Ctrl+Shift+Tab` | Próxima / anterior |
| `Ctrl+N` | Abrir nova janela |
| `Ctrl+Shift+N` | Abrir guia anônima |
| `Ctrl+D` | Adicionar ou atualizar favorito |
| `Ctrl+J` | Abrir downloads |
| `Ctrl++` / `Ctrl+-` / `Ctrl+0` | Aumentar, diminuir ou repor o zoom |
| `F5` / `Ctrl+R` | Recarregar |
| `Esc` | Parar carregamento ou sair da tela cheia |
| `Alt+←` / `Alt+→` | Voltar / avançar |
| `Alt+Home` | Abrir a página inicial |
| `F11` | Alternar tela cheia |
| `F9` | Entrar ou sair do modo de leitura |
| `Ctrl+Shift+A` | Alternar a proteção de anúncios e recarregar as abas |
| `F12` / `Ctrl+Shift+I` | Abrir o inspetor da guia atual |

O menu de três pontos e o menu de contexto também oferecem **Inspecionar
página**. O inspetor abre em uma janela separada do WebView2.

Quando o menu de três pontos ultrapassa a altura da janela, use a roda do
mouse para subir ou descer pelas opções. A rolagem também funciona sobre
os botões de zoom e nos submenus, respeitando os limites do menu.

Recarregar com `F5`, `Ctrl+R` ou o botão de recarga mantém o zoom da aba,
inclusive o zoom alterado com `Ctrl` e a roda do mouse. O áudio silenciado,
os grupos e a tela dividida também são preservados. No modo de leitura,
recarregar a mesma página atualiza o texto e mantém a fonte, o espaçamento
e o fundo escolhidos; navegar para outra página encerra o modo de leitura.

No YouTube, um vídeo que permanece sem metadados em `0:00 / 0:00` por
cerca de 20 segundos recebe uma tentativa de recuperação do player.
Se continuar sem carregar, aparecem **Tentar carregar vídeo** e
**Recarregar página**, sem precisar fechar o navegador. A recuperação
preserva volume, áudio silenciado e velocidade; não reinicia vídeos
já carregados, pausados ou lives. Ela funciona também com a proteção
de anúncios desligada.

## Favoritos, leitura e personalização

Em **Configurações > Favoritos**, crie pastas e subpastas, renomeie favoritos,
edite endereços, mova itens para outra pasta e use as setas para reordenar.
**Importar HTML** aceita arquivos exportados por outros navegadores e mantém
a hierarquia de pastas; **Exportar HTML** gera um arquivo compatível. A importação
aceita até 10 MB e 20.000 favoritos por arquivo, sem substituir os já salvos.

No menu de três pontos, **Modo de leitura** extrai o texto principal da página
e permite ajustar tamanho da fonte, espaçamento e fundo claro ou escuro.
**Voltar à página**, ou `F9`, recupera o documento original. Páginas sem texto
suficiente continuam no modo normal.

**Traduzir página** oferece português, inglês, espanhol e francês usando o
Google Tradutor. O endereço da página pública é enviado ao serviço. Páginas
internas, endereços locais e URLs com credenciais não são aceitos; conteúdo que
exige login pode não estar disponível no tradutor. O submenu permite voltar
à página original.

**Abas verticais** move as abas para a lateral, mantendo grupos, busca e abas
fixadas. O botão na lateral recolhe a lista para ícones. **Tela dividida** permite
escolher outra aba e ajustar a largura das duas páginas. A divisão usa os
documentos já abertos; encerrá-la preserva as duas abas. Selecionar outra aba
ou fechar uma das páginas encerra a divisão.

**Capturar página** oferece área visível, página inteira ou seleção por arraste,
com prévia para copiar a imagem ou salvar em PNG. Páginas acima de 40 milhões
de pixels, 16.384 pixels de largura ou 32.768 de altura devem ser capturadas
por partes.

**Gerenciar perfis** cria e renomeia perfis, como Pessoal e Trabalho. Cada um
abre em uma janela própria e mantém cookies, logins, favoritos, histórico,
sessão, preferências e dados do WebView2 separados. O perfil padrão conserva
os dados existentes. Novos perfis ficam em `%LOCALAPPDATA%\LeanBrowser\Profiles\<id>`;
o cadastro dos nomes fica em `browser-profiles.json` no diretório principal.

Em **Configurações > Nova guia**, escolha um dos quatro fundos, mostre ou
oculte relógio, saudação e atalhos, e adicione, edite, remova ou reordene até
12 atalhos. As alterações são aplicadas às novas guias abertas do mesmo perfil.
O menu **Personalizar nova guia** abre diretamente essa seção.

## Barra de endereços

| Você digita | Resultado |
|---|---|
| `https://exemplo.com/a` | Abre o endereço informado |
| `exemplo.com` | Abre `https://exemplo.com` |
| `localhost:3000` | Abre `http://localhost:3000` |
| `//cdn.exemplo.com` | Abre `https://cdn.exemplo.com` |
| `como fazer pão` | Pesquisa no buscador escolhido |

A barra sugere endereços do histórico local. O X da sugestão remove essa
entrada. Cada aba mantém seu próprio histórico de navegação.

Em **Configurações > Pesquisa**, escolha Google, Bing ou DuckDuckGo. As
sugestões remotas continuam sendo fornecidas pelo DuckDuckGo, independentemente
do buscador escolhido; nas abas anônimas elas não são consultadas.

## Sessão, organização de abas e desempenho

**Configurações > Inicialização** permite continuar com as abas normais da
sessão anterior. Um encerramento inesperado oferece recuperar a sessão.
A recuperação mantém as janelas, a aba selecionada, os grupos, as cores,
as abas fixadas e o estado de áudio silenciado. Abas anônimas e páginas
auxiliares não são gravadas. Fechar a última aba explicitamente deixa a
sessão vazia; fechar a janela preserva suas abas para o próximo início.
A restauração reabre os endereços e não recupera textos não enviados,
a posição da página nem o histórico de voltar/avançar.

Clique com o botão direito numa aba para fixá-la, criar um grupo com nome
e cor, mover para um grupo ou editar o grupo atual. Abas fixadas ficam
compactas no início. O ícone de áudio permite silenciar a aba. A lupa na
faixa de abas, ou `Ctrl+Shift+E`, pesquisa títulos, endereços e grupos.

Em **Configurações > Desempenho**, ative a economia de memória, escolha
um intervalo de 1 a 240 minutos (10 por padrão) e adicione os sites que
devem continuar ativos. A suspensão preserva o documento e termina ao
selecionar a aba. Abas visíveis, carregando, com áudio/vídeo, captura de
câmera/microfone/tela ou sinais de edição de formulários permanecem ativas.
Downloads em andamento também impedem a suspensão. São verificações
conservadoras; páginas que fazem trabalho em segundo plano podem ser
incluídas nas exceções. Um “z” identifica as abas suspensas.

O histórico armazena até 10.000 visitas e mostra datas e horários. O item
**Histórico** do menu de três pontos e o atalho `Ctrl+H` abrem diretamente essa
seção dentro das configurações. O item da barra lateral mostra o mesmo painel.
Se a aba de configurações já estiver aberta, ela é reutilizada.
Ele permite pesquisar sem diferenciar acentos, excluir visitas selecionadas
ou apagar um período. A exclusão por período também atinge os resultados
fora da pesquisa atual. Visitas anônimas não são registradas.

## Proteção de anúncios

A proteção combina três camadas:

- **uBlock Origin Lite:** pacote incorporado ao executável, instalado uma vez
  por perfil antes da navegação.
- **Lista básica de domínios:** bloqueio de recursos de rede pelo WebView2.
- **Scripts de página:** tratamento de diálogos reconhecidos, limpeza de
  elementos publicitários e complemento específico para o YouTube.

`Ctrl+Shift+A` ou **Proteção > Bloquear anúncios** alterna as camadas e
recarrega as abas. No modo avançado, o menu permite configurar os filtros do
uBlock e permitir novas janelas na origem atual.

A lista básica pode ser substituída por
`%APPDATA%\LeanBrowser\blocklist.txt`: um domínio por linha, com `#` para
comentários. O formato `0.0.0.0 dominio.com` também é aceito. Se o arquivo não
existir, o navegador usa a lista incorporada. Alterações nessa lista são
carregadas ao criar novas abas.

Os filtros incorporados do uBlock acompanham o pacote do aplicativo. A
proteção do YouTube depende da estrutura do site e não garante remover todos
os anúncios. A integração dos scripts está documentada em
[Assets/Recovery/INTEGRACAO.md](Assets/Recovery/INTEGRACAO.md).

## Favoritos, senhas e perfil

`Ctrl+D` salva ou atualiza o favorito da página atual. Os favoritos ficam
disponíveis no menu e na barra de favoritos. O WebView2 cuida das sugestões,
salvamento e preenchimento de senhas; essas opções ficam desativadas nas
guias anônimas.

| Dados | Local |
|---|---|
| Favoritos | `%LOCALAPPDATA%\LeanBrowser\bookmarks.json` |
| Histórico de sugestões | `%LOCALAPPDATA%\LeanBrowser\history.json` |
| Histórico de downloads | `%LOCALAPPDATA%\LeanBrowser\downloads.json` |
| Sessão de abas normais | `%LOCALAPPDATA%\LeanBrowser\session.json` |
| Inicialização, desempenho e buscador | `%LOCALAPPDATA%\LeanBrowser\preferences.json` |
| Perfil WebView2, cookies e logins | `%LOCALAPPDATA%\LeanBrowser\WebView2` |

Os diretórios de dados mantêm o nome `LeanBrowser`. O perfil pertence ao
CottonBrowser e é separado do perfil pessoal do Edge. Instalações e
atualizações preservam esses dados.

No modo avançado, a exclusão de senhas salvas exige confirmação. Ela não
encerra sessões autenticadas nem limpa campos já preenchidos. Mais detalhes
e verificações estão em [FEATURES.md](FEATURES.md).

## Atualizações

O navegador consulta a última Release estável do repositório ao abrir e a
cada três horas. Uma nova versão aparece em um aviso e no menu de três pontos.
Também é possível usar **Verificar atualizações** ou **Atualizar CottonBrowser**.

O pacote é baixado pelo aplicativo, conferido com o SHA-256 informado na
Release e instalado pelo atualizador, que reinicia o navegador. Uma alteração
nos arquivos de origem só chega às outras instalações depois que uma nova
Release é publicada. O processo está descrito em [UPDATES.md](../UPDATES.md).

## Easter egg do algodão

Clique cinco vezes rapidamente no ícone de algodão à esquerda da faixa de
abas para reproduzir o jumpscare do Foxy com vídeo e áudio incorporados.
Pressione `Esc` ou clique no vídeo para fechar.

## Monitoramento opcional

O envio de eventos de navegação só é ativado quando
`COTTON_MONITORING_ENDPOINT` e `COTTON_MONITORING_TOKEN` estão configurados.
O servidor e o painel Admin têm
[documentação própria](../MonitoringServer/README.md).

`Ctrl+Shift+Alt+M` abre o painel local em `http://localhost:5270/admin` no
modo avançado. O servidor precisa estar ativo e o acesso à API exige o
token Admin configurado nele.

