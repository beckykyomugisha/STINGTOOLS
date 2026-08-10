# Operator session — three blocked questions, one sitting

Everything else on this workstream is closed or measured. These four items need Revit and
cannot be answered from the repo. Do them **in this order** — 5.1 gates 5.3.

Budget ~25 minutes. Take a screenshot at each **RECORD** step.

---

## Before you start

1. Deploy the current build: `deploy.bat` from `C:\Dev\wt-kibale-integration`.
2. Confirm the manifest points where you think it does:
   ```bash
   grep -h "<Assembly>" "$APPDATA/Autodesk/Revit/Addins"/*/StingTools.addin | sort -u
   ```
3. Restart Revit. Open a project that has had **Project Setup** run — that is what applies the
   Type bindings this session depends on.

---

## 5.1 — DEPTH: are the tag gates TYPE or INSTANCE?

Held since the type-vs-instance gate. One observation settles it.

**Why an Air Terminal:** it is both in the 42 categories bound by `FAMILY_PARAMETER_BINDINGS.csv`
*and* one of only 4 categories with a shipped `categoryDepths` key, so both mechanisms are live on
the same element.

1. Place the universal tag on an **Air Terminal**.
2. Select the air terminal → **Edit Type**. Look for `TAG_PARA_STATE_3_BOOL`.
   - **Present** → the Type binding is applied here. Continue.
   - **Absent** → Project Setup has not run on this model. Run it, or use another model. Do not
     continue on this one.
3. Tick it **on the type**. Watch the tag.
   - **Row 3 does NOT appear** → expected. Host-side gates are inert.
   - **Row 3 DOES appear** → stop and report. That would overturn the §2.5(a) analysis and is a
     more significant finding than the question being asked.
4. **RECORD — this is the answer.** Select the **tag** itself and find `TAG_PARA_STATE_3_BOOL`:

   | Where it appears | Verdict |
   |---|---|
   | the tag's **Edit Type** | gates are **TYPE**. Per-type depth works today. Keep per-type; unhold §C. |
   | the tag's **instance Properties** | gates are **INSTANCE**. The type sweep never reaches them — depth is broken today. Convert, and write the per-instance writer that does not yet exist. |

5. Tick it wherever it appeared. Row 3 should appear on the tag — that confirms which parameter
   actually controls it.

**Report back: which of the two rows in step 4.** Nothing else is needed.

---

## 5.2 — G-8: does any ACTUAL binding differ from its DECLARED one?

`MR_PARAMETERS.csv` declares a `Binding_Type` per parameter — **2,997 Type / 395 Instance**. Nothing
has ever checked that against reality.

Detection already exists at `Core/Electrical/CableSizerApplyEngine.cs:286-310` — **lift it, do not
rewrite it.** Run this as a one-off macro or via the MCP tool surface:

```csharp
// Iterate every binding in the document and compare to the declared value.
var map = doc.ParameterBindings;
var it  = map.ForwardIterator();
while (it.MoveNext())
{
    var def     = it.Key as Definition;
    var binding = it.Current as ElementBinding;
    if (def == null || binding == null) continue;

    string actual   = (binding is InstanceBinding) ? "Instance" : "Type";
    string declared = DeclaredBindingType(def.Name);   // from MR_PARAMETERS.csv
    if (declared != null && !string.Equals(declared, actual, StringComparison.OrdinalIgnoreCase))
        Report(def.Name, declared, actual);
}
```

**Output three columns only: parameter / declared / actual.**

- **A clean run (no rows)** → G-8 is a **documentation defect**. Close it as such.
- **Any differing row** → a **live silent-write bug**: code written against one scope is writing
  against the other, and the write lands nowhere.

> **Do NOT change a binding on the strength of the declaration alone.** The declaration may be the
> wrong side. Report the rows; the fix is decided per row afterwards.

---

## 5.3 — Unhold the depth documentation (gated on 5.1)

Only once 5.1 has a definite answer:

- `docs/UNIVERSAL_TAG_CONFORMANCE.md` → unhold **§C** and write the resolved position.
- `GUIDES/KIBALE_NP_BIM_MODELLING_PLAYBOOK.md` → Part 3E, replace the marked depth placeholder.

**If 5.1 did not resolve, leave both held.** Do not write the likely answer.

---

## 5.4 — Clear the last leak artefact

One file remains from the content-library leak:

```
C:\Dev\STINGTOOLS\CompiledPlugin\data\TagFamilies\_BIM_COORD\.sting_live_profile_sync.json
```

This is the **live deploy target**, so:

1. **Close Revit.** Close the **Planscape Companion** tray app (right-click → Exit; it holds the
   same DLLs).
2. Confirm both are gone:
   ```bash
   tasklist | grep -iE "revit\.exe|planscape.companion"
   ```
   Expect no output. **If either is still listed, stop** — do not force it.
3. Archive, verify, then remove:
   ```bash
   cd "/c/Dev/STINGTOOLS/CompiledPlugin/data/TagFamilies"
   find _BIM_COORD -type f            # expect exactly: .sting_live_profile_sync.json
   find _BIM_COORD -type f ! -name ".sting_live_profile_sync.json"   # expect EMPTY
   tar -czf ~/sting_lastleak.tar.gz _BIM_COORD
   rm -rf _BIM_COORD
   ```
4. Confirm the filesystem is clear:
   ```bash
   find /c/Dev /c/Users/del/Documents /c/ProgramData -maxdepth 7 \
        -name ".sting_live_profile_sync.json" 2>/dev/null
   ```
   Expect no output.

The guard added in `94ab17e9f` stops this recurring; this is the last of the existing artefacts.
