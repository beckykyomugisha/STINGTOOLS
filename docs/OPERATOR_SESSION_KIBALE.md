# Operator session — everything Revit-blocked, in one sitting

Project **KNP26** · branch `claude/kibale-integration` · PR #649

Every remaining question on this workstream needs Revit. They are ordered so earlier answers gate
later ones — **do not reorder**. 3.1 gates 3.7; 3.3 gates everything from 3.4 on.

Budget ~45 minutes. **RECORD** marks the steps whose answer I need back. For those, report the
*answer*, not the observation — each one states what its outcomes mean.

---

## Before you start

Steps 0.1–0.3 are the deploy sequence. If I have already run them for you, skip to §1.

1. **Close Revit and the Planscape Companion tray app** (right-click the tray icon → Exit; it holds
   the same DLLs). Confirm:
   ```bash
   tasklist | grep -iE "revit\.exe|planscape.companion"
   ```
   Expect no output.
2. Deploy: `deploy.bat` from `C:\Dev\wt-kibale-integration`. Confirm where Revit actually loads from
   — **do not assume, it has moved before**:
   ```bash
   grep -h "<Assembly>" "$APPDATA/Autodesk/Revit/Addins"/*/StingTools.addin | sort -u
   ```
3. Start Revit, open the **KNP26** model, and run **STING → Setup → Load Shared Parameters**.
   **Nothing below works before this.** It is idempotent and safe on a live model.

---

## 1 · DEPTH — are the tag gates TYPE or INSTANCE? **RECORD**

Held since the type-vs-instance gate. One observation settles it, and it gates §7.

Use an **Air Terminal**: it is both in the 42 categories bound by `FAMILY_PARAMETER_BINDINGS.csv`
*and* one of only four with a shipped `categoryDepths` key, so both mechanisms are live on the same
element.

1. Place the universal tag on an Air Terminal.
2. Select the **air terminal** → **Edit Type** → find `TAG_PARA_STATE_3_BOOL`.
   - Absent → Project Setup has not run here. Run it, or use another model.
3. Tick it **on the type**. Watch the tag.
   - Row 3 does **not** appear → expected; host-side gates are inert.
   - Row 3 **does** appear → **stop and tell me.** That overturns the §2.5(a) analysis and matters
     more than the question being asked.
4. **RECORD.** Select the **tag** and find `TAG_PARA_STATE_3_BOOL`:

   | Where it appears | What it means | What follows |
   |---|---|---|
   | the tag's **Edit Type** | gates are **TYPE** | per-type depth works today — keep it, unhold §C as per-type |
   | the tag's **instance Properties** | gates are **INSTANCE** | the type sweep never reaches them, depth is broken today — convert, and write the per-instance writer that does not exist |

---

## 2 · G-8 — does any ACTUAL binding differ from its DECLARED one? **RECORD**

`MR_PARAMETERS.csv` declares **2,997 Type / 395 Instance**. Nothing has ever checked that against
reality. Detection already exists at `Core/Electrical/CableSizerApplyEngine.cs:286-310` — **lift it,
do not rewrite it.**

```csharp
var map = doc.ParameterBindings;
var it  = map.ForwardIterator();
while (it.MoveNext())
{
    var def     = it.Key as Definition;
    var binding = it.Current as ElementBinding;
    if (def == null || binding == null) continue;

    string actual   = (binding is InstanceBinding) ? "Instance" : "Type";
    string declared = DeclaredBindingType(def.Name);      // from MR_PARAMETERS.csv
    if (declared != null && !string.Equals(declared, actual, StringComparison.OrdinalIgnoreCase))
        Report(def.Name, declared, actual);
}
```

**Output three columns: parameter / declared / actual.**

- **No rows** → G-8 is a **documentation defect**. Close it as such.
- **Any row** → a **live silent-write bug**: code written against one scope is writing against the
  other and the write lands nowhere. This is the same defect K-16 turned out to be.

> **Do not change a binding on the declaration alone.** The declaration may be the wrong side — in
> K-16 it was the *code* that was wrong, not the data.

---

## 3 · K-11 ACCEPTANCE — are the PRJ_* now visible? **RECORD**

After Load Shared Parameters, open **Manage → Project Information** and confirm all three appear:

- `PRJ_PROJECT_COD_TXT`
- `PRJ_ORIGINATOR_COD_TXT`
- `PRJ_TB_DESIGN_STAGE_TXT`

**Present** → K-11 is accepted; set `PRJ_PROJECT_COD_TXT` = `KNP26`.
**Absent** → the binding did not apply; stop, and send me `StingTools.log`. Everything below depends
on this.

While you are here, per the corrected decision:

| Field | Value |
|---|---|
| Project Number | `KNP26` |
| Organization Name | **`ACE`** — leave it. ACE are the architects; we issue on their title block. |
| `PRJ_ORG_ORIGINATOR_CODE_TXT` | `PLN` — Planscape authored the containers *(pending ACE's written confirmation)* |
| Project Name | Kibale… — **`KIBALE`** spelling |

---

## 4 · TYPE MARK GENERATOR

**DOCS tab → Type Marks (preview)** first. It writes nothing — not to the model, not to the sequence
store.

Then **Type Marks (assign)** and verify all five:

| Check | Expected |
|---|---|
| **Monotonic** | marks ascend `DR-01`, `DR-02`, … with no gaps filled |
| **Never reused** | delete a marked type, re-run — the next mark continues **upward**, it does not reuse the freed one |
| **Adoption** | a hand-entered mark is reported *Adopted* and left exactly as typed; the sequence continues past it |
| **Idempotent** | re-run immediately — assigns nothing, reports everything already marked |
| **Join** | the join check reports no controlled-vocabulary breaks |

**RECORD** the join check specifically. If any door resolves a material suffix (`DR-STL`), confirm
the mark prefix is `DR-STL` and that segment 7 of that instance's `ASS_TAG_1_TXT` is also `DR-STL`.
A difference there is not cosmetic — the schedule and the tag would disagree about what the product
is.

---

## 5 · TYPE SCHEDULE

**DOCS tab → Type Schedules.** Then open *STING Door Type Schedule*.

- ~**12 rows** for ~**96** door instances (7 identical cottages).
- The **Count** column sums to the instance total.
- Your existing itemised schedule is **untouched** — specification and register, not alternatives.

If it renders one row per instance, `IsItemized` did not take; tell me.

---

## 6 · SHEET NUMBER — does K-13 fire?

1. Produce one sheet through the normal drawing-type path.
2. Confirm the number assembles with **no empty segment** — no `--` anywhere.
3. **Then test the failure deliberately.** Blank `PRJ_PROJECT_COD_TXT`, produce another sheet.
   - Expected: the literal **`{project}`** survives, Revit **rejects** the sheet number, and
     `StingTools.log` names the token *and* the parameter.
   - **Not** expected: a segment silently vanishing. If you see `-PLN-COT01-…` with nothing before
     the first dash, K-13 did not fire — tell me.
4. Restore the value.

---

## 7 · CONFORMANCE + the family category **RECORD** *(gated on §1)*

Run **Family Conformance Check** against the tag library.

1. **RECORD** check (4)'s result for the universal tag and for one per-category tag. Both style
   sample names were wrong until this branch — `TAG_2.5NOM_BLACK_BOOL` (literal dot) and
   `TAG_3BOLD_BLUE_BOOL` (no underscore after the digit) — so the check could not pass for *any*
   family. It should now pass for families carrying the declared matrix.
2. **RECORD — the permanent blocker.** Open `STING_Tag_Universal.rfa` → **Create → Family Category
   and Parameters**. Read the highlighted row, close **without changing it**.

   | Answer | What it means |
   |---|---|
   | **Multi-Category Tags** | cannot tag Rooms, Spaces or Areas — Revit forbids it. `STING - Room Tag.rfa` already covers Rooms; confirm whether Spaces and Areas are in scope here. |
   | a **specific category** | the master is mis-categorised for its role. Survivable (`PropagateUniversalTagCommand` recategorises clones) but it should be Multi-Category. |
   | **Generic Annotations** | it is not a tag and cannot be assigned to a host — a blocking defect. |

   This is the **only** way to establish it. Under K-11e a `.rfa` label is invisible to static
   analysis: all 207 shipped families yield zero readable parameter names because the payload is
   compressed.

---

## 8 · TITLE BLOCK — what feeds DWG NO.? **RECORD**

Open the **ACE title block** in the Family Editor. Select the **DWG NO.** label → **Edit Label** →
read which parameter is bound.

**RECORD that parameter name.** No STING-authored title block declares a `DWG NO.` field — it
appears only in `MR_SCHEDULES.csv` and `ScheduleCommands.cs`, neither of which writes a label — so it
came in with the ACE template and only an open family answers it.

While you are there, note the **Sheet Number** sample value. Both it and DWG NO. currently carry
**8-segment ASSET TAG** patterns (`DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ`) where a **7-segment ISO
drawing reference** belongs. Recommended sample: `KNP26-PLN-COT01-00-DR-A-1001`.

**Do not edit the family.** Report what it says; the change is a decision, not a fix.

---

## What to send back

Just the **RECORD** answers:

1. §1 — Edit Type, or instance Properties?
2. §2 — the parameter/declared/actual table, or "clean"
3. §3 — all three visible? yes/no
4. §4 — join check clean? yes/no
5. §7 — check (4) before/after, and the family category
6. §8 — the parameter feeding DWG NO.

Everything else is pass/fail and only needs reporting if it fails.
