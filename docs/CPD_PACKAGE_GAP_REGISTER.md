# PLNS-CPD-01 — Information Gap Register

**Reviewed:** 2026-08-26 · **Scope:** everything the syllabus (§A6) promises a delegate
or the accreditation panel receives, against what exists in the repository today.

---

## 1. Critical finding — the code tables contradict each other

**This is the most important item in the register. Fix it before anything else is
printed.**

Three documents in this repository teach the published-information codes
differently:

| Source | What it says |
|---|---|
| `marketing-site/guides/iso-19650-workflow.html` | S0–S7, with **S4–S7 mapping to Published** |
| `GUIDES/KUT_BEP_TEMPLATE.md` §4.3 | `WIP → SHARED (S0–S4) → PUBLISHED (A1/B1)` — **Published uses A/B codes, not S codes** |
| `GUIDES/KUT_MIDP_TEMPLATE.csv` | Suitability column carries `A1` against CDE State `Published` — follows the BEP |

The syllabus and the assessment were drafted from the marketing-site guide. **The
KUT BEP is the one that reflects live project practice, and it is the one that is
right.**

### The correct picture

These are **two different code families that coexist**, not competing versions of
one table:

- **S codes describe the *suitability* of SHARED information** — what the receiving
  team may do with it. S0 (WIP) through S4 (stage approval).
- **A and B codes describe the *authorization status* of PUBLISHED information** —
  whether it was accepted. `A1…An` accepted; `B1…Bn` accepted with comments.

A container does not carry an S code once it is published. It carries an
authorization code. Conflating the two is a common and consequential error, and it
is currently baked into the public-facing guide.

### Actions

| # | Action | Owner | Priority |
|---|---|---|---|
| 1.1 | Correct `iso-19650-workflow.html` — separate the S table from the A/B table and state that S codes apply to Shared, A/B to Published | Web | **P0** |
| 1.2 | Field guide teaches both families correctly — **done**, see `CPD_FIELD_GUIDE_ISO19650.md` §7–§8 | Training | **P0** — closed |
| 1.3 | Assessment: Q6 remains valid (it tests write permission, not codes). **Add a marker's note** that a delegate mentioning A/B codes is correct and should not be penalised | Training | **P0** |
| 1.4 | Re-check the syllabus §A8 claim that the course "teaches the full range" — it must now teach two ranges | Training | P1 |

**Why this matters beyond correctness:** a delegate who has worked on a UK-standard
project will spot the error in the room, in front of thirty peers. Being corrected
by a delegate on the code tables would do more damage to the CPD proposition than
any competitor could.

---

## 2. Gap register — delegate pack

Syllabus §A6 commits to six items in every delegate's pack.

| # | Item | Status | Notes |
|---|---|---|---|
| 1 | **ISO 19650 field guide**, 12pp A5 | ✅ **Drafted** | `docs/CPD_FIELD_GUIDE_ISO19650.md`. Self-audit checklist folded in as §12, which also closes item 4. |
| 2 | **BEP template**, editable | 🟡 **Partial** | `GUIDES/KUT_BEP_TEMPLATE.md` exists and is strong, but it is **project-specific to KUT** and names the client, Symbion, and Mayanja Davis. Needs generalising: strip KUT identifiers, convert `[FILL: …]` prompts into drafting notes, and issue as `.docx`. |
| 3 | **MIDP template**, spreadsheet | 🟡 **Partial** | `GUIDES/KUT_MIDP_TEMPLATE.csv` has a well-designed 17-column structure. Same problem — KUT-seeded rows and named individuals. Needs a generic version with 3–4 illustrative rows. |
| 4 | **Self-audit checklist**, six-point | ✅ **Drafted** | Field guide §12. Also needs a one-page tear-off version for Exercise 4. |
| 5 | **Exercise workbook** with model answers | ❌ **Missing** | Exercises 1–4 are described in the syllabus but not written. **This is the largest remaining gap** — see §3. |
| 6 | **Certificate** template | ❌ **Missing** | Must carry delegate name, professional registration number, course code, points awarded, date, and provider accreditation reference. Trivial to produce, easy to forget until day one. |

---

## 3. Gap register — delivery materials

Items the course cannot run without, none of which the syllabus lists because they
are the trainer's own materials.

| Item | Status | Notes |
|---|---|---|
| **Exercise 1** — name six containers from a brief | ❌ Missing | Needs the written brief, an answer sheet, and a peer-marking key. |
| **Exercise 2** — route eight documents through the state machine | ❌ Missing | Needs the eight documents with enough context to determine state, suitability and revision. **Design note:** at least two should be genuinely ambiguous, to force the "check the BEP" reflex. |
| **Exercise 3** — complete three BEP clauses + a ten-row MIDP | ❌ Missing | Needs the supplied delivery programme the MIDP is built from. Depends on gap 2.3. |
| **Exercise 4** — six-point self-audit | 🟡 Partial | Checklist drafted in the field guide; needs the standalone tear-off sheet. |
| **Slide deck** | ❌ Missing | ~40 slides for Modules 1–5. |
| **The three case failures** (Module 1 opener) | ❌ Missing | Must be written from projects you have personally seen and properly anonymised. **Cannot be delegated or invented** — this is the only item on the register that only you can produce. |
| **Demonstration model** | ❌ Missing | A deliberately non-compliant Revit model for the Module 5 demonstration. Must be non-compliant in *visible, fixable* ways. |
| **Invigilation pack** | ❌ Missing | Answer form, attendance register capturing registration numbers, feedback form. |

---

## 4. Gap register — accreditation submission

| Item | Status | Notes |
|---|---|---|
| Syllabus | ✅ Done | `docs/CPD_COURSE_ISO19650_SYLLABUS.md` |
| Assessment + marking scheme | ✅ Done | `docs/CPD_ASSESSMENT_PLNS-CPD-01.md` |
| **Trainer CV** | ❌ Missing | Must evidence the KUT Information Manager role explicitly. |
| **Professional registration certificate** | ❌ Missing | Provider credibility rests on this. |
| **Company registration / provider details** | ❌ Missing | Planscape Ltd incorporation documents. |
| **Sample delegate materials** | 🟡 Partial | Panels typically ask for the pack. Field guide ready; workbook is not. |
| **Board application form + fee** | ❌ Not started | **Blocking.** Obtain the current BORAQS provider pack — the form dictates the format of everything above, and drafting further material before reading it risks rework. |
| **Points scale confirmation** | ❌ Not started | The syllabus assumes 1 point per contact hour. **Unverified.** If the scale differs, the 4-hour format may need to change. |

---

## 5. Open questions requiring a decision

These are not gaps in documentation — they are decisions nobody has made yet.

1. **Which board first?** The syllabus assumes BORAQS (Kenya) because its criteria
   are published and clear. But your project base, credibility and venue economics
   are strongest in **Kampala**. Accrediting in Uganda first may be slower but sells
   better. *Recommendation: apply to both simultaneously; run the first seminar
   wherever accreditation lands first.*

2. **Does the course admit non-registered attendees?** Technicians, BIM
   coordinators and recent graduates are your strongest software adopters but earn
   no CPD points. *Recommendation: yes, at a reduced fee, capped at 20% of seats —
   they are the best conversion segment in the room.*

3. **Who marks the papers when you are teaching two cities a quarter?** Marking
   consistency is an accreditation risk. The marking scheme was written to be
   handed over — but the second marker has not been identified.

4. **What is the audit retention arrangement?** The assessment doc commits to
   retaining scripts for two years. Where, and whose responsibility?

---

## 6. Recommended sequence

**Before submitting:** close §1 (the code contradiction), obtain the BORAQS
application pack, confirm the points scale, assemble the trainer evidence file.

**Before the first seminar:** exercise workbook, slide deck, the three case
failures, the demonstration model, certificate template, invigilation pack.

**The critical path runs through the BORAQS application pack** — it governs the
format of the submission, and every hour spent drafting submission material before
reading it is at risk of rework. Request it this week.
