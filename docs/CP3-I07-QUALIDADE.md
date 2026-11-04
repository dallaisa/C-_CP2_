# CP3-I07 — Qualidade objetiva das alterações

Referência: [Racass/checkpoint-csharpracass-expensehub#17](https://github.com/Racass/checkpoint-csharpracass-expensehub/issues/17).

Esta issue é transversal: ela não adiciona funcionalidade, e sim registra que as alterações do CP3 não enfraqueceram as
verificações do projeto nem introduziram diagnósticos. A comparação é feita entre o commit entregue no CP2 e o estado atual.

| Referência | Commit |
|---|---|
| CP2 (merge da I10, base de comparação) | `c573cb1` |
| CP3 (estado verificado) | `main` após o merge da CP3-I06 (`a7b5fca`) |

O score geral do pipeline não é uma nota diferencial: o que vale aqui é o que mudou entre os dois commits.

## Como reproduzir a comparação

```powershell
# Arquivos de qualidade alterados desde o CP2 (a saída deve estar vazia)
git diff --stat c573cb1 HEAD -- .editorconfig Directory.Build.props .gitattributes .gitignore scripts/Invoke-CodeQuality.ps1 tests/ docs/code-quality-rules.md docs/code-quality-report.schema.json

# Supressões novas no código (a saída deve estar vazia)
git diff c573cb1 HEAD -- sources | Select-String -Pattern "pragma warning|SuppressMessage|NoWarn|GlobalSuppressions|TreatWarningsAsErrors"

# Diagnósticos de build nos dois commits
git worktree add --detach $env:TEMP\cp2-baseline c573cb1
dotnet build $env:TEMP\cp2-baseline\sources\ExpenseHub.slnx --no-incremental
dotnet build sources/ExpenseHub.slnx --no-incremental
git worktree remove --force $env:TEMP\cp2-baseline

# Verificações do projeto
dotnet format sources/ExpenseHub.slnx --verify-no-changes
pwsh scripts/Invoke-CodeQuality.ps1 -SkipGitleaks
pwsh tests/Invoke-CodeQuality.Unit.Tests.ps1
pwsh tests/Invoke-CodeQuality.E2E.ps1

# Higiene dos fontes
git ls-files | Select-String -Pattern "(^|/)(bin|obj)/|\.(dll|exe|pdb|db|db-shm|db-wal|nupkg|zip|xlsx|user|suo|log)$|\.env$"
```

## Resultados

Executado em 04/11/2026 (Windows, .NET 10).

| Critério | Verificação | Resultado |
|---|---|---|
| Sem novos diagnósticos | `dotnet build --no-incremental` no CP2 e no estado atual | 0 avisos e 0 erros nos dois |
| Sem novos diagnósticos | `dotnet format --verify-no-changes` | sem pendências |
| Naming e formatação | `.editorconfig` e `Directory.Build.props` | inalterados desde o CP2 (StyleCop, análise `Recommended`, `EnforceCodeStyleInBuild`) |
| Analisadores não enfraquecidos | `scripts/Invoke-CodeQuality.ps1`, `tests/`, `docs/code-quality-*` | inalterados desde o CP2 |
| Analisadores não enfraquecidos | busca por `pragma warning`, `SuppressMessage`, `NoWarn`, `GlobalSuppressions` no diff de `sources/` | nenhuma ocorrência nova |
| Verificações preservadas | testes do corretor de qualidade | 15 unitários e 10 E2E aprovados |
| Higiene da entrega | arquivos rastreados `bin/`, `obj/`, `.dll`, `.exe`, `.pdb`, `.db`, `.env`, `.xlsx`, `.zip`, `.log` | nenhum |
| Higiene da entrega | segredos literais no repositório (busca por senhas e tokens usados em testes locais) | nenhum |
| Higiene da entrega | arquivos rastreados acima de 300 KB | nenhum |
| Pipeline local | `Invoke-CodeQuality.ps1 -SkipGitleaks` | 100/100, `passed` |

Mudanças de configuração do CP3: somente o novo workflow `.github/workflows/publish-images.yml` (publicação das imagens,
I05). O workflow `build.yml` de qualidade não foi alterado. As alterações em `.csproj` são pacotes novos
(`DocumentFormat.OpenXml`, `RabbitMQ.Client`, `Microsoft.AspNetCore.TestHost`) e os projetos novos de worker e de testes de
integração.

## Diagnósticos encontrados e corrigidos durante o CP3

Estes achados apareceram nas alterações do CP3 e foram resolvidos **no código**, sem ajustar regras:

| Achado | Origem | Correção |
|---|---|---|
| `CA2000`, `CA1861`, `MSTEST0037` | testes novos da I04 | `StringContent` descartado em helper, arrays `static readonly` e `Assert.HasCount` |
| `FIAP2102` (possível segredo) | `compose.yaml` e workflow da I05 | referências `${...}` e `${{ secrets... }}` entre aspas, que o analisador reconhece como placeholder |
| Credencial RabbitMQ no scan | compose da I03 | corrigido na própria I03 (`3b8d422`) |
| Binário versionado | roteiro dos experimentos (I06) salvava o `.xlsx` em `docs/` | a planilha passa a ser baixada para a pasta temporária e apagada após a conferência |

Nenhuma severidade foi reduzida e nenhum arquivo foi excluído da análise para esconder achados.

## Higiene e convenções

- `.gitignore` cobre `bin/`, `obj/`, `artifacts/`, `*.db`, `*.env` e `.vs/`. O modelo de configuração é `.env.example`, que
  não contém valores.
- Finais de linha em LF, como exige o `.editorconfig`; a verificação `ENDOFLINE` do `dotnet format` passa.
- Arquivos novos seguem as convenções existentes: classes internas com documentação XML, `using` explícitos (sem
  `ImplicitUsings`), namespaces com escopo de arquivo, campos privados estáticos com prefixo `_`.
- Imagens de evidência para o README (prints) são mídia de documentação, não binários de build; devem ser PNG pequenos em
  `docs/images/`, sem tokens, senhas nem dados pessoais.

## Limitações

- O **Gitleaks não foi executado localmente** (binário ausente). Ele roda no pipeline (`code-quality`); o resultado da PR deve
  ser conferido lá.
- A comparação de diagnósticos cobre o que o build e o `dotnet format` reportam. Os arquivos de infraestrutura (`Dockerfile`,
  `compose.yaml`, workflows) não passam por analisador específico além do scanner de segredos do projeto.
