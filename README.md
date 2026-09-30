# ExpenseHub — I01 a I06

Versão preparada para concluir:

- **I01 — Fundação da solução e Entity Framework Core**
- **I02 — Identity, Admin e autenticação**
- **I03 — Cadastro HTTP e gerenciamento de roles**
- **I04 — Criar e editar rascunho**
- **I05 — Enviar, listar e consultar**
- **I06 — Ownership e matriz de acesso**

## I01 implementada

- API e projeto de testes mantidos na solução;
- Entity Framework Core com SQLite;
- entidades mínimas `Expense`, `ExpenseCategory`, `ExpenseHistory` e `PaymentRecord`;
- `ExpenseHubDbContext` com mapeamentos relacionais;
- persistência local em `expensehub.db`;
- arquivos SQLite ignorados pelo Git;
- criação reproduzível do banco com `EnsureCreatedAsync`.

## I02 implementada

- ASP.NET Core Identity persistido no mesmo banco relacional;
- roles `Admin`, `Employee`, `Approver`, `Finance` e `Auditor`;
- seed idempotente das roles;
- seed de **somente uma conta Admin**;
- senha do Admin fora do código e fora do `appsettings.json`;
- autenticação bearer;
- `POST /login`;
- proteção de rotas por autenticação e roles;
- usuário anônimo em rota protegida recebe `401 Unauthorized`;
- usuário autenticado sem a role necessária recebe `403 Forbidden`.

## I03 implementada

- `POST /register` para cadastro público de usuários;
- cadastro não aceita nem atribui roles enviadas pelo cliente;
- tentativa de enviar propriedades extras como `role` não promove o usuário;
- validação de email, senha e dados de entrada;
- tratamento de usuário já existente;
- `GET /api/admin/users` restrito à role `Admin`;
- listagem de usuários com identificador, email e roles atuais;
- `PUT /api/admin/users/{id}/roles` restrito à role `Admin`;
- atribuição e remoção das roles permitidas:
  - `Admin`
  - `Employee`
  - `Approver`
  - `Finance`
  - `Auditor`
- roles desconhecidas não são criadas implicitamente;
- usuário inexistente retorna `404 Not Found`;
- entrada ou role inválida retorna `400 Bad Request`;
- o Admin autenticado não pode remover de si mesmo a própria role `Admin`, retornando `409 Conflict`;
- após alteração de roles, o usuário deve realizar um novo login para que o novo token reflita suas permissões atualizadas.

## I04 implementada

- `POST /api/expenses` cria um reembolso sempre em `Draft`, restrito à role `Employee`;
- `PUT /api/expenses/{id}` edita somente um `Draft` do próprio usuário, restrito à role `Employee`;
- identificador gerado pelo servidor;
- proprietário obtido exclusivamente do token (`ClaimTypes.NameIdentifier`);
- a API recebe o DTO `ExpenseRequest` com apenas `description`, `amount`, `expenseDate` e `categoryId`;
  campos como `id`, `ownerId`, `status`, ator ou horários enviados pelo cliente são ignorados;
- validações:
  - descrição obrigatória, entre 10 e 500 caracteres (espaços nas pontas não contam);
  - valor `decimal` entre `0.01` e `2147483647` (`Int32.MaxValue`), com no máximo 2 casas decimais;
  - data da despesa válida e não futura (data UTC do servidor);
  - categoria existente;
- regras de ownership e estado aplicadas no `ExpenseService` e em `ExpenseDraftRules`;
- histórico (`ExpenseHistory`) gravado na mesma chamada de `SaveChanges` da alteração:
  - `Created` na criação (ator, instante UTC, estado anterior nulo, estado posterior `Draft`);
  - `Updated` em cada edição com alteração real, com os campos alterados em JSON (`from`/`to`);
  - edição sem alteração não gera histórico.

### Respostas

| Situação | Status |
|---|---|
| Criação válida | `201 Created` |
| Edição válida | `200 OK` |
| Entrada inválida | `400 Bad Request` (`ValidationProblem`) |
| Sem token | `401 Unauthorized` |
| Autenticado sem role `Employee` | `403 Forbidden` |
| Reembolso inexistente ou de outro usuário | `404 Not Found` |
| Reembolso fora de `Draft` | `409 Conflict` |

### Categorias

As categorias são criadas de forma idempotente na inicialização:

| Id | Nome |
|---:|---|
| 1 | Alimentação |
| 2 | Transporte |
| 3 | Hospedagem |
| 4 | Material de escritório |
| 5 | Outros |

## I05 implementada

- `POST /api/expenses/{id}/submit` executa `Draft → Submitted`, restrito à role `Employee` e ao proprietário;
- o estado é definido exclusivamente pelo servidor; o endpoint não recebe corpo;
- repetir o envio ou enviar fora de `Draft` retorna `409 Conflict` sem gravar histórico;
- o `UPDATE` do envio só é aplicado se o estado no banco ainda for o lido (`Status` como token de concorrência),
  então dois envios simultâneos não geram histórico duplicado;
- histórico `Submitted` com reembolso, ator do token, instante UTC do servidor, estado anterior `Draft` e posterior `Submitted`,
  gravado na mesma chamada de `SaveChanges`;
- `GET /api/expenses` e `GET /api/expenses/{id}` aplicam a mesma matriz de visibilidade (`ExpenseVisibility`):

| Role | Reembolsos visíveis |
|---|---|
| `Employee` | somente os próprios |
| `Approver` | somente `Submitted` |
| `Finance` | somente `Approved` e `Paid` |
| `Auditor` | todos |
| `Admin` | nenhum (`403`), pois Admin não concede acesso funcional |

- roles acumuladas recebem a união das permissões;
- o filtro é uma expressão traduzida pelo Entity Framework para o `WHERE` do SQL, antes de materializar os dados;
- consultas assíncronas e `AsNoTracking`;
- reembolso inexistente ou fora do escopo retorna `404 Not Found`, sem distinguir os dois casos.

### Respostas da I05

| Situação | Status |
|---|---|
| Envio válido / consulta visível | `200 OK` |
| Sem token | `401 Unauthorized` |
| Envio sem role `Employee` | `403 Forbidden` |
| Consulta sem `Employee`, `Approver`, `Finance` ou `Auditor` | `403 Forbidden` |
| Reembolso inexistente, de outro Employee ou fora do escopo | `404 Not Found` |
| Envio repetido ou fora de `Draft` | `409 Conflict` |

## I06 implementada

A autorização combina três camadas:

1. **Autenticação**: política padrão (`FallbackPolicy`) exige usuário autenticado em qualquer rota sem regra explícita;
   somente `/health`, `/login`, `/register` e o OpenAPI (apenas em Development) são públicos.
2. **Role na rota**: cada endpoint exige as roles que podem usá-lo (`403` quando o usuário não tem nenhuma delas).
3. **Regra contextual no serviço** (`ExpenseAccessPolicy` e `ExpenseVisibility`): ownership, estado e visibilidade
   decidem se aquele usuário pode agir sobre aquele reembolso.

Detalhes:

- o serviço recebe a identidade completa (`ExpenseViewer`) e revalida a role funcional, além do atributo da rota;
- criação, edição e envio usam `ExpenseAccessPolicy.EvaluateDraftChange` (Employee, proprietário e `Draft`);
- a busca por reembolso próprio filtra `Id` e `OwnerId` no SQL;
- leitura e listagem usam `ExpenseVisibility`, traduzida para o `WHERE` antes da materialização;
- `ExpenseAccessPolicy.EvaluateApprovalDecision` e `EvaluatePayment` implementam as proibições de
  autoaprovação e autopagamento, inclusive com roles acumuladas; os endpoints de aprovar, reprovar e pagar
  pertencem às I07 e I08 e devem usar essas regras;
- Admin não recebe acesso funcional aos reembolsos.

### Matriz aplicada

| Operação | Employee | Approver | Finance | Auditor | Admin | Regra contextual (serviço) | Implementada em |
|---|:---:|:---:|:---:|:---:|:---:|---|---|
| Registrar | público | público | público | público | público | Cadastro nunca aceita role | I03 |
| Login | público | público | público | público | público | Credenciais válidas | I02 |
| Listar usuários | não | não | não | não | sim | Somente Admin | I03 |
| Alterar roles | não | não | não | não | sim | Roles conhecidas; não remover a própria role Admin | I03 |
| Criar reembolso | sim | não* | não* | não | não* | Proprietário vem do token | I04 |
| Editar reembolso | sim | não* | não* | não | não* | Proprietário e `Draft`; outro dono → `404`; fora de `Draft` → `409` | I04/I06 |
| Enviar reembolso | sim | não* | não* | não | não* | Proprietário e `Draft`; outro dono → `404`; fora de `Draft` → `409` | I05/I06 |
| Listar reembolsos | próprios | `Submitted` | `Approved`/`Paid` | todos | não | Filtro no SQL; roles somam | I05 |
| Consultar detalhe | próprio | `Submitted` | `Approved`/`Paid` | todos | não | Fora do escopo → `404` | I05 |
| Aprovar/Reprovar | não | sim | não | não | não | Não proprietário → senão `403`; `Submitted` → senão `409` | Regra: I06 · Rota: I07 |
| Pagar | não | não | sim | não | não | Não proprietário → senão `403`; `Approved` → senão `409` | Regra: I06 · Rota: I08 |
| Consultar histórico | próprio | visível | visível | todos | não | Mesma visibilidade do reembolso | I08 |

`*` A permissão existe apenas se o usuário também possuir `Employee`.

### Decisões de resposta

| Situação | Status |
|---|---|
| Sem token ou token inválido | `401` |
| Autenticado sem a role da rota | `403` |
| Approver/Finance agindo sobre reembolso próprio | `403` (o reembolso é visível, mas a operação é proibida) |
| Reembolso inexistente, de outro Employee ou fora do escopo | `404` |
| Transição fora do estado esperado | `409` |
| Aprovar/reprovar reembolso que já saiu da fila (`Approved`, `Rejected`, `Paid`) | `409` |
| Pagar reembolso ainda não aprovado (`Draft`, `Submitted`, `Rejected`) | `404` (fora do escopo do Finance) |

## Banco de dados

Provider: SQLite.

Pacotes principais:

- `Microsoft.EntityFrameworkCore.Sqlite` 10.0.12
- `Microsoft.EntityFrameworkCore.Design` 10.0.12
- `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.12

Na primeira execução, o banco local `expensehub.db` é criado automaticamente.

Os arquivos locais do SQLite não devem ser versionados:

```text
*.db
*.db-shm
*.db-wal
```

## Configurar o Admin sem versionar senha

O projeto utiliza .NET User Secrets.

Na raiz do repositório:

```shell
dotnet user-secrets set "SeedAdmin:Email" "admin@expensehub.local" --project ./sources/ExpenseHub.Api/ExpenseHub.Api.csproj
dotnet user-secrets set "SeedAdmin:Password" "$ADMIN_PASSWORD" --project ./sources/ExpenseHub.Api/ExpenseHub.Api.csproj
```

Cada desenvolvedor deve configurar os User Secrets localmente em sua própria máquina.

Não coloque a senha do Admin em:

- `README.md`;
- `appsettings.json`;
- arquivos `.http`;
- commits;
- pull requests;
- código-fonte.

## Executar

Na raiz do repositório:

```shell
dotnet restore ./sources/ExpenseHub.slnx
dotnet build ./sources/ExpenseHub.slnx
dotnet test ./sources/ExpenseHub.slnx
dotnet run --project ./sources/ExpenseHub.Api/ExpenseHub.Api.csproj
```

O build deve concluir com:

```text
0 Error(s)
0 Warning(s)
```

## Autenticação

### Login

```http
POST /login
```

Exemplo de body:

```json
{
  "email": "usuario@exemplo.com",
  "password": "senha-configurada-pelo-usuario"
}
```

Quando as credenciais forem válidas, a aplicação retorna um token bearer.

Para acessar rotas protegidas:

```text
Authorization: Bearer <accessToken>
```

## Validar I02

1. Configure o Admin por User Secrets.
2. Inicie a aplicação.
3. Faça `POST /login`.
4. Guarde o `accessToken`.
5. Acesse uma rota protegida com:

```text
Authorization: Bearer <accessToken>
```

6. Uma requisição anônima em rota protegida deve retornar `401 Unauthorized`.
7. Um usuário autenticado sem a role necessária deve retornar `403 Forbidden`.

## Validar I03

### Registrar usuário

```http
POST /register
```

Exemplo:

```json
{
  "email": "funcionario@expensehub.local",
  "password": "<senha-local>"
}
```

O cliente não pode atribuir roles durante o cadastro.

Mesmo que seja enviada uma propriedade adicional como:

```json
{
  "role": "Admin"
}
```

o usuário não deve ser promovido.

### Listar usuários

```http
GET /api/admin/users
Authorization: Bearer <AdminToken>
```

Somente um usuário com role `Admin` pode acessar essa rota.

### Alterar roles

```http
PUT /api/admin/users/{id}/roles
Authorization: Bearer <AdminToken>
Content-Type: application/json
```

Exemplo:

```json
{
  "roles": [
    "Employee"
  ]
}
```

Somente as roles conhecidas pelo sistema são aceitas.

Após uma alteração de role, o usuário deve realizar um novo login para receber um token atualizado.

### Casos validados

- cadastro de usuário retorna `201 Created`;
- cadastro não permite autopromoção;
- usuário comum tentando acessar rota Admin recebe `403 Forbidden`;
- Admin consegue atribuir uma role válida;
- role inexistente retorna `400 Bad Request`;
- usuário inexistente retorna `404 Not Found`;
- Admin tentando remover sua própria role `Admin` recebe `409 Conflict`.

## Validar I04

Use um usuário com a role `Employee` (atribuída pelo Admin) e faça login novamente após a atribuição.

### Criar rascunho

```http
POST /api/expenses
Authorization: Bearer <EmployeeToken>
Content-Type: application/json
```

```json
{
  "description": "Almoço com cliente",
  "amount": 150.75,
  "expenseDate": "2026-09-30",
  "categoryId": 1
}
```

### Editar rascunho

```http
PUT /api/expenses/{id}
Authorization: Bearer <EmployeeToken>
Content-Type: application/json
```

```json
{
  "description": "Jantar com cliente",
  "amount": 200.00,
  "expenseDate": "2026-09-30",
  "categoryId": 2
}
```

### Casos validados

- Employee cria `Draft` com proprietário do token: `201 Created`;
- criação sem autenticação: `401 Unauthorized`;
- usuário sem `Employee` (inclusive Admin): `403 Forbidden`;
- descrição fora dos limites, valor inválido, data futura ou categoria inexistente: `400 Bad Request`;
- `ownerId`, `status` ou `id` enviados pelo cliente não alteram os dados controlados pelo servidor;
- proprietário edita o próprio `Draft`: `200 OK`;
- outro Employee tentando editar: `404 Not Found`;
- edição de reembolso fora de `Draft`: `409 Conflict`.

## Validar I05

```http
POST /api/expenses/{id}/submit
Authorization: Bearer <EmployeeToken>
```

```http
GET /api/expenses
Authorization: Bearer <Token>
```

```http
GET /api/expenses/{id}
Authorization: Bearer <Token>
```

### Casos validados

- proprietário envia `Draft`: `200 OK` e estado `Submitted`;
- repetir o envio: `409 Conflict`, sem novo histórico;
- outro Employee tentando enviar: `404 Not Found`;
- Approver ou Auditor tentando enviar: `403 Forbidden`;
- Employee lista somente os próprios reembolsos;
- Approver lista somente `Submitted`;
- Finance lista somente `Approved` e `Paid`;
- Auditor lista todos;
- Employee + Approver lista os próprios e os `Submitted`;
- Admin sem outra role: `403 Forbidden`;
- detalhe fora do escopo: `404 Not Found`;
- rota protegida sem token: `401 Unauthorized`.

## Validar I06

Crie usuários com as roles `Employee`, `Approver`, `Finance`, `Auditor`, `Employee`+`Approver`,
`Employee`+`Finance` e um usuário sem role, além do Admin do seed. Faça login novamente após atribuir roles.

### Casos negativos validados

- requisição anônima ou com token inválido em rota protegida: `401`;
- usuário autenticado sem role: `403` em todas as rotas de reembolso e de administração;
- trocar o identificador na URL não expõe reembolso de outro Employee (`404` em detalhe, edição e envio);
- listagem de um Employee não inclui reembolsos de outros;
- `ownerId`, `status`, `id`, ator e horário enviados pelo cliente são ignorados;
- Auditor lê tudo, mas recebe `403` ao criar, editar, enviar ou alterar roles;
- Admin recebe `403` em todas as rotas de reembolso e continua administrando usuários;
- envio repetido, envio de `Approved`/`Paid` e edição de `Submitted`: `409`;
- Approver lendo `Draft`/`Approved` e Finance lendo `Draft`/`Submitted`: `404`;
- roles acumuladas recebem a união das permissões de leitura;
- autoaprovação e autopagamento: `403`, cobertos por testes unitários até que as rotas das I07/I08 existam.

## Qualidade e segurança

O projeto não deve versionar:

```text
bin/
obj/
*.dll
*.pdb
*.db
*.db-shm
*.db-wal
```

Também não devem ser versionados:

- senhas;
- tokens;
- connection strings sensíveis;
- User Secrets;
- arquivos locais de execução.

Antes de cada Pull Request:

```shell
dotnet restore ./sources/ExpenseHub.slnx
dotnet build ./sources/ExpenseHub.slnx
dotnet test ./sources/ExpenseHub.slnx
```

O objetivo é manter:

```text
0 Error(s)
0 Warning(s)
```

## GitHub

### I01

```text
branch: i01-foundation-ef
PR: I01 — Fundação da solução e Entity Framework Core
referência: Racass/checkpoint-csharpracass-expensehub#1
```

### I02

```text
branch: i02-identity-auth
PR: I02 — Identity, Admin e autenticação
referência: Racass/checkpoint-csharpracass-expensehub#2
```

### I03

```text
branch: i03-user-roles
PR: I03 — Cadastro HTTP e gerenciamento de roles
referência: Racass/checkpoint-csharpracass-expensehub#3
```

### I04

```text
branch: i04-expense-draft
PR: I04 — Criar e editar rascunho
referência: Racass/checkpoint-csharpracass-expensehub#4
```

### I05

```text
branch: i05-submit-query
PR: I05 — Enviar, listar e consultar
referência: Racass/checkpoint-csharpracass-expensehub#5
```

### I06

```text
branch: i06-ownership-access
PR: I06 — Ownership e matriz de acesso
referência: Racass/checkpoint-csharpracass-expensehub#6
```

Não use `Closes`, `Fixes` ou `Resolves` nas referências ao backlog central.
