# CP3-I02 — Autorização e isolamento

> Evolução CP3-I03: o publicador real e o Worker separado agora estão implementados.
> As seções de integração pendente abaixo registram a entrega original e foram sucedidas por
> [CP3-I03-RABBITMQ-WORKER.md](CP3-I03-RABBITMQ-WORKER.md), que descreve a retomada de Processing,
> retries limitados e confirmação real. Permanecem válidos os contratos de cálculo e autorização.


Referência: [Racass/checkpoint-csharpracass-expensehub#12](https://github.com/Racass/checkpoint-csharpracass-expensehub/issues/12).

## Análise e reaproveitamento da I01

A I01 já implementava autenticação bearer do Identity, permissão de criação por escopo,
consultas filtradas por `RequestedByUserId`, rejeição de campos JSON desconhecidos e
processamento por ID carregando os parâmetros salvos. Esses mecanismos foram preservados,
assim como filtros por ExpenseDate, decimal, cinco estados e snapshots sem recálculo.
Não foram criados novos perfis, autenticação ou endpoints de processamento.

A I02 reforça o contrato com propriedades de criação `init`, testes HTTP de roles acumuladas
e três contas isoladas em todos os estados. Também corrige a resposta provisória de publicação:
a tabela complementar da issue #12 exige `503` com ID persistido e erro sanitizado, substituindo
o `201` original da I01. Essa adequação não simula mensageria; o publicador padrão continua indisponível.

## Matriz de acesso

| Roles | Solicitar Mine | Solicitar Global | Listar, detalhar e baixar |
|---|---|---|---|
| Employee | Sim | Não | Somente solicitações próprias |
| Auditor | Não | Sim | Somente solicitações próprias |
| Employee + Auditor | Sim | Sim | Somente solicitações próprias |
| Admin, Approver, Finance, sem Employee/Auditor | Não | Não | Sem acesso funcional às rotas |
| Admin/Approver/Finance + Employee | Sim | Não | Somente solicitações próprias |
| Admin/Approver/Finance + Auditor | Não | Sim | Somente solicitações próprias |
| Anônimo | Não | Não | Não |

Roles são cumulativas. Global autoriza agregar despesas de todos os usuários, não acessar
solicitações de outros. A consulta não revalida o escopo de criação contra as roles atuais:
exige uma role funcional de relatórios e ownership, preservando o comportamento da I01.

## Identidade e processamento

- O corpo do POST aceita somente `fromDate`, `toDate`, `scope`. Campos extras são `400`.
- `ownerId`, `RequestedByUserId`, `userId` e demais campos não definem o solicitante.
- Query strings e cabeçalhos arbitrários não substituem o principal autenticado.
- O ID é gerado no servidor; o solicitante vem de `ClaimTypes.NameIdentifier` do Identity.
- ID, solicitante, escopo, período e instante de criação têm setters `init`. O EF continua
  materializando esses dados normalmente, sem mudança de esquema nem perda de dados existentes.
- A entidade não é um DTO de entrada. `init` impede reatribuição comum no código, mas não é uma
  barreira contra escrita direta no banco ou reflexão; o worker deve preservar a fronteira de confiança.
- `ReportProcessor.ProcessAsync(Guid, CancellationToken)` recebe apenas o ID e carrega `Report`
  por `IReportStore.FindAsync`. Não aceita owner, scope ou período como parâmetros da mensagem.
- `EfReportStore.ReadExpensesAsync` filtra com os campos persistidos; `ReportCalculator` também
  aplica o filtro Mine pelo solicitante. Global usa todas as despesas dentro do período autorizado.
- O cálculo não precisa dos tokens nem de roles vindas da fila. A autorização ocorre antes de salvar
  a solicitação; os parâmetros persistidos representam o escopo autorizado naquele momento.

## Códigos HTTP e ausência de vazamento

| Situação | Resposta |
|---|---|
| Anônimo em qualquer uma das quatro rotas | 401 |
| Autenticado sem role funcional ou sem permissão para solicitar o escopo válido | 403 |
| Datas ou escopo inválidos, campos extras no corpo | 400 para usuário autorizado à rota |
| Listagem própria | 200; array somente do solicitante |
| Detalhe/Excel inexistente ou alheio, inclusive Auditor | 404 sem resultado privado |
| Excel próprio Pending, Processing ou Failed | 409 |
| Excel próprio Completed | 200, XLSX com snapshot salvo |
| Persistência falha antes de publicar | 503 ProblemDetails sanitizado, sem ID de solicitação |
| Registro salvo, mas publicação não confirmada | 503 `{ id, error: { code, message } }`; sem Location |
| Registro salvo e publicação confirmada | 202 + Location + envelope da I01 |

A autorização da rota é anterior ao binding e à validação. Assim, anônimos continuam recebendo 401
e usuários sem role funcional 403, mesmo que enviem entrada inválida. Para quem tem acesso à rota,
escopo desconhecido é 400 e escopo conhecido sem a role correspondente é 403.

Sem confirmação, `error.code` é `REPORT_PUBLICATION_UNCONFIRMED`; a mensagem é fixa e sanitizada.
A solicitação fica Pending, com erro de processamento nulo. Exceções do publicador são registradas
nos logs internos e produzem o mesmo contrato seguro; cancelamento pelo cliente é propagado.
Falha de persistência usa `REPORT_PERSISTENCE_UNAVAILABLE`, não chama o publicador e não retorna
um GUID que não foi salvo. Nenhum desses caminhos emite 202. Dados de exceções não vão para a resposta.

## Casos automatizados e evidências

`ReportHttpTests` usa middleware real via TestServer, transporte em memória e store fake.
A identidade de teste por cabeçalhos existe apenas no projeto de testes; não foi adicionada à API.
Os testes de handlers, cálculo e processamento continuam isolados, sem banco, rede ou RabbitMQ reais.

- 401 para POST/listagem/detalhe/Excel; 403 para Admin, Approver, Finance e usuário sem role funcional.
- Matriz Employee/Auditor e combinações com Admin, Approver e Finance, incluindo escopos proibidos.
- Escopo desconhecido/não canônico e propriedades de identidade extras retornam 400.
- Query string de owner/scope e `X-User-Id` não modificam a identidade nem o escopo persistidos.
- Duas contas Employee e uma Auditor possuem solicitações distintas nos quatro estados.
- Cada listagem inclui apenas o relatório do próprio usuário; detalhe e Excel alheios são 404,
  com corpo vazio, inclusive para Auditor e para resultados Completed.
- Excel próprio não Completed retorna 409; inexistente retorna 404; Completed retorna XLSX.
- No processamento dos mesmos registros, Mine de A soma 100, Mine de B soma 250 e Global do Auditor
  soma 350: o processador usa solicitante e escopo salvos, apesar de o fake retornar todas as despesas.
- Publicação sem confirmação e exceção do publicador retornam 503 com ID salvo e sem marcar Failed.
- Erro de persistência não publica, não expõe detalhes da infraestrutura e não inventa ID persistido.
- Testes I01 preservam limites inclusivos, decimal, cinco estados, conjunto vazio, UTC e imutabilidade do resultado.

Arquivos de teste ampliados: `ReportHttpTests.cs`, `ReportEndpointTests.cs`, `ReportTestSupport.cs`.
Novo: `ReportPublicationTests.cs`. Nenhuma dependência NuGet adicional foi necessária.

## Obrigações para CP3-I03

1. Publicar somente o ID persistido. O consumidor deve validar o formato do ID e chamar
   `ReportProcessor.ProcessAsync(reportId, cancellationToken)` em um novo escopo DI por entrega.
2. Não desserializar mensagens diretamente em `Report`, não anexar entidades oriundas da fila ao EF
   e não copiar owner, scope, datas ou roles do payload para o registro. Carregar os valores do banco.
3. Preservar filtros Mine e Global e a propriedade nas rotas; nunca conceder leitura alheia a Auditor.
4. Implementar confirmação real no `IReportPublisher`; somente `true` confirmado permite 202/Location.
   Manter 503 com ID salvo quando a confirmação for desconhecida. O cliente não deve interpretar 503
   como inexistência do registro: pode consultar o ID; repetir POST cria outra solicitação.
5. Manter o resultado salvo imutável e não recalcular durante GET ou Excel.
6. Implementar outbox/reconciliação, retry, DLQ, ACK após persistência e recuperação de Processing
   abandonado. Não redefinir estado nem escopo a partir de mensagem não confiável.
7. Validar o fluxo com broker/worker reais e acesso ao mesmo banco. Essa integração continua pendente.

## Checklist oficial

- [x] Autenticação e permissão por perfil/escopo (5 pontos): implementadas e testadas.
- [x] Listagem, detalhe e Excel isolados por solicitante, inclusive Auditor (5 pontos).
- [x] Solicitante e escopo controlados pelo servidor até o processador disponível (5 pontos).
- [x] Mine não soma despesas alheias; Global autorizado agrega todos no período.
- [x] Contrato complementar de falhas sem 202 fictício.
- [ ] Execução integrada com RabbitMQ e worker CP3-I03 (dependência externa a esta entrega).

Os pontos acima são os pesos da especificação, não uma atribuição de nota.

## Validação executada

```sh
dotnet build sources/ExpenseHub.slnx
dotnet test sources/ExpenseHub.slnx
dotnet format sources/ExpenseHub.slnx --verify-no-changes --no-restore
```

- Build: zero avisos e zero erros.
- Testes: 208 aprovados, zero falhas, zero ignorados (22 casos adicionais à I01).
- Formatação/analisadores: verificação concluída sem alterações pendentes.
- `git diff --check`: sem problemas.
- Verificação complementar em harness temporário fora da suíte unitária, com SQLite descartável em
  memória: materialização EF com `init`, preservação de dados CP2, inicialização idempotente,
  snapshot salvo após alteração de despesa, isolamento por criador, XLSX válido e concorrência.
  Nenhum banco local existente foi alterado por essa verificação.
- Pipeline PowerShell/Gitleaks não executado: `pwsh` e `gitleaks` não estão instalados neste ambiente.
  `.editorconfig`, `Directory.Build.props` e as regras existentes permanecem inalterados.
