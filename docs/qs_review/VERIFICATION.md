# Independent verification — site, civil and external-utility work-section codes

Verification of the completed review recorded in `docs/qs_review/nrm2_site_civil_review.csv`,
as applied to `StingTools/Data/STING_CSI_MASTERFORMAT_MAP.csv`.

---

## 1. Scope and basis

### What was checked

All 37 reviewed rows (rows 109–112, 123, 131–162 of the classification map), each judged on:

- whether the code now held in the map is the right one for the element the rule matches;
- whether the rule actually selects the elements the reviewer assumed it selects, tested
  against the map's own match-scoring rules;
- whether rows that must move together do move together;
- whether the three new sections were the right structural answer.

The following supporting material was read in full and relied upon:

| Source | Relied on for |
|---|---|
| `StingTools/BOQ/BOQCostManager.cs`, `GuessSectionName` | The section vocabulary — the authority on what each integer means, because it is the function that prints the heading on the bill |
| `StingTools/BOQ/BOQCostManager.cs`, `DeriveNrm2Section` | What happens to an element that matches no rule |
| `StingTools/BOQ/BOQCostManager.cs`, take-off exclusion set | Whether the presentation-content exclusion is real |
| `StingTools/Core/Classification/CsiMasterFormat.cs`, `Score` | The match weights, so that "which rule wins" is established fact rather than assumption |
| `StingTools/Data/STING_CSI_MASTERFORMAT_MAP.csv` | The rules as they now stand, including rows outside the reviewed 37 that interact with them |
| `StingTools/BOQ/MeasurementStandard/MeasurementStandards.cs` | What the alternative measurement standards do with the codes used |
| `docs/qs_review/README.md` | The reviewer's stated basis |

### Basis of judgement — read this before reading any verdict

**The `Nrm2` column in this map is an in-house section vocabulary. It is not RICS NRM2
work-section numbering.** The two agree at `14` Masonry, `15` Structural metalwork and
`16` Carpentry and diverge elsewhere — `1` is Demolitions here and Preliminaries in NRM2,
`3` is Groundworks here and Demolitions in NRM2, `33` is Mechanical services here and
Drainage above ground in NRM2, `36` is Security and fire alarm here and Fencing in NRM2.

Every verdict below is given against the in-house vocabulary. Writing a technically correct
published-NRM2 number into this column would move money into the wrong section of an issued
bill while looking, to a reader who spot-checks it, exactly like a correction.

Where the vocabulary itself appears to be the problem, that is recorded separately at
section 7 and is deliberately **not** encoded in any row verdict.

### Verdict scale

| | |
|---|---|
| **CONFIRMED** | The code is right. I would have reached the same answer. |
| **ACCEPTABLE** | Defensible; another surveyor could properly differ, and I would have done otherwise. Not an error, and not a reason to withhold signature. |
| **WRONG** | Should change. The correct code is stated. |

---

## 2. Summary

| Verdict | Rows |
|---|---:|
| CONFIRMED | 31 |
| ACCEPTABLE | 5 |
| WRONG | 1 |
| **Total** | **37** |

The single WRONG finding is row 153, and it cannot be corrected on its own — see section 3.

The reviewer's work is sound. The reasoning recorded against each row is specific to the
element the rule matches rather than generic, the two genuinely contested rows were flagged
as contested rather than resolved silently, and the structural gap in the vocabulary was
identified and closed rather than worked around. Verification checks that go beyond reading
the notes are recorded at section 6 so that this record shows what was actually tested.

---

## 3. Adjudication — row 153, buried gas distribution

**Rule:** `Pipes` / type name matching `site|external|buried|underground` / `Sys = GAS`
→ CSI `33 51 00` Natural-Gas Distribution. **Currently `33` Mechanical services.**

**Determination: change to `32` Piped supply systems — and move the in-building gas row
with it.**

Reasoning:

1. **The heading test.** `32` is titled *Piped supply systems*. A buried medium-density
   polyethylene gas main in a trench is a piped supply system on the plain reading of that
   heading. That is what the section exists for: gas, water and fuel supply distribution.
2. **What `33` actually contains.** Every other member of `33` in this map is HVAC — ducts
   and duct fittings, air terminals, hydronic and refrigerant pipework, air-handling units,
   chillers, boilers, fans, heat pumps, energy-recovery units. Gas is the only occupant of
   that section that is a supply utility rather than part of a heating, cooling or
   ventilating installation. It is the outlier, not the precedent.
3. **The package test.** A buried site gas main is a civils/utilities package priced against
   a trench, a bedding and a purge-and-test. Billing it in the same section as air-handling
   units invites a tenderer to price it as building services and to miss the excavation
   interface entirely.
4. **Consistency across the pair.** The reviewer is right that row 153 and the in-building
   gas row (`Pipes` / `Sys = GAS` → CSI `23 11 23`) must not be separated. I considered and
   rejected splitting them — buried gas at `32`, in-building gas at `33` — on the ground
   that it would bill two lengths of the same installation under two headings decided by
   whether the pipe type name happens to contain the word "buried". That is a naming
   accident deciding a bill section, which is precisely the failure this whole review
   exists to prevent.

**The dependency is binding, and it runs the other way too.** The in-building gas row is
**outside the reviewed 37** and therefore was not in the sheet. Changing row 153 alone would
create exactly the split rejected at point 4 above, and would be **worse than leaving both
at `33`**. Either both rows move to `32`, or neither moves. If the signer is not prepared to
authorise the out-of-scope row, the correct action is to leave row 153 at `33` and record the
point as open — not to apply half of it.

I record that the reviewer's answer was defensible: keeping the pair consistent was the more
important of the two considerations, and with only the in-scope row available to change,
leaving it alone was the safe call. A definite answer was asked for, and mine is that both
rows belong at `32`.

## 3b. Adjudication — row 162, packaged sewage treatment plant

**Rule:** `Mechanical Equipment` / family name matching `sewage treatment|package plant|STW`
→ CSI `33 30 00` Sanitary Sewerage Utilities. **Currently `31` Drainage below ground.**

**Determination: `31` is correct. Confirmed. No change.**

Reasoning:

1. The plant is a terminal component of a below-ground foul drainage installation. It is
   installed by the drainage contractor, in the same excavation, in the same sequence, and
   it is commissioned as part of the same system as the runs discharging into it.
2. The map already bills septic tanks at `31` (row 160). A packaged treatment plant is
   functionally an aerated septic tank; billing the two under different headings would put
   directly comparable items in different bills and defeat any like-for-like comparison at
   tender.
3. The contrary argument — that packaged plant is mechanical plant and belongs at `33` —
   rests on the plant's internal content (blowers, pumps, control panel). That content is
   supplied within a proprietary package at a single rate. It is not separately measured
   here, so classifying the package by its internals bills a mechanical item that nobody
   prices as one.
4. The reviewer's observation that `32` is the weakest of the three candidates is correct
   and I adopt it.

One qualification for the signer, not a change to the code: where a scheme uses a large
above-ground treatment works with a building and a distinct M&E content, that M&E content
should be measured and billed separately at `33` rather than absorbed into a single `31`
line. That is a project-specific measurement decision, not a defect in this rule, and the
single line at `31` is the right default.

---

## 4. Rows rated ACCEPTABLE or WRONG

### WRONG

| Row | Rule | Current | Should be | Why |
|---|---|---|---|---|
| 153 | `Pipes` + type `site\|external\|buried\|underground` + `Sys=GAS` — buried gas distribution | `33` | `32` | See section 3. **Conditional on the in-building gas row (CSI `23 11 23`) moving with it.** Applying this row alone is worse than applying neither. |

### ACCEPTABLE

**Row 110 — `Structural Foundations` matching `shoring|underpin|sheet pile` → `4` Foundations.**
Underpinning and permanent sheet piling are substructure, and `4` is right for those. The
rule also catches temporary shoring, which is a preliminaries item and not permanent work.
The reviewer identified this and correctly noted there is nowhere better for it to go: the
vocabulary has no preliminaries section. I would additionally note that the phase-keyed
machinery does not rescue it — only the `Demolished` state carries a rule, so an element
phased `Temporary` still matches this row and bills as permanent foundation work. The
project should either add a `Temporary`-phase rule or exclude such elements from take-off.
The code as it stands is defensible.

**Row 135 — `Generic Models` matching `erosion|silt fence|sediment|swale` → `3` Groundworks.**
Defensible: all four terms describe earthworks-adjacent site preparation, and a grassed swale
is an excavated channel, which is groundworks by measurement. I would have separated the
rule. `silt fence` and `sediment` control are temporary works removed at completion, while a
`swale` is a permanent surface-water feature that arguably sits with `31` Drainage below
ground or `42` Soft landscaping depending on its finish. One code covering both a temporary
control and a permanent drainage feature is a compromise, and the reviewer said so.

**Row 141 — `Generic Models` matching `\bkerb\b|\bcurb\b|gutter` → `40` External works.**
The code is right for the elements the rule is aimed at. The concern is rule scope, not the
code: `gutter` is unanchored and unqualified, so it will also match a roof gutter or
"guttering" modelled as a generic model. A roof gutter is rainwater goods and belongs at
`17` or `30`, not external works — and the take-off's own keyword fallback classifies
"gutter" as `17`, so the map and the fallback disagree on the same word. Most projects model
roof gutters on the dedicated category and will never trip this, which is why it is
acceptable rather than wrong. Narrowing the pattern to the kerb-and-channel context would
remove the exposure.

**Row 142 — `Walls` with type matching `retaining` → `14`.**
Defensible and internally consistent: this map bills every wall at `14`, including gypsum
partitions and cast-in-place concrete walls, so `14` is being used as the wall section rather
than as a masonry section. On that convention a retaining wall belongs there, and the
reviewer's rejection of `4` Foundations is right. I would have differed: a site retaining
wall to a road, ramp or terrace is external works priced with the surfacing package, and
now that `40` exists it has a home that did not exist when this convention was set. The
practical consequence of leaving it is that a reinforced concrete retaining wall prints
under a heading reading "Masonry", which will read as an error to anyone pricing it. See
also section 7.

**Row 144 — `Site` matching `bench|bollard|litter|planter|seat|shelter` → `22` Furniture,
fittings and equipment.**
Defensible on the stated ground that the map already bills benches and seating at `22`. I
would have differed. External site furniture is a landscape or external-works package;
billing bollards, litter bins, planters and bus shelters in the same section as internal
loose furniture and casework mixes two trade packages under one heading and makes the
FF&E total unusable as a check figure. This is the one row where the new external-works
block was available and was not used, and it is a direct consequence of the gap noted at
section 5 — there is no external-fixtures section for it to go to.

---

## 5. The three new sections — 40, 41, 42

`40` External works — roads, paving and kerbs · `41` Fencing, gates and barriers ·
`42` Soft landscaping.

**The right call.** Verified and endorsed, on four grounds:

1. **The alternative was indefensible.** Before this change the scheme had no external-works
   section of any kind, and roads, car parks, footways, kerbs, fencing and turf all carried
   `4`, printing under a heading reading "Foundations". That is not a defect of degree — a
   bill that heads its turfing "Foundations" is not a usable tender document.
2. **Three is the right number, not one.** Surfacing, fencing and soft landscaping are
   three different trade packages, commonly three different subcontractors, with three
   different measurement bases (m², m, m²/number). Collapsing them into one external-works
   section would have produced a section nobody can price as a unit.
3. **The numbering choice is sound.** Taking a block above the existing range rather than
   reusing published-NRM2 external-works numbers is the correct decision for the reason
   given in the code comment: this vocabulary already agrees with published NRM2 at
   `14`/`15`/`16`, so any further coincidence deepens the impression that it *is* NRM2.
   `35` and `36` were in any case already occupied here by services.
4. **Bill order is correct.** Section ordering parses the code as an integer, so `40`/`41`/
   `42` sort after `36` and the external works appear at the end of the bill, which is where
   a reader expects them.

**Assignments are correct.**

| Section | Rows assigned | Verified |
|---|---|---|
| `40` | 136, 137 (roads — concrete and asphalt), 138 (markings), 139 (car park paving), 140 (external unit paving), 141 (kerbs and channels), 145 (general site improvements), 146 (inert — see below) | Correct. All are surfacing or surfacing-adjacent work measured with the road and paving package. |
| `41` | 123 (railings named as fence/gate), 143 (generic models named as fence/gate) | Correct, and the split of the former combined fence/gate/balustrade rule was necessary — a balustrade is a railing, not a fence, and now correctly bills at `20` under its own appended rule. |
| `42` | 147 (trees), 148 (turf and grasses), 149 (plants) | Correct. |

**One structural gap remains.** There is no section for external fixtures and site
furnishings — the published-NRM2 equivalent of section 38. That gap is the sole reason row
144 has to reach back into the internal FF&E section for bollards, planters and shelters.
If a fourth section is ever added, row 144 is the row that should move into it.

**Row 146 (`Entourage` → `40`) is inert and was verified as such.** `OST_Entourage` is in the
take-off exclusion set in `BOQCostManager`, and the project-configurable exclusion list only
adds to that set, so the category cannot reach take-off and this code can never be reached.
Setting it to `40` rather than leaving it at `4` is right on the principle that dead data
should still not assert something false.

---

## 6. Verification steps beyond reading the notes

Recorded so that this document shows what was tested rather than only what was concluded.

1. **The vocabulary was read from source, not from the review sheet.** All 26 cases of
   `GuessSectionName` were read directly. Every code appearing in the reviewed rows —
   `3`, `4`, `14`, `22`, `31`, `32`, `33`, `34`, `40`, `41`, `42` — is named there, so no
   reviewed row bills under a code that would fall through and print a raw model category
   as its heading.
2. **The map was checked against the sheet.** Every one of the 37 rows carries in the map
   the code the sheet records as its post-review value. The review was applied faithfully.
3. **Match weights were read from source and the contested precedences were worked through.**
   Category alone scores 2; each name or system qualifier adds 2; a declared material adds 3;
   a matched phase adds 10. On that basis the following were confirmed to resolve as the
   reviewer assumed:
   - **Piling (rows 109/111/112) versus the bare foundation fallback.** A foundation whose
     family name contains `pile` scores 4 and beats the bare foundation row at 2, so it bills
     at `4`. A precast pile scores 7 (category + name + declared material) and beats the
     generic precast-concrete foundation row at 5, so row 112 does its job. The split is
     defensible — but see the caveat at section 7, which is the one thing on this list a
     signer must not skip.
   - **Landscape irrigation (row 151)** scores 4 and beats the bare fire-sprinkler row at 2,
     so irrigation heads are correctly diverted away from fire suppression. This rule's real
     purpose is verified as working.
   - **Buried site services (rows 152, 153, 157)** score 6 against the in-building rows'
     4, so the site rule wins wherever the type name identifies a site run. The mechanism
     the map header claims is real.
   - **A generic model named as a storm-water storage tank** matches both the storm-drainage
     rule (row 159) and the water-storage-tank rule (row 161) at 4; the tie resolves to the
     earlier row, which is the drainage one. That is the right outcome and it happens by
     file order, not by design — a point worth knowing before rows are ever reordered.
4. **The presentation-content exclusion was verified in code**, not taken from the note.
5. **Downstream consumers of these codes were checked.** This produced the material finding
   at section 8.

---

## 7. Comment on the vocabulary itself — not encoded in any row verdict

Two observations about the section scheme, recorded here deliberately so that they are not
confused with findings against the review. Neither should be actioned by editing this map.

1. **`14` is titled "Masonry" but is used as the wall section.** Gypsum partitions,
   cast-in-place concrete walls and retaining walls all bill there. The classification is
   internally consistent; the heading is not truthful for most of what it now contains. The
   fix is a heading, not a code.
2. **There is no preliminaries section and no external-fixtures section.** The first forces
   temporary works (row 110's shoring, row 135's silt fence) into permanent sections; the
   second forces external site furniture into internal FF&E (row 144). Both are visible in
   the review as compromises the reviewer identified and could not resolve within the column.

---

## 8. Limitations — what this verification does not establish

A reader must not take this document for more than it is.

1. **It is a desk check of a classification map.** It establishes which section each rule
   directs an element to. It does not establish that the resulting bill is correct.
2. **Nothing here has been checked against a model.** No project model was opened, no rule
   was run against real elements, and no element was observed to classify. Whether the
   families and types in this project are named such that these regular expressions fire on
   the right elements is untested, and rule scope was repeatedly the weak point found
   (rows 135, 141, and the `gate` pattern, which will also match a "gate valve" modelled as
   a generic model).
3. **No quantity has been checked.** Classification and measurement are separate questions.
   Whether the take-off produces a cut/fill volume for a topographic surface rather than a
   solid volume, and whether car-park paving measured from parking-space components
   double-counts against paving measured from surfaces, are measurement questions this
   review does not reach and this verification does not answer.
4. **No rate has been checked and no bill has been priced.** The advisory unit column was not
   reconciled against any rate basis.
5. **The reviewed set is 37 rules out of a map of several hundred.** Rows outside divisions
   31/32/33 were read only where they interact with a reviewed row.
6. **Nothing here has been checked against the specification or the drawings.** A rule can be
   correctly classified against this vocabulary and still be measuring something the
   specification does not describe.

---

## 9. Matters the signer must be told before signing

1. **Row 153 cannot be corrected in isolation.** Applying it alone splits one gas
   installation across two bill sections on a pipe-naming accident. Move both gas rows to
   `32`, or leave both at `33` and record the point as open. Do not apply half.
2. **Piling has a defensible split with an undefended edge.** Rows 109/111/112 now bill at
   `4` Foundations, and that is right. The bare foundation fallback — outside the reviewed
   37 — remains at `5` In-situ concrete. A pile whose family name does not contain `pile`,
   `bored pile` or `CFA` therefore falls to the fallback and bills at `5`, so two identical
   piles can bill under two headings according to how the family was named. This is
   currently controlled by naming discipline alone. Either the fallback should move to `4`
   so that all foundation work bills consistently, or the naming requirement must be made
   explicit in the modelling standard. **This is a live exposure, not a theoretical one, and
   it is the item on this list most likely to move money.**
3. **Two alternative measurement standards do not know the new or newly-used codes.** The
   CESMM4 and POMI cross-walks in
   `StingTools/BOQ/MeasurementStandard/MeasurementStandards.cs` map section codes onto their
   own classes by an explicit list. Codes `3`, `31`, `40`, `41` and `42` appear in neither
   list and fall to the miscellaneous class. This review is correct and those cross-walks
   are now behind it: under the default standard the bill is right, but a bill produced
   under CESMM4 or POMI would collapse the entire groundworks, below-ground drainage and
   external-works content into a miscellaneous class. The same file also maps `4` onto
   CESMM4 Class E Earthworks, so moving piling to `4` classifies piles as earthworks under
   that standard, where CESMM4 has a dedicated piling class. **These are defects in the
   downstream cross-walks, not reasons to withhold signature on this map**, but they should
   be raised before any bill is issued under a standard other than the default.
4. **The three new sections are the reviewer's own additions to the vocabulary, not merely
   a re-coding of rows.** Signing this endorses the creation of three bill sections as well
   as the assignment of rows to them.
5. **Signature does not close the measurement question.** See section 8.

---

## 10. Sign-off

Signing below attests that the section codes recorded in
`StingTools/Data/STING_CSI_MASTERFORMAT_MAP.csv` for the 37 site, civil and external-utility
rules identified in `docs/qs_review/nrm2_site_civil_review.csv` have been independently
verified as a classification against the section vocabulary defined in
`StingTools/BOQ/BOQCostManager.cs`, subject to the limitations at section 8 and the matters
at section 9 — and does not attest to any quantity, rate or priced amount.

| | |
|---|---|
| Name | |
| RICS / professional registration number | |
| Organisation | |
| Date | |
| Signature | |
