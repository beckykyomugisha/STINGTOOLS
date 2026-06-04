# Viewer cost + discipline data flow (M3)

How per-element **cost** and **discipline** reach the web viewer's Properties panel and
the VISUALIZE engine.

```
Revit (StingTools plugin)
└─ PublishModelCommand.BuildElementMap()                 → <model>-elements.json
   • key   = Element.UniqueId   (same id the glTF exporter writes to node.extras.uniqueId)
   • cost  = ASS_CST_UNIT_RATE_NR × measured qty (volume m³ / area m² / length m)
             → entry.cost (+ entry.costCurrency from ASS_CST_CURRENCY_TXT)
             → omitted entirely when no rate is set (viewer shows "—", never a fake)
   • discipline = ASS_DISCIPLINE_COD_TXT, else DeriveDisciplineFromCategory(category)
                  so as-built/untagged models still populate BY DISCIPLINE
   RevitGltfExporter writes node.extras.uniqueId + node.extras.category into the GLB.

Planscape.Server
└─ GET /api/projects/{p}/models/{m}/element-map
   • streams <model>-elements.json
   • M3: if a sibling "<model>-costs.json" exists in storage (a project BOQ export keyed
     by guid → number | {cost,currency}), MergeCostByGuid() augments entries in place.
     Backward-compatible: no cost sidecar ⇒ base map returned unchanged.

Web viewer (coordination-viewer.js)
└─ rebuildGuidIndex()  (M0)  resolves mesh → meta via userData.uniqueId/guid (+ ancestors)
   • assignElementIds (viewer.html) sets userData.elementGuid from guid OR uniqueId.
└─ renderProperties(guid)  (M3)
   • findCost(meta): meta.cost / estimatedCost / totalCost / any CST_* → currency-formatted
   • full identity + dimensions + performance + every scalar, grouped, filterable, scrollable
└─ discOf(meta)  (M1)  uses the discipline token, deriving from category when absent.
```

**Verifying end-to-end:** publish a model whose elements carry `ASS_CST_UNIT_RATE_NR`,
open it in the viewer, single-click a known element → the Properties **Cost** row shows
`rate × quantity` in the element's currency. Elements without a rate show `—`.
