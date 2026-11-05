# E03 - Duplicata apos reinicio do worker

Execucao 20261104222945 (UTC).

reportId: `9101475a-406b-4013-9678-1cd33181835e` (concluido no E01)
- [PASS] Mensagem republicada na fila (routed) 
- [PASS] Resultado identico 
- [PASS] generatedAt e completedAt inalterados 
- [PASS] Quantidade de solicitacoes inalterada (2 -> 2)
- [PASS] Fila vazia apos consumo (ACK) 

**Logs de worker para o reportId 9101475a-406b-4013-9678-1cd33181835e** (filtrados e sanitizados):

```text
worker-1  |       Processing report 9101475a-406b-4013-9678-1cd33181835e
worker-1  |       Report 9101475a-406b-4013-9678-1cd33181835e saved as Completed
worker-1  |       Report 9101475a-406b-4013-9678-1cd33181835e delivery acknowledged after persistence
worker-1  |       Report 9101475a-406b-4013-9678-1cd33181835e is already Completed; duplicate delivery ignored without new effect
worker-1  |       Report 9101475a-406b-4013-9678-1cd33181835e delivery acknowledged after persistence
```
- [PASS] Log do worker registra duplicata ignorada 
