# CP3-I03 — RabbitMQ, Worker e idempotência

Referência: [Racass/checkpoint-csharpracass-expensehub#13](https://github.com/Racass/checkpoint-csharpracass-expensehub/issues/13).

## Arquitetura e contrato

Fluxo: **API → RabbitMQ → ExpenseHub.Worker → SQLite compartilhado**.

A API valida o usuário, roles, período e escopo da I02, salva Pending e chama `RabbitMqReportPublisher`.
O produtor real substitui o publicador indisponível da I01/I02; o substituto sem confirmação agora
existe somente no projeto de testes. Nenhum endpoint calcula relatórios.

O cliente `RabbitMQ.Client` 7.2.1 usa canal com `publisherConfirmationsEnabled: true` e
`publisherConfirmationTrackingEnabled: true`. `BasicPublishAsync` é aguardado com timeout;
`mandatory: true` também detecta mensagem devolvida por falta de rota. A fila `expensehub.reports`
é declarada `durable: true`, `exclusive: false`, `autoDelete: false`. As mensagens usam
`Persistent = true`, `ContentType = application/json`, MessageId e CorrelationId iguais ao reportId.
A conexão/canal do produtor são reutilizados; um semáforo serializa publicações no canal.

Esses modos de confirmação seguem o [guia oficial de publisher confirms](https://www.rabbitmq.com/tutorials/tutorial-seven-dotnet).
Conexões e canais são duradouros, conforme o [guia oficial do cliente .NET](https://www.rabbitmq.com/client-libraries/dotnet-api-guide).

O corpo transporta **somente**:

```json
{"reportId":"08d1b86a-96eb-412c-96d2-2f753ab290ac"}
```

`ReportMessage` rejeita JSON inválido, ID vazio/inválido, campos extras (incluindo identidade e escopo)
e payload acima de 1 KiB. O worker não desserializa mensagens como entidades EF nem aceita parâmetros
de autorização da fila. Ele carrega solicitante, período e escopo do registro salvo no servidor.

| Evento | Resposta da API |
|---|---|
| Banco falha antes de registrar | 503 sanitizado, sem ID fictício e sem publicação |
| Registro salvo, publicação falha/timeout/nack/return | 503 com ID persistido e erro sanitizado; Pending preservado |
| Registro salvo e broker confirma | 202 + Location `/api/reports/{id}` + envelope |

O timeout do produtor não equivale a certeza de que nada foi publicado. A mensagem pode ter chegado.
O consumidor pode inclusive concluir antes da consulta seguinte. A resposta 503 de publicação não
marca o relatório Failed e não altera resultados eventualmente gravados pelo worker.

## Configuração externa

A API lê `appsettings.json`; o Worker lê exclusivamente `appsettings.Worker.json` no diretório do
executável, seguido de variáveis de ambiente e argumentos de linha de comando. Isso evita herdar
o caminho relativo do banco da API e conflitos no empacotamento. Ambos aceitam estas variáveis
(sem valores secretos versionados):

| Variável | Padrão / função |
|---|---|
| `RabbitMq__HostName` | localhost |
| `RabbitMq__Port` | 5672; configurar 5671 quando o servidor TLS usar essa porta |
| `RabbitMq__VirtualHost` | / |
| `RabbitMq__QueueName` | expensehub.reports; deve ser igual nos dois processos |
| `RabbitMq__UserName` | Obrigatória, sem padrão |
| `RabbitMq__Password` | Obrigatória, sem padrão |
| `RabbitMq__UseTls` | false para o broker local; configurar true conforme o ambiente |
| `RabbitMq__TimeoutSeconds` | 10; limite de 1 a 120 segundos |
| `ConnectionStrings__ExpenseHub` | O mesmo arquivo SQLite absoluto nos dois processos |
| `Worker__MaxAttempts` | 5; limite de 1 a 20 |
| `Worker__RetryDelaySeconds` | 5; limite de 1 a 300 |
| `Worker__PollIntervalMilliseconds` | 500; limite de 50 a 60000 |

Credenciais ausentes não impedem a API de oferecer os demais endpoints: o POST persiste Pending e
retorna 503 sanitizado ao tentar publicar. No Worker, configuração inválida impede consumo.
TLS usa validação normal do certificado e o hostname configurado; não há bypass de segurança.
Não coloque senhas em appsettings versionados, URLs, comandos com valores literais ou logs.

## RabbitMQ local e execução

Requer Docker com Compose 2.30+ (suporte a `env_file.format: raw`) e .NET SDK 10. O arquivo `infrastructure/compose.rabbitmq.yml` mantém
os dados do broker em volume nomeado e expõe portas somente em localhost. O exemplo usa a tag
`rabbitmq:4-management`; fixe versão/digest no ambiente de entrega da I06 para reprodutibilidade.

Crie uma única vez `infrastructure/rabbitmq.env` a partir de `infrastructure/rabbitmq.env.example`,
sem sobrescrever um arquivo existente. O padrão versionado contém campos vazios, não credenciais.
Preencha `RABBITMQ_DEFAULT_USER` e `RABBITMQ_DEFAULT_PASS` no arquivo local usando um editor seguro
ou seu gerenciador de segredos. Restrinja o acesso ao arquivo (`chmod 600 infrastructure/rabbitmq.env`).
Ele já é ignorado pela regra `*.env` do Git; não use `git add -f`.

O formato raw preserva `$`, `#` e outros caracteres literalmente; não coloque aspas de shell ao redor
dos valores e não execute esse arquivo com `source`. O Compose exige que o arquivo exista. Antes de
iniciar o entrypoint oficial do RabbitMQ, o wrapper exige usuário e senha não vazios, evitando fallback
para credenciais padrão. Em volume existente, utilize as credenciais já provisionadas; não apague dados.

Configure também `RabbitMq__UserName` e `RabbitMq__Password` externamente, com os mesmos valores,
para API e Worker. O arquivo do Compose não exporta variáveis para os processos no host.
A partir da raiz do repositório, sem imprimir seus valores:

```sh
docker compose -p expensehub-cp3 -f infrastructure/compose.rabbitmq.yml up -d
export RabbitMq__HostName=localhost
export RabbitMq__QueueName=expensehub.reports
export ConnectionStrings__ExpenseHub="Data Source=$PWD/sources/ExpenseHub.Api/expensehub.db"
dotnet build sources/ExpenseHub.slnx
dotnet run --project sources/ExpenseHub.Api --no-build
```

Inicie a API primeiro para inicializar o esquema existente. Em outro terminal, com as mesmas
variáveis de RabbitMQ e a **mesma connection string absoluta**, execute:

```sh
dotnet run --project sources/ExpenseHub.Worker --no-build
```

O Worker é um executável separado, não um serviço hospedado dentro da API. Não fornece nova
rota HTTP nem requer tokens de usuários para consumir. Para a API, use as contas/roles e o
login Identity da CP2. Tokens devem permanecer locais e não entrar nas evidências versionadas.

O worker não cria automaticamente outro banco: a connection string é obrigatória, e a API deve
inicializar o esquema. SQLite continua sendo arquivo compartilhado local, não serviço remoto.
Containers/processos separados precisam ver o mesmo arquivo; máquinas distintas exigem uma
estratégia de persistência compartilhada fora do escopo desta issue.

Para pausar o broker sem apagar dados:

```sh
docker compose -p expensehub-cp3 -f infrastructure/compose.rabbitmq.yml stop
```

Não use remoção de volumes. As credenciais `RABBITMQ_DEFAULT_*` inicializam um volume novo;
mudar variáveis não redefine usuários de um broker já inicializado. Não apague o volume para contornar isso.

## Consumo, estados e ACK

O Worker usa `BasicGetAsync(autoAck: false)` com polling configurável e uma entrega por vez.
Fila vazia causa espera assíncrona; não existe busy loop. Esse desenho é intencionalmente simples,
adequado ao experimento obrigatório com um worker. Cada tentativa abre novo escopo DI/DbContext.

1. Valida a mensagem e carrega o relatório pelo ID.
2. Completed/Failed já persistidos retornam sem recalcular.
3. Pending ou Processing é reivindicado com `StartedAt` UTC estritamente crescente e Status Processing.
4. O claim é salvo antes de ler despesas. O cálculo usa apenas o solicitante, escopo e período persistidos.
5. Resultado/GeneratedAt/CompletedAt e Completed são salvos juntos; falha definitiva salva Failed,
   CompletedAt e erro fixo `REPORT_PROCESSING_FAILED`, sem resultado.
6. `BasicAckAsync(multiple: false)` só acontece depois de `SaveAsync` confirmar o estado terminal
   ou depois de ler um estado terminal previamente salvo.

`GET /api/reports`, detalhe e Excel continuam com isolamento I02. Excel próprio não Completed
continua 409. As despesas originais não são modificadas. Logs de publicação, processamento,
conclusão, falha e ACK usam reportId; mensagens rejeitadas não expõem seu corpo nos logs.

## Idempotência persistente e retomada

A proteção está no SQLite, não em coleção ou cache do Worker:

- Completed preserva exatamente ResultJson, GeneratedAt e CompletedAt nas duplicatas, inclusive
  após encerrar e recriar o processo/DbContext. Failed também é terminal.
- `Status` **e `StartedAt`** são tokens de concorrência otimista do EF. Não há nova coluna ou
  migração: a I03 usa a coluna StartedAt já criada pela I01.
- Cada retomada incrementa StartedAt se o relógio não avançou. O UPDATE compara o estado e o
  timestamp originais. Uma tentativa antiga não pode salvar sua conclusão após um novo claim.
- Duas tentativas podem repetir o cálculo; apenas a tentativa que ainda possui o claim pode salvar
  o efeito terminal. Uma tentativa perdedora não recebe ACK de sucesso e deve reler o banco.
- Queda antes do ACK deixa a mensagem não confirmada; o fechamento da conexão faz o broker
  reentregá-la. Processing pode ser retomado imediatamente pelo worker reiniciado.
- O desenho é para um consumidor serial; concorrência adicional não é uma exigência. O controle
  persistente também impede sobrescritas entre claims concorrentes, mas não pretende otimizar escala.

Não se afirma entrega exatamente uma vez: o transporte admite reentrega e o cálculo pode repetir.
O efeito terminal salvo é protegido contra duplicação/sobrescrita.

## Falhas e retries limitados

| Falha | Comportamento |
|---|---|
| JSON inválido, ID vazio ou campos extras | Reject sem requeue; não cria relatório nem ACK de cálculo |
| ID válido inexistente no banco | Reject sem requeue, log por ID; sem loop |
| Dados de negócio inválidos / cálculo definitivo | Salva Failed sanitizado; ACK somente após salvar |
| DbException/DbUpdateException durante cálculo | Propaga para retry, não converte indisponibilidade em Failed |
| Falha ao salvar Completed ou o próprio Failed | Sem ACK; nova tentativa em novo DbContext |
| Concorrência perdida | Sem ACK prematuro; nova tentativa recarrega o estado |
| ACK falha após commit | Encerra worker/conexão; reentrega lê o terminal já salvo |
| Cancelamento/desligamento | Sem ACK antecipado; conexão é descartada |

Cada entrega tem no máximo `MaxAttempts`, com espera `RetryDelaySeconds` entre tentativas.
Esgotadas as tentativas, o Worker termina com código de saída 1 e fecha a conexão sem ACK,
devolvendo a entrega pendente ao broker. Isso evita um loop infinito interno de requeue em caso
de banco indisponível. Corrija a causa e reinicie; um supervisor deve usar política de restart com
backoff. Conexão inicial tem o mesmo limite; perda de conexão/canal durante consumo encerra o processo.
O produtor reabre conexões/canais fechados na próxima solicitação. Não existe recuperação universal.

Falhas definitivas já salvas não são reenviadas infinitamente. Não há DLQ nesta implementação;
mensagens malformadas/inexistentes são descartadas pelo Reject, com registro no log. Isso é uma
limitação operacional documentada, permitida pela issue. Administração da fila não deve republicar
payloads de autoridade, nem trocar owner/escopo de registros salvos.

## Janela sem Transactional Outbox

Salvar no SQLite e publicar no RabbitMQ são duas operações independentes:

- Queda depois do commit e antes da publicação pode deixar Pending **sem mensagem**.
- Confirmação perdida pode deixar incerto se a mensagem chegou ao broker.
- Reiniciar o Worker não recupera uma mensagem que nunca chegou à fila.
- Ausência de confirmação produz 503 com ID salvo; o usuário pode consultar esse ID.
- Repetir POST cria outra solicitação; reentregar o mesmo reportId é outra operação.

Não existe outbox/republicação automática nesta entrega; não são obrigatórias pela issue.
Reprocessamento administrativo futuro deve republicar apenas IDs existentes, preservando ownership
nas operações expostas a usuários. Nenhuma rota administrativa adicional foi criada.

## Testes e evidências

```sh
dotnet build sources/ExpenseHub.slnx
dotnet test sources/ExpenseHub.slnx
dotnet format sources/ExpenseHub.slnx --verify-no-changes --no-restore
```

- Unitários: fakes/serialização em memória, sem banco/rede/RabbitMQ. Cobrem espera de confirmação,
  503 com ID, falha de persistência, contrato mínimo da mensagem, rejeição, ACK após salvar,
  duplicata, handler recriado, Failed, erro transitório, retry em escopo novo e perda de ACK.
- `ExpenseHub.IntegrationTests`: projeto separado, categoria SQLite, usa arquivos temporários
  exclusivos. Reabre DbContexts/processadores e verifica snapshot após alteração de despesa,
  retomada de Processing, concorrência entre claims e Failed persistido. Remove somente os
  arquivos aleatórios criados pelo próprio teste, nunca bancos existentes do usuário.
- Os testes I01/I02 de Mine, Global, ownership, HTTP e XLSX continuam na suíte.

Neste ambiente, Docker/RabbitMQ não foram encontrados no PATH nem nos caminhos usuais verificados.
Portanto, **não foi executado fluxo com broker real nem reinício real do processo Worker conectado**.
Os testes de reinício descritos acima recriam componentes sobre estado persistido; não substituem
as evidências de transporte e processo abaixo. Não se afirma execução real de publisher confirms
ou entrega durável neste ambiente sem broker.

### Roteiro obrigatório para I05/I06 com broker real

1. Suba o broker local, inicie API e Worker com o mesmo banco absoluto. Solicite Mine com Employee:
   espere 202 e Location, consulte até Completed e baixe o Excel.
2. Pare apenas o processo Worker. Faça outro POST; confirme 202 e Pending consultável, mensagem
   aguardando na fila durável. Reinicie Worker e confira Completed, resultado e timestamps.
3. Salve o JSON de detalhe concluído. Pare e reinicie Worker. Pela interface management local
   (`http://localhost:15672`, acesso com credenciais externas), publique novamente na fila somente
   `{"reportId":"<id salvo>"}` com delivery mode 2. Confirme que ResultJson/GeneratedAt/CompletedAt
   continuam iguais e nenhuma despesa foi alterada. Não use outro POST para simular duplicata.
4. Pare o broker sem remover o volume; faça POST e confira 503 com ID consultável. Reinicie o
   broker e observe que Pending sem mensagem não é recuperado automaticamente.
5. Durante um processamento, interrompa Worker antes do ACK e reinicie. Confira retomada ou
   reconhecimento do terminal persistido, sem novo efeito. Colete logs correlacionados sem tokens.
6. Exercite indisponibilidade temporária de banco em ambiente descartável: sem ACK antecipado,
   retries limitados e saída 1; restaure acesso e reinicie. Não altere permissões do banco real.

## Preparação para I04, I05 e I06

- I04: consumir os contratos HTTP existentes, tratar 202 como aceitação e 503 com ID como publicação
  incerta, acompanhar estados e respeitar 404/409 no Excel. Não prometer recuperação automática.
- I05: ampliar testes de transporte, indisponibilidade e encerramento real de processos conforme
  o roteiro; preservar a separação dos testes unitários e dos que exigem serviços externos.
- I06: provisionar credenciais externas, broker com armazenamento persistente, caminhos do SQLite,
  supervisor com backoff, logs por reportId e imagem RabbitMQ fixada por versão/digest.

Esses são pontos de integração; não declaram as issues I04/I05/I06 implementadas.

## Resultado final da validação local

- `dotnet build sources/ExpenseHub.slnx`: sucesso, zero erros e zero avisos.
- `dotnet test sources/ExpenseHub.slnx`: 225 unitários e 4 integrações SQLite aprovados;
  229 no total, zero falhas e zero ignorados.
- `dotnet format sources/ExpenseHub.slnx --verify-no-changes --no-restore`: sucesso.
- `dotnet publish sources/ExpenseHub.Worker/ExpenseHub.Worker.csproj --no-restore -o <diretório temporário>`:
  sucesso; configuração do Worker separada para evitar conflito com appsettings da API.
- `git diff --check`: sem problemas de whitespace.
- Pipeline PowerShell/Gitleaks não executado: ferramentas indisponíveis no ambiente.
- Broker real, espera na fila com Worker parado e reinício real conectado: pendentes conforme roteiro acima.

## Checklist oficial

- [x] Publicador real com fila durável, mensagens persistentes e confirmação antes de 202.
- [x] Worker C# independente; ACK condicionado ao estado terminal persistido.
- [x] Conclusão consistente, erro definitivo sanitizado e ausência de ACK quando a persistência falha.
- [x] Proteção persistente contra duplicatas; retomada de Processing e concorrência verificadas em SQLite.
- [x] Identidade, escopo e período provenientes do banco; regras I02 preservadas.
- [x] Falha de publicação explícita e janela sem outbox documentada.
- [ ] Evidências de transporte e reinício com RabbitMQ real: executar no ambiente I05/I06.


## Correção do alerta FIAP1002 na PR #13

A regra `Test-CustomSecrets` em `scripts/Invoke-CodeQuality.ps1` faz correspondência textual por linha.
Na interpolação antiga, ela reconhecia `PASSWORD:` dentro de `${RABBITMQ_PASSWORD:?...}` e capturava
`?Defina` como valor, não a interpolação completa. Por isso, o teste de placeholder não se aplicava.
O relatório do Actions confirmou FIAP1002 no Compose, linha 6; não havia senha literal nessa linha.

As credenciais agora vêm de `rabbitmq.env` externo, obrigatório e não versionado. O exemplo contém
somente campos vazios. O wrapper recusa campos vazios antes de iniciar o entrypoint oficial.
Nenhuma regra do scanner, exceção ou configuração de autenticação foi relaxada. Fila durável,
volume persistente e portas locais foram preservados.

Validação local desta correção: 229 testes aprovados, verificação de formatação e `git diff --check`
sem erros. A expressão de detecção foi reproduzida para confirmar a causa e a ausência do achado
no Compose corrigido; o wrapper foi executado em `/bin/sh` com entradas descartáveis, verificando
rejeição de usuário/senha ausentes ou vazios. Isso não substitui a execução do container.
Docker, PowerShell e Gitleaks não estão instalados localmente; a execução integral do scanner é
verificada pelo GitHub Actions após atualizar a PR existente.
