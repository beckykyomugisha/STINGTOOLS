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
| U2 shell chrome | DONE | typecheck clean · build ✓ Compiled successfully · `npm test` **45 passed** incl. 13 new breadcrumb / nav-model / tenant-switch tests |
| U3 primitives | DONE | typecheck clean · build ✓ · `npm test` **59 passed** incl. 14 DataGrid tests that assert the contract itself — optimistic apply, **rollback + server message on failure**, no-op on unchanged value, Escape abandons, edit never navigates |
| U4 route migration | DONE | typecheck clean · build ✓ · `npm test` **62 passed** incl. 3 new route invariants: no hard-coded palette utility survives anywhere, every rail link has a real `page.tsx`, every page is inside the AppShell |
| U5 polish + a11y | DONE | typecheck clean · build ✓ · `npm test` **69 passed** incl. 7 new Menu keyboard/ARIA tests (arrow keys, Home/End, Escape restores focus, disabled items skipped) |

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

### U2 — shell chrome
- [ ] **Rail:** left rail shows global nav; opening a project adds a "Project" group (Overview,
      Issues, Clashes, Models, 3D viewer, Documents, Transmittals, Meetings, Site photos, Members).
      The current section is highlighted, and `/projects` does NOT stay highlighted while inside a project.
- [ ] **Collapse:** the ☰ button collapses the rail to icons; hovering an icon shows its label as a
      tooltip; the choice survives a reload.
- [ ] **Mobile:** under ~1024px the rail becomes a drawer — ☰ opens it, the scrim or a route change
      closes it, and it never covers content after navigating.
- [ ] **Project switcher:** the top bar shows the current project; opening it lists projects and
      jumping to one navigates without a full reload.
- [ ] **Tenant switcher:** with an account in ONE firm, no org control appears at all. With an
      account in TWO firms it appears — switching reloads into `/projects` showing the OTHER firm's
      projects, and the old firm's data is gone. **This is also the two-firm isolation spot-check.**
- [ ] **Theme:** avatar menu → Light / Dark / System each apply immediately and survive a reload;
      System follows an OS theme change live; there is **no white flash** on a hard refresh in dark
      mode (that's the blocking head script).
- [ ] **Breadcrumb:** on a deep route (`/projects/<id>/meetings/<id>`) the trail reads
      Projects / #xxxxxxxx / Meetings / #xxxxxxxx, every crumb but the last navigates.
- [ ] **Full-bleed:** content now fills the width — the old `max-w-5xl` cap is gone, so a wide table
      uses the screen instead of scrolling inside a 64rem column.
- [ ] **Skip link:** press Tab on page load — the first stop is "Skip to content" and it jumps past the rail.

### U3 — primitives
- [ ] **Modal / Drawer:** open one → focus moves inside, Tab cycles within it, Escape closes and
      focus returns to the trigger, the background does not scroll.
- [ ] **Tabs:** arrow keys move between tabs (roving tabindex), not just clicks.
- [ ] **Grid sort:** click a header → ▲, again → ▼, again → back to server order.
- [ ] **Grid filter:** typing narrows across ALL columns; a no-match says "No rows match that
      filter", not "nothing here yet".
- [ ] **Inline edit — success:** click an editable cell → editor appears → change it → cell keeps the
      new value and a success toast appears.
- [ ] **Inline edit — failure (the important one):** stop the API (or edit a row you lack permission
      on) → the cell **snaps back** to its previous value and an error toast shows the server's own
      message, and that toast does NOT auto-dismiss.
- [ ] **Selection:** filter the grid, then select-all → only the visible rows are selected; the count
      chip matches.
- [ ] **Row click vs cell edit:** clicking a non-editable cell opens the detail route; clicking an
      editable cell or a checkbox does not navigate.
- [ ] **Toast stacking:** trigger three failures — they stack bottom-right, each dismissible, and
      don't cover the grid toolbar.

### U4 — route migration
- [ ] **Issues route exists:** the rail's Issues link opens a grid (it 404'd before this slice —
      the project overview was doubling as the issues list).
- [ ] **Project overview is a summary:** stat tiles (open issues / high+critical / new clashes /
      compliance) plus a "Needs attention" list; the tiles navigate; it is no longer a second copy
      of the issues list.
- [ ] **Issues grid:** status, priority and assignee edit inline and persist after a reload.
      Title/description are NOT editable here (they're on the detail page) — confirm that reads as
      deliberate rather than broken.
- [ ] **Clashes grid:** status, assigned-to and resolution note edit inline; severity and overlap
      volume are read-only (detector output, no write endpoint).
- [ ] **Members grid:** project role edits inline; ISO 19650 role is only editable when the server
      returns the role vocabulary — with an older API that column should be plain text, not an
      empty dropdown.
- [ ] **Documents:** no inline status cell; the row offers exactly one legal transition
      (WIP→Share, SHARED→Publish, PUBLISHED→Archive) plus Download. Upload is a modal.
- [ ] **Transmittals:** same shape — one action button per row (Send/Acknowledge/Respond), Respond
      opens a modal for notes instead of `window.prompt`.
- [ ] **Dark mode across every route:** switch to Dark and walk all 23 routes. Nothing should stay
      white — this is what the palette→token migration was for, and the test only proves the
      utilities are gone, not that each result *looks* right.
- [ ] **Wide tables:** a grid with many columns scrolls inside its own container, and the page
      itself does not scroll horizontally.

### U5 — polish + accessibility
- [ ] **Keyboard-only pass:** unplug the mouse. Tab from page load → Skip to content → rail → top
      bar. Open the avatar menu with Enter, move with ↑/↓, Home/End jump to the ends, Escape closes
      and focus lands back on the avatar.
- [ ] **Reduced motion:** enable "Reduce motion" in the OS. Skeleton shimmer stops, the drawer stops
      sliding, toasts stop fading. Nothing should become invisible or stuck — reduced motion means
      instant, not absent.
- [ ] **Screen reader (NVDA/VoiceOver):** loading a grid announces "Loading rows", and settling
      announces the row count. A failed inline edit announces the error toast.
- [ ] **Zoom to 200%:** the rail collapses/drawers rather than crushing the content column; no
      horizontal page scroll.
- [ ] **Contrast audit with a real tool** (axe / Lighthouse) on: a grid, a form, the rail, and a
      badge of each tone, in BOTH themes. The token ramp was chosen from HSL values by eye and has
      never been measured — assume something needs a nudge.
- [ ] **Focus visible on every control**, including inside modals and inside grid cells, in dark
      mode as well as light.

---

## 5. What is NOT done

Written down so nobody assumes it was covered:

- **No visual verification of any kind.** No browser, no screenshots, no Lighthouse. Every slice's
  proof is typecheck + build + vitest. The whole of §4 is genuinely open.
- **No responsive design work below ~640px** beyond the rail becoming a drawer. Grids will scroll
  horizontally on a phone; that is the intended v1 behaviour, not a tested one.
- **The viewer, live-meeting and photo routes kept their existing layouts** — they were
  token-migrated so dark mode works, not redesigned. They are canvas/media surfaces where the grid
  and card primitives don't apply.
- **No bulk-edit UI.** The DataGrid supports selection and the contract describes bulk semantics,
  but no grid currently exposes a bulk action.
- **No server-side sort/filter.** Client-side only, per the contract's default.
