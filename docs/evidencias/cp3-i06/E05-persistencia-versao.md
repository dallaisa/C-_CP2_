# E05 - Persistencia e versao

Execucao 20261104222945 (UTC).

reportId: `9101475a-406b-4013-9678-1cd33181835e` (concluido no E01)

**docker compose images (antes):**

```text
CONTAINER               REPOSITORY                           TAG                                        PLATFORM            IMAGE ID            SIZE                CREATED
expensehub-api-1        ghcr.io/dallaisa/expensehub-api      52443052bab09fc7e4da4aa9acb6a39041bc8387   linux/amd64         f9e48fabb4ee        412MB               11 minutes ago
expensehub-rabbitmq-1   rabbitmq                             4.1-management                             linux/amd64         e1763bfb4a15        386MB               8 days ago
expensehub-worker-1     ghcr.io/dallaisa/expensehub-worker   52443052bab09fc7e4da4aa9acb6a39041bc8387   linux/amd64         f323c5fb259d        413MB               11 minutes ago

```
- [PASS] Revisao da imagem api = IMAGE_TAG (52443052bab09fc7e4da4aa9acb6a39041bc8387)
- [PASS] Revisao da imagem worker = IMAGE_TAG (52443052bab09fc7e4da4aa9acb6a39041bc8387)
- [PASS] API saudavel apos recriar api e worker 
- [PASS] Resultado anterior consultavel apos recriar 
- [PASS] Resultado identico ao anterior 
- [PASS] Excel ainda baixavel 
- [PASS] Revisao inalterada apos recriar (api) 
- [PASS] Revisao inalterada apos recriar (worker) 

Comando usado: `docker compose up -d --force-recreate api worker` (sem remover volumes).
