Foi mais facil transformar o arquivo json em csv e copiar ele para o container do que trabalhar com o json direto no arquivo init.sh

```bash
gunzip -c references.json.gz | jq -r '.[] | "[\(.vector | join(","))]|\(.label)"' > references.csv

gzip references.csv
```