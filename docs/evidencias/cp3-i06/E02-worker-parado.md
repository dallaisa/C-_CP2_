# E02 - Worker parado

Execucao 20261104222945 (UTC).

reportId: `40d0370b-1832-413d-86cf-74438ed48289`
- [PASS] POST retorna 202 com o worker parado (HTTP 202)
- [PASS] RabbitMQ: mensagem Ready na fila (Ready=1, Unacked=0)
- [PASS] Relatorio continua Pending 
- [PASS] Excel do relatorio nao concluido retorna 409 
- [PASS] Mesmo reportId chega a Completed 
- [PASS] Fila esvaziada (Ready=0, Unacked=0) (Ready=0, Unacked=0)

**Logs de worker para o reportId 40d0370b-1832-413d-86cf-74438ed48289** (filtrados e sanitizados):

```text
worker-1  |       Processing report 40d0370b-1832-413d-86cf-74438ed48289
worker-1  |       Report 40d0370b-1832-413d-86cf-74438ed48289 saved as Completed
worker-1  |       Report 40d0370b-1832-413d-86cf-74438ed48289 delivery acknowledged after persistence
```
