# Proteção de anúncios — integração de 18/09/2026

## Origem do pacote enviado

`Original-v1.1.3.zip` preserva o arquivo Bloqueio_Cinema_Recuperacao_v1.1.3.zip enviado pelo usuário.
`dialog-guard.js` e `overlay-cleaner.js` foram copiados desse pacote sem alteração e são incorporados ao executável. O instalador .bat não é executado.

## Integração

- O uBlock Origin Lite já incluído no projeto é habilitado e instalado uma vez por perfil antes da navegação inicial. Seus filtros de rede e scripts complementam a lista básica de domínios.
- DocumentProtection registra os scripts do pacote no início de cada documento HTTP/HTTPS, inclusive frames. O modo de diálogos é `known`, que recusa ofertas reconhecidas de extensões sem aceitar confirmações.
- A limpeza visual conservadora do pacote opera nos demais sites; no YouTube usa-se o complemento específico para evitar varrer todos os elementos a cada mudança de página.
- O comportamento de bloqueio de novas janelas do pacote é adaptado à política nativa PopupPolicy. Uma exceção explícita está disponível em Proteção > Permitir novas janelas neste site e expira ao mudar de origem.
- O código Chrome de background/bridge/popup não é instalado como uma segunda extensão. O menu nativo substitui seus controles. Cinema e bloqueio indiscriminado de alertas não são ativados.
- O complemento do YouTube oculta elementos publicitários conhecidos e aciona botões de pular visíveis somente quando o player sinaliza um anúncio. Não avança o tempo, não acelera nem silencia vídeos.
- Ctrl+Shift+A e o menu Proteção alternam as camadas de proteção, com recarga das abas para aplicar a mudança. As configurações do uBlock ficam acessíveis pelo mesmo menu.

## Validação

Teste com perfil isolado no WebView2: instalação e ativação da extensão; listas de rede e scripts registrados; correspondência de regra para googleads.g.doubleclick.net; carregamento do painel; desativação/reativação; pop-ups e exceções por origem; injeção dos scripts enviados; preservação de players; limpeza e botão de pular em uma página de teste com a estrutura do YouTube.

Não foi validada uma sessão real de reprodução com anúncios do YouTube. As estruturas e métodos de anúncios podem mudar; a integração não garante bloquear todos os anúncios, especialmente os inseridos no próprio fluxo de vídeo. As listas do pacote uBlock embarcado não se atualizam automaticamente por uma loja de extensões.
