# Source documents

**The originals stay where they are.** This folder holds pointers, not copies — a copy
in a git branch would silently go stale the moment a document is edited in Word, and a
stale copy is worse than none.

## The four documents reviewed on 2026-09-08

| Document | Location | Version reviewed |
|---|---|---|
| MoWT corporate proposal | `C:\Dev\MoW\MoWT_BIM_Corporate_Proposal_v2.0_Aug2026.docx` | v2.0, Aug 2026 (ref PLNS-MOWT-BIM-001) |
| USA initiative proposal | `C:\Dev\UGANDA SOCIETY OF ARCHITECHTS(USA)\USA_BIM_Initiative_Proposal_Sep2026_1.docx` | Sep 2026, rev 1 |
| UDB loan application | `C:\Dev\UGANDA SOCIETY OF ARCHITECHTS(USA)\UDB_Loan_Application_Sep2026_1.docx` | Sep 2026, rev 1 |
| USA–Planscape MOU | `C:\Dev\UGANDA SOCIETY OF ARCHITECHTS(USA)\USA_Planscape_MOU_Sep2026_1.docx` | Sep 2026, rev 1 |

A PDF of the MoWT proposal sits alongside the .docx and is the version sent to Ken on
30 August 2026.

## Also in `C:\Dev\MoW\` — related, not reviewed here

- `MoWT_BIM_Corporate_Proposal_v3_2026.docx` / `.pdf` (Aug 2026) — **note the version
  confusion:** a file named v3 dated 19 Aug sits beside the v2.0 dated 21 Aug, which is
  the one carrying the "What has changed in this version" section and the one sent to
  Ken. Worth resolving which is current before either is sent anywhere else.
- `MoWT_BIM_Corporate_Proposal_v2_2026.docx` (Apr 2026) — the March/April submission
  that v2.0 responds to
- `PLNS-PROP-BIM-001, v1_planscape_bim_adoption_proposal.pdf`
- KUT BIM manager proposals and the Multikonsults brief offer

## Correspondence referenced in the review

| When | What |
|---|---|
| 30 Aug 2026, 18:03 | WhatsApp to Ken: MoWT proposal PDF, plus the request for funds to complete the PlanScape server and for a meeting |
| 5 Sep 2026, 10:52 | Email to Ken: the three-document USA package, USD 72,680 summarised |
| 8 Sep 2026, 14:12 | Ken confirms attendees: Chair Board of Practice, Chairman ICT Cluster, ~3 members |

## Re-reading the .docx files

The extraction script used for this review writes headings, paragraphs and tables to
plain text without needing Word installed:

```bash
python dx.py "C:/Dev/UGANDA SOCIETY OF ARCHITECHTS(USA)/USA_BIM_Initiative_Proposal_Sep2026_1.docx"
```

It reads `word/document.xml` from the .docx zip and walks the XML directly. If it is
needed again and no longer exists, it is ~50 lines using only `zipfile` and
`xml.etree` from the standard library.

## Rule

When any of these documents is revised, **record what changed and when** in
`04_ACTION_TRACKER.md`, and check the change against `05_CONSISTENT_NARRATIVE.md`
before it is sent to anyone.
