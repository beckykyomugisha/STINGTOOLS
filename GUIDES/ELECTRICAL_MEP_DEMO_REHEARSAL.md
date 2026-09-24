# STING Tools — MEP / Electrical Demo: Rehearsal Runbook

**Talk:** How STING Tools works with MEP (electrical focus) · **Length:** 15–20 min live + Q&A
**Build under rehearsal:** branch `claude/electrical-mep-presentation-review-30b524` (PR #976), deployed
to `C:\Dev\STINGTOOLS\.claude\worktrees\stoic-goldstine-a8c0c5\CompiledPlugin`.

> **The one rule:** only click what you have clicked successfully in the last 24 hours, on the
> exact model file you will present from. Everything else is a slide, a screenshot, or a
> "we're finishing that" answer.

---

## 0. The story you are telling (memorise this, not the buttons)

1. **One model, many deliverables.** The engineer models the electrical system once in Revit;
   STING turns it into panel schedules, a single-line diagram, annotated drawings, Excel
   registers and compliance evidence — without re-typing anything.
2. **ISO 19650 by default.** Every element gets a structured asset tag, so the drawings, the
   schedules and the FM handover all speak the same language.
3. **Round-trip, not one-way.** Excel out → engineer edits → Excel back in, with a diff.
4. **Honest engineering.** Calculations are *design-assist*, with the basis and assumptions shown.
   The engineer signs, not the software.

If a button fails live, the story still holds — say the sentence, move to the next step.

---

## 1. Three-day plan

| When | Do | Done when |
|---|---|---|
| **Day 1 (today)** | §2 build the demo model · §3 first-light checks · first full run of §5 with a stopwatch | Every §5 step worked once; times noted |
| **Day 2** | Two full dry runs of §5 **out loud**, to an empty room or a colleague · record a backup screen video of a clean run · print §7 Q&A | Two clean runs under 18 min; video saved in two places |
| **Day 3 (talk day)** | §4 pre-flight 60 min before · one silent run-through · close everything else | Pre-flight all ticked |

Budget: if a step fails twice in rehearsal, **cut it** and replace it with a screenshot. Do not
debug on Day 3.

---

## 2. Build the demo model (Day 1, ~2 hours)

Work on a **copy**: `…\DEMO\STING_MEP_Demo.rvt`. Several buttons change the whole project.

**Content**
- [ ] 1 × main switchboard **MDB** (400/230 V, 3-phase) as the supply root — leave its own supply **un-circuited** so it is the root.
- [ ] 2 × distribution boards **DB-L1**, **DB-L2** (400/230 V), each fed from the MDB by a circuit.
- [ ] Give every board a real **Panel Name** (MDB, DB-L1, DB-L2) — not just the type name.
- [ ] 8–12 **power and lighting circuits** on DB-L1/DB-L2 with real loads (mix of 1-pole and 3-pole), circuit paths drawn so **circuit length is non-zero**.
- [ ] 3–4 **rooms** with names (Office, Corridor, Store, WC), 6–10 **lighting fixtures** with wattage and lumens filled in, one fixture family/type with **"Emergency"** in its name.
- [ ] A few **conduits** from DB-L1 to devices, physically **connected** (fittings joined).
- [ ] A **roof** element (not a floor) and 2–4 **air terminals** whose family name contains "Air Terminal", for the lightning section.

**Setup buttons (run once, in this order)**
- [ ] Main STING panel → **CREATE TAGS** tab → **Load Params** (binds the shared parameters).
- [ ] Main STING panel → **SETUP** tab → symbols → **SLD** (builds the IEC 60617 SLD symbol families). Then **load them into the project** if they are not already (Insert → Load Family from the folder it reports). *Without these the SLD is lines and text only — the dialog now tells you so.*
- [ ] Make sure the project has at least one **panel schedule template** (Manage → Panel Schedule Templates). Revit's API cannot create one.
- [ ] Project Information: set **Number** (project code) and address.

**Save the model.** Keep a pristine copy `STING_MEP_Demo_CLEAN.rvt` to restore from.

---

## 3. First-light checks (Day 1, 10 min) — stop if any fails

Ribbon → **STING Tools** → **STING Panels** → **STING Electrical** opens the Electrical panel
(tabs: PNLS · CIRCTS · CALCS · CABLE · SLD · LITE · RPRT).

| # | Check | Expect | If not |
|---|---|---|---|
| 1 | Revit loads the plugin | STING Tools ribbon present | Run in Git Bash: `grep -h "<Assembly>" "$APPDATA/Autodesk/Revit/Addins"/*/StingTools.addin` — must point at the stoic-goldstine `CompiledPlugin` |
| 2 | Pick your longest lighting circuit (e.g. ~25 m of 1.5 mm² at ~6 A). Hand-check: 29 mV/A/m × 6 A × 25 m = 4.4 V = **1.9 %** of 230 V. Then **CALCS → ▶ Recalculate All** | STING's VD % for that circuit is within ~20 % of your hand figure | If it is ~10× smaller (≈0.2 %), the unit fix is not live — stop and report |
| 3 | **PNLS → ⚡ Batch Create Schedules** | Result panel lists a schedule per board | "No PanelScheduleTemplate" → add a template (§2) |
| 4 | **SLD → ▶ Generate SLD Drafting View** | Readable diagram, **Symbols placed > 0** | "Symbols placed: 0" → load the SLD families (§2) |

---

## 4. Talk-day pre-flight (60 min before)

- [ ] Laptop on mains power, notifications off, Windows updates paused, second monitor mirrored or extended as rehearsed.
- [ ] Close the Planscape Companion tray app and every other program.
- [ ] Restore the demo model from `STING_MEP_Demo_CLEAN.rvt` → open it → **Save As** today's name.
- [ ] Open the STING Electrical panel and dock it right. Open the LPS panel and tab it behind.
- [ ] Open, in this order, views you will jump to: **Level 1 electrical plan**, **3D view**, and the **panel schedule for DB-L1**.
- [ ] Have **Excel** open and minimised (the Excel round-trip needs it).
- [ ] Backup video open in a media player, paused on frame 1.
- [ ] Browser tab with the slide deck; screenshots of anything you cut.

---

## 5. Run of show (≈16 min)

Each step: **Say** (one or two sentences) → **Click** → **Expect** → **If it fails**.

### ① Opening — the model (1 min)
- **Say:** "This is an ordinary Revit electrical model: a main switchboard, two distribution boards, lighting and power circuits. Everything you'll see comes from this model. Nothing is typed twice."
- **Click:** orbit the 3D view briefly, then open the Level 1 plan.

### ② Panel schedules in one click (2 min)
- **Say:** "Normally someone builds each panel schedule by hand. STING builds all of them, picks the right template per board, and fills the board data."
- **Click:** Electrical panel → **PNLS** → **⚡ (Batch Create Schedules)**.
- **Expect:** result panel with one schedule per board. Open **DB-L1**'s schedule from the Project Browser.
- **If it fails:** "The template in this model isn't set up for that board type. Here's the output from our test project" → screenshot.

### ③ Excel round-trip (3 min)
- **Say:** "Engineers still live in Excel. STING exports every schedule, you edit it there, and it comes back into the model with a list of exactly what changed."
- **Click:** **RPRT** → **▶ Export to Excel** → save → open in Excel → change **one circuit description** → save and close Excel → **▶ Import from Excel** → **▶ Show Last Import Diff**.
- **Expect:** the diff shows your one change, and the panel schedule shows the new text.
- **If it fails:** show the exported workbook only: "export is the everyday path; import is the power-user path."

### ④ Protective device sizing (2 min)
- **Say:** "STING proposes breaker ratings from the design current. It shows the proposal first, and the engineer decides."
- **Click:** **CALCS** → **▶ Preview** (breakers) → review → **▶ Apply to Model**.
- **Expect:** the preview grid, then updated ratings on the circuits / schedule.
- **Frame it:** "The software proposes; the engineer approves."

### ⑤ Single-line diagram (3 min)
- **Say:** "The single-line diagram is drawn from the actual circuit hierarchy in the model, not redrawn in CAD. If the model changes, you regenerate it."
- **Click:** **SLD** → **▶ Generate SLD Drafting View**.
- **Expect:** a drafting view "STING - SLD - …" with IEC symbols for MDB → DB-L1 / DB-L2 → circuits, and **Symbols placed > 0**. Zoom in on one board.
- Optional: **▶ Generate Riser Diagram** (vertical riser, outline boxes).
- **If it looks wrong:** switch to the SLD you generated and checked during rehearsal (keep one in the model: "here's the one I generated this morning").
- **Do not** turn on SLD sync, and do not click the SLD **Annotate** buttons.

### ⑥ Annotated drawings (2 min)
- **Say:** "Wiring annotation (cores, cable size, circuit reference, home-run arrows) is placed on the plan from the circuit data."
- **Click:** open the Level 1 plan → **CABLE** → **Stamp all** → **Conduit all** → **Home all**.
- **Expect:** labels and home-run arrows on the conduits.
- **If labels read "? Wire":** the conduits aren't connected to a circuit. Say "these runs aren't connected yet", then **CABLE → Clear** and move on.

### ⑦ Lighting design check (1.5 min)
- **Say:** "Lighting power density per room, colour-coded against the target."
- **Click:** **LITE** → **▶ Calculate W/m²** → **▶ Color rooms in view**. Optionally **▶ Emergency Circuit Audit**.
- **Expect:** rooms tinted by pass/fail. *(Room colour needs the room fill visible in the view — check in rehearsal; if nothing shows, show the result table instead.)*

### ⑧ Lightning protection — BS EN 62305 (1.5 min)
- **Say:** "Lightning risk to BS EN 62305-2, which matters in Uganda, one of the highest lightning-density regions in the world."
- **Click:** Ribbon → **STING Panels** → **STING LPS** → **RISK** → **Run risk**, then **AIR-TERM** → **3D coverage** (rolling sphere).
- **Expect:** risk components and protection class; a 3D coverage view on the roof.
- **Don't** quote protection angles.

### ⑨ Close (30 s)
- **Say:** "One model: schedules, SLD, annotated plans, Excel registers and lightning compliance, all from the same data and all ISO 19650 tagged. The calculations assist the engineer, and the engineer signs."

---

## 6. Do NOT click during the talk

The calculation engines below were **rebuilt this week** (PR #976) and pass their unit tests
against hand calculations, but **none has run in Revit yet**. Keep them out of the live demo
unless you have run them on the demo model, checked one result by hand, and done so 24 h
before the talk.

| Button | Why |
|---|---|
| CALCS **⚡ Arc Flash Calc** and its label / schedule / boundary | Now IEEE 1584-**2002** (indicative, three-phase only). **Never show PPE numbers in a talk.** |
| CALCS **▶ Calculate Fault Levels**, **Stamp to Panels**, fault schedule | New IEC 60909-style method, not yet verified in a live model |
| CABLE **▶ Calculate** / **Apply to Circuit**, **Cable size** sync, feeder sizing | New BS 7671 App 4 method, but **only PVC 70 °C Cu, method C** ships; anything else is refused |
| CALCS **📈 TCC Curve Plot**, **Selective Coordination Viewer** | Generic IEC 60898 bands; most real pairs report "Not assured" (correct, but confusing live) |
| CABLE **🗺 Auto-Route Conduit** | Rectilinear L/Z runs, no obstacle avoidance; not presentation quality |
| RPRT **EasyPower / DIALux / ETAP** exports | Data hand-off drafts (`*_DRAFT_*`), not native imports |
| CIRCTS **🗑** ("Delete") | Removes spares/spaces in every schedule (it asks — answer **No**) |
| CALCS **✕ Clear Overrides** | Resets every override in the view (it asks — answer **No**) |
| CIRCTS **🔢 Renumber** | Now really moves circuits between slots via Revit's panel-schedule API: **changes the model**, unrehearsed |
| CIRCTS **⬇ Sort**, **⚖ Balance** | Revit derives numbers/phases from slots; they correctly report few or no changes, which looks like a failure on stage |
| SLD **Annotate …** buttons, **SLD sync** | Annotate was fixed this week but is unverified; SLD sync stays off |

## 7. Q&A — likely questions, honest answers

**"Is this BS 7671 compliant?"** "STING checks against BS 7671 rules (disconnection times, Zs
limits, adiabatic k-values) and produces the Appendix 6 certificate template. Compliance is
signed by the engineer and the inspector; STING provides the evidence and does the checking."

**"Can it do arc flash / fault levels?"** "Fault levels follow the IEC 60909 method for LV
networks, with every assumption listed per board. Arc flash currently uses IEEE 1584-2002 and
is labelled indicative. The 2018 edition is next. I won't show PPE numbers until they've been
independently validated: for safety calculations we'd rather be late than wrong."

**"Does it work with ETAP / DIALux?"** "It exports the model data those tools need. The native
import formats are on the roadmap. Today it's a structured data hand-off."

**"What about HVAC and plumbing?"** "There are dedicated HVAC and Plumbing panels, same
approach: sizing, schedules, compliance checks. Today I'm focusing on electrical." *(Only demo
them if you have rehearsed them.)*

**"Uganda / local standards?"** "There are regional defaults for Uganda: wind, seismic, rainfall
and live load. The project can declare its own supply earthing values, e.g. UMEME's Ze, which
override the UK defaults." *(The override file is `_BIM_COORD/bs7671_disconnection.json`. Try
it once before saying this.)*

**"Revit versions?"** "Revit 2025 and 2026." *(2027 is installed but not verified. Don't claim it.)*

**"Is my data safe / does it need the internet?"** "It runs inside Revit on your machine. The
Planscape server is optional, for team coordination."

**"How is this different from Revit's own tools?"** "Revit stores the circuits; STING turns them
into deliverables: batch schedules, SLD, Excel round-trip, ISO 19650 tagging, BS 7671 evidence,
lightning risk, in one workflow."

If you don't know: "Good question. I'll confirm and follow up by email." Write it down.

---

## 8. If something goes wrong live

1. **One failure:** say the sentence for that step, then "I'll show you the output from this morning's run", and open the screenshot or the pre-generated view.
2. **Revit hangs more than 20 s:** don't wait. Switch to the backup video at the matching step, and narrate over it.
3. **Plugin panel missing:** ribbon → STING Panels → STING Electrical. If still missing, go to the video.
4. Never debug in front of the audience. Never apologise for more than one sentence.

---

## 9. Timing sheet (fill during rehearsal)

| Step | Target | Run 1 | Run 2 | Run 3 |
|---|---|---|---|---|
| ① Model | 1:00 | | | |
| ② Schedules | 2:00 | | | |
| ③ Excel | 3:00 | | | |
| ④ Breakers | 2:00 | | | |
| ⑤ SLD | 3:00 | | | |
| ⑥ Annotation | 2:00 | | | |
| ⑦ Lighting | 1:30 | | | |
| ⑧ LPS | 1:30 | | | |
| ⑨ Close | 0:30 | | | |
| **Total** | **16:30** | | | |

---

## 10. What changed in the build you are rehearsing

PR #976 now also contains the calculation rebuild (fault current, cable sizing, voltage drop,
arc flash, coordination, lighting, wiring annotation, routing). The demo path in §5 does
**not** depend on any of it except breaker sizing (④), which now also checks the breaker
against the cable (In ≤ Iz) and blocks an oversize breaker instead of applying it. If ④
shows "blocked" rows, that is the new safety check working. Say so.

**Nothing on the §6 list moves into the demo unless it passes a §3-style first-light check
on the demo model at least 24 hours before the talk.** If a new build is deployed, repeat §3
in full.
