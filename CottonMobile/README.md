# CottonBrowser para Android (prévia)

Esta edição usa o WebView do Android. O executável Windows, o WebView2 e o
instalador `.exe` não funcionam em celulares; por isso o aplicativo móvel tem
interface e perfil próprios.

## Instalar

1. Abra a [última Release do CottonBrowser](https://github.com/tiaprendizabapa-collab/CottonBrowser/releases/latest) no celular.
2. Baixe `CottonBrowser-Android-preview.apk` e abra o arquivo.
3. Se o Android pedir, permita a instalação de aplicativos dessa fonte apenas
   para concluir esta instalação.

Requer Android 8.0 (API 26) ou mais recente. O APK de prévia é assinado com uma
chave de desenvolvimento gerada na compilação. Compilações futuras podem usar
outra chave e exigir a remoção da prévia antes de instalar uma nova. Isso apaga
os dados locais da prévia; não a use como única cópia de favoritos importantes.

## Recursos desta edição

- Endereço ou pesquisa na mesma barra; navegação, atualização e múltiplas abas.
- Favoritos locais, downloads pelo gerenciador do Android e links que pedem
  nova janela em uma nova aba.
- Faixa de favoritos abaixo da barra de endereços; toque para abrir e mantenha
  pressionado para remover. Histórico local recente e busca de texto na página.
- Menu com acesso a downloads, compartilhamento, mídia direta da página e
  atualização. O aviso de nova versão aparece sobre a página e oferece
  **Não mostrar novamente**; a instalação ocorre pela Release do GitHub.
- WebView com proteção contra conteúdo misto, acesso a arquivos locais e
  JavaScript bridge desativados. Endereços HTTP sem criptografia exigem
  confirmação por origem durante a sessão. O APK distribuído não permite depuração.
- Bloqueio local de anúncios e rastreadores: lista básica mais 46.728 domínios
  extraídos da EasyList embarcada, preservando 519 exceções de compatibilidade.
  Filtros CSS ocultam espaços publicitários conhecidos e o primeiro destino de
  popups é verificado, inclusive quando um toque no site abriu a janela.
  Toque no escudo para ver a cobertura e os recursos barrados ou desativar/reativar
  a proteção no site atual. Endereços digitados continuam acessíveis.
- Nas páginas do YouTube, um filtro adicional remove instruções publicitárias
  reconhecidas em dados iniciais, respostas fetch e XHR do player móvel. Também
  oculta slots e aciona botões de pular anúncio já disponíveis. O filtro mantém
  os endereços de vídeo, posição e volume dos vídeos comuns.

Os favoritos, histórico e senhas do Windows não são copiados para o Android.
O histórico móvel fica apenas no aparelho e pode ser limpo no menu. O download
de mídia aceita somente URLs diretas HTTP(S) expostas pela página; streams
segmentados, vídeos protegidos e URLs `blob:` (comuns no YouTube) não são
baixados por essa opção. Para essas páginas, o download depende dos recursos
oferecidos pelo próprio site.
O bloqueio Android atua nos recursos de rede do WebView e dos service workers.
Ele não cobre todos os anúncios hospedados no mesmo domínio do conteúdo nem
todos os redirecionamentos. A lista móvel importa apenas regras de domínio
incondicionais; regras que dependem do tipo de recurso/terceiro não são
convertidas em bloqueio absoluto. As exceções são conservadoras e podem permitir
alguns anúncios para manter o site funcional. A origem de service workers pode
não estar disponível. Dados dos filtros, licença e gerador estão documentados
em `app/src/main/assets/FILTERS-NOTICE.txt`.
O filtro do YouTube depende do WebView instalado e
pode precisar de ajustes quando o site mudar; anúncios ainda podem aparecer.
Em provedores antigos, a injeção ocorre após o carregamento e pode perder a
primeira resposta do player; atualize o Android System WebView no aparelho.
Vídeos com publicidade inserida diretamente no stream não são removidos pelo
filtro de resposta. O comportamento em cada aparelho deve ser verificado após
instalar o APK atualizado.
O bloqueador baseado na extensão WebView2 não funciona no Android. O modo
anônimo, o painel Admin e o inspetor WebView2 do Windows não estão incluídos
nesta prévia. Nenhuma
telemetria corporativa é ativada pelo app móvel.
Caso um service worker não informe o site de origem da requisição, a exceção
por site pode não se aplicar a essa requisição. A contagem exibida na guia
considera apenas os recursos interceptados diretamente pelo WebView.
Câmera, microfone e localização solicitados por sites também estão desativados
até que haja um fluxo de permissão por origem. A seleção de arquivos iniciada
pelo usuário continua disponível.

## Compilar

Abra esta pasta no Android Studio com JDK 17 e Android SDK 35. Execute
`gradle :app:assembleRelease` para produzir
`app/build/outputs/apk/release/app-release.apk`. A publicação do repositório
compila o mesmo APK em GitHub Actions e o anexa à Release.

## Verificação da proteção

O pipeline valida a geração reproduzível dos filtros, regras permitidas e
bloqueadas, exceções por origem, reprodução/login legítimos e os hooks do player
móvel (JSON/fetch/XHR). Um teste instrumentado em emulador Android 15 exercita
o WebView da Activity: recurso publicitário interceptado, slot ocultado,
injeção antecipada no YouTube, popup conhecido barrado e exceção por site.
São páginas controladas para regressão, não uma garantia de cobertura de todos
os anúncios ao vivo do YouTube.
