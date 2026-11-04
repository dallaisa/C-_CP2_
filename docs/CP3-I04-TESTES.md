# CP3-I04 — Testes próprios significativos

Referência: [Racass/checkpoint-csharpracass-expensehub#14](https://github.com/Racass/checkpoint-csharpracass-expensehub/issues/14).

## Objetivo

Detectar regressões das regras de relatórios (cálculo, autorização, mensageria, resultado persistido e Excel) com
testes de comportamento. A suíte unitária não usa banco, rede, RabbitMQ, Docker nem nuvem: a persistência é um
`MemoryReportStore`, a publicação é um double (`ConfirmingPublisher`, `UnavailableReportPublisher`), o relógio é fixo
e o HTTP roda em memória com `TestServer` e autenticação simulada.

## Comandos

Testes unitários (sem dependências externas):

```powershell
dotnet test sources/ExpenseHub.UnitTests/ExpenseHub.UnitTests.csproj
```

Testes de integração (arquivos SQLite descartáveis em pasta temporária, sem rede nem RabbitMQ; categoria `SQLite`):

```powershell
dotnet test sources/ExpenseHub.IntegrationTests/ExpenseHub.IntegrationTests.csproj
```

Tudo, mais verificação de qualidade:

```powershell
dotnet build sources/ExpenseHub.slnx
dotnet test sources/ExpenseHub.slnx
dotnet format sources/ExpenseHub.slnx --verify-no-changes
pwsh scripts/Invoke-CodeQuality.ps1 -SkipGitleaks
```

## Mapeamento dos critérios

Classes em `sources/ExpenseHub.UnitTests/` (U) e `sources/ExpenseHub.IntegrationTests/` (I).

### Cálculo, conjunto vazio e período (5 pts)

| Cenário | Teste |
|---|---|
| Exemplo oficial (Mine 3/175, Global 4/375), limites inclusivos, exclusão fora do período, estado do reembolso intacto | U `ReportCalculatorTests.OfficialExampleAndInclusiveBoundaries` |
| Cinco estados, contagem por estado, aritmética `decimal` (0,1 + 0,2) | U `ReportCalculatorTests.EveryStateHasCorrectCountAndDecimalAmount` |
| Conjunto vazio: cinco estados e totais zero (Mine e Global) | U `ReportCalculatorTests.EmptySetHasFiveZeroRows` |
| Período invertido, datas ausentes/inválidas, escopo inválido | U `ReportCalculatorTests.InvalidRequest`, `InvalidPeriodOrScopeThrows`, `InvalidCalendarDateIsRejected` |
| Período invertido via HTTP: 400, nada persistido, nada publicado | U `ReportContractTests.InvertedPeriodDoesNotPersistOrPublish` |
| Filtro de período e escopo traduzido para SQL (Mine/Global, bordas inclusivas) | I `ReportQueryTests.MineFiltersRequesterAndInclusivePeriodInSql`, `GlobalIncludesAllEmployeesInsidePeriod` |
| Worker usa solicitante e escopo salvos (Mine ignora outros, Global soma todos) | U `ReportContractTests.ProcessorUsesSavedRequesterAndScope` |

### Negações e isolamento (5 pts)

| Cenário | Teste |
|---|---|
| Matriz de escopo por perfil (Employee, Auditor, Employee+Auditor, Admin/Approver/Finance sem permissão implícita) | U `ReportEndpointTests.CreationEnforcesScopeRoles`, `ReportHttpTests.ScopeMatrixAndUntrustedParameters` |
| 401 anônimo e 403 sem perfil em todas as rotas | U `ReportHttpTests.MiddlewareRejectsAnonymousAndUnauthorizedRoles` |
| Solicitante e escopo vindos do servidor; `ownerId`/`requestedByUserId`/`status`/query/headers do cliente rejeitados ou ignorados | U `ReportHttpTests.InvalidHttpBodyCreatesNoWork`, `ScopeMatrixAndUntrustedParameters`, `ReportCalculatorTests.RejectsExtraClientFields` |
| Listagem só própria; detalhe e Excel alheios → 404 sem corpo, inclusive Auditor, em todos os estados | U `ReportHttpTests.ThreeAccountsAreIsolatedAcrossAllStates`, `ReportEndpointTests.OwnershipAppliesToListDetailAndDownload` |
| Payload da fila sem autoridade (campos extras de escopo/usuário rejeitados) | U `ReportDeliveryTests.InvalidMessageIsRejected`, `MessageCarriesOnlyPersistedIdentifier` |

### Duplicata, resultado persistido e Excel (5 pts)

| Cenário | Teste |
|---|---|
| Reentrega de Completed não altera resultado, `GeneratedAt`, `CompletedAt` nem grava | U `ReportContractTests.DuplicateOfCompletedReportKeepsGeneratedAtAndCompletedAt`, `ReportProcessingTests.SnapshotAndExcelRemainUnchangedAfterExpenseMutation` |
| Reentrega de Failed não reprocessa | U `ReportContractTests.DuplicateOfFailedReportHasNoEffect` |
| ACK só após salvar; duplicata com handler/store recriados | U `ReportDeliveryTests.AckFollowsPersistenceAndRestartedDuplicateHasNoEffect`, `LostAckDoesNotRepeatPersistedEffect` |
| Falha definitiva → Failed sanitizado; falha ao salvar ou transitória → sem ACK; Processing retomável | U `ReportDeliveryTests.DefinitiveFailureIsPersistedBeforeAck`, `TerminalPersistenceFailureDoesNotAck`, `TransientFailureDoesNotAckAndProcessingCanResume`, `RetryUsesFreshScopeAndRecoversTransientRead`, `ReportProcessingTests.FailureIsSanitizedAndPersisted`, `MissingIsNotProcessedAndProcessingIsResumed`, `LostClaimDoesNotCalculate` |
| Proteção persistente (não em memória) com contextos novos sobre SQLite | I `ReportPersistenceTests.RestartAndDuplicatePreserveSnapshotAndGeneratedAt`, `RestartResumesProcessing`, `ConcurrentReclaimFencesOlderAttempt`, `DefinitiveFailureSurvivesRestart` |
| Consultas repetidas após alterar reembolso devolvem o resultado salvo, sem ler reembolsos | U `ReportContractTests.RepeatedReadsReturnPersistedResultWithoutRecalculation`, `ReportEndpointTests.ReadRoutesUseOnlyPersistedSnapshotAndDownloadContract` |
| XLSX real validado com OpenXML: cabeçalhos, metadados, cinco estados, células numéricas, totais, vazio | U `ReportProcessingTests.EmptyReportProducesValidExcel`, `ReportContractTests.WorkbookHasExpectedHeadersMetadataAndNumbers` |
| Content-Type, `attachment` e `report-{id}.xlsx` | U `ReportHttpTests.PendingCreationAndOwnershipFlow`, `ReportContractTests.WorkbookHasExpectedHeadersMetadataAndNumbers` |
| Excel de Pending/Processing/Failed próprio → 409 sem arquivo; alheio/inexistente → 404 | U `ReportContractTests.ExcelOfUnfinishedOwnReportIsConflictWithoutFile`, `ReportEndpointTests.UnfinishedOwnedReportReturnsConflict` |

### Publicação e contrato HTTP

| Cenário | Teste |
|---|---|
| 202 + `Location` + envelope Pending só com confirmação, após persistir | U `ReportContractTests.ConfirmedPublicationReturnsAcceptedWithLocationAndPendingEnvelope`, `ReportEndpointTests.AcceptedRequiresConfirmedPublicationAfterPersistence`, `ReportPublicationTests.AcceptanceWaitsForPublisherConfirmation` |
| Novo POST válido → novo ID | U `ReportContractTests.EachValidPostCreatesNewIdentifier`, `ReportEndpointTests.UnconfirmedPublicationStaysPendingAndNewPostsHaveDifferentIds` |
| 503 com ID salvo, relatório continua Pending (não Failed) | U `ReportContractTests.UnconfirmedPublicationIsNotAFailedReport`, `ReportPublicationTests.PublicationExceptionReturnsSavedIdAndSafeError` |
| Falha de banco antes do registro: 503 sem ID e sem publicação | U `ReportPublicationTests.PersistenceFailureDoesNotPublishOrReturnId` |

## Resultados da validação

Executado em 04/11/2026 (Windows, .NET 10):

| Item | Resultado |
|---|---|
| Testes unitários | 225 antes da I04, 238 depois (13 novos), 0 falhas |
| Testes de integração | 4 antes, 6 depois (2 novos), 0 falhas |
| `dotnet build sources/ExpenseHub.slnx` | 0 avisos, 0 erros |
| `dotnet format --verify-no-changes` | sem alterações pendentes |
| `scripts/Invoke-CodeQuality.ps1 -SkipGitleaks` | 100/100, status `passed` |

## Limitações

- Gitleaks não foi executado localmente (binário ausente); ele roda no pipeline do GitHub.
- Nenhum teste automatizado usa RabbitMQ real: confirmação do broker, entrega, ACK/NACK e reinício de processo são
  simulados por doubles. Os testes de SQLite recriam contextos e processadores, o que não equivale a reiniciar o worker.
- O banco dos testes de integração é SQLite em arquivo temporário; outros provedores não são cobertos.

## Testes automatizados × experimentos da I06

Estes testes comprovam regras e contratos de forma isolada e repetível. Os experimentos E01 a E05 (fluxo real com
Excel, worker parado e retomado, duplicata após reinício real do worker, isolamento com contas reais, recriação de web e
worker com imagens publicadas) exigem RabbitMQ, Compose e imagens, e pertencem à I06. Capturas de testes unitários não
substituem essa evidência.
