# E04 - Isolamento

Execucao 20261104222945 (UTC).

Relatorio do Employee A: `f44747d7-b9bb-4170-9b1d-d38997ea508c`
- [PASS] Employee solicita Mine (HTTP 202)
- [PASS] Employee nao solicita Global (403) 
- [PASS] Auditor nao solicita Mine (403) 
- [PASS] Auditor solicita Global (HTTP 202)
- [PASS] Anonimo recebe 401 
- [PASS] Employee B: detalhe alheio 404 
- [PASS] Employee B: Excel alheio 404 
- [PASS] Auditor: detalhe alheio 404 
- [PASS] Auditor: Excel alheio 404 
- [PASS] Listagem do Auditor nao contem o relatorio alheio 
- [PASS] Employee A: detalhe do relatorio do Auditor 404 
- [PASS] Controle: Employee A le o proprio relatorio (200) 
