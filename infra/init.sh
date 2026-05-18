#!/bin/bash
set -e

echo "[INIT] Criando extensão vector e tabelas..."
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
    CREATE EXTENSION IF NOT EXISTS vector;
    
    CREATE TABLE IF NOT EXISTS items (
        id SERIAL PRIMARY KEY,
        vector vector(14),
        label char(5)
    );
EOSQL

echo "[INIT] Descompactando references.csv.gz e inputando na tabela items"
gunzip -c /var/lib/postgresql/data_import/references.csv.gz | \
    psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -c "COPY items (vector, label) FROM STDIN WITH (FORMAT csv, DELIMITER '|');"


# Lists em 1732 era o ideal, pq eh a raiz quadrada de 3 milhoes
# porem requer 590 MB de memoria so pro maintenance_work, o que nao temos
# por isso o valor abaixo
# https://github.com/pgvector/pgvector#ivfflat
echo "[INIT] Criando index IVFFlat"
psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -c "SET maintenance_work_mem = '80MB'; CREATE INDEX ON items USING ivfflat (vector vector_l2_ops) WITH (lists = 600);SET maintenance_work_mem = '64MB';"

echo "[INIT] Carga inicial concluída com sucesso!"