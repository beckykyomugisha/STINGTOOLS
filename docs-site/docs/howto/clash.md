# Run clash detection

Clash detection finds physical conflicts between elements in different discipline models — a beam through a duct, a pipe through a wall opening, two ducts occupying the same space. Planscape runs clash detection server-side over the federated model, returning grouped clusters in seconds.

## When to run it

- Before a coordination meeting — to focus the discussion on real conflicts
- Before a stage-gate sign-off — to verify the design is co-ordinated
- After a major model update from any discipline
- Routinely (e.g. weekly) during the spatial coordination phase

You don't need to run it after every small change — it's an on-demand action, not a continuous one.

## Step 1 — Open the federated view

From the dashboard, **Coordination → Federated Model**. The 3D viewer loads the latest revision of every model marked **Include in federation**. You can pan, orbit, zoom, and click to inspect.

The viewer shows a small badge in the corner: how many models are loaded, the timestamp of each.

## Step 2 — Configure the run

Click **Run Clash Detection**. The configuration panel asks:

- **Discipline pairs** — which pairs to check. Defaults are:
    - Structure × MEP (the most common — beam clashing duct or pipe)
    - MEP × MEP (cross-system — duct clashing pipe)
    - Architecture × Structure (slab edge vs wall, opening vs beam)
    
    Untick pairs that aren't relevant for your project phase.
- **Tolerance** — the minimum overlap volume to count as a clash. Defaults to 5 mm; raise it during early-stage coordination to filter noise.
- **Filter by level** — restrict to a single level for spot-checking, or all levels (default).
- **Filter by zone** — restrict to a zone if your project is split that way.

Click **Run**. The button shows a progress bar; results typically take 10–30 seconds for a 30k-element federation, longer for very large models.

## Step 3 — Review the results

The clash report opens in a new panel. Results are grouped into **clusters** — bundles of intersecting elements that represent a single coordination problem. A duct passing through three closely-spaced beams is one cluster, not three clashes.

Each cluster shows:

- The two disciplines involved
- The element tags participating
- The cluster volume (a rough severity proxy)
- Whether it's already linked to an open issue
- A "fly-to" button that orbits the viewer to zoom on the cluster

Sort by volume (descending) to triage the worst clashes first.

## Step 4 — Create issues from clashes

For each cluster you want to action:

1. Click the cluster.
2. **Create Issue**. The new issue dialog opens, pre-filled with:
    - The cluster's element tags listed in the description
    - A link back to the clash report
    - Type defaulted to **NCR** (non-conformance) — change to RFI or SI if more appropriate
    - Discipline = the discipline of the element typically expected to move (configurable per pair in **Project Settings → Clash Routing**)
3. Set the assignee (defaults to the discipline lead) and due date.
4. Click **Raise**.

The cluster shows a yellow "Issue raised" pill from now on. The issue tracks through the normal RFI/NCR workflow.

## Step 5 — Verify after the fix

Once the underlying models have been updated and re-synced, re-run clash detection. The prior clusters are matched against the new run by element tag. If the cluster no longer appears in the new run, the linked issue auto-resolves with a note: *"Clash cluster auto-resolved by clash run on 2026-05-04."* The originator is notified.

If the cluster does still appear, the linked issue stays open and the cluster is flagged **Persisted** in the report.

## Export the clash report

From the report panel, **Export → BCF 2.1** produces a BIM Collaboration Format file compatible with ACC, Solibri, Navisworks, BIMcollab, and most other coordination tools. **Export → CSV** gives a flat tabular export for spreadsheet review.

For stage-gate sign-off you typically attach both the BCF and the issue list as evidence.

## Tips

- Don't run with 0 mm tolerance — every model has rounding errors that will produce false positives.
- For early stage models, exclude soft-clash-prone categories (insulation, cable trays without final routes) until you're past the coordination phase.
- Clash routing rules let you auto-assign — e.g. "Structure × MEP clashes go to the MEP lead by default" — but you can always override per cluster.

## Next steps

- [Issue an RFI](rfi.md) — for design questions arising from clashes
- [Sign off a stage gate](stage-gate.md) — using the clash report as evidence
