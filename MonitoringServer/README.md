# CottonBrowser Monitoring Server

Servidor opcional para receber eventos de navegação do CottonBrowser. O
cliente só envia dados quando `COTTON_MONITORING_ENDPOINT` e
`COTTON_MONITORING_TOKEN` estão definidos.

Configure tokens fortes antes de iniciar:

```powershell
$env:COTTON_INGEST_TOKEN = "token-de-ingestao-longo-e-aleatorio"
$env:COTTON_ADMIN_TOKEN = "token-de-admin-longo-e-aleatorio"
dotnet run --project .\MonitoringServer --urls http://localhost:5270
```

Para o navegador, use o endpoint completo e o token de ingestão:

```powershell
$env:COTTON_MONITORING_ENDPOINT = "https://servidor.exemplo/api/telemetry/navigation"
$env:COTTON_MONITORING_TOKEN = $env:COTTON_INGEST_TOKEN
```

Os relatórios e o hub SignalR exigem o token Admin. O armazenamento padrão é
limitado a 50 mil eventos em memória para facilitar o teste local. O arquivo
`schema.sql` contém o modelo PostgreSQL particionado para substituir esse
armazenamento em produção.

Abra `http://localhost:5270/admin.html` para usar o painel de monitoramento
original. Informe o token Admin no próprio painel; ele consulta os relatórios
e mantém uma conexão SignalR para mostrar novas navegações em tempo real.

## Novo Admin Dashboard (protótipo)

`/admin` abre agora a estrutura modular em `wwwroot/admin/`. O painel de
monitoramento anterior permanece em `/admin.html`. A nova tela permite revisar
rascunhos de URLs permitidas/bloqueadas, IDs de extensões e a preferência
`blockExeDownloads` por escopo global ou departamento. O modo Demonstração não
usa a API; seus dados se perdem ao recarregar a página.

### Permitir um site no CottonBrowser deste computador

Execute o servidor e o CottonBrowser atualizado **no mesmo usuário do Windows**.
Abra `/admin`, conecte com `COTTON_ADMIN_TOKEN`, selecione **Toda a empresa** e
acesse **Controle de segurança**. Em **Sites permitidos**, informe, por exemplo,
`192.168.3.205/login`, clique **Adicionar**, depois **Salvar rascunho** e
**Ativar sites neste computador**. Reabra ou atualize a página no navegador.
O endereço é convertido em `http://192.168.3.205/`: a exceção vale para toda
a origem (mesmo protocolo, IP e porta), não só para `/login`. Se o site usar
HTTPS ou uma porta diferente, cadastre essa origem separadamente. HTTP não
criptografa credenciais nem dados.

A ativação grava `%LOCALAPPDATA%\LeanBrowser\site-exceptions.json`, que o
navegador lê a cada navegação principal. A regra dispensa a troca forçada de
HTTP por HTTPS e o bloqueador básico embutido para a origem permitida. **Ela
não desativa a verificação de malware nem a extensão uBlock Origin Lite.**
Remover um site exige salvar e ativar novamente. O modo Demonstração não
aplica nada. O servidor precisa ser reiniciado após atualizar o código, e um
executável antigo do navegador não conhece essa funcionalidade.

Com um token Admin, o cliente chama `GET` e `PUT
/api/admin/policy-drafts?scope=global` (ou `department:ti`,
`department:marketing`, `department:financeiro`). O `PUT` exige
`expectedRevision` e retorna 409 quando outra sessão já gravou uma revisão.
O token fica apenas na memória da aba. As rotas exigem a política `Admin`
do servidor; o token de ingestão não pode alterá-las.
`GET /api/admin/site-exceptions` informa o que está ativo neste usuário do
Windows. `POST /api/admin/site-exceptions/apply` recebe
`{"expectedRevision": 1}` e só grava a lista se a revisão ainda for a atual.

No CottonBrowser atualizado, o gestor em **modo avançado** pode abrir o Admin
com `Ctrl+Shift+Alt+M`. O atalho não aparece no menu e abre uma nova aba em
`http://localhost:5270/admin`; se o servidor não estiver ativo, o navegador
mostra uma orientação. No modo padrão, o atalho não é interceptado. O modo
avançado só altera a interface: **não substitui o token Admin exigido pelo
servidor**. Quem souber a URL ainda pode abrir a tela de login, mas não acessar
as APIs sem o token.

Organização: `wwwroot/admin/index.html` contém o layout; `styles.css`, o visual;
`app.js` e `state.js`, a navegação e o estado; `security.js` e `overview.js`,
as telas; `api.js`, os adaptadores HTTP e demonstração. Os contratos, validação
e armazenamento de rascunhos estão em `Admin/`.

**Limitações deliberadas:** os rascunhos ficam só em memória no servidor e são
perdidos ao reiniciar; apenas as exceções locais de sites permitidos persistem.
Não há SSO, banco conectado, distribuição das políticas,
confirmação de aplicação nos dispositivos, aplicação das URLs bloqueadas ou das
regras de extensões, DLP ou atualização remota. O arquivo local é modificável
pelo usuário do Windows e **não constitui política corporativa à prova de
adulteração**. O toggle de uploads não autorizados está desabilitado
e a API rejeita sua ativação. O arquivo `admin_policy_schema.sql` é um modelo
para migração futura, não uma migração executada pelo aplicativo. Não use esta
versão como controle corporativo de segurança em produção.

Para verificar modelos e concorrência sem pacotes de teste adicionais:

```powershell
dotnet run --project .\MonitoringServerChecks
```
