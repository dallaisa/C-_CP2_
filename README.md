# ExpenseHub — Etapa I01

Versão preparada a partir do template do checkpoint para concluir a **I01 — Fundação da solução e Entity Framework Core**.

## Implementado

- solução `ExpenseHub.slnx` mantida com API e projeto de testes;
- Entity Framework Core com SQLite;
- `ExpenseHubDbContext`;
- entidades mínimas `Expense`, `ExpenseCategory`, `ExpenseHistory` e `PaymentRecord`;
- relacionamentos, limites de tamanho, precisão monetária e índice único de pagamento;
- banco SQLite local criado automaticamente com `EnsureCreatedAsync`;
- arquivos locais do SQLite ignorados pelo Git;
- endpoint original `GET /health` preservado.

## Banco de dados

Provider: SQLite.

Pacotes:
- `Microsoft.EntityFrameworkCore.Sqlite` 10.0.12
- `Microsoft.EntityFrameworkCore.Design` 10.0.12

A connection string local não contém segredo e está em `appsettings.json`.

Na primeira execução, o arquivo `expensehub.db` é criado automaticamente e não deve ser versionado.

Para alterações futuras de schema, o projeto já possui o pacote de design do EF Core. Antes da entrega final, migrations podem ser adotadas com:

```shell
dotnet tool install --global dotnet-ef
dotnet ef migrations add NomeDaMigration --project ./sources/ExpenseHub.Api/ExpenseHub.Api.csproj
dotnet ef database update --project ./sources/ExpenseHub.Api/ExpenseHub.Api.csproj
```

## Executar

```shell
dotnet restore ./sources/ExpenseHub.slnx
dotnet build ./sources/ExpenseHub.slnx
dotnet test ./sources/ExpenseHub.slnx
dotnet run --project ./sources/ExpenseHub.Api/ExpenseHub.Api.csproj
```

Depois, teste:

```text
GET http://localhost:5245/health
```

## Issue

Branch sugerida: `i01-foundation-ef`

Na pull request, referencie:

```text
Racass/checkpoint-csharpracass-expensehub#1
```

Não use `Closes`, `Fixes` ou `Resolves`.
