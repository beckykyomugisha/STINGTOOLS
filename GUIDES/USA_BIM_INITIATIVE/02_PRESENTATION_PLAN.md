# Presentation plan

**Audience:** President Arch. Amunsimiire Kenneth (convening) · Chair, Board of Practice
**Arch. Abdu Wahab Nyanzi** · Chairman, ICT Cluster (name to confirm) · ~3 members.
**Date:** not yet scheduled → `00_DECISION_LOG.md` D-05.
**Format:** 20 minutes of content, 40 minutes of discussion. Not a document read aloud.

**Purpose, revised 2026-09-08:** convince them with what exists, so that **the Society
helps find the funding**. Not a request for money, and not a request to borrow.

---

## Before the meeting: ask for one more person

**Ask Ken to add Arch. Daniel Sekamwa, Chair of the Board of Education.** The meeting is
largely about a training programme; CPD and training are his remit, and the CPD argument
is the strongest one you have. Arch. Clare Ruhweza (Research and Development) is worth
asking for too, or worth a follow-up.

Asking costs nothing and signals that you understand how the Society is organised —
which is itself a small credibility deposit before you have said anything else.

---

## The ask

### What you are asking for

1. **Endorse the training programme** as a Society initiative for members, and explore
   accrediting it for CPD.
2. **Name a technical counterpart** in the ICT Cluster, so the evaluation is theirs.
3. **Help identify the funding route** — with `07_FUNDING_ROUTES.md` in hand, so they
   are choosing between options rather than inventing one.

### What you are explicitly *not* asking for

- Not asking the Society to borrow. The UDB route is parked; say so in the opening.
- Not asking the Society for money from its own reserves.
- Not asking for a decision on the MOU or on branding today.

### Say the shape out loud, early

> "You have three documents from me, and one of them asks the Society to take on a bank
> loan. I've set that aside — I'd rather not ask a professional body to carry debt for a
> platform it hasn't evaluated yet.
>
> What I'm here for is different. I want to show you what exists, tell you honestly what
> isn't finished, and ask your help finding a route to fund the rest. There are doors a
> professional body can open that I can't."

That does three things at once: removes the threat, shows you have read your own risks,
and makes the real ask sound modest by comparison.

---

## The arc — three arguments, in this order

Detail and evidence in `06_VALUE_TO_THE_SOCIETY.md`.

### 1 · A change is coming to your members' market (make the problem theirs)

ISO 19650 is now the accepted international standard for project information, and the
UK, UAE, Singapore and Germany mandate BIM on public work. Kenya — the obvious regional
comparison — is documented as lagging, with poor coordination of information named as
the consequence. Uganda has neither a national standard nor locally supported tooling.

Practices with no answer stop being shortlisted and never learn why; they compete
against firms that already work this way; and they pay for it on site.

A professional body exists to protect its members' standing. This is squarely its
business, and it is happening whether or not the Society engages with you.

**Say "the direction of travel", never "it is being mandated here".** There is no
Ugandan mandate, and claiming one is the fastest way to lose this room.

### 2 · Here is what exists — the demonstration

The centre of the meeting, and the thing that will decide the ICT Cluster chair's
verdict. See the rehearsal gate below.

State the completion position **before** you demo, in the words from
`05_CONSISTENT_NARRATIVE.md`. Volunteer it; never be caught by it.

### 3 · Here is what the Society gets

- **Statutory CPD.** 20 points a year, required for licence renewal. A flagship
  programme the Society owns, in content that is hard to source locally.
- **A national standard the Society authors.** Not a PDF — a working implementation that
  software can check compliance against, so the Society arrives at any future standards
  conversation with a working draft rather than an opinion.
- **Member economics.** Per-organisation pricing, unlimited free external members,
  shilling billing — and a Society-negotiated member rate, offered before they ask.

Then, and only then: what remains to be finished, what it costs, and the funding ask.

---

## Two spines, because two chairs want different things

### Spine A — for the Chair of the Board of Practice (Arch. Abdu Wahab Nyanzi)

*Does this expose us? Is it good for the profession?*

- **Today's ask carries no financial obligation.** The borrowing route is parked and the
  personal-guarantee exposure goes with it. Volunteering a risk you have just removed
  buys more than one you never raised.
- **Documentation quality, evidence and disputes** — his language. Audit, revision
  control, a defensible record of what was issued and when.
- **Continuity** — open formats, escrow, self-hosting, a priced exit path. Answers
  "what if you're unavailable" before it is asked.
- **No exclusivity, no ownership transfer, no obligation on members.**

### Spine B — for the Chairman of the ICT Cluster

*Is it real, and is it sound?*

- **The demo carries this spine.** Nothing in a deck moves this chair as much as watching
  a model get tagged, audited and a BOQ extracted.
- **Open formats and no lock-in** — IFC 4, BCF 2.1, COBie, RVT, DWG, Excel, PDF.
- **Local build, local hosting, data sovereignty**, subscription spend staying in the
  domestic economy.
- **Lead with the multi-platform story.** Revit, ArchiCAD, Tekla and a free Blender
  route, with one element identity across all four. **This is the slide that wins this
  chair.** Be exact about depth: Revit is a native add-in, the others go through IFC, and
  there is no native ArchiCAD or Tekla plug-in.
- **What is not validated** — the MEP calculation engines. Then invite the Society's
  engineers to be the validators.

---

## Run sheet (20 minutes)

| Min | Segment | Whose attention |
|---|---|---|
| 0–2 | Thanks, who I am, and **the three asks stated up front** | Board of Practice |
| 2–5 | **The change coming to members' market** — the standard, and the region | Both |
| 5–7 | What the platform is, and **exactly where it stands** (verbatim) | ICT Cluster |
| 7–13 | **Demonstration** | ICT Cluster |
| 13–17 | **What the Society gets** — CPD, the standard, member economics | Both |
| 17–19 | What is left to finish, what it costs, and the funding routes | Both |
| 19–20 | The three things I am asking for | Both |
| 20–60 | Discussion | — |

**MoWT is out of this presentation entirely** (Davis, 2026-09-08). That conversation is
still pending and is not to be used as evidence here. If it is raised because Ken has the
proposal, say only that it is a separate discussion you are not able to characterise yet,
and move on.

---

## Demo rehearsal gate

**Nothing is demonstrated live that has not been proven twice, end to end, on the build
the demo will actually run on.** Not the repository, not a local dev run — the deployed
thing. A demo that half-works in front of the ICT chair is worse than a recording that
works.

Resolve by **D-7**:

- [ ] Write the demo path as numbered steps
- [ ] Confirm which deployment it runs against — **verify the live target, do not assume**
- [ ] **Rehearsal 1** — full path, timed. Log every failure.
- [ ] Fix or cut what failed. Cutting is a legitimate outcome.
- [ ] **Rehearsal 2** — full path, clean, no interventions
- [ ] Record the successful run as the fallback
- [ ] Test projector, resolution, and **assume no internet**
- [ ] Prepare an offline copy of everything

**Decision point at D-7 — pick one and commit:**

| Option | When to choose it |
|---|---|
| Full live, Revit + PlanScape | Both rehearsals clean end to end |
| Revit + StingTools live, PlanScape recorded | Local path solid, server path unreliable. **Most likely outcome, and still strong** — the local half is what an architect can judge on sight |
| Recorded only | Neither rehearsal clean. No shame in it. |

**Choose demo content an architect cares about.** Not MEP calculations — a model being
tagged and audited, drawings and schedules coming out consistent, a BOQ extracted, a
handover register produced. These are architects; show them architectural work.

Record the outcome in `00_DECISION_LOG.md` D-04.

---

## Materials

**Take in and hand over:** `07_FUNDING_ROUTES.md`, reformatted as a clean one- or
two-pager on your letterhead. This is what converts goodwill into an action.

**Leave-behind:** the initiative proposal, with the completion paragraph (done) and §9
re-dated (F-03, outstanding). The proposal is the leave-behind, never the script.

**Do not bring the UDB application.** It is parked. Handing round a loan request you have
just said you are not making creates exactly the confusion this is meant to avoid.

**Have ready if asked:** the demo recording, and a one-page summary of what a cohort
covers.

**Do not bring:** anything describing the MOU as ready to sign, or any timeline
containing a date that has passed.

---

## What a good outcome looks like

- Endorsement of the training programme, and agreement to look at CPD accreditation
- A named technical counterpart in the ICT Cluster
- **A name and an introduction** for at least one funding route — a development partner
  contact, a sponsor, or the person who runs CPD accreditation
- The Society's registration details, founding year and membership numbers
- Who takes this forward, when they next meet, and what they need from you before then

**The last two bullets are the real test.** A committee saying "we'll consider it" is
the normal outcome of a first meeting and is not a failure — but only if you leave with
a person, a date and a next step written down. A resolution to explore funding is worth
very little; a name and an introduction is worth a great deal.
