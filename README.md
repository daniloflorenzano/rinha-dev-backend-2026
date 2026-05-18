# Rinha de Backend 2026 - .NET 10 e PostgreSQL com pgvector

Implementacao feita sem o uso de LLMs. Usando as tecnicas de otimizacao que ja conhecia (inclusive de outras edicoes da rinha),
mais o que aprendi com a documentacao do pgvector.

Exemplos de tuning aplicados:
- `worker_connections` do Nginx
- `Maximum Pool Size` da string de conexao com o Postgres
- .NET publish como AOT

O codigo c# foi escrito usando majoritariamente o paradigma funcional, pois acreditei ser um perfeito caso de uso. 

## Resultado obtido

```json
{
  "expected": {
    "total": 54100,
    "fraud_count": 24058,
    "legit_count": 30042,
    "fraud_rate": 0.4447,
    "legit_rate": 0.5553,
    "edge_case_count": 797,
    "edge_case_rate": 0.0147
  },
  "p99": "1383.69ms",
  "scoring": {
    "breakdown": {
      "false_positive_detections": 64,
      "false_negative_detections": 65,
      "true_positive_detections": 10618,
      "true_negative_detections": 13402,
      "http_errors": 0
    },
    "failure_rate": "0.53%",
    "weighted_errors_E": 259,
    "error_rate_epsilon": 0.010725,
    "p99_score": {
      "value": -141.04,
      "cut_triggered": false
    },
    "detection_score": {
      "value": 1245.11,
      "rate_component": 1969.6,
      "absolute_penalty": -724.49,
      "cut_triggered": false
    },
    "final_score": 1104.07
  }
}
```

