# E01 - Fluxo normal

Execucao 20261104222945 (UTC).

Dados: A=100 (Approved), 50 (Approved), 25 (Paid) no periodo; B=200 (Approved); A=500 (Paid) fora do periodo.

reportId: `9101475a-406b-4013-9678-1cd33181835e`
- [PASS] POST /api/reports retorna 202 (HTTP 202)
- [PASS] Location aponta para o detalhe 
- [PASS] Relatorio chega a Completed 
- [PASS] totalCount = 3 e totalAmount = 175 
- [PASS] Approved 2 / 150 e Paid 1 / 25 
- [PASS] Cinco estados presentes 
- [PASS] Excel retorna 200 e .xlsx 
- [PASS] Planilha e um arquivo zip OOXML 
- [PASS] Planilha contem 3, 175, 150 e 25 

**Logs de api para o reportId 9101475a-406b-4013-9678-1cd33181835e** (filtrados e sanitizados):

```text
api-1  |       Report 9101475a-406b-4013-9678-1cd33181835e publication confirmed
```

**Logs de worker para o reportId 9101475a-406b-4013-9678-1cd33181835e** (filtrados e sanitizados):

```text
worker-1  |       Processing report 9101475a-406b-4013-9678-1cd33181835e
worker-1  |       Report 9101475a-406b-4013-9678-1cd33181835e saved as Completed
worker-1  |       Report 9101475a-406b-4013-9678-1cd33181835e delivery acknowledged after persistence
```
