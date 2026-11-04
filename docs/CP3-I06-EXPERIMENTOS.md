# CP3-I06 — Experimentos e diagnóstico

Referência: [Racass/checkpoint-csharpracass-expensehub#16](https://github.com/Racass/checkpoint-csharpracass-expensehub/issues/16).

Este documento reúne o roteiro dos cinco experimentos obrigatórios (E01 a E05), o diagnóstico do comportamento observado
e o modelo do dossiê. **Os resultados reais só existem depois de executar o roteiro contra as imagens publicadas**; a seção
[Dossiê de evidências](#dossiê-de-evidências) é preenchida com a saída dessa execução e não deve conter valores inventados.

## Pré-requisitos

- Imagens publicadas pelo workflow `publish-images` (I05) e `.env` preenchido com `IMAGE_TAG` igual ao SHA completo do
  commit publicado (veja `docs/CP3-I05-EXECUCAO.md`).
- Ambiente em execução: `docker compose pull && docker compose up -d && docker compose ps`.
- PowerShell 7 (`pwsh`) e Docker Compose 2.30+.

## Como executar

Execução automatizada (grava evidências sanitizadas em `docs/evidencias/cp3-i06/`):

```powershell
pwsh scripts/Invoke-Cp3Experiments.ps1 -Experiment All     # ou E01, E02, E03, E04, E05
```

O script lê `RABBITMQ_DEFAULT_*`, `SEED_ADMIN_*` e `IMAGE_TAG` do `.env` (ou do ambiente). A cada execução cria cinco
usuários descartáveis (`cp3-<data>-<papel>@example.com`) com **senha aleatória mantida só em memória**, atribui as roles pelo
Admin e usa a API real. **Tokens, senhas e cabeçalhos de autenticação nunca são gravados**; as linhas de log passam por um
filtro de sanitização e só entram no dossiê quando contêm o `reportId`. Nenhum comando remove volumes; E05 usa
`--force-recreate` apenas em `api` e `worker`.

As mesmas chamadas podem ser feitas manualmente com `sources/ExpenseHub.Api/ExpenseHub.Reports.http`.

## Experimentos

| Exp. | O que o roteiro faz | Resultado esperado |
|---|---|---|
| E01 | Cria despesas conhecidas (exemplo oficial), autentica, `POST /api/reports` Mine, guarda o id, acompanha até `Completed`, baixa o Excel e confere resultado e planilha | 202 + `Location`; `totalCount` 3, `totalAmount` 175; Approved 2/150, Paid 1/25; `.xlsx` válido com 3, 175, 150 e 25 |
| E02 | `docker compose stop worker`; solicita; observa Ready na fila e relatório `Pending`; `docker compose up -d worker`; acompanha o **mesmo id** | 202 com o worker parado; Ready ≥ 1; Excel 409; depois `Completed`; fila com Ready 0 e Unacked 0 |
| E03 | Após `Completed`, `docker compose restart worker`; republica `{"reportId":"<id>"}` na fila; compara antes e depois | Resultado, `generatedAt` e `completedAt` idênticos; mesma quantidade de solicitações; log de duplicata ignorada; fila vazia |
| E04 | Employee não solicita Global; Auditor não solicita Mine; Auditor solicita Global; detalhe e Excel alheios (Employee e Auditor) | 403 / 403 / 202 ou 503 / 404 / 404; anônimo 401; listagem só com próprias |
| E05 | Registra versão das imagens, `docker compose up -d --force-recreate api worker`, reloga e consulta o resultado do E01 | Mesma revisão (`IMAGE_TAG`); mesmo resultado; Excel ainda baixável |

### Republicar a mensagem manualmente (E03)

O roteiro faz isso pela API de gerenciamento do RabbitMQ. Manualmente, pelo painel `http://localhost:15672`:
*Queues and Streams → `expensehub.reports` → Publish message*, com `Delivery mode = 2 - Persistent`, `content_type =
application/json` e o payload exatamente igual a:

```json
{"reportId":"<id do relatório concluído no E01>"}
```

Não há endpoint de negócio para isso e o banco não é alterado. O contrato aceita **somente** `reportId`: campos extras são
rejeitados, e o worker lê solicitante, período e escopo do registro salvo.

### Correlação por `reportId`

Todos os eventos usam o `reportId` e podem ser filtrados por ele em `docker compose logs`:

| Evento | Serviço | Mensagem de log |
|---|---|---|
| Publicação confirmada | `api` | `Report {id} publication confirmed` |
| Publicação não confirmada | `api` | `Report publication was not confirmed for {id}` |
| Início | `worker` | `Processing report {id}` |
| Conclusão | `worker` | `Report {id} saved as Completed` |
| ACK após persistir | `worker` | `Report {id} delivery acknowledged after persistence` |
| Duplicata | `worker` | `Report {id} is already Completed; duplicate delivery ignored without new effect` |
| Falha definitiva | `worker` | `Report calculation failed for {id}` e `saved as Failed` |

## Diagnóstico

**Aceitação (202) não é conclusão.** A API só responde 202 depois de persistir a solicitação **e** receber a confirmação de
publicação do broker. O relatório nasce `Pending`; quem o leva a `Processing` e `Completed` é o worker, e só então o
resultado fica salvo. Por isso o E01 acompanha o mesmo id por consulta.

**Reentrega e efeito idempotente.** Se o worker cair antes do ACK, ou se uma mensagem for republicada (E03), o broker entrega
de novo. O cálculo pode ser repetido, mas o **efeito persistido não**: `Completed` e `Failed` são estados terminais no
SQLite, e uma duplicata apenas lê esse estado, registra o log de duplicata e confirma. A proteção está no banco, não em memória,
e por isso sobrevive ao reinício do worker.

**Ready e Unacked não são Pending e Processing.** *Ready* é mensagem disponível na fila; *Unacked* é mensagem entregue sem
confirmação de consumo. São métricas do broker. *Pending* e *Processing* são estados da solicitação no banco. Um
relatório `Pending` pode ter a mensagem em Ready (worker parado, E02) ou nem ter mensagem (janela abaixo).

| Situação | O que acontece | Recuperação |
|---|---|---|
| **Worker parado** (E02) | A API confirma a publicação; a mensagem espera em Ready; o relatório fica `Pending` | Subir o worker: ele consome a mesma mensagem e conclui o mesmo id |
| **Broker indisponível** | A API persiste e não obtém confirmação: responde **503 com o id salvo**; o relatório continua `Pending` (não `Failed`). Se o banco falhar antes de registrar, o erro é explícito e sem id | Reiniciar o worker **não** ajuda: não existe mensagem. Consultar o id; novo POST cria outra solicitação |
| **Janela sem outbox** | Salvar no SQLite e publicar no RabbitMQ são operações separadas: uma queda entre elas, ou uma confirmação perdida, deixa `Pending` sem mensagem (ou com publicação incerta) | Não há republicação automática nesta entrega (outbox é opcional). Reprocessamento futuro deve republicar apenas ids existentes |

O tratamento de publicação sem confirmação existe no código e é verificado por testes unitários
(`ReportPublicationTests`, `ReportContractTests.UnconfirmedPublicationIsNotAFailedReport`); um experimento de interrupção do
broker não é obrigatório.

## O que já foi validado localmente

Sem Docker nem RabbitMQ, só o que não depende deles:

| Item | Resultado |
|---|---|
| E04 contra a API real (SQLite temporário, sem broker) | 12 verificações aprovadas: 403 de escopo, 401 anônimo, 404 de detalhe e Excel alheios, isolamento da listagem |
| Preparo de dados do E01 (criar, enviar, aprovar e pagar despesas) | Executado com sucesso; a solicitação respondeu 503 por não haver broker, como esperado |
| Log de duplicata | Teste unitário `ReportContractTests.DuplicateDeliveryIsLoggedWithReportId` |

O restante (E01 completo, E02, E03 e E05) exige as imagens publicadas e o ambiente Docker, e **ainda não foi executado**.

## Dossiê de evidências

Preencher após a execução, colando as linhas `[PASS]`/`[FAIL]` e os `reportId` geradas pelo roteiro (arquivos em
`docs/evidencias/cp3-i06/`). Não anexar tokens, senhas, cabeçalhos de autenticação nem connection strings.

| Item | Valor |
|---|---|
| Commit final / `IMAGE_TAG` | _a preencher_ |
| Imagens | `ghcr.io/dallaisa/expensehub-api:<commit>` · `ghcr.io/dallaisa/expensehub-worker:<commit>` |
| Data e hora (UTC) da execução | _a preencher_ |
| E01 — reportId e resultado | _a preencher_ |
| E02 — reportId, Ready antes/depois | _a preencher_ |
| E03 — reportId, comparação antes/depois e log de duplicata | _a preencher_ |
| E04 — resultado das verificações | _a preencher_ |
| E05 — revisão antes/depois e resultado preservado | _a preencher_ |
| Falhas encontradas e explicação | _a preencher_ |

## Limitações

- Um worker, consumo serial; sem escala, DLQ, retentativas completas nem outbox (fora do escopo).
- E05 não prova perda de dados nem remove volumes; se a autenticação precisar ser refeita, isso não invalida a prova de
  persistência do resultado.
- A evidência da versão em execução vem do rótulo `org.opencontainers.image.revision` das imagens e de `docker compose images`.
