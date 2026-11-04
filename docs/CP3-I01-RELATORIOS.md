# CP3-I01 — Contrato, cálculo e resultado do relatório

Referência: [Racass/checkpoint-csharpracass-expensehub#11](https://github.com/Racass/checkpoint-csharpracass-expensehub/issues/11).
Continua a CP2, com a mesma autenticação bearer do Identity, SQLite e regras de reembolsos.

## Solicitação e autorização

```http
POST /api/reports
Authorization: Bearer <accessToken>
Content-Type: application/json
```

```json
{
  "fromDate": "2026-09-01",
  "toDate": "2026-09-30",
  "scope": "Mine"
}
```

Datas são obrigatórias, válidas no formato `yyyy-MM-dd`, com `fromDate <= toDate`.
Um único dia é permitido. Escopo obrigatório e sensível a maiúsculas: `Mine` ou `Global`.
Não há restrição adicional de período máximo ou datas futuras no contrato de relatórios.
Campos extras, inclusive `ownerId`, `requestedByUserId`, `id` e `status`, são rejeitados com `400`.
O servidor gera um GUID e usa o `NameIdentifier` do principal autenticado para `RequestedByUserId`.
Cada POST válido cria uma solicitação diferente.

| Perfil | Mine | Global | Listar / detalhar / baixar |
|---|---|---|---|
| Employee | Sim | Não | Somente solicitações próprias |
| Auditor | Não | Sim | Somente solicitações próprias |
| Employee + Auditor | Sim | Sim | Somente solicitações próprias |
| Admin, Approver, Finance ou sem role funcional de relatórios | Não | Não | Não |

Roles adicionais não removem permissões de Employee/Auditor. Admin sozinho não tem privilégio implícito.
Todas as rotas exigem autenticação (`401` anônimo) e uma role funcional (`403` sem Employee/Auditor).
Para usuários autorizados às rotas, inexistente e alheio são indistinguíveis (`404`), inclusive para Auditor.
Ter escopo Global não permite ler relatórios criados por outros usuários.

## Rotas e respostas

| Rota | Sucesso | Falhas esperadas |
|---|---|---|
| `POST /api/reports` | `202` apenas com confirmação real; sem confirmação, `503` com ID salvo e erro sanitizado (CP3-I02) | `400` entrada inválida; `401`; `403` role/escopo não autorizado |
| `GET /api/reports` | `200` array de envelopes próprios, mais recentes primeiro | `401`; `403` |
| `GET /api/reports/{id}` | `200` envelope e resultado persistido | `401`; `403`; `404` inexistente/alheio |
| `GET /api/reports/{id}/excel` | `200` arquivo XLSX da solicitação própria concluída | `401`; `403`; `404` inexistente/alheio; `409` própria não Completed |

Os identificadores nas rotas são GUIDs; um identificador malformado não corresponde à rota (`404`).

Envelope comum ao POST, detalhe e itens da listagem:

```json
{
  "id": "08d1b86a-96eb-412c-96d2-2f753ab290ac",
  "scope": "Mine",
  "fromDate": "2026-09-01",
  "toDate": "2026-09-30",
  "status": "Pending",
  "createdAt": "2026-10-01T12:00:00+00:00",
  "startedAt": null,
  "completedAt": null,
  "result": null,
  "error": null
}
```

Instantes são UTC (`+00:00` é equivalente a `Z`). A identidade do solicitante é persistida,
mas não precisa ser exposta no envelope. Não se aceita identidade no corpo.

| Estado | Campos |
|---|---|
| Pending | `createdAt`; resultado, início, término e erro nulos |
| Processing | `startedAt`; ainda sem resultado |
| Completed | `completedAt` e `result`; erro nulo |
| Failed | `completedAt` e `error: { code, message }`; resultado nulo |

Falhas de cálculo usam `REPORT_PROCESSING_FAILED` e uma mensagem fixa sanitizada. Exceções ficam nos logs
internos, não nas respostas. Falha ou ausência de confirmação de publicação não marca o cálculo como Failed.

## Cálculo e persistência

`ReportCalculator` não depende de EF, HTTP ou RabbitMQ. Recebe despesas e o período/escopo persistidos;
usa `TimeProvider` para registrar `generatedAt` em UTC. Filtra `ExpenseDate` entre os limites inclusivos.
Mine restringe `OwnerId` ao solicitante. Global inclui todos os funcionários. Não altera despesas.

A soma usa `decimal` e sempre produz `Draft`, `Submitted`, `Approved`, `Rejected`, `Paid`, com contagem e
valor por estado, `totalCount` e `totalAmount`. Conjunto vazio inclui as cinco linhas com zero.
No exemplo oficial, Mine resulta em 3 despesas / 175,00; Global em 4 / 375,00.

```json
{
  "generatedAt": "2026-10-01T12:00:02+00:00",
  "totalCount": 3,
  "totalAmount": 175.00,
  "byStatus": [
    { "status": "Draft", "count": 0, "amount": 0 },
    { "status": "Submitted", "count": 0, "amount": 0 },
    { "status": "Approved", "count": 2, "amount": 150.00 },
    { "status": "Rejected", "count": 0, "amount": 0 },
    { "status": "Paid", "count": 1, "amount": 25.00 }
  ]
}
```

`Reports.ResultJson` salva o resultado completo, inclusive `generatedAt`, na mesma gravação do estado
Completed e `completedAt`. O processador lê os valores atuais durante o processamento, não durante o POST.
Os GETs desserializam exclusivamente o snapshot salvo; alterar uma despesa depois não altera o relatório.
O JSON preserva os decimais, sem agregação financeira em ponto flutuante no SQLite.

`ReportSchemaInitializer` cria tabela e índice ausentes usando SQL estático `IF NOT EXISTS`, após
`EnsureCreatedAsync`. Não exclui tabelas, não recria o banco e não modifica dados CP2. Não há migrations.
O mapeamento EF usa nomes de enums persistidos como texto e `Status` como token de concorrência.

## Download Excel

Gerado com `DocumentFormat.OpenXml` 3.3.0 (MIT), formato Open XML real, não CSV.

- `Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`.
- `Content-Disposition: attachment; filename=report-{id}.xlsx` (o framework pode também emitir filename*).
- Uma aba `Reembolsos` com Id, Scope, FromDate, ToDate e GeneratedAt.
- Cabeçalho Status/Count/Amount, cinco linhas de estados e linha Total.
- Quantidades e valores são células numéricas; metadados são texto ISO para preservar data e UTC.
- O arquivo é construído somente a partir de `ReportResponse.Result` desserializado do banco.
- Pending, Processing e Failed próprios retornam `409`; inexistentes/alheios retornam `404` antes de verificar estado.

## Integração exata com CP3-I03

A I01 **não executa worker automático**. `UnavailableReportPublisher` retorna `false` explicitamente,
sem enviar mensagem. A solicitação fica Pending e, desde a CP3-I02, o POST retorna `503` sem `Location`,
com ID persistido e erro sanitizado. O fluxo final de aceitação `202` depende da I03. Não se deve usar essa resposta
como prova de enfileiramento ou prometer que o relatório será processado nesta versão.

1. Crie o projeto com assembly name `ExpenseHub.Worker` e referência ao projeto `ExpenseHub.Api`.
   A API já concede `InternalsVisibleTo` a esse nome. Alternativamente, extraia Reports e persistência
   para uma biblioteca compartilhada, preservando contratos e testes. Não exponha um endpoint de processamento.
2. Implemente `IReportPublisher.PublishConfirmedAsync(Guid reportId, CancellationToken)` no produtor.
   Mensagem sugerida: `{ "reportId": "<guid persistido>" }`, sempre com o mesmo GUID da rota e do banco.
   O worker deve carregar período, solicitante e escopo do banco; não confiar nesses campos em mensagens externas.
3. Substitua o registro `AddSingleton<IReportPublisher, UnavailableReportPublisher>()` em `Program.cs`
   pelo publicador real. Retorne `true` somente após confirmação positiva real do broker; rejeição, timeout
   e confirmação incerta não podem retornar `true`. O handler já salva Pending antes de publicar e emite
   `202` + `Location: /api/reports/{id}` somente no ramo confirmado. Não marque Failed por timeout de publicação.
   Preserve a resposta `503` com ID persistido e erro sanitizado implementada na CP3-I02 para ausência
   de confirmação. O `201` provisório original foi substituído para atender à issue oficial #12.
4. No host do worker, registre as mesmas dependências, com a mesma connection string:

   ```csharp
   services.AddDbContext<ExpenseHubDbContext>(options => options.UseSqlite(connectionString));
   services.AddScoped<IReportStore, EfReportStore>();
   services.AddSingleton(TimeProvider.System);
   services.AddSingleton<ReportCalculator>();
   services.AddScoped<ReportProcessor>();
   ```

   O host padrão já fornece logging. Inicialize o esquema antes de consumir (pela API ou pelo inicializador).
   O SQLite precisa apontar para o mesmo arquivo acessível a ambos os processos; caminhos relativos diferentes
   criam bancos diferentes. Distribuição em máquinas distintas exigirá uma estratégia de persistência compartilhada.
5. Para cada entrega, crie um escopo DI e chame
   `await processor.ProcessAsync(reportId, cancellationToken)`. O processador reivindica Pending → Processing
   com concorrência otimista, grava `startedAt`, lê despesas, calcula e salva Completed/result ou Failed/error.
   Duas reivindicações concorrentes não processam a mesma solicitação. Reentrega de Completed/Failed retorna
   `true` sem consultar despesas ou recalcular; Processing ou inexistente retorna `false`.
6. Dê ACK somente após confirmação de persistência terminal (`true`). Um retorno `false` não equivale a
   conclusão: defina retry, inspeção e DLQ na I03. Exceção de gravação ou cancelamento deve impedir ACK automático.
   O erro Failed é terminal nesta implementação; novas tentativas de cálculo exigem uma política explícita.
7. Implemente na I03 recuperação de mensagens e registros Pending não publicados (por exemplo, outbox),
   confirmação incerta, backoff, DLQ e recuperação de Processing abandonado por queda/cancelamento do worker.
   Não há lease, timeout de processamento nem recuperação automática nesta I01. Nunca redefina Processing
   para Pending sem verificar se outro consumidor ainda trabalha. A concorrência atual protege a disputa
   inicial, não substitui a política distribuída de recuperação e idempotência da I03.
8. Valide com broker real: persistir → confirmar publicação → 202/Location → consumir → salvar → ACK;
   também teste indisponibilidade, reentrega, queda entre etapas e snapshot/Excel após mudança de despesas.

## Verificação

```sh
dotnet build sources/ExpenseHub.slnx
dotnet test sources/ExpenseHub.slnx
dotnet format sources/ExpenseHub.slnx --verify-no-changes --no-restore
```

Testes adicionados em `sources/ExpenseHub.UnitTests/Reports`:

| Arquivo | Evidência |
|---|---|
| ReportCalculatorTests | Exemplo oficial, Mine/Global, datas inclusivas, cinco estados, decimal, vazio, entrada e campos proibidos |
| ReportProcessingTests | Estados, concorrência de claim via fake, snapshot imutável, falha sanitizada, reentrega, XLSX validado por OpenXmlValidator |
| ReportEndpointTests | Matriz de acesso, identidade, persistência antes da confirmação, 503/202, ownership, 404/409 e download sem recálculo |
| ReportHttpTests | Middleware real em TestServer: 401/403, binding 400, criação e leitura próprias, ausência de Location sem confirmação |
| ReportTestSupport | Relógio fixo, store e publisher de teste; sem dependências externas |

Os testes não precisam de banco de dados, rede ou RabbitMQ. TestServer usa transporte em memória e a
autenticação substituta existe somente no projeto de testes; a API preserva o Identity bearer da CP2.

### Resultado da validação desta implementação

- `dotnet build sources/ExpenseHub.slnx`: sucesso, zero avisos e zero erros.
- `dotnet test sources/ExpenseHub.slnx`: 186 aprovados, zero falhas, zero ignorados.
- `dotnet format sources/ExpenseHub.slnx --verify-no-changes --no-restore`: sucesso.
- Verificação adicional com SQLite descartável em memória: atualização de esquema CP2 executada duas vezes
  sem perder usuários/categorias/despesas; snapshot persistido relido em novo DbContext após alterar despesa;
  isolamento por criador; XLSX validado; segunda transição concorrente recusada.
- `git diff --check`: sem problemas de whitespace.
- O pipeline PowerShell/Gitleaks completo não foi executado: `pwsh` e `gitleaks` não estão disponíveis neste
  ambiente. As configurações de qualidade e os analisadores existentes foram preservados.
- O fluxo RabbitMQ real permanece sem validação, pois sua implementação pertence à CP3-I03.


A validação acima registra a entrega original da I01. Para a evolução, 208 testes e contrato de falhas
atualizado, consulte [CP3-I02-AUTORIZACAO.md](CP3-I02-AUTORIZACAO.md).
