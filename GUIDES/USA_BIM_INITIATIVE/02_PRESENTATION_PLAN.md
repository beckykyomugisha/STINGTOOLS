# Presentation plan

**Audience:** Chair, Board of Practice · Chairman, ICT Cluster · ~3 members. Five
people, senior, professional-body governance.
**Date:** not yet scheduled → `00_DECISION_LOG.md` D-05.
**Format:** 20 minutes of content, 40 minutes of discussion. Not a document read aloud.

---

## The ask, staged

### Primary — decidable in the room, costs the Society nothing

1. **Endorse the training programme** as a Society initiative for members.
2. **Name the ICT Cluster as counterpart** for technical evaluation.
3. **Agree a pilot cohort** with a date, and Society support for member outreach.

### Secondary — explicitly *not* asked for on the day

4. **The UDB financing is off the table** (parked 2026-09-08). Say so plainly. The
   MOU and the national-platform branding are phase-two matters for the committee's
   own process. Open items in `00_DECISION_LOG.md` are named honestly if asked.

### Say the staging out loud

Do not let them think you are hiding the loan — they have already read it. Open with
the structure:

> "You have three documents from me, and one of them asks the Society to take on a
> bank loan. I've set that aside. I'd rather not ask a professional body to carry debt
> for a platform it hasn't evaluated yet, and I want to look at other routes first.
> What I'm asking for today is much smaller, and it costs the Society nothing."

That sentence does more work than any slide. It removes the threat, demonstrates you
have read your own risks, and makes the smaller ask feel like a concession you are
offering rather than a favour you are seeking.

---

## Two spines, because two chairs want different things

### Spine A — de-risking, for the Chair of the Board of Practice

Their question: *does this expose us?*

- **Declare the conflict in the first two minutes.** The Davis–Ken professional
  relationship, stated by you before anyone raises it. USA §8 already commits to this.
- **Name the governance route, don't assume it.** "This needs the Executive Committee,
  or whatever the constitution requires — I am not asking any individual to commit the
  Society."
- **Be explicit that today's ask carries no liability.** The borrowing route is
  parked, and the personal-guarantee exposure goes with it — that was the sharpest
  risk in the original package. Volunteering a risk you have just removed is the
  single most credibility-positive move available.
- **Continuity.** Lift MoWT §11.3 wholesale: open formats (RVT, DWG, IFC 4, BCF 2.1,
  COBie), source-code escrow, self-hosting, documented configuration, priced exit to
  the all-Autodesk stack. This answers "what if Davis is unavailable" before it is
  asked.
- **What the Society is not being asked to do:** no exclusivity, no ownership transfer,
  no obligation to members.

### Spine B — substance, for the Chairman of the ICT Cluster

Their question: *is this real, and is it sound?*

- **The demo carries this spine.** Nothing in a deck will move this chair as much as
  watching a model get tagged, audited, and a BOQ extracted in front of them.
- **State the completion position first**, in the exact words from
  `05_CONSISTENT_NARRATIVE.md`. Volunteer it; do not wait to be caught.
- **Open formats and no lock-in**, same material as Spine A but framed technically.
- **Why not just Autodesk** — see `03_ANTICIPATED_QUESTIONS.md` Q7. The MoWT proposal
  §7 comparison is the strongest existing material.
- **What is not yet validated:** MEP calculation engines have not carried a commercial
  project through independent professional validation (MoWT §11.2). Say so. Then invite
  the ICT Cluster to be the validators — that converts a weakness into a role for them.

---

## Run sheet (20 minutes)

| Min | Segment | Owner of the room's attention |
|---|---|---|
| 0–2 | Thanks, who I am, **the conflict declaration**, and the staged ask | Board of Practice |
| 2–5 | The problem: BIM adoption in Uganda, what members currently pay for imported tooling | Both |
| 5–8 | What the platform is, and **exactly where it stands** (narrative doc, verbatim) | ICT Cluster |
| 8–14 | **Demonstration** | ICT Cluster |
| 14–17 | The training programme: what a cohort covers, what a member walks away able to do | Both |
| 17–19 | What I am *not* asking for: the loan route is set aside, and why | Board of Practice |
| 19–20 | The three things I am asking for today | Both |
| 20–60 | Discussion | — |

**MoWT:** bring it deliberately, as evidence of traction — "the same platform is under
discussion with the Ministry of Works and Transport." If it surfaces on its own *after*
you have discussed completion status, it reads as inconsistency instead of momentum.

---

## Demo rehearsal gate

**Rule: nothing is demonstrated live that has not been proven twice, end to end, on
the build the demo will actually run on.** Not the repository, not a local dev run —
the deployed thing. A demo that half-works in front of the ICT chair is worse than a
recording that works.

Resolve by **D-7**:

- [ ] Decide the exact demo path and write it down as numbered steps
- [ ] Confirm which deployment the demo runs against (verify the live target, do not
      assume it)
- [ ] **Rehearsal 1** — full path, timed, on the deployed build. Log every failure.
- [ ] Fix or cut whatever failed. Cutting is a legitimate outcome.
- [ ] **Rehearsal 2** — full path, clean, no interventions
- [ ] Record the successful run as the fallback
- [ ] Test the venue: projector, resolution, and **assume no internet**
- [ ] Prepare an offline copy of everything

**Decision point at D-7 — pick one and commit:**

| Option | When to choose it |
|---|---|
| Full live, Revit + PlanScape | Both rehearsals clean end to end |
| Revit + StingTools live, PlanScape recorded | Local path solid, server path unreliable. **Most likely outcome — and still strong**, because the local half is the part the ICT chair can judge on sight |
| Recorded only | Neither rehearsal clean. No shame in it; ship the recording and be honest that a live build is weeks away |

Record the outcome in `00_DECISION_LOG.md` D-04.

---

## Materials

**Leave-behind:** the USA proposal, with §9 re-dated (F-03) and the completion
paragraph inserted. The proposal is the leave-behind, never the script.

**Do not bring the UDB application at all.** It is parked. Handing round a loan
request you have just said you are not making creates exactly the confusion the
staging is meant to avoid.

**Bring but do not hand out unless asked:** the MOU. It is a draft with open
placeholders, and its economics need restating now the loan is gone (→ D-10).

**Have ready if asked:** the MoWT proposal, the demo recording, a one-page summary of
what a training cohort covers.

**Do not bring:** anything describing the MOU as ready to sign, or any timeline
containing a date that has passed.

---

## What a good outcome looks like

- Verbal endorsement of the training programme, with a named counterpart in the ICT
  Cluster
- A date, or a route to a date, for a pilot cohort
- The Society's registration details and constitutional position → closes D-02 and
  D-03, and fills the gap at F-07
- A named person to approach if and when a financing proposition is ready

**Anything beyond this is upside.** With the loan removed there is no longer a hard
ask that can be refused — which is precisely why this version of the meeting is more
likely to end well than the one the September package set up.
