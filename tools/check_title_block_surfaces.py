#!/usr/bin/env python3
"""check_title_block_surfaces.py — keep the four title-block surfaces agreeing.

WHY THIS GATE EXISTS
--------------------
A title-block cell only prints when FOUR separate files agree about one
parameter name, and nothing was checking that they did:

  1. StingTools/Data/TITLE_BLOCK.csv
         the row an operator edits in "Edit CSV..."
  2. StingTools/Data/MR_PARAMETERS.txt
         the shared-parameter definition, so the name exists at all
  3. StingTools/Data/RESOLVED_BINDINGS.csv
         the binding, so "Load / Create Shared Parameters" puts it on sheets
  4. StingTools/Data/STING_TITLE_BLOCK_PARAMETERS.txt
         the curated file a user browses to in Manage > Shared Parameters, so
         the parameter can be added to the family and a label bound to it

Break any one and the symptom is identical and silent: the command reports
"written", and the cell on the drawing stays blank. That ambiguity has now cost
this project six separate debugging sessions, each ending at a different one of
the four. It is not a reasoning problem — it is four lists restating each other
with no gate, so drift is invisible until a drawing is printed.

Measured when this gate landed: the CSV shipped 38 rows and Populate iterated a
hard-coded array of 22, so 16 rows — PRJ_TB_DRAWN_BY_TXT and
PRJ_TB_CHECKED_BY_TXT among them — were edited, saved and ignored. Six more were
missing from the curated family file, so no label could reach them.

Exit code 1 on any disagreement. Run from the repo root.
"""

import io
import os
import sys

DATA = os.path.join("StingTools", "Data")


def param_file(path):
    """Revit shared-parameter file -> {name: (guid, datatype)}."""
    out = {}
    for line in io.open(path, encoding="utf-8"):
        if line.startswith("PARAM\t"):
            f = line.rstrip("\n").split("\t")
            out[f[2]] = (f[1], f[3])
    return out


def csv_rows(path):
    """TITLE_BLOCK.csv -> ordered list of the parameter names it has rows for."""
    names = []
    for i, line in enumerate(io.open(path, encoding="utf-8-sig")):
        line = line.rstrip("\n")
        if i == 0 or not line.strip() or line.lstrip().startswith("#"):
            continue
        name = line.split(",")[0].strip()
        if name and name not in names:
            names.append(name)
    return names


def bound_names(path):
    out = set()
    for line in io.open(path, encoding="utf-8"):
        if line.startswith("#") or "," not in line:
            continue
        out.add(line.split(",")[0].strip())
    return out


def main():
    csv = csv_rows(os.path.join(DATA, "TITLE_BLOCK.csv"))
    mr = param_file(os.path.join(DATA, "MR_PARAMETERS.txt"))
    tb = param_file(os.path.join(DATA, "STING_TITLE_BLOCK_PARAMETERS.txt"))
    bound = bound_names(os.path.join(DATA, "RESOLVED_BINDINGS.csv"))

    failures = []

    def fail(title, names, fix):
        if not names:
            return
        failures.append((title, sorted(names), fix))

    fail("CSV rows with no shared-parameter definition",
         [n for n in csv if n not in mr],
         "Add the row to StingTools/Data/MR_PARAMETERS.txt, or delete the CSV row. "
         "Without a definition the name does not exist and every write is a no-op.")

    fail("CSV rows that Load / Create Shared Parameters never binds",
         [n for n in csv if n in mr and n not in bound],
         "Add '<name>,<ALL>' to StingTools/Data/RESOLVED_BINDINGS.csv beside the other "
         "PRJ_TB_* rows. Unbound, the parameter exists in the file and on no sheet.")

    fail("CSV rows absent from the curated title-block parameter file",
         [n for n in csv if n not in tb],
         "Add the row to StingTools/Data/STING_TITLE_BLOCK_PARAMETERS.txt, copying the "
         "GUID and datatype from MR_PARAMETERS.txt. That file is what a user browses to "
         "in Manage > Shared Parameters; a name missing from it can never reach a label.")

    # The GUID is the parameter; the name is a label on top of it. Two files
    # disagreeing here produces two same-named parameters, the write landing on
    # one and the label reading the other — permanently blank, and invisible in
    # every Revit UI.
    drift = [
        f"{n}: title-block file {tb[n][0]}, MR_PARAMETERS {mr[n][0]}"
        for n in tb
        if n in mr and tb[n][0].lower() != mr[n][0].lower()
    ]
    fail("GUID disagreement between the two shared-parameter files", drift,
         "Make the title-block file's GUID match MR_PARAMETERS.txt. Mismatched GUIDs "
         "create two parameters with one name: the plugin writes one, the label reads "
         "the other, and nothing anywhere reports a problem.")

    types = [
        f"{n}: title-block file {tb[n][1]}, MR_PARAMETERS {mr[n][1]}"
        for n in tb
        if n in mr and tb[n][1] != mr[n][1]
    ]
    fail("Datatype disagreement between the two shared-parameter files", types,
         "Make the datatypes match. A YESNO written as text fails silently.")

    print(f"Title-block surface gate")
    print(f"  TITLE_BLOCK.csv rows                      : {len(csv)}")
    print(f"  STING_TITLE_BLOCK_PARAMETERS.txt entries  : {len(tb)}")
    print(f"  MR_PARAMETERS.txt definitions             : {len(mr)}")
    print(f"  RESOLVED_BINDINGS.csv bound names         : {len(bound)}")

    if not failures:
        print("\n  OK — every CSV row is defined, bound, and offered to the family.")
        return 0

    for title, names, fix in failures:
        print(f"\n  FAIL — {title} ({len(names)}):")
        for n in names:
            print(f"      {n}")
        print(f"    Fix: {fix}")

    print(f"\n{sum(len(n) for _, n, _ in failures)} problem(s) across "
          f"{len(failures)} check(s).")
    return 1


if __name__ == "__main__":
    sys.exit(main())
