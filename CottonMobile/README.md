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

Os favoritos, histórico e senhas do Windows não são copiados para o Android.
O bloqueador baseado na extensão WebView2 e o modo anônimo do Windows não estão
incluídos nesta prévia. Nenhuma telemetria corporativa é ativada pelo app móvel.
Câmera, microfone e localização solicitados por sites também estão desativados
até que haja um fluxo de permissão por origem. A seleção de arquivos iniciada
pelo usuário continua disponível.

## Compilar

Abra esta pasta no Android Studio com JDK 17 e Android SDK 35. Execute
`gradle :app:assembleRelease` para produzir
`app/build/outputs/apk/release/app-release.apk`. A publicação do repositório
compila o mesmo APK em GitHub Actions e o anexa à Release.
