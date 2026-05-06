# Model federation

A real building is rarely modelled in a single Revit file. Architecture, structure, and MEP each live in their own model — sometimes split further by floor or by wing. **Federation** is the act of bringing those models together into a coordinated whole, so a coordinator can spot the structural beam clashing with the supply duct, or the architect's window opening missing in the structural model.

## What federation means in Planscape

Each discipline-author syncs their `.rvt` independently from the Revit plugin. Each upload becomes a separate **model record** in the project, tagged by discipline and revision. The cloud holds all of them.

When a coordinator opens the project in the dashboard or the mobile app, the **federated view** stitches the latest revision of each model into a single 3D scene. The viewer is built on three.js with a custom IFC-derived geometry pipeline that streams meshes to the client at the right level of detail for the device — full detail on a workstation, simplified meshes on a mobile.

You get one scene, navigated as a unit, with elements coloured by their source model. Click an element to see its tag, source discipline, and any open issues attached to it.

## Linking models

By default Planscape federates **every** synced model in the project. To exclude a model — for example a legacy as-built that's no longer relevant — go to **Project Settings → Models** and toggle **Include in federation**. Excluded models remain in the document register but don't load into the federated view.

For very large projects you can configure named **federation sets** — e.g. "L00–L05 only" or "MEP-only" — and switch between them from the viewer.

## Clash detection

From the federated view, **Coordination → Run Clash Detection** kicks off a server-side geometric clash run. The server:

1. Pulls the latest revision of each included model.
2. Builds a spatial index per discipline.
3. Runs pairwise intersection tests between configured discipline pairs — typically Structure × MEP, MEP × MEP within different systems, Architecture × Structure.
4. Groups intersecting elements into **clash clusters** — cohesive bundles where the same conflict spans multiple elements.
5. Returns a clash report.

A typical 30k-element federated model takes 10–30 seconds for a full clash run. The result lists clusters by discipline pair, severity (count of intersecting volume), and assigned-to (initially nobody).

## Linking issues to clashes

From a clash cluster you can **Create Issue** — the new RFI or NCR pre-fills with the clash context, the element tags involved, and a link back to the clash report. The issue moves through the normal workflow; the clash itself is marked as **In progress**.

Once the underlying models are updated and re-synced, the next clash run will either find the cluster again (issue still valid) or not (cluster auto-closes, issue is auto-resolved with a note).

## Re-running clashes

Clash detection is on-demand, not continuous — running it on every model upload would be wasteful. The dashboard shows the timestamp of the last clash run; coordinators trigger a fresh run after major model updates or before stage-gate sign-offs.

## Exporting the report

From the clash report screen, **Export → BCF 2.1** produces a BIM Collaboration Format file you can import into ACC, Solibri, Navisworks, or any other BCF-compatible viewer. **Export → CSV** gives you a flat table for spreadsheet analysis.

## Mobile federation

The mobile app downloads a simplified federated mesh on first project open — typically 30–80 MB depending on project size. The viewer supports pan, zoom, rotate, and tap-to-select. Tapping an element shows its tag and any open issues; long-press creates an issue pinned to that element.

This works fully offline once the mesh is cached.

## Next steps

- [Run clash detection](../howto/clash.md) — step-by-step
- [Issue an RFI](../howto/rfi.md) — including how to raise one from a clash cluster
