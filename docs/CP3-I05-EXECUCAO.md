# CP3-I05 — Publicação e execução reproduzível

Referência: [Racass/checkpoint-csharpracass-expensehub#15](https://github.com/Racass/checkpoint-csharpracass-expensehub/issues/15).

## Visão geral

Duas imagens são publicadas no GitHub Container Registry (GHCR), depois que build e testes passam, e o
`compose.yaml` na raiz as executa junto com o RabbitMQ.

| Serviço Compose | Imagem | Função |
|---|---|---|
| `rabbitmq` | `rabbitmq:4.1-management` | Broker; fila durável `expensehub.reports`; painel em `127.0.0.1:15672` |
| `api` | `ghcr.io/dallaisa/expensehub-api:<commit>` | Aplicação web; porta `127.0.0.1:8080` |
| `worker` | `ghcr.io/dallaisa/expensehub-worker:<commit>` | Consumidor C# dos relatórios |

Volumes nomeados: `expensehub-data` (SQLite e chaves de autenticação, montado em `/data` nos dois processos) e
`rabbitmq-data` (mensagens e fila).

## Imagens publicadas e versão

- Workflow: `.github/workflows/publish-images.yml`. Roda em `push` na `main` e em `workflow_dispatch`.
- O job `publish` tem `needs: test`: só publica se `dotnet restore`, `build` e `test` da solução passarem.
- Tag = **SHA completo do commit** (`${{ github.sha }}`). Não existe tag `latest`.
- Cada imagem carrega o rótulo `org.opencontainers.image.revision` e a variável `EXPENSEHUB_REVISION` com o mesmo SHA.
- Dockerfiles: `sources/ExpenseHub.Api/Dockerfile` e `sources/ExpenseHub.Worker/Dockerfile` (contexto: raiz do repositório).
  Ambas as imagens usam a base `aspnet`, pois o worker referencia o projeto da API e precisa do
  `Microsoft.AspNetCore.App`. Os processos rodam como usuário não-root (`$APP_UID`).

### Acesso para correção

Pacotes do GHCR nascem **privados**. Antes da entrega, o grupo deve, em *Packages → expensehub-api / expensehub-worker →
Package settings → Change visibility*, torná-los **públicos**, ou fornecer ao professor um token somente de leitura
(`read:packages`) pelo Teams. Credenciais nunca entram no repositório.

Referências completas a informar na entrega (substitua `<commit>` pelo SHA final da `main`):

```text
ghcr.io/dallaisa/expensehub-api:<commit>
ghcr.io/dallaisa/expensehub-worker:<commit>
```

## Banco e armazenamento

- Provedor: **SQLite**, o mesmo da CP2. Não há migração geral de banco.
- Os dois processos usam `Data Source=/data/expensehub.db` no **mesmo volume nomeado** `expensehub-data`. A configuração
  vem do mesmo trecho do `compose.yaml`, então não há bases divergentes.
- A `api` inicializa o esquema (`EnsureCreated` + tabela `Reports`) e as categorias. O `worker` só inicia depois que a
  `api` está saudável (`depends_on: condition: service_healthy`) e não cria banco vazio.
- Use **volume nomeado**, não pasta do Windows montada por bind: SQLite com WAL não é confiável em compartilhamento NTFS.
- As chaves do Data Protection (que protegem os tokens bearer) ficam em `/data/keys` (`DataProtection__KeysPath`).
  Assim, recriar a `api` não invalida a configuração de autenticação.
- O nome do host do RabbitMQ é fixo (`expensehub-rabbitmq`): o nó do RabbitMQ deriva do hostname, e um hostname novo a
  cada recriação deixaria a fila durável e as mensagens para trás no volume.

## Configuração sem segredos

```powershell
Copy-Item .env.example .env   # o .env é ignorado pelo Git (*.env)
```

Preencha no `.env`:

| Variável | Conteúdo |
|---|---|
| `IMAGE_TAG` | SHA completo do commit das imagens a executar |
| `RABBITMQ_DEFAULT_USER` / `RABBITMQ_DEFAULT_PASS` | Credenciais do broker, repassadas à web e ao worker |
| `SEED_ADMIN_EMAIL` / `SEED_ADMIN_PASSWORD` | Conta Admin criada na primeira inicialização |

O Compose se recusa a iniciar se qualquer uma estiver ausente (`${VAR:?mensagem}`). Não há valor padrão para credenciais.
`SEED_ADMIN_PASSWORD` deve cumprir a política do ASP.NET Core Identity (6 ou mais caracteres, com maiúscula, minúscula, número e
símbolo). Com uma senha fraca, a `api` encerra na inicialização, reinicia em loop e o `worker` não sobe; o log mostra a regra
violada, nunca a senha. Evite `$`, `#`, aspas, `\` e espaços no `.env`.
`RABBITMQ_DEFAULT_*` só inicializam um volume novo do RabbitMQ; em um volume existente use as credenciais já provisionadas.

## Execução

```powershell
docker compose pull
docker compose up -d
docker compose ps
docker compose logs --tail 80 worker
```

Se o pacote for privado: `docker login ghcr.io -u <usuario>` antes do `pull`, com o token fornecido (sem gravá-lo no repositório).

Teste rápido: `GET http://localhost:8080/health` deve retornar `{"status":"ok"}`. O fluxo de relatórios usa as rotas da API
(`POST /login`, `POST /api/reports`, etc.); veja `sources/ExpenseHub.Api/ExpenseHub.Api.http`. O painel do RabbitMQ fica em
`http://localhost:15672`, com as credenciais do `.env`.

### Worker parado e retomado (E02)

```powershell
docker compose stop worker
# solicite um relatório; a mensagem fica Ready na fila expensehub.reports
docker compose up -d worker
docker compose logs --tail 80 worker
```

### Recriar somente web e worker (E05)

```powershell
docker compose up -d --force-recreate api worker
```

Os volumes `expensehub-data` e `rabbitmq-data` permanecem, então relatórios concluídos continuam consultáveis.
**Não use `docker compose down -v`, nem remova volumes**: isso apaga o banco e não prova persistência.

### Evidência da versão em execução

```powershell
docker compose images
docker compose ps --format "table {{.Service}}\t{{.Image}}"
docker inspect expensehub-api-1 --format "{{ index .Config.Labels \"org.opencontainers.image.revision\" }}"
docker inspect expensehub-worker-1 --format "{{ index .Config.Labels \"org.opencontainers.image.revision\" }}"
docker compose exec api printenv EXPENSEHUB_REVISION
```

Os dois rótulos devem coincidir com `IMAGE_TAG` e com o commit final da `main`. Os nomes dos contêineres
(`expensehub-api-1`, `expensehub-worker-1`) vêm do projeto `expensehub` definido no `compose.yaml`.

## Verificações realizadas

| Item | Resultado |
|---|---|
| `dotnet build sources/ExpenseHub.slnx` | 0 avisos, 0 erros |
| `dotnet test sources/ExpenseHub.slnx` | testes unitários e de integração passando |
| `dotnet publish` da API e do worker | concluído; o worker exige `Microsoft.AspNetCore.App` e leva `appsettings.Worker.json` |
| Teste unitário das chaves do Data Protection | payload protegido por um provedor é lido por um novo provedor com o mesmo diretório |
| Sintaxe YAML de `compose.yaml` e do workflow | válida |

## Limitações

- **A execução com Docker não foi feita no ambiente de desenvolvimento**, que não tem Docker instalado. Os Dockerfiles, o
  `compose.yaml` e o workflow foram escritos e validados apenas quanto à sintaxe e ao `dotnet publish`. A construção das
  imagens, a publicação no GHCR, o `docker compose pull/up` e o `--force-recreate` precisam ser executados e registrados
  no dossiê da I06, a partir da primeira publicação real.
- O healthcheck da `api` usa `/dev/tcp` do bash por a imagem `aspnet` não incluir `curl`.
- A tag `rabbitmq:4.1-management` não está fixada por digest; fixe-a no ambiente de entrega se precisar de
  reprodutibilidade estrita.
- SQLite é um arquivo compartilhado de uma única máquina; execução em hosts distintos está fora do escopo.
