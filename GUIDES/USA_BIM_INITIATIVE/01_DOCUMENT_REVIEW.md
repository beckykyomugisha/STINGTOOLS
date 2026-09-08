# Document review — 2026-09-08

Four documents reviewed in full:

| # | Document | Date | Sent to |
|---|---|---|---|
| 1 | `MoWT_BIM_Corporate_Proposal_v2.0_Aug2026.docx` | Aug 2026 | Ministry of Works and Transport; **PDF also sent to Ken, 30 Aug** |
| 2 | `USA_BIM_Initiative_Proposal_Sep2026_1.docx` | Sep 2026 | Ken, 5 Sep |
| 3 | `UDB_Loan_Application_Sep2026_1.docx` | Sep 2026 | Ken, 5 Sep |
| 4 | `USA_Planscape_MOU_Sep2026_1.docx` | Sep 2026 | Ken, 5 Sep |

**Overall:** these are well-built documents. The USA proposal's §5 and §8 — where it
volunteers its own weaknesses — are the strongest thing in the package and will earn
more trust than any claim in it. The arithmetic is clean throughout. The problems are
not sloppiness; they are **one cross-document contradiction, one governance
assumption, a dead timeline, and four gaps a lender will find.**

---

## F-01 · CRITICAL — the documents disagree on whether the platform is finished

Ken holds both, and this is the first hard question the ICT Cluster chair will ask.

| Source | Says |
|---|---|
| MoWT §11.1 | "The PlanScape platform is operational and in commercial service from Kampala: the coordination core … is complete and in daily use." |
| UDB §4 / USA §4.3 | Requests **USD 36,200 over four months to complete the platform**, including "the work to support several organisations and countries safely" — i.e. multi-tenancy, a foundational capability |
| WhatsApp, 30 Aug | "I need some funds to complete the planscape server before I can fully present." |

**Nothing needs retracting.** MoWT §11.1 already says the platform "remains in active
development", §11.2 is candid about unvalidated MEP engines, and the risk register
carries "Platform maturity (toolchain still completing)". The failure is that the
headline sentence and the loan's premise were never reconciled in writing.

**Fix:** adopt the single paragraph in `05_CONSISTENT_NARRATIVE.md` and use it in the
room, in the MoWT proposal §11.1, and anywhere else the question arises. Say it
yourself, first, before anyone finds it.

---

## F-02 · CRITICAL — the named signatory may be wrong

UDB application §12 and the MOU signature block both put **Kenneth Amunsiimire** as
**President**. The meeting is with the *Chair, Board of Practice* and the *Chairman,
ICT Cluster*. If Ken chairs the Board of Practice rather than holding the presidency,
three things are wrong in documents already sent: the applicant contact, the signature
block, and the assumed approval route. → `00_DECISION_LOG.md` D-01.

**Related:** USA §8 correctly declares the Davis–Ken professional relationship as a
possible conflict. The consequence is under-stated — **it means Ken cannot be the
approver.** Build the presentation to stand without his advocacy.

---

## F-03 · HIGH — the published timeline has already expired

USA proposal §9 sets: first training cohort **week of 7–11 September 2026**;
UDB submission **14–18 September**, with legal review, internal approval, business
case and financial projections all inside that window.

Today is **8 September**. The cohort is not running and the meeting is not yet
scheduled. Presenting a schedule whose first two milestones have already failed is a
free point for any sceptic in the room.

**Fix:** reissue §9 with dates anchored on the meeting (D-day + n), not on calendar
dates chosen in August. Same for the training cohort reference in USA §4.6.

---

## F-04 · HIGH — MoWT appears nowhere in the USA or UDB documents

Searched all three: **zero mentions** of the Ministry, MoWT, or Works and Transport.

There is a live, priced proposal in front of the most infrastructure-intensive
institution in government, at USD 8,160/year for the Government tier. That is exactly
the traction a lender wants to see and exactly the credibility a professional body's
ICT chair wants. Meanwhile the UDB application's Year-1 projection — 20 organisations
at ~USD 150/month — reads as pure hope.

**Fix:** add MoWT to UDB §7 (revenue model) and to §10 (supporting documents), framed
as a live prospect, not a signed contract. **And add the flip side to §11's risk
register**: one customer at roughly 23% of projected Year-1 revenue is a concentration
risk, currently unacknowledged.

---

## F-05 · HIGH — four gaps a lender will find

The arithmetic is sound; these are gaps in reasoning, not errors.

**Verified correct:** platform completion sums to 36,200 · subtotal 63,200 ·
contingency 9,480 (15%) · total 72,680 · tranches 25,400 + 29,100 + 18,180 at
35/40/25% · UGX 268.9M at 3,700 · debt service USD 18,964/yr on 72,680 at 11% over 5
years, so "roughly USD 20,000" is right *for the stated basis*.

| Gap | Why it matters |
|---|---|
| **Grace-period interest unstated** | If interest capitalises over 3 years, the balance at amortisation is ~USD 99,400 and debt service ~USD 26,000/yr over an 8-year term — not the USD 20,000 presented. → D-08 |
| **No FX risk anywhere** | Everything is USD at a fixed 3,700. UDB will likely lend in shillings; revenue arrives in UGX, KES, RWF, TZS. Not one line in the §11 risk register. |
| **The Company's balance sheet is never shown** | §11 makes the repayment agreement the mitigation for the Society's risk. But a repayment promise from an entity with no financials shown is thin — and §3 already uses the Company's short trading history as the *reason* the Society must borrow. Cannot have it both ways without addressing the gap. |
| **Revenue projection has no bottom-up basis** | §7 honestly labels itself illustrative, then §11 leans on it to answer the revenue risk. Needs: how many AE firms exist in Uganda, what share is 20, what conversion rate. |

---

## F-06 · MEDIUM — MOU review

**Structurally right, and worth saying so:** IP stays with the Company (§4). The
repayment obligation correctly lives in a separate binding agreement rather than in
the MOU (§6). Confidentiality survives termination (§9). §12 requires Executive
Committee approval before signature. A UDB delay triggers a review rather than
automatic termination (§10).

**Exposed:**

- **§7.1 economics are asymmetric.** 3–5% of net income, capped at 1.5× the loan,
  ending 12 months after repayment. On the Year-3 projection that is ~USD 14,600/yr
  against a USD 72,680 liability possibly behind a personal guarantee. → D-10
- **§1's "Net Platform Income" is narrow.** Subscriptions and usage only. Excludes
  training, implementation and BIM-manager fees — where much of the real money is
  per MoWT §8.7. The Society carries the loan that funds training but shares in the
  line that excludes it. → D-09
- **§13 must not be oversold.** "Neither Party liable for the other's debts" governs
  the parties between themselves. **The Society is liable to UDB regardless.** Do not
  let this clause be presented in the room as protection from the bank.
- **Not signature-ready.** 5 blank signature fields, 2 registration numbers, 2
  addresses, 8 open commercial variables. It is a good draft — describe it as one.
- **Footnote flags a real issue:** many professional bodies require two signatories.
  Confirm against the constitution. → D-03

---

## F-07 · MEDIUM — placeholders still open in the loan application

Not a criticism of a draft, but they must be tracked, because §2 and §9 are the two
sections UDB will read first.

- §2 *About the applicant* — entirely a placeholder: "[a brief description of the
  Society's registration status, founding year, membership size, and public role to be
  inserted here]"
- §9 *Security offered* — "[To be completed once confirmed with UDB and the Society's
  leadership.]"
- Facility type, submission date, branch/relationship manager, registration number,
  registered address, and 3× phone/email pairs

**Only the Society can fill §2.** That is a legitimate, concrete thing to ask for in
the meeting, and asking for it is itself a soft commitment step.

---

## F-08 · Structural — three decisions bundled into one ask

The package asks the Society to decide, simultaneously: (1) borrow USD 72,680 in its
own name, possibly behind a personal guarantee; (2) enter a 24-month commercial MOU
with a member's private company; (3) lend its national identity to a privately-owned
product.

Three different approval thresholds. Bundled, the hardest dominates — and it is the
one the proposal itself admits may not be legally possible (§8). It can sink two much
easier yeses.

**Resolved 2026-09-08:** stage the ask. Endorsement first, financing as phase two.
See `02_PRESENTATION_PLAN.md`.

**Worth keeping in reserve:** rather than the Society borrowing, the Society
**commits to purchase** — underwriting training cohorts or member subscriptions. That
gives Planscape revenue-backed lending capacity in its own name, reaches similar
money, and asks the Society for something its constitution almost certainly permits.
Reframed, the Society is not taking financial risk for a private company; it is
**buying capability for its members**.
