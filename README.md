# ExpenseHub — I01 + I02

Versão preparada para concluir:

- **I01 — Fundação da solução e Entity Framework Core**
- **I02 — Identity, Admin e autenticação**

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
- rota de verificação `GET /api/admin/health` protegida por role `Admin`;
- usuário anônimo recebe `401`;
- usuário autenticado sem role Admin recebe `403`.

> A rota `/api/admin/health` é apenas uma evidência temporária da I02. Na I03 ela pode ser substituída pelas rotas administrativas obrigatórias.

## Banco

Provider: SQLite.

Pacotes principais:

- `Microsoft.EntityFrameworkCore.Sqlite` 10.0.12
- `Microsoft.EntityFrameworkCore.Design` 10.0.12
- `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.12

Na primeira execução, `expensehub.db` é criado automaticamente.

Se você já executou a versão da I01 antes de aplicar a I02, apague **apenas o arquivo local `expensehub.db`** uma vez antes de iniciar a I02. Ele é ignorado pelo Git e a I02 adiciona as tabelas do Identity.

## Configurar o Admin sem versionar senha

O projeto usa .NET User Secrets.

No terminal, na raiz do repositório:

```shell
dotnet user-secrets set "SeedAdmin:Email" "admin@expensehub.local" --project ./sources/ExpenseHub.Api/ExpenseHub.Api.csproj
dotnet user-secrets set "SeedAdmin:Password" "$ADMIN_PASSWORD" --project ./sources/ExpenseHub.Api/ExpenseHub.Api.csproj
```

No PowerShell, prefira informar a senha pela sua sessão local em vez de escrevê-la em arquivo versionado.

Não coloque a senha no README, `appsettings.json`, `.http`, commit ou pull request.

## Executar

```shell
dotnet restore ./sources/ExpenseHub.slnx
dotnet build ./sources/ExpenseHub.slnx
dotnet test ./sources/ExpenseHub.slnx
dotnet run --project ./sources/ExpenseHub.Api/ExpenseHub.Api.csproj
```

## Validar I02

1. Inicie a aplicação.
2. Faça `POST /login` com o Admin configurado.
3. Guarde o `accessToken` retornado.
4. Faça `GET /api/admin/health` com `Authorization: Bearer <accessToken>`.
5. Sem token, a rota deve retornar `401`.
6. Com usuário autenticado sem role Admin, a rota deve retornar `403` quando a I03 disponibilizar cadastro/roles.

## GitHub

Para I01:

```text
branch: i01-foundation-ef
PR: I01 — Fundação da solução e Entity Framework Core
referência: Racass/checkpoint-csharpracass-expensehub#1
```

Para I02:

```text
branch: i02-identity-auth
PR: I02 — Identity, Admin e autenticação
referência: Racass/checkpoint-csharpracass-expensehub#2
```

Não use `Closes`, `Fixes` ou `Resolves` nas referências ao backlog central.
