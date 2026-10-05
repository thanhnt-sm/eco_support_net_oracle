# Golden-corpus collection tooling

`collect_hallucinations.py` turns real model mistakes into draft cases for
`tests/DataGuard.GoldenCorpus.Tests/golden-corpus/`. It implements the loop described in
`research/muc_tieu/4.md` ("Bộ test corpus theo taxonomy"): instead of inventing test cases,
ask several models to write data-access SQL against a real schema, run what they produce on a
real database, and keep the failures, labeled with the H1/H2/H3 taxonomy.

This is documented tooling, **not a CI step**. It is standard-library Python 3.10+ only and
contains no model or database client; both are pluggable commands.

## The loop

1. **Schema.** Start a database with a realistic schema. The Testcontainers images used by the
   live tests work well (`gvenzl/oracle-free:23-slim-faststart`, `mcr.microsoft.com/mssql/server:2022-latest`,
   `mysql:8.4`, `postgres:16-alpine`). Export the same schema as JSON in the corpus shape
   (`{"tables": [{"name": "ORDERS", "columns": ["ID", "CUSTOMER_ID"]}]}`), and optionally the DDL as text
   for the prompt (`--schema-prompt`).
2. **Prompt.** Write DAO-layer tasks (`--prompt-file`: JSON list/JSON lines of `{id, prompt}`, or text
   blocks separated by blank lines), for example "write a Dapper query for the top 10 customers by revenue".
3. **Generate.** `--generate-cmd` runs once per prompt (`--samples N` for more), receives the schema text
   plus the prompt on stdin, and prints the answer; the first fenced `sql` block is used. To replay a run
   offline, pass `--candidates` (JSON lines `{prompt_id, prompt, sql}`) instead.
4. **Execute.** `--execute-cmd` receives each SQL statement on stdin. A non-zero exit or recognizable error
   text (`ORA-nnnnn`, `ERROR nnnn`, `Msg n, Level n`, `ERROR:`) is a failure; SQL that runs is discarded
   (the model got it right).
5. **Label.** Database errors are mapped to the taxonomy:

   | Label | Signal | Draft `expectedDiagnostics` |
   |---|---|---|
   | H1 phantom identifier | missing table (ORA-00942, `Invalid object name`, MySQL 1146, PG `relation ... does not exist`), or a column that exists in no table | `DG015` / `DG016` Error |
   | H2 column/table mismatch | missing column that exists in another table of the schema | `DG016` Error |
   | H3 dialect confusion | syntax error on the target dialect, or a foreign-dialect function/clause (`GROUP_CONCAT`, `ISNULL`, `TOP`, `LIMIT`, `NVL`, `[Col]`, ...) | left empty: fill in by hand |

   Failures that match nothing are dropped unless `--keep-unlabeled`.
6. **Write drafts.** Each failure becomes `out/<Category>/<id>.json` (`out/` is git-ignored) with
   `provenance: {"source": "llm", "model": "<--model>", "date": "<--date>"}` and the database error in `notes`.
7. **Review (human).** Run the draft through the corpus test, correct `expectedDiagnostics` (the corpus
   compares the full Error/Warning set, so every finding must be listed), trim `notes`, and move it into the
   corpus directory. If DataGuard misses a real hallucination, keep the case and open an issue for the rule:
   that gap is exactly what the corpus exists to show.

## Example

```bash
docker run -d --name dg-oracle -p 1521:1521 -e ORACLE_PASSWORD=Secret_1 \
  -e APP_USER=dataguard -e APP_USER_PASSWORD=DataGuard_Test_1! gvenzl/oracle-free:23-slim-faststart

python3 tools/corpus/collect_hallucinations.py \
  --prompt-file prompts.txt --schema-prompt schema.sql --schema schema.json \
  --provider oracle --model qwen2.5-coder:7b --samples 3 \
  --generate-cmd "ollama run qwen2.5-coder:7b" \
  --execute-cmd "docker exec -i dg-oracle sqlplus -s dataguard/DataGuard_Test_1!@FREEPDB1"
```

Other execute commands:

```bash
--execute-cmd "docker exec -i dg-pg psql -v ON_ERROR_STOP=1 -U dataguard dataguard"
--execute-cmd "docker exec -i dg-mysql mysql -udataguard -pDataGuard_Test_1! dataguard"
--execute-cmd "docker exec -i dg-mssql /opt/mssql-tools18/bin/sqlcmd -C -b -S localhost -U sa -P <password>"
```

`--dry-run` skips execution and labels from the SQL text only (useful to test prompts).

## Case schema reminder

```json
{
  "testCase": "H1_LLM_QWEN2_5_CODER_7B_P001_01",
  "category": "H1_Phantom_Identifier",
  "description": "...",
  "provenance": { "source": "llm", "model": "qwen2.5-coder:7b", "date": "2026-10-05" },
  "input": { "sql": "...", "provider": "Oracle", "databaseSchema": { "tables": [] } },
  "expectedDiagnostics": [ { "ruleId": "DG015", "messageContains": "Table 'ORDERS_SUMMARY'", "severity": "Error" } ]
}
```

Hand-written cases use `"source": "manual"` and `"model": null`. See `docs/07-testing/test-strategy.md`
("Golden Corpus Tests") for the assertions the corpus enforces.
