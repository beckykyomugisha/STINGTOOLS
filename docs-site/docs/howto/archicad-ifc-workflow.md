# ArchiCAD → Planscape IFC Workflow

This guide walks an ArchiCAD team through connecting their model to Planscape for ISO 19650-compliant coordination, issue tracking, and document management — without leaving their authoring tool.

## How it works

```
ArchiCAD  ──IFC export──▶  Planscape Upload  ──▶  Web Viewer + Issue Tracker
                                                          │
ArchiCAD  ◀──BCF import──  Planscape BCF Export  ◀───────┘
```

Planscape reads the IFC file ArchiCAD produces. Coordinators raise issues against elements in the viewer. You export those issues as a BCF file and import it straight back into ArchiCAD. No plugin required on the ArchiCAD side.

---

## Step 1 — Set up your IFC export in ArchiCAD

Open **File → Publish** (or **File → Save As → IFC**) and configure:

| Setting | Recommended value |
|---|---|
| Format | IFC 4 (preferred) or IFC 2x3 CV2 |
| Model View Definition | Coordination View 2.0 or Reference View |
| Export properties | ✅ All element properties |
| Export quantities | ✅ Base quantities |
| Export IFC type objects | ✅ |
| Split by storey | Optional — single file is fine |

> **IFC 4 vs IFC 2x3**: Use IFC 4 when possible. Planscape reads both, but IFC 4 carries richer property sets and better element classification.

### Save as a Publisher Set

Go to **File → Publish → New Publisher Set** and name it `Planscape Coordination`. Point the output to a shared folder or directly upload from there. This lets you re-publish with one click when the model updates.

---

## Step 2 — Upload to Planscape

1. Open your project in Planscape → **Models** tab
2. Click **Upload IFC**
3. Select the `.ifc` file exported from ArchiCAD
4. Planscape ingests the file and maps ArchiCAD property sets to STING coordination parameters automatically (see [Property mapping](#property-mapping) below)
5. The model appears in the federated 3D viewer within 1–2 minutes for files under 200 MB

### Keeping the model current

Re-export from ArchiCAD and upload a new version whenever the model is updated. Planscape versions each upload and keeps the issue pin locations referenced to the element `IfcGlobalId` — so existing issues remain anchored to the right element even after geometry changes.

---

## Step 3 — Coordination in Planscape

Once uploaded, your team works entirely in Planscape:

- **Issues** — raise RFIs, NCRs, clashes, or design queries directly on elements in the 3D viewer
- **Documents** — manage drawing transmittals, CDE state transitions (WIP → Shared → Published)
- **Compliance dashboard** — ISO 19650 tag completeness per discipline and level
- **Mobile app** — site team raises issues with GPS + photo from iOS/Android

ArchiCAD coordinators do not need a Revit licence or the StingTools plugin. They use only the Planscape web or mobile app.

---

## Step 4 — Export issues as BCF and import into ArchiCAD

When the coordination team has raised issues against your model:

1. In Planscape, go to **Issues → Export BCF**
2. Filter by status (e.g. `OPEN` only) or leave blank for all issues
3. Download the `.bcfzip` file
4. In ArchiCAD, open **File → Interoperability → BCF Manager**
5. Click **Import** and select the `.bcfzip`
6. Each issue appears as a BCF topic with the camera viewpoint set to the flagged location

ArchiCAD will highlight the referenced element using the `IfcGlobalId` embedded in each BCF viewpoint. You can navigate directly to the issue location, resolve it in the model, and mark the topic as resolved.

### Closing the loop

After resolving issues in ArchiCAD:

1. Re-export IFC → upload to Planscape (new model version)
2. Open the BCF Manager in ArchiCAD → mark resolved topics as `Closed`
3. Export BCF → import back into Planscape (**Issues → Import BCF**)
4. Planscape updates the issue status automatically from the BCF `TopicStatus`

---

## Property mapping

When Planscape ingests an ArchiCAD IFC file, it reads the following ArchiCAD property sets and maps them to STING coordination parameters:

| ArchiCAD property | IFC Pset | STING parameter |
|---|---|---|
| Element ID | `AC_Pset_ElementID.ElementID` | `ASS_SEQ_NUM_TXT` |
| Zone Name | `AC_Pset_ZoneBasicProperties.ZoneName` | `ASS_ZONE_TXT` |
| Zone Number | `AC_Pset_ZoneBasicProperties.ZoneNo` | `ASS_LOC_TXT` |
| Level code | `AC_Pset_RoomBasicProperties.RoomFloorPlanArea` → storey name | `ASS_LVL_COD_TXT` |
| Renovation status | `AC_Pset_RenovationOverride.RenovationStatus` | `ASS_STATUS_TXT` |
| Classification code | `AC_Pset_ElementProperties.Classification` | `ASS_PRODCT_COD_TXT` |
| MEP system name | `AC_Pset_MEPSystemData.SystemName` | `ASS_SYSTEM_TYPE_TXT` |

> Properties that ArchiCAD doesn't export (e.g. STING's ISO 19650 discipline code) remain empty and can be set by coordinators manually in the Planscape dashboard.

---

## Federated models (Revit + ArchiCAD)

If your project uses both Revit and ArchiCAD (e.g. Revit for MEP, ArchiCAD for architecture):

1. Upload each IFC file separately to Planscape as named **Model Sources**
2. Planscape federates them in the viewer automatically — elements from both files appear together
3. Clash detection runs across the federation
4. BCF issues include the originating model's `IfcGlobalId` so each authoring tool receives only its own issues when you import BCF back

---

## Checklist

- [ ] IFC export configured with all properties and base quantities
- [ ] Publisher Set saved in ArchiCAD for one-click re-export
- [ ] First IFC uploaded and model visible in Planscape viewer
- [ ] Team members invited to Planscape project with correct roles
- [ ] BCF Manager enabled in ArchiCAD (requires ArchiCAD 22 or later)
- [ ] Test issue raised in Planscape, BCF exported, imported into ArchiCAD
- [ ] Coordination cycle agreed: re-export frequency, BCF exchange cadence

---

## Troubleshooting

**Model does not appear after upload**
Check the file is valid IFC (not IFC-XML). ArchiCAD exports standard binary IFC by default — confirm the extension is `.ifc` not `.ifcxml`.

**Elements show no properties in Planscape**
Re-export with **Export all element properties** checked. In ArchiCAD 26+, this is under Translator Settings → Properties.

**BCF import shows no camera position**
The issue was raised without a 3D viewpoint. Ask the Planscape coordinator to anchor issues to elements in the viewer rather than raising them from the list view.

**IFC Global IDs change between exports**
This happens when elements are deleted and recreated in ArchiCAD rather than modified. Ask the ArchiCAD author to use **Modify** rather than Delete + Place. Stable `IfcGlobalId` values are the backbone of issue traceability.
