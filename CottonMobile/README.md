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
- WebView com proteção contra conteúdo misto, acesso a arquivos locais e
  JavaScript bridge desativados. O APK distribuído não permite depuração.
- Bloqueio local de anúncios e rastreadores conhecidos, com a mesma lista de
  domínios da versão para Windows. Toque no escudo da barra de endereço para ver
  os recursos barrados ou desativar/reativar a proteção no site atual.

Os favoritos, histórico e senhas do Windows não são copiados para o Android.
O bloqueio Android atua nos recursos de rede do WebView e dos service workers.
Ele não cobre anúncios hospedados no mesmo domínio do conteúdo nem todos os
redirecionamentos; o bloqueador baseado na extensão WebView2 não funciona no
Android. O modo anônimo do Windows não está incluído nesta prévia. Nenhuma
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
