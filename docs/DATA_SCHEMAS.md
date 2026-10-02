# Data files and their schemas

Every data file the plugin or its gates read — `StingTools/Data/**`,
`project-templates/**` (JSON/CSV), `GUIDES/kibale-project-config/**`, and JSON/CSV
under `tools/` — is registered in **`tools/data_schemas.json`**. That file is the
one source of truth for each file's shape. `tools/validate_data_schemas.py` checks
the tree against it, and CI runs it on every PR and every push to `main`
(`stingtools-plugin.yml` → *Validate data files*).

## Why this matters

Nothing here fails loudly. Newtonsoft drops an unknown JSON key without a word,
and a CSV reader that indexes by position reads its neighbour's column after a
column is inserted. D6 added `PROD` to `cost_rates_5d.csv`; the 5D Cost Trace,
still indexing by position, read the discipline letter as the USD rate and
reported "No cost rates found" against a full file.

## Changing a data file

1. Edit the file.
2. If you added, removed, renamed or reordered a column (CSV) or a key (JSON with
   a hand-declared `keys` list), update its entry in `tools/data_schemas.json` **in
   the same commit**, and bump that entry's `schemaVersion`.
   - JSON bound to a POCO (`"poco": [file, class]`) needs no registry edit: the
     allowed keys are read from the C# class. Add the property to the class.
   - A column that may legitimately be absent gets `"optional": true`. There is no
     way to allow undeclared extra columns: declare them.
3. Update every reader of the file. Read CSV columns **by header name** — never by
   position. `BOQ/Rates/CostRateCsv.cs` is the pattern.
4. Run `python tools/validate_data_schemas.py`. Errors name the file, the line,
   the column and what was expected.

## Adding a data file

The gate fails on any file under a registry root that is not registered, and
prints the command to run:

```bash
python tools/validate_data_schemas.py --scaffold "StingTools/Data/MY_FILE.csv"
```

Paste the printed entry under `"schemas"`, replace the `TODO`s (description,
readers, column descriptions, `required` / `enum` / `unique` where they hold). If
the file genuinely has no column or key schema — a multi-table CSV, JSON read by
hand-written code — register it under `"structural"` with the reason instead.
Binaries and documentation go under `"nonData"` as a glob.

## Reading a schema

```bash
python tools/validate_data_schemas.py --describe "StingTools/Data/cost_rates_5d.csv"
```

## What every registered file gets

- UTF-8; no mixed CRLF/LF line endings; not empty.
- JSON: strict parse with **no duplicate keys** (both Newtonsoft and Python keep
  the last one silently).
- CSV table: header equals the declared columns in order; no duplicate or
  whitespace-padded header names; every row has exactly the header's field count
  (an unquoted comma shows up here); `num` / `int` / `enum` / `min` / `pattern` /
  `required` cells; `unique` key columns.
- `--self-test` mutates real files and fails if any of those defects goes
  uncaught; CI runs it before the validation itself.
