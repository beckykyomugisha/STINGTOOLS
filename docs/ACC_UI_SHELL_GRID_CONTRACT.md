# ACC-style UI shell — grid contract + slice log

**Scope: `planscape-web` ONLY.** Not the WPF BIM Coordination Center (different platform,
local-first, needs Revit to build) and not the Expo app (a phone doesn't get a left rail). Saying
that out loud early is how this stays a two-week workstream instead of a three-month one.

Branch `claude/livekit-corporate-ui-research-3233e0` · PR #512 · scoped from
[`docs/LIVEKIT_AND_CORPORATE_UI_FINDINGS.md`](LIVEKIT_AND_CORPORATE_UI_FINDINGS.md) §3.

---

## 1. The grid contract — a default, not a decree

Nobody specified this. Rather than block, here is a documented default. **Every clause below is
meant to be overridden** — change it here and the code follows, rather than the decision being
buried in a component.

### 1a. Which columns are editable

**Editable = exactly the fields the entity's existing REST write endpoint already accepts.**
No new writable fields were invented, and no endpoint was widened to suit the UI. Verified against
the controllers, not guessed:

| Grid | Endpoint | Editable columns | Everything else |
|---|---|---|---|
| **Issues** | `PUT /api/projects/{id}/issues/{issueId}` (`UpdateIssueRequest`) | `status`, `priority`, `assignee` / `assigneeEmail`, `description` | read-only |
| **Clashes** | `PATCH /api/projects/{id}/clashes/{clashId}` (`ClashUpdateDto`) | `status`, `assignedTo`, `resolutionNote` | read-only |
| **Documents** | `PUT …/documents/{docId}/state` | `newState`, `suitabilityCode`, `revision` — a **state transition**, not a free edit | read-only |
| **Members** | `PUT …/members/{memberId}` | `projectRole`, `iso19650Role` | read-only |
| **Transmittals** | `PUT …/transmittals/{txId}/{send\|acknowledge\|respond}` | **no inline cells** — these are *actions*, not field edits; they render as row buttons | read-only |

Two consequences worth stating rather than discovering later:
- **Documents and transmittals are not really "editable grids"** — their write surface is a state
  machine. Inline-editing a status cell would imply an arbitrary transition the server will reject.
  They get an explicit action control (a state `Select` bound to the transition endpoint, and row
  action buttons) instead of a text cell.
- **Issues uses PUT, not PATCH.** `updateIssue` sends a partial body to a PUT route; the server's
  `UpdateIssueRequest` treats `null` as "leave unchanged", so a partial body is safe. The grid still
  sends only the changed field.

### 1b. Save semantics

**Optimistic, per-cell.**

1. Commit the edit to local state immediately, mark the row `saving`.
2. Fire the write with **only the changed field**.
3. **Success** → keep the value, clear `saving`, flash the cell.
4. **Failure** → roll that cell back to its previous value and toast the server's message.

**No merge-conflict UI in v1. Last write wins**, surfaced via the toast. A 409 is reported with its
server message and the cell rolls back — the user re-applies if they still want it. Building a
three-way merge before anyone has hit a conflict is speculative.

### 1c. Everything else

- **Sort** — client-side on the loaded page; every list endpoint already returns a workable page.
- **Filter** — a per-grid toolbar; a text query plus the entity's own status facet.
- **Selection** — checkbox column; bulk actions apply the same single-field write per row, sequentially, and report `n succeeded, m failed`.
- **Empty / loading / error** — `EmptyState` and `Skeleton` primitives, never a bare blank panel.
- **Row click** — opens the existing detail route. Inline edit never navigates.

### 1d. Open questions for the user (defaults chosen, easy to change)

1. Should bulk edit be transactional (all-or-nothing) rather than per-row best-effort? Default: per-row.
2. Should a 409 auto-refetch the row so the user sees the winning value? Default: no, just roll back and toast.
3. Server-side sort/filter for large projects? Default: client-side until a grid actually hurts.

---

## 2. How this is verified — read this before trusting a slice

**There is no browser or preview tooling in this environment**, so nothing below has been *seen*
rendering. Per this repo's own CLAUDE.md rule, that is stated plainly rather than papered over:

> For UI or frontend changes … if you can't test the UI, say so explicitly rather than claiming success.

**What IS machine-verified per slice:** `npm run typecheck` (tsc, strict) and `npm run build`
(Next.js production build, which type-checks every route and fails on a bad import, bad hook usage
or a server/client boundary violation) — plus `npm test` (vitest + jsdom) where a slice adds
behaviour worth asserting.

**What is NOT verified:** that any of it *looks* right, that the rail collapses gracefully, that
contrast passes, that a grid edit round-trips against a live API. All of that is in §4.

---

## 3. Slice log

| Slice | Status | Machine proof |
|---|---|---|
| U1 design tokens | DONE | `npm run typecheck` clean · `npm run build` ✓ Compiled successfully · `npm test` **32 passed** incl. 8 new token-contract tests |
| U2 shell chrome | — | — |
| U3 primitives | — | — |
| U4 route migration | — | — |
| U5 polish + a11y | — | — |

---

## 4. PENDING-HUMAN-VERIFY

Run `cd planscape-web && npm install && npm run dev`, point `NEXT_PUBLIC_API_BASE` at a running API
(`http://localhost:5000` with the docker stack up), sign in, and walk these.

### U1 — design tokens
- [ ] **Light mode reads as a product, not a default:** every page has a visible surface/background
      separation (cards are not the same colour as the page) and body text is comfortably readable.
- [ ] **Dark mode:** run `document.documentElement.classList.add('dark')` in the console — the whole
      app flips, nothing stays white-on-white or black-on-black, and shadows still read as depth
      rather than smudge.
- [ ] **Focus ring:** Tab through a page — every focusable control shows the same 2px ring, and a
      *mouse click* on a button does NOT paint it.
- [ ] **Contrast:** spot-check `fg-muted` on `surface-2` and any badge (`warning` on
      `warning-subtle`) against WCAG AA (4.5:1 body, 3:1 large). These were picked by eye from HSL
      values, never measured — this is the most likely thing to need a tweak.
