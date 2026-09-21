# CottonBrowser Monitoring Server

Servidor opcional para receber eventos de navegação do CottonBrowser. O
cliente só envia dados quando `COTTON_MONITORING_ENDPOINT` e
`COTTON_MONITORING_TOKEN` estão definidos.

Configure tokens fortes antes de iniciar:

```powershell
$env:COTTON_INGEST_TOKEN = "token-de-ingestao-longo-e-aleatorio"
$env:COTTON_ADMIN_TOKEN = "token-de-admin-longo-e-aleatorio"
dotnet run --project .\MonitoringServer
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

Abra `http://localhost:5270/admin` para usar o painel Admin. Informe o token
Admin no próprio painel; ele consulta os relatórios e mantém uma conexão
SignalR para mostrar novas navegações em tempo real.
