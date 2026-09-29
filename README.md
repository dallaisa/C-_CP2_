# ExpenseHub — I01 + I02 + I03

Versão preparada para concluir:

- **I01 — Fundação da solução e Entity Framework Core**
- **I02 — Identity, Admin e autenticação**
- **I03 — Cadastro HTTP e gerenciamento de roles**

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

Não use `Closes`, `Fixes` ou `Resolves` nas referências ao backlog central.
