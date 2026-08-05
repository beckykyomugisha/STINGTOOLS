# MEP Foundation Guide — Symbols, Family Authoring & Placement

---

## How to use this guide

This guide is a single, self-contained reference for three topics that always go together: making MEP symbols, authoring families correctly so that automated placement tools can use them, and using the STING Placement Centre to deploy those families across a building model.

Read it from beginning to end the first time. After that, use the chapter headings and quick-reference tables at the back to find specific information fast.

Code blocks, table references, and button labels are written exactly as they appear on screen — what you see in Revit and in STING is exactly what is written here.

> **How to use the blockquotes:** anywhere you see a `> **Stuck?**` blockquote, stop and read it before moving on. It explains what a confusing screen means and what to do about it.

---

## Who this guide is for

You have opened Revit. You have placed a door or a desk family by clicking `Insert → Load Family` and then clicking in the model. You have not spent much time in the Family Editor — the window that opens when you edit or create a family from scratch.

This guide assumes:

- You know what a Revit project is (a `.rvt` file that contains a building model).
- You know what a family is in loose terms (a reusable component — a light fitting, a socket outlet, a basin).
- You have not yet built a family from scratch in the Family Editor.
- You have not used STING's automated placement tools before.

You do not need to be a programmer. All instructions are written for someone using the mouse and the keyboard, clicking menus and typing values into fields.

---

## The Big Picture — Read This Before Anything Else

MEP (Mechanical, Electrical, Plumbing) drawings show two kinds of things: real equipment that will be installed in a building, and the symbols that represent that equipment on a drawing. A socket outlet on a wall is the real thing. The small circle with two slots drawn on a floor plan is the symbol.

In Revit, the work happens in three stages that always go in this order:

**Stage 1 — Build symbols.** You draw the 2D shapes that represent each piece of equipment: the circle for a smoke detector, the oval for a basin, the rectangle for a distribution board. These shapes live inside Revit family files (`.rfa` files).

**Stage 2 — Author families.** You make sure each family file is set up correctly so that automated tools can place it in the right location, at the right height, on the right wall or ceiling. A family that hasn't been set up correctly will either fail to place automatically, or land in the wrong position.

**Stage 3 — Use the Placement Centre.** With correct families loaded, you write rules — plain-language instructions like "put one socket every 3 metres along office walls at 1100 mm above the floor" — and STING executes those rules across every room in the building, placing the right family in the right place automatically.

An analogy: imagine you are baking 600 identical muffins for a conference. Stage 1 is writing the recipe. Stage 2 is making sure your mixing bowl, tins, and oven are the right sizes. Stage 3 is running the production line. Skipping Stage 1 means you have no recipe. Skipping Stage 2 means the recipe calls for a 12-inch tin but you only have 9-inch tins — nothing comes out right. Stage 3 doesn't work without the first two.

---

# Chapter 1 — MEP Symbol Creation

---

## 1.1 What is an MEP Symbol?

A symbol is a small 2D drawing that represents a real piece of equipment on a plan or section drawing.

Think of a map legend. On a road map, a small red cross means hospital. On an MEP drawing, a small circle with the letter "S" inside means smoke detector. A small oval shape means basin or hand basin. A small rectangle with the letters "DB" means distribution board.

Symbols matter for two reasons:

1. **Communication.** An electrician on site reads the symbol to know what goes where. A commissioning engineer reads it to know what to test. An architect reads it to know whether the services fit in the ceiling void.

2. **Standards.** Different countries use different symbol conventions. A UK project follows BS EN 60617. A US project follows NFPA 170 and IEEE 315. An international project may need all three. Symbols must follow the correct standard for the jurisdiction so that the drawing is legally and professionally valid.

STING's MEP symbol library covers nine disciplines: Gas, Controls/BMS, Lighting, Public Health/Drainage, Communications/Low Voltage, Fire Protection, Plumbing, Electrical, and HVAC. Each discipline follows one or more published standards, and the library carries three standard variants (IEC/ISO, BS/EN, and ANSI/NFPA) inside a single family file so one file can serve any jurisdiction.

---

## 1.2 Revit Family Editor Setup — A Step-by-Step Walkthrough

The Family Editor is a separate application mode inside Revit. When you open a `.rfa` file, Revit switches into Family Editor mode. You can tell you're in Family Editor mode because the ribbon changes completely — instead of the Architecture / Structure / Systems tabs you see in a project, you see `Create`, `Insert`, `Annotate`, `View`, and `Manage` tabs.

Think of a Revit family like a rubber stamp. The family is the stamp — a fixed shape. When you place the family in a project, each placement is an impression of that stamp. You make the stamp once (Family Editor), then use it as many times as you need (project).

### Opening the Family Editor

There are two ways to open the Family Editor, depending on whether you are creating a brand new family or editing an existing one.

**To create a brand new family:**

1. In the main Revit window, click `File` in the top-left corner (the large R menu on older versions, or the File tab on newer ones).
2. Hover over `New`.
3. A flyout menu appears. Click `Family`.
4. A file browser dialog opens titled "New Family — Select Template File". This is where you choose which kind of family you are creating.

**To edit an existing family:**

1. In the Revit project, find the family in the Project Browser (the panel usually docked on the left of the screen). It is under `Families → [Category] → [Family Name]`.
2. Right-click the family name.
3. Click `Edit...`. The Family Editor opens with that family loaded.

Alternatively: Insert tab → Load Family → navigate to the `.rfa` file → open it directly in Revit which will open the Family Editor.

> **Stuck?** If you do not see a `File → New → Family` option, you may be looking at a view of the project rather than the main application menu. Click the large `R` (or the File tab) in the very top-left of the Revit window, not the tabs in the middle of the ribbon.

### Choosing the Right Template

When you create a new family, the first decision is the template. A template is a blank starting point with the right settings already configured for a particular type of family.

Think of a template like a form with fields already labelled. A medical intake form has spaces for name, date of birth, blood type. A tax form has spaces for income, deductions, tax code. Choosing the wrong template is like filling in a tax form when you needed a medical form — the spaces don't match what you need to record.

The "New Family — Select Template File" dialog shows a list of `.rft` files. You need to navigate to the right folder. On most Revit installations:

- Revit 2025: `C:\ProgramData\Autodesk\RVT 2025\Family Templates\English\`
- Revit 2026: `C:\ProgramData\Autodesk\RVT 2026\Family Templates\English\`
- Revit 2027: `C:\ProgramData\Autodesk\RVT 2027\Family Templates\English\`

The folder contains many subfolders. The ones most relevant to MEP symbols are:

| Template file | Where to find it | When to use it | What it enables |
|---|---|---|---|
| `Generic Annotation.rft` | Root of the English folder | Standalone symbols not attached to a wall, ceiling, or floor | The default for 80% of MEP symbols. The family scales with the view and can be placed anywhere. |
| `Lighting Fixture Ceiling Based.rft` | `English\` root | Ceiling-mounted luminaires | The family automatically attaches to a ceiling when placed. |
| `Lighting Fixture Wall Based.rft` | `English\` root | Wall-mounted luminaires and switches | Attaches to a wall. |
| `Plumbing Fixture Floor Based.rft` | `English\` root | Floor-mounted sanitary fixtures (shower trays, floor drains) | Attaches to the floor slab. |
| `Plumbing Fixture Wall Based.rft` | `English\` root | Wall-hung sanitary fixtures (basins, WCs) | Attaches to the wall. |
| `Plumbing Fixture Ceiling Based.rft` | `English\` root | Ceiling-mounted plumbing (overhead showers) | Attaches to the ceiling. |
| `Mechanical Equipment.rft` | `English\` root | Large HVAC plant (AHUs, boilers) | For equipment, not annotation symbols. |

**The critical rule:** use `Generic Annotation.rft` for any symbol that is a 2D annotation only. Use a hosted template only when the physical family genuinely attaches to a host element (a wall, ceiling, or floor) in the real building.

**Do not use** `Generic Model.rft` for MEP symbols. Generic Model families are three-dimensional and appear in 3D views and rendered images. Annotation symbols should only appear on 2D drawings; Generic Annotation families are hidden in 3D views automatically.

After selecting the template, click `Open`. The Family Editor opens.

> **Stuck?** If the `English` folder is empty or missing, your Revit installation may be in a language other than English, or the Family Templates component may not have been installed. Go to `Control Panel → Programs → Autodesk Revit [version] → Modify` and make sure Family Templates is ticked.

### The Family Editor Canvas — What You Are Looking At

The Family Editor window looks similar to a Revit project, but simpler. Here is what you see:

**The canvas (large centre area):** This is where you draw. It shows either a plan view, an elevation view, or a 3D view of the family. When you first open a Generic Annotation template, you are looking at a plan view.

**The ribbon (top):** The Family Editor ribbon has these tabs:
- `Create` — the main tab for drawing geometry, adding parameters, and adding labels.
- `Insert` — for loading other families to nest inside this one.
- `Annotate` — for adding dimensions and text to the canvas (mostly used for documentation, not for symbol geometry).
- `View` — for changing which view of the family you are looking at.
- `Manage` — for managing shared parameters, project units, and other settings.

**The pink/green reference planes:** When you open a Generic Annotation template, you immediately see two lines crossing at the centre of the canvas — one horizontal and one vertical. These are called reference planes. They are shown in pink or green depending on which is selected.

Reference planes are like the crosshairs on a gunsight. They mark the family's origin — the point that Revit will snap to the exact location where you click when you place the family in a project. Everything you draw must be aligned to these planes.

An analogy: the reference planes are the pin you push through a paper map to stick it to a board. Revit places the family pin-first — exactly at the point you click. If you draw your symbol 20 mm to the right of the pin, the symbol will appear 20 mm to the right of wherever you click in the project.

**The Properties palette (usually docked on the left):** Shows properties of whatever is selected. When nothing is selected, it shows the view properties.

**The Project Browser (also usually on the left):** In the Family Editor, this shows the views available inside the family — typically `Floor Plans: Ref. Level`, `Elevations`, and possibly a 3D view.

### Configuring the Reference Planes — The Critical First Step

Before drawing any geometry, check and configure the reference planes.

1. Click on the horizontal reference plane (the horizontal line crossing the canvas). The line turns blue to show it is selected.

2. Look at the Properties palette on the left. You should see:
   - `Is Reference:` with a dropdown. Change this to `Strong Reference` if it is not already set.
   - `Defines Origin:` tick this box if it is not already ticked.

3. Click somewhere empty to deselect. Then click the vertical reference plane and repeat: set `Is Reference: Strong Reference` and tick `Defines Origin`.

4. The intersection of these two planes is now the family origin — the pin point. All symbol geometry must be drawn centred on this intersection.

> **Stuck?** If you cannot see the Properties palette, go to `View → User Interface → Properties`. If you cannot find the reference planes (they may be very faint), go to `View → Visibility/Graphics`, find "Reference Planes" in the list, and make sure they are ticked visible.

**Why Strong Reference matters:** Strong Reference makes the reference plane selectable as a dimension anchor when this family is placed in a project. This allows the placement engine to precisely position the family relative to walls, doors, and other elements.

### Adding Symbolic Lines — Drawing the Symbol

Now you can draw the actual symbol geometry. In the Family Editor, 2D shapes drawn for plan-view symbols are called "Symbolic Lines." They are called this because they represent what the element looks like symbolically — not a 3D geometry of the object itself.

Step by step:

1. Make sure you are looking at the plan view (`Floor Plans: Ref. Level` in the Project Browser on the left).

2. Click the `Create` tab in the ribbon.

3. In the `Detail` panel (one of the groups of buttons in the Create tab ribbon), click `Symbolic Lines`. A small toolbar appears at the top of the canvas with line drawing tools.

4. Before drawing, set the subcategory. In the `Properties` palette on the left, find `Subcategory` and click the dropdown. If the subcategory you need does not exist yet, you need to create it first (see Section 2.5 on subcategories below). For now, if you just want to draw and test, you can leave it as `<None>` and change it later.

5. Also in the small toolbar that appeared, check the line style. There is a dropdown showing the current line style (usually `Solid`). Leave it as `Solid` for most symbols.

6. Click `Line` in the toolbar (the straight line tool). Your cursor changes to a crosshair with a pencil icon.

7. Click once to start the line, click again to end it. The line appears on the canvas.

8. To draw a circle: in the same toolbar, click `Circle`. Click once to place the centre, then drag to set the radius and click again.

9. To draw a rectangle: in the same toolbar, click `Rectangle`. Click two opposite corners.

10. When you have finished drawing, press `Escape` or click the green tick button (Finish) in the toolbar. You return to the regular selection mode.

> **Stuck?** If you cannot find the `Symbolic Lines` button, look carefully at the `Create` tab ribbon. The `Detail` panel group is towards the right side of the ribbon, after the `Datum`, `Forms`, and `Work Plane` groups. The buttons in the Detail panel are `Component`, `Symbol`, `Masking Region`, `Filled Region`, `Detail Line`, and `Symbolic Lines` — in roughly that order.

**Setting line weights:** After drawing, select the line. In the Properties palette, look for `Subcategory`. Line weights in Revit families are set on the subcategory, not on individual lines. If your subcategory has line weight 2 (approximately 0.18 mm at printed scale), all lines on that subcategory will print at that weight.

To change the line weight for a subcategory: `Manage` tab → `Object Styles` → find the subcategory in the list → change the `Line Weight: Projection` number.

**The "Visible in 3D Views" checkbox:** When a line is selected, the Properties palette shows `Visible: [checkbox]`. By default, Symbolic Lines are NOT visible in 3D views — only in plan, section, and elevation views. This is the correct behaviour for annotation symbols. Leave the checkbox alone.

### Adding Filled Regions — Solid Shapes

Some symbols need a solid filled shape rather than just outline lines. For example, a fire alarm manual call point may have a solid red square. A gas cylinder symbol may have a grey filled circle.

A Filled Region is a closed 2D boundary with a fill pattern applied inside it.

1. `Create` tab → in the `Detail` panel → click `Filled Region`.

2. A new toolbar appears. In the `Properties` palette, you will see `Filled Region Type` — click the dropdown to choose the fill pattern. `Solid Fill` is the most common for symbols.

3. Draw the boundary of the filled region using the line tools in the toolbar. The boundary must be a fully closed loop — every line must connect to another at both ends with no gaps.

4. When the boundary is closed, click the green tick button to finish. The filled region appears on the canvas.

5. To change the fill pattern colour, you need to do it through the subcategory (same as line colour — set it on the subcategory, not on individual elements).

> **Stuck?** If Revit says "The boundary is not closed" when you click Finish, you have at least one gap between your lines. Click Cancel, then zoom in very close to where you think the lines meet. Use Trim/Extend (in the Modify tab, Geometry panel) to force the lines to meet exactly.

### Setting Up Parameters — The Fields on the Form

Parameters are the information stored inside a family. Think of them as the fields on a form. A family for a socket outlet might have parameters for: mounting height, gang count, IP rating, circuit number, STING tag.

There are two kinds of parameters and the distinction is critical:

**Family Parameters** exist only inside the family file. They cannot be scheduled in a Revit project, cannot be read by external tools, and cannot be shared across multiple families. Think of them as a private note on a sticky label — only visible if you open the specific family to look.

**Shared Parameters** are defined in an external text file (the "shared parameter file") and can be used across many different families and across projects. They can be scheduled, exported to COBie, read by STING's tagging pipeline, and exported to Excel. Think of them as a column in a shared spreadsheet — the same column appears in many different tables.

For MEP symbols that will be used with STING, always use Shared Parameters for any data that STING needs to read. Family Parameters are only for internal family behaviour (like "is the IEC variant visible").

**To add a Family Parameter:**

1. `Create` tab → in the `Properties` panel → click `Family Types`. A dialog opens showing all the types and parameters in the family.

2. Click `Add Parameter...` at the bottom.

3. The "Parameter Properties" dialog opens. In the top section, choose `Family parameter` (not `Shared parameter`).

4. Give it a Name, choose a Discipline (Common, Electrical, HVAC, etc.), choose a Type of Parameter (Yes/No, Text, Length, etc.), and choose whether it is a Type parameter or Instance parameter.

5. Click `OK`. The parameter appears in the Family Types dialog.

**To add a Shared Parameter (the right way for STING):**

1. First, make sure the STING shared parameter file is set as the current shared parameter file:
   - `Manage` tab → `Settings` panel → `Shared Parameters`.
   - In the dialog that opens, click `Browse...` and navigate to `StingTools/Data/MR_PARAMETERS.txt`.
   - Click `Open`, then `OK`.

2. Now go to `Create` tab → `Family Types` → `Add Parameter...`.

3. In the Parameter Properties dialog, choose `Shared parameter`.

4. Click `Select...`. The "Shared Parameters" dialog opens. It shows the groups and parameters from `MR_PARAMETERS.txt`.

5. Click on the group that matches your discipline (e.g. `ELC_` for electrical, `HVC_` for HVAC).

6. Find and select the parameter you need (e.g. `ELC_CIRCUIT_NUM_TXT`).

7. Click `OK`. Click `OK` again to close Parameter Properties. The parameter now appears in the family and can be read by STING.

**Linking a parameter to a label on the symbol:**

Some symbols need to display a value from a parameter directly on the drawing — for example, a controls sensor displays the letter "T" for temperature. In a family, this is done with a Label.

1. `Create` tab → in the `Text` panel → click `Label`.

2. Click on the canvas where you want the label to appear.

3. The "Edit Label" dialog opens. On the left is a list of all available parameters. On the right is the label content.

4. Find the parameter you want to display (e.g. `DEVICE_TYPE_TXT`), click it, then click the arrow in the middle to move it to the right side.

5. You can also add a prefix or suffix text in the columns on the right.

6. Click `OK`. The label appears on the canvas showing the parameter value (or a placeholder if no value is set).

7. You can change the text size, font, and style by selecting the label and editing its Properties.

### Saving and Loading the Family

**To save the family:**

1. `File → Save As → Family`.
2. Navigate to the correct folder. For STING symbol families:
   - Gas symbols: `CompiledPlugin/Data/SymbolLibrary/Gas/`
   - Controls symbols: `CompiledPlugin/Data/SymbolLibrary/Controls/`
   - Lighting symbols: `CompiledPlugin/Data/SymbolLibrary/Lighting/`
   - Public Health: `CompiledPlugin/Data/SymbolLibrary/PublicHealth/`
   - Comms/LV: `CompiledPlugin/Data/SymbolLibrary/Comms/`
   - Fire Protection: `CompiledPlugin/Data/SymbolLibrary/FireProtection/`
   - Plumbing: `CompiledPlugin/Data/SymbolLibrary/Plumbing/`
   - Electrical: `CompiledPlugin/Data/SymbolLibrary/Electrical/`
   - HVAC: `CompiledPlugin/Data/SymbolLibrary/HVAC/`
3. Name the file following the STING convention: `STING_[DISC]_[symbol_id].rfa`. For example: `STING_E_E_SKT_13A_1.rfa` for a UK 13A socket outlet electrical symbol.
4. Click `Save`.

**To load the family into a Revit project:**

1. In the Revit project (not the Family Editor), go to the `Insert` tab.
2. In the `Load from Library` panel, click `Load Family`.
3. Navigate to the `.rfa` file you saved.
4. Click `Open`. The family is now available in the project.

Alternatively, with the family open in the Family Editor:

1. `Create` tab → in the `Family Editor` panel → click `Load into Project`.
2. If multiple projects are open, a dialog asks which project to load into.
3. The family loads into the selected project.

> **Stuck?** After loading, if you cannot find the family in the Project Browser, look under `Families → [Category name]`. If the category name is unexpected, you may have used the wrong family template. For annotation families (Generic Annotation), they appear under `Families → Annotation Symbols`.

---

## 1.3 Standard MEP Symbol Morphology by Discipline

STING's library covers 516 symbols across 9 disciplines. Each discipline follows specific published standards and has established visual conventions (called "morphology" — the shape and form of the symbols).

### Gas (21 symbols)

**Standards:** ISO 14617-2/8 (international P&ID), BS 1553-1 (UK gas), ASME B31.8 (US distribution).

**When to use which standard:** UK domestic and commercial projects follow BS 1553-1. International and oil/gas projects use ISO 14617. US projects use ASME B31.8.

**Visual conventions:** Gas symbols carry a yellow highlight per BS 1710 colour coding. Small text labels ('G', 'NG', 'LPG') identify the gas type. Valves use the international P&ID two-triangle body with valve-specific markers:
- Spring line = pressure relief valve
- Fusible link = emergency shutoff valve (ESV)
- 'E' = ESV for gas
- Solid disc = ball valve

| Symbol group | Count | Shape |
|---|---|---|
| Pipes | 3 | Line types only — not families |
| Meters | 2 | 6-8 mm rectangles, centred origin |
| Regulators/valves | 7 | Two-triangle P&ID body + identifier |
| Gas train assembly | 1 | 20 × 8 mm compound symbol |
| Equipment connections | 3 | Wall-hosted stubs |
| Manifold/riser/drain/cylinder | 5 | Individual shapes |

**Tip for UK projects:** Note that medical gases (oxygen, nitrous oxide, medical air, vacuum) are NOT in the Gas discipline — they live in the Plumbing discipline under HTM 02-01 references, to prevent confusion between fuel gas and medical gas on drawings.

### Controls / BMS (32 symbols)

**Standards:** ISO 14617-6 / BS EN ISO 14617-6 (P&ID instrumentation), ASHRAE Guideline 4 (US HVAC controls). Fieldbus protocols: BACnet (ISO 16484-5), Modbus (IEC 61158), KNX (ISO 22510).

**Visual conventions:** Controls symbols use a consistent letter-in-shape language used worldwide:
- Sensors: small circle Ø4-5 mm with one identifying letter (T=temperature, H=humidity, P=pressure, F=flow, L=level, CO2=carbon dioxide)
- Actuators: the host valve or damper symbol with a small 'M' box above it indicating a motorised actuator
- Controllers: labelled rectangles (DDC for Direct Digital Control, PLC, HMI)
- Fieldbus cables: dashed lines labelled with the protocol name

**Tip:** Controls symbols are largely standard-agnostic in practice. The same circle-with-letter convention works across all three jurisdictions. What changes is the sensor letter codes (ISA-style PIC-100 vs. simple 'P') and the fieldbus labelling.

### Lighting (31 symbols)

**Standards:** IEC 60617-11-02 / BS EN 60617-11 (luminaire symbols), NFPA 170 (US life-safety), ISO 7010 E001 (running-man exit sign — identical across all standards).

**Emergency lighting:** BS EN 60598-2-22 and BS 5266 (UK/EU), UL 924 (US).

**Visual conventions:**
- Luminaires: circles or rectangles with a cross or dot indicating the light source position
- Emergency variant: same shape with shaded half-circle or 'M' (maintained) / 'NM' (non-maintained) label
- Switches: angled line meeting a circle, with dots indicating the number of ways (1-way, 2-way, intermediate), with added dimmer arrow for dimmer switches
- Exit signs: ISO 7010 running-man pictogram — the same across all standards

**Important note:** Revit already ships many lighting fixture families with built-in geometry. The STING library adds the IEC/BS/ANSI plan-view annotation overlay on top of Revit's existing families, rather than replacing them.

### Public Health / Drainage (40 symbols)

**Standards:** ISO 4067-1 (international piping), BS EN 752 (drainage outside buildings), BS 5572 (sanitary pipework), BS 8301 (building drainage), ASPE 45 (US), BS 8582 (SuDS).

**Visual conventions:** Drainage symbols emphasise flow direction, trap types, and access points:
- Gullies: small square or rectangular footprints with grate markings
- Traps: shown as P-trap, S-trap, bottle trap, or running trap with the correct bend
- Access points: rodding eye (small circle with diagonal line), inspection chamber, manhole (larger rectangle with cover lines)
- Rainwater: circles for rainwater outlets, rectangles for hoppers
- SuDS (Sustainable Drainage): stippled fills indicate permeable surfaces; dashed outlines for below-ground attenuation

Note on SuDS symbols: these operate at larger scales (1:100 to 1:500) and use a separate subcategory `ISO_Symbols_PH_SuDS_*` to allow them to be shown or hidden independently of drainage symbols.

### Communications / Low Voltage (39 symbols)

**Standards:** IEC 60617-11 / BS EN 60617-11 (comms outlets), TIA-606 (US cabling labelling), TIA-568 (US cabling performance), BS EN 50173 (structured cabling UK/EU), BS 5839-9 (fire telephones), HTM 08-03 (nurse call).

**Visual conventions:**
- Data outlets: triangles with numbers inside (1 = single RJ45, 2 = double, 4 = quad)
- Comms rooms: rectangles labelled RACK or WR
- CCTV: dome (circle with dome shape), bullet (rectangle), PTZ (rectangle with rotation arc)
- PA speakers: traditional speaker horn icon
- Nurse call: cross icon with call button

### Fire Protection (52 symbols)

**Standards:** ISO 6790 (detection/suppression symbols), BS EN 54 series (detection devices), BS EN 12845 (sprinklers), BS 5839 (detection and warning), NFPA 170/13/14/15/2001 (US). Gaseous agents: ISO 14520-9 (FM-200), ISO 6183 (CO2).

**This discipline has the highest life-safety stakes — invest time in getting the symbols right.**

**Visual conventions:**
- Detection: square or circle Ø5-6 mm with a single letter (S=smoke, H=heat, M=multi-sensor, CO=carbon monoxide, V=video smoke)
- Manual call points: squares with 'MCP' text or break-glass icon
- Sounders: bell symbol; beacons: triangle (VAD per BS EN 54-23, with category C/W/O)
- Sprinklers: Ø4 mm circles with a small triangle indicating orientation — pointing down for pendant, up for upright, sideways for sidewall
- Extinguishers: vertical ovals with colour-coded letters per BS EN 3-7 (red body, agent-specific colour strip)
- Gaseous suppression nozzles: Ø5 mm circles on ceiling

### Plumbing (81 symbols)

**Standards:** ISO 4067-1/6 (piping P&IDs, fluid colour-coding), BS 5572, BS 8558, BS 6700 (UK), ASME A112.19.2, ASPE 45 (US). Medical gases: ISO 7396-1 / BS EN ISO 7396-1 / NFPA 99.

**The three half-days of build work (by complexity):**

*Sanitaryware (23 rows):* WC, urinal, basin, bath, shower, bidet, sink. Most use plan-view "footprint" shapes — the shape you would see if you looked straight down at the fixture. The insertion point (origin) sits at the trap or at the fixture's geometric centre.

*Service pipes (13 rows + 2 fittings):* These are mostly line types and labels in the project, not individual families. CWS (cold water supply), HWS (hot water supply), LTHW (low temperature hot water) and others are identified by colour-coded lines per BS 1710.

*Valves, pumps, tanks, accessories (41 rows):* All valves use the international two-triangle P&ID body. Six base valve shapes cover all variants:
- Gate valve: lines through the body
- Globe valve: filled body
- Ball valve: solid circle in the body
- Butterfly valve: disc line through the body
- Check/non-return valve: triangle pointing in flow direction
- Pressure relief valve: spring symbol above body

### Electrical (101 symbols)

**Standards:** IEC 60617 series / BS EN 60617 (UK/EU), IEEE 315 / ANSI Y32.9 / NEMA (US), AS/NZS 3000 (AU/NZ).

**The most jurisdiction-sensitive discipline.** Socket outlet morphology differs between standards:
- UK (BS 1363): circle with two slots and earth, usually with safety shutters shown
- EU Schuko (CEE 7/4): circle with two round pins and earth clips
- US NEMA 5-15R: circle with two vertical slots and earth
- AU/NZ: circle with angled slots

| Symbol group | Rows | Typical geometry |
|---|---|---|
| Sockets/outlets | 15 | Circles with jurisdiction-specific pin/slot arrangement |
| FCUs, isolators, spurs | 5 | Small wall-hosted rectangles |
| Control devices | 5 | Emergency stop, push-start, key switch, pull cord |
| Floor boxes / wall management | 10 | Rectangles, often with port count label |
| Distribution boards / switchgear | 6 | Larger rectangles with 'DB', 'MSB', 'CU' labels |
| Transformers, UPS, generators | 8 | Visually distinct labelled shapes |
| Protection devices | 12 | MCB, RCD, RCBO — IEC thermal+magnetic symbol |
| Motors and starters | 6 | Circle with 'M' or 'G' inside |
| Cabling and containment | 12 | Mostly line styles: trunking, tray, conduit |
| Lightning and renewables | 10 | Air rod, PV module, EV charger — unique shapes |

### HVAC / Mechanical (120 symbols)

**Standards:** ISO 14617-8 (fluid-power and HVAC P&IDs), BS 1553-1 / BS EN 1505 (ductwork), ASHRAE Fundamentals, SMACNA (US). Fire and smoke dampers: BS EN 15650, UL 555/555S.

**The largest discipline — plan three working days for a complete build.**

**Visual conventions:**
- Air terminals: squares with arrows radiating outward (supply, 4-way diffuser), or double lines (extract/return grille), or slot line (linear slot diffuser)
- Dampers: rectangle with a blade line inside, labelled VCD (volume control), FD (fire), FSD (fire/smoke), SD (smoke)
- AHU/FCU: labelled rectangles with schematic icons — fan wheel, heating coil (parallel lines), cooling coil (zigzag), filter (dotted lines)
- Radiators: rectangle with wavy heat lines; TRV (thermostatic radiator valve): radiator with a valve cap symbol

---

## 1.4 Wire Annotation — Complete Workflow

### 1.4.1 What is Wire Annotation?

Wire annotation is the layer of text and graphical marks on an electrical floor plan drawing that tells an electrician how to wire up the circuits they see.

Imagine you are looking at an electrical drawing of an office floor. You can see symbols for socket outlets, light switches, distribution boards, and luminaires. But how does the electrician know:

- Which sockets are on the same circuit?
- What size cable (2.5 mm² twin and earth? 4 mm² SWA?) connects them?
- Which circuit number in the distribution board does this group connect to?
- Where does the circuit enter the floor (a "homerun" back to the board)?

Wire annotation answers all of those questions. It consists of:

1. **Wire lines** drawn on the drawing to show circuit routes (not physical cables, but schematic routes).
2. **Slash marks** crossing wire lines to show the number of conductors in a cable (one diagonal slash per conductor — so a 2.5 mm² twin and earth cable gets two slashes, a three-core gets three).
3. **Circuit reference labels** showing which circuit number from the distribution board feeds each group.
4. **Cable size labels** showing the conductor cross-sectional area (e.g. "2.5mm²", "4mm²", "10mm²").
5. **Homerun arrows** — a special arrowhead on the wire line pointing toward the distribution board, indicating "this is where the circuit goes up or away to the panel".

Wire annotation is used on:
- Domestic electrical drawings
- Commercial lighting and power layouts
- Industrial electrical distribution layouts
- Any drawing where individual circuit routing matters

### 1.4.2 Revit's Wire Tool vs STING Wire Annotation

Revit has a built-in wire tool under the `Systems → Electrical → Wire` command. It draws schematic wire connections between electrical elements. However, it has limitations:

- Revit's native wires are part of the electrical system connectivity — they drive calculations like voltage drop and circuit load. This is useful but means they require proper electrical system connections to work.
- Native wires are difficult to control for precise graphical annotation (line weight, slash marks, circuit labels all need separate work).
- Native wires appear in 3D views in a way that can clutter coordination models.

STING's wire annotation approach uses annotation families — the same Generic Annotation family type used for MEP symbols — to create a clean, drafting-quality wire annotation layer that sits on the drawing independently of the electrical system model. This gives precise graphical control while keeping the coordination model clean.

You can use both approaches on the same project: Revit's native wires for system connectivity and load calculations, and STING annotation families for the polished, contractor-readable drawing deliverable.

### 1.4.3 Creating a Wire Annotation Family in the Family Editor

This section walks through building a wire annotation family from scratch, step by step.

**Before you start:** make sure STING's shared parameter file is set. See Section 1.2 above for how to do this (Manage → Settings → Shared Parameters → Browse to `StingTools/Data/MR_PARAMETERS.txt`).

**Step 1: Open the Family Editor with the correct template**

1. `File → New → Family`.
2. In the template browser, navigate to the `English` folder.
3. Find and select `Generic Annotation.rft`.
4. Click `Open`.

The Family Editor opens. You see a plain canvas with two reference planes crossing at the centre. This template is specifically designed for annotation families — things that appear on drawings but are not 3D objects.

**Step 2: Plan what your annotation will show**

Before drawing anything, decide which type of wire annotation you are making. Common types:

- **STING_WIRE_ANN_HOMERUN.rfa** — for circuit homeruns (the arrow that indicates the circuit exits the plan to the distribution board)
- **STING_WIRE_ANN_BRANCH.rfa** — for branch circuit runs (the wire line with slash marks for conductor count)
- **STING_WIRE_ANN_3WIRE.rfa** — for 3-wire circuits specifically (three slashes)
- **STING_WIRE_ANN_LABEL.rfa** — for circuit number labels only (no wire line, just text)

This walkthrough builds `STING_WIRE_ANN_HOMERUN.rfa`.

**Step 3: Save immediately with the correct name**

Before drawing anything, save the file with the correct name. This avoids saving over the template accidentally.

1. `File → Save As → Family`.
2. Navigate to `Families/Annotations/Wire/` (create this folder if it doesn't exist).
3. In the File name box, type `STING_WIRE_ANN_HOMERUN`.
4. Click `Save`.

**Step 4: Create the subcategories**

Wire annotation families use subcategories to allow project-wide visibility control. You will create one subcategory called `STING_Wire_Annotation`.

1. `Manage` tab → in the `Settings` panel → click `Object Styles`.
2. The Object Styles dialog opens. Make sure you are on the `Annotation Objects` tab (not Model Objects).
3. Click `New...` at the bottom of the list.
4. In the "New Subcategory" dialog, type `STING_Wire_Annotation` in the Name box.
5. The Parent Category should be `Generic Annotations`. Click `OK`.
6. The new subcategory appears in the list. Set `Line Weight: Projection = 2`, `Line Color = Black`, `Line Pattern = Solid`.
7. Click `OK` to close Object Styles.

**Step 5: Draw the wire line**

The wire line represents the cable route on the drawing.

1. `Create` tab → `Detail` panel → `Symbolic Lines`.
2. In the Properties palette, set `Subcategory: STING_Wire_Annotation`.
3. Choose `Line` in the drawing toolbar.
4. Click at a point to the LEFT of the origin (the cross in the centre of the canvas). Click at a point to the RIGHT of the origin. You have drawn a horizontal wire line passing through the origin.

The line should extend about 15-20 mm on each side of the origin at typical annotation size (the exact length matters less than the origin being on the line).

5. Press `Escape` to finish drawing.

> **Stuck?** The origin is at the intersection of the two reference planes (the pink cross). If your line does not pass through the origin, the annotation will not align correctly to the circuit element when placed. Undo, zoom in, and redraw more carefully — snap to the reference plane intersection.

**Step 6: Add the homerun arrowhead**

A homerun arrowhead is a filled arrow at one end of the wire line, pointing toward the distribution board. In practice, you place the annotation at the point where the circuit exits the drawing, and the arrow points in the direction the circuit travels to reach the board.

1. Still in the Symbolic Lines drawing mode (or click `Symbolic Lines` again in the Create tab).
2. In the Properties palette: `Subcategory: STING_Wire_Annotation`.
3. Look at the drawing toolbar at the top. There is a `Line Style` dropdown and a row of buttons for different line and endpoint types. Find the endpoint style controls — these are small dropdowns showing arrows and dots for line ends.
4. Set the endpoint style to `Filled Arrow` for the left endpoint (the end pointing toward the board).
5. Draw a short arrow line (about 3-4 mm) pointing left from the start of the wire line. This is the homerun indicator.

Alternatively, draw the arrowhead as a separate filled region:

1. `Create` → `Filled Region`.
2. In the Properties palette, choose `Solid Fill` as the filled region type.
3. Draw a small filled triangle (3-4 mm high, 2 mm wide) at the left end of the wire line.
4. Click the green tick to finish.

**Step 7: Add conductor slash marks**

Slash marks are short diagonal lines crossing the wire line. Convention: one slash per conductor. For a standard twin-and-earth cable (live, neutral, earth): three slashes. For a twin cable (live and neutral, no earth): two slashes.

1. `Create` tab → `Symbolic Lines`.
2. `Subcategory: STING_Wire_Annotation`.
3. Draw a short diagonal line (about 5 mm long at 45 degrees) crossing the wire line. Position it 5-7 mm to the right of the origin.
4. For a homerun family representing a standard 3-core circuit, draw two more slash marks alongside the first, spaced 1.5 mm apart.

> **Note on count:** If you want the slash count to be variable (1, 2, 3, or 4 conductors), you need to build four separate families (one per count), or use a more advanced approach with visibility parameters that show/hide individual slash marks per count. For most projects, building three standard families (2-wire, 3-wire, 4-wire) is sufficient.

**Step 8: Add the circuit number label**

The circuit number label shows which circuit in the distribution board feeds this wire run.

1. `Create` tab → `Text` panel → `Label`.
2. Click on the canvas just ABOVE the wire line, near the centre. A label cursor appears.
3. The "Edit Label" dialog opens. On the left, you see a list of available parameters — if you have not yet added `ELC_CIRCUIT_NUM_TXT` to the family, the list will be empty or show only built-in parameters.

**First, add the shared parameter to the family:**

Close the Edit Label dialog by clicking Cancel. Then:

1. `Create` tab → `Properties` panel → `Family Types`.
2. In the Family Types dialog, click `Add Parameter...`.
3. In Parameter Properties, choose `Shared parameter`.
4. Click `Select...`.
5. In the Shared Parameters dialog, navigate to the `ELC_` group (Electrical parameters).
6. Find `ELC_CIRCUIT_NUM_TXT` and click `OK`.
7. Back in Parameter Properties: set Group = `Identity Data`, and choose `Instance` (so each placed annotation can have its own circuit number).
8. Click `OK`. The parameter now appears in Family Types.

Now add the label:

1. `Create` tab → `Text` panel → `Label`.
2. Click above the wire line.
3. The Edit Label dialog now shows `ELC_CIRCUIT_NUM_TXT` in the left-side parameter list.
4. Click on `ELC_CIRCUIT_NUM_TXT` in the left list.
5. Click the middle arrow button (pointing right) to add it to the label content on the right.
6. In the `Prefix` column, you can optionally type `L` (for luminaire circuits) or `P` (for power circuits) to prefix the circuit number automatically. Leave blank for no prefix.
7. Click `OK`.

The label appears on the canvas. When the annotation is placed in a project and the parameter is filled in (e.g. "1.1" for circuit 1, sub-circuit 1), the label will display `1.1` on the drawing.

**Styling the label:** Select the label. In the Properties palette:
- Set `Type` to a suitable text type (small annotation text, approximately 1.5-2.0 mm at the typical 1:50 scale).
- Check that `Horizontal Align: Center` so it centres over the wire line.

**Step 9: Add the cable gauge label**

The cable gauge label shows the conductor size in mm².

1. Repeat the parameter-adding process for `ELC_WIRE_GAUGE_TXT` (in the `ELC_` group of STING shared parameters). If this parameter does not exist in the shared parameter file, use a family parameter named `Wire_Gauge_TXT` instead for now.

2. Add a second `Label` below the wire line (so the circuit number is above the wire, the gauge is below).

3. In the Edit Label dialog, add `ELC_WIRE_GAUGE_TXT` with a suffix of `mm²` in the `Suffix` column.

4. Click `OK`.

The finished annotation should look like this on the canvas (schematically):

```
         L1.1
 ←───── ////── ─────
         2.5mm²
```

Where `///` represents the three conductor slash marks.

**Step 10: Test in the Family Types dialog**

Before saving, test that the labels work:

1. `Create` tab → `Family Types`.
2. In the Family Types dialog, find `ELC_CIRCUIT_NUM_TXT` and type a test value: `L1.1`.
3. Find `ELC_WIRE_GAUGE_TXT` and type: `2.5`.
4. Click `Apply`.
5. Look at the canvas. The label above the wire line should now show `L1.1` and the label below should show `2.5mm²`.
6. If they update correctly, click `OK`.

> **Stuck?** If the labels do not update when you change the parameter value, make sure the label is linked to the parameter (not just displaying static text). Double-click the label on the canvas to open Edit Label and verify that `ELC_CIRCUIT_NUM_TXT` appears in the right-side content panel.

**Step 11: Save the family**

1. `File → Save` (or Ctrl+S).

The file is saved to `Families/Annotations/Wire/STING_WIRE_ANN_HOMERUN.rfa`.

### 1.4.4 Loading Wire Annotation Families into a Project

1. Open the Revit project.
2. `Insert` tab → `Load from Library` panel → `Load Family`.
3. Navigate to `Families/Annotations/Wire/`.
4. Select `STING_WIRE_ANN_HOMERUN.rfa` (and any other wire annotation families you have built).
5. Click `Open`.

The families are now loaded into the project. You can verify this in the Project Browser: expand `Families → Annotation Symbols → STING_WIRE_ANN_HOMERUN`.

### 1.4.5 Placing Wire Annotations on a Drawing

Wire annotation families are placed using the Symbol tool, not the Tag tool.

1. Go to an electrical floor plan view (a floor plan with the Electrical discipline setting, showing socket outlets, luminaires, etc.).

2. `Annotate` tab → `Detail` panel → `Symbol`.

3. In the ribbon, a `Type Selector` dropdown appears (usually at the top left of the screen, showing the current family and type). Click the dropdown and find `STING_WIRE_ANN_HOMERUN`.

4. Click on the drawing near the point where the circuit exits toward the panel (typically near the edge of the plan, or near the distribution board symbol).

5. Press `Escape` to stop placing.

6. The annotation appears. It shows placeholder text for the circuit number and gauge (`<ELC_CIRCUIT_NUM_TXT>` and `<ELC_WIRE_GAUGE_TXT>`) until the parameters are filled in.

**Filling in the parameters:**

1. Select the placed annotation.
2. In the Properties palette on the left, find `ELC_CIRCUIT_NUM_TXT` and type the circuit reference (e.g. `L1.1`).
3. Find `ELC_WIRE_GAUGE_TXT` and type the cable size (e.g. `2.5`).
4. The labels on the canvas update immediately.

> **Stuck?** If you cannot find `ELC_CIRCUIT_NUM_TXT` in the Properties palette, the shared parameter was not added correctly to the family, or the family was not loaded with the shared parameter bound. Go back to the Family Editor, check that the parameter is a Shared Parameter (not a Family Parameter), re-save the family, and reload it into the project.

### 1.4.6 Editing Wire Annotations After Placement

**To change the circuit number:** Select the annotation in the drawing. In the Properties palette, edit `ELC_CIRCUIT_NUM_TXT` directly.

**To change the cable gauge:** Same process — edit `ELC_WIRE_GAUGE_TXT` in the Properties palette.

**To move the annotation:** Click and drag it. The annotation is a free-standing element (not linked to a specific host element), so it moves wherever you drag it.

**To rotate the annotation:** Select it, then use the rotation handle (blue circle with rotation arrow) that appears at the top of the selection box when it is selected. Drag the rotation handle to orient the wire line in the correct direction for the circuit route.

**To delete the annotation:** Select it and press `Delete`. This only removes the annotation symbol from the drawing — it does not affect any electrical elements in the model.

### 1.4.7 STING's Wire Annotation in the Electrical Workflow

Wire annotations link to the broader STING electrical workflow in several ways:

**Panel schedules:** When you run the `Panel_BatchSchedules` command (STING → BIM tab → Panels), STING creates panel schedule views and fills in circuit data including back-references to the circuit numbers. The circuit numbers in your wire annotation families (stored in `ELC_CIRCUIT_NUM_TXT`) should match the circuit IDs in the panel schedule.

**Automatic circuit assignment:** If your electrical fixtures already have `ELC_CIRCUIT_NUM_TXT` filled in through STING's auto-tagging pipeline (the `RunFullPipeline` command that writes all STING parameters), wire annotations placed near those fixtures can be manually updated to match, or — if you author the wire annotation to read from the host element rather than storing its own value — they update automatically.

**Validation:** Running `ValidateTagsCommand` (STING → Tags → Validate) flags any electrical elements where `ELC_CIRCUIT_NUM_TXT` is empty. This report helps you identify where wire annotations are needed and where circuit assignment is incomplete.

**COBie export:** When STING exports a COBie handover spreadsheet, it reads `ELC_CIRCUIT_NUM_TXT` from electrical elements to populate the Component sheet's circuit reference column.

### 1.4.8 Common Wire Annotation Mistakes

**Placing the annotation on a non-electrical element:** If you click on a generic model family (a table, a partition) instead of an empty space in the drawing, the annotation may attach to that element. If the element is deleted later, the annotation may disappear or move unexpectedly. Always place wire annotations in open space, not on top of model elements.

**Not loading the shared parameter file first:** If `ELC_CIRCUIT_NUM_TXT` does not appear in the Family Types parameter list when you are building the family, the STING shared parameter file is not set. Go to `Manage → Settings → Shared Parameters → Browse` and point it to `StingTools/Data/MR_PARAMETERS.txt` before adding any parameters.

**Placing in a 3D view:** Generic Annotation families are not visible in 3D views. If you switch to a 3D or perspective view and your wire annotations disappear, this is normal and expected. They will reappear when you switch back to a plan or elevation view.

**Drawing the wire line not passing through the origin:** If the annotation rotates and jumps to a wrong position when you rotate it in the project, the wire line probably does not pass through the origin. Return to the Family Editor, check that the wire line intersects the reference plane crosshairs, and re-save.

**Using a Family Parameter instead of a Shared Parameter for the circuit number:** Family parameters cannot be seen in the project's Properties palette and cannot be scheduled. Always use Shared Parameters from `MR_PARAMETERS.txt` for any data that needs to be visible, schedulable, or readable by STING.

---

# Chapter 2 — Placement Family Authoring

---

## 2.1 What Does "Placement-Ready" Mean?

STING's Placement Centre can automatically place hundreds of families across a building in seconds. But it can only do this if each family "speaks STING's language" — that is, each family is set up correctly so the engine knows:

- Exactly where to grab the family (the origin / insertion point)
- Which direction is "front" (orientation)
- What parameters to fill in after placing (STING shared parameters)
- Which variant of the family to use for each rule

A family that is NOT placement-ready will either be skipped by the engine with a warning ("no FamilySymbol found") or placed in the wrong position (origin in the wrong place means the fixture sits inside the wall, or floats above the ceiling).

Think of the Placement Centre as a robotic chef following a recipe. The recipe says "take one socket outlet, place it 300 mm from the door, 1100 mm above the floor, facing into the room." The robot knows the dimensions, but it needs to know which end of the socket outlet is the "front face" and which end goes into the wall. If the family isn't oriented correctly, the robot places it facing backward. That is what "placement-ready" means — the family is set up so the robot can orient it correctly.

---

## 2.2 The Three Universal Rules Every Family Must Follow

These three rules apply to every single family that will be used with STING's automated placement. There are no exceptions.

### Rule 1: The Insertion Point (Origin) Must Be at the Correct Location

Every family has an origin — the point that Revit snaps to the exact location where you click when placing the family. The Placement Centre uses the origin as its placement point.

For different types of equipment, the origin goes in different places:

- **Wall-mounted fixtures (sockets, switches, data outlets):** The origin sits at the fixing-centre midpoint of the front face (the faceplate plane). NOT at the back of the back box. NOT at the geometric centre of the complete assembly.

- **Ceiling-mounted fixtures (downlights, smoke detectors, sprinklers):** The origin sits at the point where the fixture meets the ceiling — the visible face of the fitting.

- **Floor-mounted fixtures (shower trays, floor drains):** The origin sits at the geometric centre of the footprint at floor level.

- **Wall-hung sanitary fixtures (basins, WCs):** The origin sits at the centre of the fixture footprint, not the bowl or the basin centre.

An analogy: think of the origin as a drawing pin you push through a map to stick it to a board. Revit places the family pin-first. If the pin is at the back of the box (25 mm behind the wall face), the socket will sit 25 mm inside the wall — invisible and wrong. If the pin is at the correct face position, the socket sits flush with the wall surface.

**Common mistake:** Many families downloaded from manufacturer websites or generic libraries have the origin at the geometric centre of the 3D solid — the middle of the box, not the faceplate. You MUST check and correct this before using the family with STING's Placement Centre.

**How to fix a wrong origin:** Open the family in the Family Editor. Switch to a front elevation view. Move the model geometry so that the faceplate plane aligns exactly with the vertical reference plane. The vertical reference plane is the origin line.

### Rule 2: Reference Planes Must Be Named Correctly

Reference planes in the Family Editor are used by the Placement Centre to understand the family's orientation. Specifically, the plane that represents the wall face (for wall-mounted fixtures) must be named correctly and configured correctly.

The required reference plane name is `Center (Front/Back)`.

Why this name? The Placement Centre's auto-orientation code (`OrientPlacedInstance` in the engine) looks for a reference plane with this name to determine which direction is "outward into the room" — so it can flip the family to face the correct direction.

**To check and fix the reference plane configuration:**

1. Open the family in the Family Editor.
2. Switch to a plan view.
3. You should see two reference planes: one horizontal (the `Center (Left/Right)` plane) and one vertical (the `Center (Front/Back)` plane).
4. Click on the vertical reference plane (the one running from top to bottom in plan view — this represents the wall face for a wall-mounted fixture).
5. In the Properties palette:
   - `Name:` should read `Center (Front/Back)`. If it reads anything else, click in the Name field and type the correct name.
   - `Is Reference:` set to `Strong Reference`.
   - `Defines Origin:` ticked.
6. Save the family.

> **Stuck?** If you cannot see the reference plane names, look in the Properties palette while the reference plane is selected. The name appears near the top of the properties list. If the name field shows `<unnamed>`, you need to type a name in.

### Rule 3: STING Shared Parameters Must Be Bound

Without STING's shared parameters, the Placement Centre cannot write tag data to the family after placing it, cannot track the provenance of the placement, and cannot run the COBie export correctly.

The minimum required shared parameters for every family are:

| Parameter name | Type | Purpose |
|---|---|---|
| `STING_BOX_LOCATION_ID` | Text | Two-phase matching — links the second-fix fitting to the first-fix back box |
| `STING_NOGGIN_REQUIRED` | Yes/No | Flags that a structural noggin is needed for pendants and heavy fittings |
| `STING_AUTO_PLACED_BOOL` | Yes/No | The Placement Centre sets this to Yes after placing; identifies auto-placed elements |
| `STING_FIXTURE_VARIANT_TXT` | Text | The variant hint the placement rule matches against (e.g. "FLUSH", "IP65", "BASIN") |

Additionally, the catalogue parameters (`MK_BOX_DEPTH_MM`, `MK_FIXING_CENTRES_MM`, `MK_GANG_COUNT`, `MK_CATALOGUE_REF`) are needed for the manufacturer catalogue matching system.

Section 2.4 below walks through adding these parameters step by step.

---

## 2.3 Per-Category Authoring Rules

Each Revit category has its own specific requirements. Use this table to find the rules for the category you are working on:

| Category | Origin location | Required reference planes | Key STING parameters | Special notes |
|---|---|---|---|---|
| Electrical Fixtures (sockets, FCUs, cooker points) | Fixing-centre midpoint of the FRONT face (faceplate plane) | `Center (Front/Back)` = wall face; `Center (Left/Right)` for symmetry | `MK_BOX_DEPTH_MM`, `MK_FIXING_CENTRES_MM`, `MK_GANG_COUNT`, `MK_CATALOGUE_REF`, `STING_FIXTURE_VARIANT_TXT` | Set `Family Placement Type = One Level Based Hosted`. Engine auto-flips to face the room. |
| Lighting Devices (switches, dimmers, 2-way, DALI) | Faceplate midpoint | `Center (Front/Back)` = wall face | `MK_BOX_DEPTH_MM`, `STING_FIXTURE_VARIANT_TXT` ("DIMMER" / "DALI" / "2-WAY") | Set `Always Vertical = Yes` or switch lands flat on door head. Engine auto-rotates so the rocker faces into the room. |
| Lighting Fixtures (pendants, downlights, LED panels) | Visible drop centre (for pendants), flush face (for downlights) | `Ceiling` plane, `Defines Origin = Yes` | `MK_CATALOGUE_REF`, `STING_PHOTOMETRIC_LM` (lumens), `IS_INDIVIDUAL_LUMINAIRE = Yes` | Must be Ceiling-Hosted (not OneLevelBased-Unhosted) or the engine cannot find a ceiling to attach to. |
| Plumbing Fixtures (WC, basin, shower, urinal) | Centre of the FIXTURE FOOTPRINT (not the bowl centre) | `Center (Left/Right)`, `Center (Front/Back)`, both `Reference = Strong` | `IS_FIXTURE_BACK_TO_WALL = Yes`, `STING_FIXTURE_VARIANT_TXT` ("WC" / "BASIN" / "SHOWER") | Engine does NOT rotate plumbing fixtures — "facing away from wall" must be correct in the family. Room name regex filters (word-boundary matched) prevent WCs in wardrobes. |
| Mechanical Equipment (AHUs, boilers, chillers) | Equipment footprint centre at floor level | `Center (Front/Back)` for access door side | `PLACE_WEIGHT_KG`, `MNT_ENV_W_MM`, `MNT_ENV_D_MM`, `MNT_ENV_H_MM`, `STING_CLEARANCE_MM` | Large equipment uses structural pre-flight check (`PLACE_WEIGHT_KG` vs floor loading). |
| Fire Protection (MCP, sounder, beacon) | Faceplate centre | `Center (Front/Back)` = wall face | `BS5839_DEVICE_KIND` ("MCP" / "SOUNDER" / "BEACON"), `STING_FIXTURE_VARIANT_TXT` | Must be Wall-Hosted. Ceiling-hosted smoke/heat detectors use separate rules. |
| Fire Protection (smoke, heat detectors) | Sensor head face (bottom of detector) | Ceiling plane | `BS5839_DEVICE_KIND` ("SMOKE" / "HEAT"), `Coverage Radius` | Must be Ceiling-Hosted. The LightingGridCalculator checks structural fixing suitability for ceiling hosts. |
| Sprinklers | Deflector centre | Face of ceiling or soffit | `BS_5306_K_FACTOR`, `Coverage Radius`, `STING_FIXTURE_VARIANT_TXT` ("UPRIGHT" / "PENDANT" / "CONCEALED") | Ceiling-Hosted or Face-Based. Engine enforces 600 mm minimum BS 5306 separation from other sprinklers. |
| Data/Comms Devices (RJ45, HDMI outlets) | Faceplate midpoint | `Center (Front/Back)` = wall face | `STING_FIXTURE_VARIANT_TXT` ("HDMI" / "USB-C" / "RJ45"), `MK_MODULE_PITCH_MM = 25.4` | Must be the correct Revit category (Communications Devices, not Generic Model). |
| Generic Models (grab rails, miscellaneous) | Geometric centre | Standard reference planes | `STING_FIXTURE_VARIANT_TXT`, `STING_AUTO_PLACED_BOOL` | Used for accessibility items. Engine places using standard anchor types. |

---

## 2.4 Binding STING Shared Parameters to a Family

Shared parameters must be explicitly added to each family file. This is a manual process but a fast one once you know the steps.

**Step 1: Set the STING shared parameter file as the current file**

1. Open the family `.rfa` file in the Family Editor.
2. `Manage` tab → `Settings` panel → click `Shared Parameters`.
3. The "Edit Shared Parameters" dialog opens.
4. Click `Browse...` and navigate to `StingTools/Data/MR_PARAMETERS.txt`.
5. Click `Open`. The dialog now shows the parameter groups from the STING shared parameter file.
6. Click `OK` to close the dialog.

**Step 2: Add the required parameters**

1. `Create` tab → `Properties` panel → `Family Types`.
2. The Family Types dialog opens.
3. Click `Add Parameter...` at the bottom-left.
4. The "Parameter Properties" dialog opens. Choose `Shared parameter` (not `Family parameter`).
5. Click `Select...`.
6. The shared parameters dialog opens. Find the correct group for your family's category:
   - `MK_` group = MK Electric manufacturer catalogue parameters (sockets, switches, etc.)
   - `STING_` group = STING automation parameters (auto-placed bool, box location ID, etc.)
   - `ELC_` group = Electrical parameters (circuit number, wire gauge, etc.)
   - `HVC_` group = HVAC parameters
   - `PLM_` group = Plumbing parameters
   - `FLS_` group = Fire and life safety parameters
7. Click on the group, then click the specific parameter you want to add.
8. Click `OK`.
9. Back in Parameter Properties: choose whether it should be a `Type` or `Instance` parameter.
   - Most STING automation parameters (`STING_AUTO_PLACED_BOOL`, `STING_BOX_LOCATION_ID`) should be **Instance** parameters — each placed copy has its own value.
   - Catalogue parameters like `MK_BOX_DEPTH_MM` and `MK_GANG_COUNT` are typically **Type** parameters — all instances of the same type share the same depth and gang count.
10. Click `OK`. The parameter appears in the Family Types list.
11. Repeat for each required parameter.
12. Click `OK` to close the Family Types dialog.
13. `File → Save`.

**Step 3: Verify the parameters in a test project**

1. `Create` tab → `Family Editor` panel → `Load into Project`.
2. In the project, place a test instance of the family.
3. Select the placed instance.
4. In the Properties palette, look for your newly added parameters. They should appear under the relevant group heading.
5. Try typing a value into one of the parameters. It should save without error.

> **Stuck?** If a parameter you added does not appear in the project's Properties palette for the placed instance, it may have been added as a Type parameter when it should be an Instance parameter (or vice versa). Go back to the Family Editor, open Family Types, find the parameter, and check/change its Type vs Instance setting. Re-save and reload.

---

## 2.5 Testing a Family Before Production Use

Run this checklist every time you author or modify a family for use with STING:

- [ ] **Origin placement:** Place the family in a test project at multiple levels. Does it sit at the correct height and position? A socket at 300 mm AFF with a 1100 mm rule should sit with its face flush with the wall at 300 mm, not 25 mm inside the wall or floating 25 mm in front.

- [ ] **Orientation:** After placing, does the family face the correct direction? For wall-mounted fixtures, the faceplate should face into the room. If it faces the wall, the origin or the `Center (Front/Back)` reference plane is wrong.

- [ ] **Auto-tag:** Select the placed family and run the STING Auto Tag command (STING panel → SELECT tab → Auto Tag, or from the dock panel Tags/Tokens section). Does STING successfully write the DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, and SEQ tokens?

- [ ] **Validate Tags:** Run `ValidateTagsCommand` (STING panel → Tags → Validate). Are there any errors reported for this family's placed instances?

- [ ] **Shared parameters visible:** In the Properties palette for a placed instance, can you see `STING_AUTO_PLACED_BOOL`, `STING_BOX_LOCATION_ID`, `STING_FIXTURE_VARIANT_TXT`, and `MK_CATALOGUE_REF`?

- [ ] **Ceiling/wall attachment:** For hosted families (Ceiling-Based, Wall-Based), can you place the family on the correct host? Try placing a ceiling-hosted fixture and then moving the ceiling — does the fixture move with it?

- [ ] **Placement Centre test:** Open the Placement Centre, find or create a rule for this family's category, run Preview. Do the preview dots appear at the correct locations?

- [ ] **STING Placement Audit:** Run `Placement_AuditSetup` from the STING dock panel. This command cross-checks all placement requirements against the live model and writes a CSV report identifying missing parameters, wrong reference planes, and other issues.

---

# Chapter 3 — The Placement Centre

---

## 3.1 What the Placement Centre Does

The 30-second version: you write rules ("put one smoke detector every 7.5 metres in corridors per BS 5839-1"). The Placement Centre executes those rules automatically across every room in the building.

In an average UK office building, one floor needs approximately:
- 200 wall sockets
- 80 light switches
- 120 luminaires
- 40 emergency luminaires
- 30 smoke detectors
- 20 sprinklers
- 40 air diffusers

That is roughly 600 placements per floor. On a six-storey building, 3,600 placements. Doing this by hand takes weeks per revision. The Placement Centre does it in minutes — and every placement carries a provenance stamp recording which rule placed it, when, and with which standard reference.

When an inspector asks "why is this socket here?" the answer is one click away: the provenance stamp says `Rule: elec-perimeter-socket-01, Standard: Approved Doc M / BS 7671, Placed: 2026-04-25 14:30 UTC`.

---

## 3.2 Before You Start — Prerequisites

Before running the Placement Centre for the first time on a project, confirm all four prerequisites:

**1. Families must be placement-ready (Chapter 2)**

Every family you want the Centre to place must have the correct origin, correct reference planes, and STING shared parameters bound. Run `Placement_AuditSetup` to check this automatically.

**2. STING shared parameters must be loaded into the project**

The shared parameters must be bound to the Revit project, not just to individual families.

To do this: STING dock panel → SELECT tab → scroll to find `Load Shared Parameters` button → click it. STING runs through a two-pass binding process that loads all STING shared parameters from `MR_PARAMETERS.txt` into the project.

Alternatively: `Tags.LoadSharedParamsCommand` from the Tags dropdown.

> **Stuck?** If the Load Shared Parameters button asks you to confirm overwriting existing parameters, say Yes — this is safe and just ensures all parameters are bound to the current project.

**3. A placement rules file must exist or be loaded**

STING ships with a baseline rule set of approximately 43 rules (`StingTools/Data/Placement/STING_PLACEMENT_RULES.json`), plus seven additional discipline packs covering electrical, mechanical, lighting, accessibility, commissioning, and more.

On first use, the Placement Centre automatically loads the baseline rules. You do not need to do anything.

**4. The project must have Phases set up correctly**

The Placement Centre uses Revit Phases to separate first-fix work (the back boxes and conduit installed before plastering) from second-fix work (the fittings installed after plastering). You need at least two phases named `Construction` and `Handover` in the project.

To check: `Manage` tab → `Phases`. Make sure at least two phases exist with names the Centre can recognise.

---

## 3.3 The STING_PLACEMENT_RULES.json File

Placement rules are stored in JSON files — text files in a format that both humans and computers can read. You do not need to edit JSON directly; the Placement Centre has a visual editor for all rule fields. But understanding the file structure helps when you need to share rules between projects or troubleshoot unexpected behaviour.

**Where the files live:**

```
StingTools/Data/Placement/
├── STING_PLACEMENT_RULES.json                      (baseline ~43 rules)
├── STING_PLACEMENT_RULES.electrical.json           (electrical discipline pack)
├── STING_PLACEMENT_RULES.mechanical.json           (mechanical discipline pack)
├── STING_PLACEMENT_RULES.architecture.json         (architectural services pack)
├── STING_PLACEMENT_RULES.healthcare-education.json (healthcare and school rules)
├── STING_PLACEMENT_RULES.accessibility.json        (BS 8300-2 / Approved Doc M)
├── STING_PLACEMENT_RULES.commissioning.json        (T&C test points and gauges)
├── STING_PLACEMENT_RULES.medical-gases.json        (HTM 02-01 bedhead trunking)
└── STING_PLACEMENT_RULES.routing.json              (cable tray, conduit, duct riser routes)

[project folder]/
├── STING_PLACEMENT_RULES.project.json              (your project-specific overrides)
└── STING_PLACEMENT_RULES.learned.json              (output from LearnPlacementV4Command)
```

**The layering system (four layers, highest wins):**

When the Centre loads, it reads all layers in order. Rules with the same ID in a higher layer override rules in a lower layer:

1. Baseline (`STING_PLACEMENT_RULES.json`) — lowest priority
2. Discipline packs (the seven pack files above)
3. Learned rules (`STING_PLACEMENT_RULES.learned.json`) — Priority 90
4. Project overrides (`STING_PLACEMENT_RULES.project.json`) — highest priority

This means: if you save a modified version of the emergency lighting rule to the project file, YOUR version runs instead of the shipped version, on your project only.

**Anatomy of a single rule:**

Every rule answers the questions: what, where, in which rooms, how many, per which standard. Here is a simplified example of what a rule looks like in JSON:

```json
{
  "RuleId": "elec-light-emergency-route",
  "CategoryFilter": "Lighting Fixtures",
  "VariantHint": "EM",
  "RoomFilter": "(?i)\\b(corridor|lobby|stair|escape|exit)\\b",
  "AnchorType": "ESCAPE_ROUTE_CENTRELINE",
  "MountingHeightMm": 0,
  "MountingReference": "CEILING",
  "MinSpacingMm": 8000,
  "RuleKind": "Linear",
  "PerLinearMetre": 8.0,
  "MaxPerRoom": 0,
  "StandardRef": "BS 5266-1 / BS EN 1838",
  "Priority": 70
}
```

Reading this plain-English: "In rooms whose name contains corridor, lobby, stair, escape, or exit, place Lighting Fixtures with the EM (emergency) variant at ceiling height, spaced no closer than 8 metres, one every 8 linear metres of escape route centreline, per BS 5266-1."

**The 43 built-in rules cover:**

Perimeter sockets at 600 mm centres, door-hinge switches per Approved Doc M, smoke detectors per BS 5839-1, sprinklers per BS EN 12845, emergency lighting per BS 5266-1, air diffusers per BS EN 12464-1, thermostats, data outlets at desks, MCP travel distance per BS 5839-1, accessible WC grab rails per BS 8300-2, and more.

---

## 3.4 Every Button in the Placement Centre Explained

The Placement Centre opens as a floating dialog or a docked panel. It has a toolbar at the top, a rule list on the left, a rule editor on the right, and a status bar at the very bottom.

| Button label | Keyboard shortcut | What it does | When to use it |
|---|---|---|---|
| `Reload Defaults` | (none) | Discards all in-memory edits and reloads the shipped baseline rules | "I've made a mess — give me back factory settings." Will prompt before discarding unsaved edits. |
| `Import…` | (none) | Opens a file picker and merges rules from a `.json` file | Importing discipline packs from a colleague or from a project library. Rules with the same ID are skipped (not overwritten). |
| `Export…` | (none) | Writes all valid rules to a `.json` file | Sharing your rule set or backing it up. Invalid rules are skipped. |
| `Save Project` | Ctrl+S | Writes the current rule set to `[project]/STING_PLACEMENT_RULES.project.json` | Every time you edit or create a rule. Orange dot in the rule list column means unsaved. |
| `Run Placement` | Alt+R | Runs the engine — reads every rule, walks every room in scope, places real Revit family instances | When you are ready to commit placements to the model. Wraps in a single TransactionGroup (one Ctrl+Z undoes the whole batch). |
| `Preview` | Alt+P | Dry-run — shows a 3D coloured overlay of where fixtures would land, without touching the model | Before committing. Each rule gets its own colour. Press Escape to clear the overlay. |
| `Validate` | Alt+V | Runs validators against the placed elements or project-wide | After a placement run, or for a standalone audit. Eight validators available (see Section 3.10). |
| `Undo last run` | (none) | Deletes every element placed by the most recent run (reads provenance stamps to find them) | "I placed 600 fixtures but something's wrong — take them all back." Prompts with the count before deleting. |
| `Push to Families` | (none) | Writes the selected rule's values (clearances, weights, mounting height) onto every family Type in that category | Keeping family parameters in sync with rules so COBie export and schedules read the right numbers. |
| `Heat-map` | (none) | Paints an Analysis Visualization Framework (AVF) compliance overlay — green where rules are satisfied, red where not | Management reviews, deliverable QA, showing completion status to a reviewer. |
| `Save view preset` | (none) | Stamps the current view scope and settings so a run is reproducible | At the end of a successful placement run you want to be able to repeat. |
| `GD Study` | (none) | Explains how to launch the Generative Design study (`.dyn` file in `Data/GenerativeDesign/`) | Optimising rule weights when you don't yet know the right values. |
| `Close` | (none) | Closes the Centre | Unsaved edits are kept in memory but lost when Revit closes. Always Save Project first. |

**The rule list grid (left side):**

| Column | What it means |
|---|---|
| `●` | Orange dot = unsaved edits |
| `Category` | Revit category name (e.g. "Lighting Devices") |
| `Variant` | The variant hint (or blank) |
| `Room` | Room filter regex (or blank = all rooms) |
| `Anchor` | The anchor type |
| `Pri` | Priority 0-100. Higher priority wins when rooms compete for slots. |

The search box above the grid filters by any column. Type "door" to see only door-related rules. Clear it to see everything.

The `● Dirty` and `✗ Invalid` toggle buttons show only unsaved or only invalid rules. Useful for focused editing.

---

## 3.5 Placing Fixtures — Step by Step

This worked example places socket outlets along the perimeter of office rooms.

**Scenario:** You have an office floor plan. Office rooms need perimeter socket outlets at 1100 mm above the floor, one every 600 mm along every wall.

**Step 1: Verify families are loaded**

1. In the project, go to the Project Browser.
2. Expand `Families → Electrical Fixtures`.
3. Look for a socket outlet family with a `FLUSH` type (the `STING_FIXTURE_VARIANT_TXT` value). If it is not there, load it: `Insert → Load Family → navigate to your socket outlet family → Open`.

**Step 2: Open the Placement Centre**

From the STING dock panel:
- `TAGS` tab → `Fixtures` sub-tab → click `Place Fixtures`.

Or from the ribbon (if using the STING ribbon tab):
- `STING Tools` tab → Placement group → `PlaceFixtures`.

The Placement Centre dialog opens.

**Step 3: Find the perimeter socket rule**

In the rule list on the left, look for a rule named approximately `elec-perimeter-socket` or similar. You can type "socket" or "perimeter" in the search box to filter.

Click the rule to select it. The right side shows all the rule's settings.

**Step 4: Verify the rule settings**

In the `Rule Core` group on the right:
- `Category Filter:` should read `Electrical Fixtures`
- `Variant Hint:` should read `FLUSH` (or `FLUSH,SURFACE` as a fallback chain)
- `Room Filter (regex):` should read something like `(?i)\b(office|workspace|open plan)\b`
- `Anchor:` should read `PERIMETER_OFFSET`

In the `Geometry` group:
- `Mount Height (mm):` `1100`
- `Mount Reference:` `FFL` (above finished floor)
- `Min Spacing (mm):` `600`

In the `Rule Kind` group:
- `Kind:` `Linear`
- `Per linear m:` `0.6` (one per 600 mm = one per 0.6 metres)

In `Standards & Classification`:
- `Standard ref:` should read `Approved Doc M / BS 7671`

If any of these don't match what you need, edit the fields directly. Remember to click Save Project (Ctrl+S) after editing.

**Step 5: Set the scope**

In the `Run Options` group (bottom of the right panel):
- `Scope:` choose `Active view` for just the current floor plan, or `Project` for all floors.

**Step 6: Preview first**

Click `Preview` (or press Alt+P) in the toolbar.

The canvas shows coloured dots/crosses at every location where a socket would be placed. Each rule has its own colour, so you can identify socket placements vs other fixture placements.

Check:
- Are the dots along the walls of office rooms? (Yes = correct anchor type)
- Are there dots in the corridor? (If yes, your Room Filter may be too broad — it is matching corridor rooms)
- Are the dots at roughly 600 mm spacing? (If they look closer or further, check the Per linear m value)

Press `Escape` to clear the preview.

**Step 7: Run Placement**

When the preview looks correct, click `Run Placement` (or press Alt+R).

A progress indicator appears. For a large floor with many rooms, this may take 10-60 seconds.

When complete, a result panel appears showing:
- Rooms visited: N
- Candidates evaluated: M
- Placed: P
- Skipped (rule conditions not met): Q
- Warnings: W (if any)

The placed elements are automatically selected in Revit — you can immediately see them highlighted in the canvas.

**Step 8: Review the result**

Click somewhere empty to deselect, then look at the model. Socket outlets should appear along the walls of office rooms.

If something looks wrong:
- Click `Undo last run` in the Centre toolbar (not Ctrl+Z — use the Centre's own undo button for batch placements).
- Adjust the rule as needed.
- Preview again.
- Re-run.

**Step 9: Run Validate**

After any successful placement run, click `Validate` (Alt+V) in the toolbar.

The validator checks clearances, maintenance access, and any other validators you have selected in the `Validators` checkbox panel. Any issues appear in a result panel.

Common validation findings after socket placement:
- "Clearance violation: M-ELC-01-0003 is 180 mm from M-ELC-01-0004" — two sockets are too close (probably because the room corner forced them together). Click the element reference to select and inspect.
- "Spec mismatch: STING_FIXTURE_VARIANT_TXT expected FLUSH, found SURFACE" — the family resolved to a SURFACE type instead of FLUSH (the preferred variant was not loaded). Load the FLUSH type family and re-run.

> **Stuck?** If Run Placement reports "No FamilySymbol found for category Electrical Fixtures," there is no family of that category loaded in the project. Load one first (Insert → Load Family), then re-run. See Section 3.10 for more troubleshooting.

---

## 3.6 The Lighting Grid

The lighting grid is a specific placement pattern for luminaires — a regular rectangular grid of light fittings calculated to meet a target illuminance level per BS EN 12464-1.

Think of ceiling tiles in an office. They are arranged in a perfect grid, usually 600 × 600 mm or 1200 × 600 mm. Luminaires in an office follow a similar grid, sized so that the spacing between luminaires provides the required light level (measured in lux) at the working plane (typically 0.85 m above the floor for a desk, or 0.0 m for a car park).

**What the LightingGridCommand does:**

1. For each room in scope, reads the room's dimensions and the target lux level from the rule.
2. Calculates the required luminaire spacing using the Light Loss Factor, luminaire lumens, and utilisation factor.
3. Generates a grid of candidate points snapped to the nearest ceiling tile centre (if `SnapToCeilingTile = Yes` in the rule) or at regular spacing.
4. Checks each point against clearance rules (minimum distance from walls, from sprinklers per BS 5306).
5. Places luminaires at the accepted points.
6. Reports uniformity ratio (minimum lux / average lux) and flags rooms that cannot achieve the target uniformity.

**Step-by-step for a standard office lighting grid:**

1. Open the Placement Centre.
2. Find the rule `lux-grid-office-500` (or similar) in the rule list. If it does not exist, click `+` and create one:
   - `Category Filter:` `Lighting Fixtures`
   - `Variant Hint:` `RECESSED` (or whatever your office luminaire type is)
   - `Room Filter:` `(?i)\b(office|workspace|open plan)\b`
   - `Anchor:` `LIGHTING_GRID`
   - `Mount Reference:` `CEILING`
   - `Mount Height (mm):` `100` (100 mm below the ceiling — adjust for your ceiling type)

3. In the `Geometry` group, set `Min Spacing (mm):` `1200` (minimum 1.2 m between luminaires — prevents the grid from placing luminaires too close together in small rooms).

4. In `Run Options`, make sure `Scope:` is set correctly (`Active view` for a single floor plan).

5. Click `Preview`. You should see a regular grid of dots on the ceiling of office rooms.

**Boundary options:**

The grid can be bounded in three ways, selected in the rule's `Geometry` group:

- **Room boundary (default):** The grid is calculated to fit inside the room boundary with a margin (typically 600 mm from the wall, editable). This is the most common option.
- **Crop region:** The grid fills the active view's crop region. Useful for open-plan layouts that span multiple rooms.
- **Manual:** You draw a boundary in the project before running. Not currently supported through the Centre's UI — use Room boundary or Crop region.

**Adjusting after placement:**

Luminaires placed by the lighting grid can be moved manually after placement. When you move a luminaire, you break its "auto-placed" provenance link — but the luminaire remains tagged and parametric. Moving a small number of luminaires to avoid ceiling features (beams, HVAC grilles) is normal practice.

If you need to re-run the grid after moving luminaires (for example, after a room layout change), click `Undo last run` in the Centre, make your changes to the rules or room boundaries, and re-run.

---

## 3.7 Learning from Existing Placements (LearnPlacementV4Command)

Imagine you receive a project where someone has already manually placed a representative sample of fixtures in a few rooms — a careful drafter has placed sockets, switches, and lights correctly in the ground floor. Now you need to place the same pattern across the remaining five floors.

The `LearnPlacementV4Command` analyses the existing placements in the model and reverse-engineers the placement rules that would produce them. It then saves those rules to a `STING_PLACEMENT_RULES.learned.json` file next to the `.rvt`.

**When to use this command:**

- When you inherit a partially modelled project and want to continue with consistent placement rules.
- When a client provides a sample layout for one floor that should be replicated across all others.
- When you want to verify that STING's built-in rules match what was already in the model.

**Step by step:**

1. Make sure the source model contains at least one well-placed example of each fixture type you want to learn. The more examples (more rooms), the more reliable the learned rules.

2. From the STING dock panel: `TAGS` tab → `Fixtures` sub-tab → click `Learn Placement V4`.

Or from the Revit search (press the `/` key or use the search box in the ribbon): type `Placement_Learn` and press Enter.

3. The command runs. It walks the model, groups placed fixtures by category and room type (using the room name), and calculates the statistical centre, spacing, and mounting height for each group.

4. A result dialog appears showing how many rule "prototypes" were found:
   ```
   Learned 12 rule prototypes:
   - Lighting Fixtures / office: 1200 mm spacing, 2400 mm AFF, 47 instances
   - Electrical Fixtures / FLUSH / office: 600 mm linear, 1100 mm AFF, 83 instances
   - Plumbing Fixtures / BASIN / bathroom: 1 per room, 850 mm AFF, 8 instances
   ...
   ```

5. Click `Save` in the result dialog. The rules are written to `STING_PLACEMENT_RULES.learned.json`.

6. Open the Placement Centre. In the toolbar, click `Reload Defaults`. The Centre now loads the learned rules (at Priority 90) alongside the baseline rules.

7. In the rule list, the learned rules appear with a `SourcePack: learned` tag. Review them — the engine is making educated guesses from statistical analysis, so some rules may need tweaking.

8. Edit any rules that don't look right. Save Project.

9. Run Placement for the remaining floors.

**What the output looks like:**

The `STING_PLACEMENT_RULES.learned.json` file contains rules in exactly the same JSON format as the shipped baseline. The rules are tagged with Priority 90 so they override the baseline (Priority ~50-70) but can be overridden by project-specific rules (Layer 4, any priority).

---

## 3.8 Rehosting Elements — Moving Equipment Between Hosts

### What is Rehosting?

In Revit, some families are "hosted" — they physically attach to a host element. A ceiling-mounted downlight is hosted by the ceiling. A wall socket is hosted by the wall. A wall-hung basin is hosted by the wall.

This hosting relationship means the family moves when the host moves. If you move the ceiling 50 mm lower, all the downlights hosted by that ceiling move down 50 mm with it. This is a useful behaviour — services follow the structural decisions automatically.

But hosting can cause problems:

- If you delete the ceiling and recreate it (common during design changes), the downlights lose their host and become "unhosted" — they float in space at their original position.
- If you change a wall's location significantly, the hosted socket may end up in a void or inside another wall.
- If a level changes height, elements hosted to the slab may shift unexpectedly.

Rehosting means assigning a new host element to a family instance that has lost its original host, or deliberately moving a family from one host to another.

### When Rehosting is Needed

- **Ceiling deleted and recreated:** The most common cause. Any design coordination change that requires deleting and recreating a ceiling will orphan its hosted luminaires.
- **Wall demolished and rebuilt:** Moving a wall to a new location and back may require rehosting wall-mounted fixtures.
- **Level height changed:** If a level is shifted in the Levels view, elements may not re-host correctly to the revised level.
- **Phase change:** Elements placed in Phase 1 on a temporary ceiling may need rehosting to the permanent ceiling in Phase 2.
- **Linked model changes:** If a structural engineer updates their linked model and ceiling soffits change, MEP fixtures may lose their hosts.

### How to Rehost in Revit — The Native Way

Revit's built-in rehosting tool is called "Pick New Host." Here is how to use it:

1. Select the element that needs rehosting (the orphaned luminaire, the socket on the deleted wall, etc.).

2. In the ribbon, go to the `Modify` tab (this tab appears whenever an element is selected).

3. Look in the `Host` panel (usually on the right side of the Modify tab). Click `Pick New Host`.

4. The cursor changes to indicate it is waiting for you to select a host. Move over the element you want to use as the new host (the new ceiling, the replacement wall). When the correct element is highlighted, click.

5. The family instance reattaches to the new host. It moves to the host's surface — which may change its position if the new host is at a different location.

6. Check the family's position. You may need to adjust the offset to maintain the correct location.

> **Stuck?** If the "Pick New Host" button is greyed out or not visible, the selected family is not a hosted family — it is either face-based or unhosted. Face-based families can be rehosted; unhosted families cannot.

### How STING Helps with Rehosting

STING provides two tools that automate rehosting tasks:

**ValidateFillsCommand:** Identifies elements that have lost their host (or have other connectivity issues). Run it from the STING dock panel (Routing tab or BIM tab depending on your version). The report shows elements that are "orphaned" (no valid host found).

**AutoDropCommand:** The AutoDropCommand is primarily for routing MEP services, but it also re-establishes host connections when MEP elements have been orphaned. Run it after any significant model change that may have broken hosting.

### Rehosting Workflow — Step by Step

When you receive a message like "1 warning: Lighting Fixtures lost host" (shown in Revit's warnings) or the ValidateFills report shows orphaned elements, follow this workflow:

1. **Identify orphaned elements:**
   - Run `ValidateFillsCommand` (STING dock panel → Routing tab → Validate Fills).
   - In the result panel, note which elements are flagged as "No valid host" or "Orphaned."
   - Alternatively: use Revit's native "Review Warnings" (Manage → Review Warnings) to find "Hosted element lost its host."

2. **Find the replacement hosts:**
   - Go to the area in the model where the orphaned elements are.
   - Identify the new ceiling, wall, or floor that should host each orphaned element.

3. **Rehost each element:**
   - Select the orphaned element.
   - Modify tab → Pick New Host → click the replacement host.
   - Verify position.

4. **For large batches:** If dozens of luminaires have lost their host due to a ceiling redesign:
   - Consider deleting the orphaned elements and re-running the Placement Centre for that area.
   - The Placement Centre's "Undo last run" + "Re-run" workflow is faster than manually rehosting large numbers of elements.

5. **Re-validate:**
   - Run `ValidateFillsCommand` again.
   - All previously-orphaned elements should now show as hosted.

### Common Rehosting Errors and Fixes

| Error | Cause | Fix |
|---|---|---|
| "Hosted element lost its host" (Revit warning) | The host element was deleted or significantly modified | Use Pick New Host, or delete and re-place using Placement Centre |
| Family appears at wrong height after rehosting | New host is at a different elevation than the old host | Adjust the vertical offset in the family's Properties after rehosting |
| "Cannot pick host: invalid host" | Trying to host a wall-based family on a ceiling (wrong host category) | Delete and re-place using the correct family template (wall-based vs ceiling-based) |
| Family disappears after rehosting | New host is in a different Design Option or Phase | Check that you are viewing the correct Design Option and Phase |
| Rehosted family faces wrong direction | The new wall has a different normal direction (inside/outside swapped) | After rehosting, select the family → click the blue flip arrow that appears near the family to mirror it |

---

## 3.9 Auto Drop — Letting STING Place Elements at the Correct Level

### What Does "Drop" Mean?

In MEP engineering, "dropping" means routing a service from where it travels (at ceiling level, above the suspended ceiling) down to where it is used (at the wall face or at a piece of equipment). 

Think of water in a building. The main cold water supply pipe runs at ceiling level in a plant room, then drops down the risers, then branches horizontally at each floor level, then drops again down to the basins and WCs. Each "drop" is a short vertical section that connects the horizontal service run to the point of use.

The same principle applies to:
- Electrical conduit: drops from the containment system (cable tray above the ceiling) down to socket outlet back boxes, switch boxes, and luminaire terminals.
- Pipe: drops from horizontal branch mains down to plumbing fixtures.
- Duct: drops from main horizontal duct runs down to diffusers.

### The Three Auto Drop Engines

STING has three separate auto-drop engines, one for each service type:

**AutoConduitDrop:** Routes conduit from the cable tray or conduit system above the ceiling down to every electrical fixture (socket outlets, switches, luminaires, fire alarm devices) placed by the Placement Centre. Uses the conduit routing preferences set in the project's MEP settings.

**AutoPipeDrop:** Routes pipe from the horizontal branch main down to every plumbing fixture. Calculates the correct pipe size, drop angle, and the correct fitting type at the branch connection.

**AutoDuctDrop:** Routes duct from the horizontal supply or extract duct down to every air terminal (diffuser, grille, VAV box). Sizes the drop duct based on the terminal's airflow requirement.

### Running AutoDropCommand

1. After running Placement (so fixtures are in position), open the STING dock panel.
2. Go to the `TAGS` tab → `Routing` sub-tab (or find the Routing section in the appropriate tab for your version).
3. Click `Auto Drop`.

The AutoDrop dialog opens. Options:
- **Drop type:** `Electrical`, `Pipe`, or `Duct` — choose which service to route.
- **Scope:** `Selected elements`, `Active view`, or `Project`.
- **Drop route preference:** `Shortest`, `Via tray` (routes to the nearest cable tray first, then drops), `Vertical only` (straight down).

4. Click `Run Drop`.

The engine calculates routes for each fixture and creates the conduit/pipe/duct geometry. A result panel reports:
- Routes created: N
- Routes failed (no path found): M
- Warnings (e.g. pipe size too small for calculated flow): W

5. Review any failed routes manually. Failed routes are usually where there is a structural obstruction or where the horizontal service run has not been modelled yet.

### Reading the Result

After AutoDropCommand runs:
- In the 3D view, you should see short vertical sections of conduit/pipe/duct connecting from the ceiling-level service runs down to each fixture.
- In the clash detection view (BIM Coordination Center → Clash tab), any clashes between the drops and structural elements appear as red spheres.
- Validation results appear automatically if "Run validators after placement" is set to On.

---

## 3.10 Troubleshooting the Placement Centre

| Problem | Likely cause | Fix |
|---|---|---|
| Rule shows `✗ Invalid` chip | A required field is empty, the category name is misspelled, or the regex is malformed | Click the rule and look at the orange error message at the bottom of the right panel. Fix the field it names. |
| Run Placement says "No FamilySymbol found for category X" | No family of that Revit category is loaded in the project | Load a family of the correct category (Insert → Load Family), then re-run. |
| Preview shows no candidates for a rule | Room filter too strict; room area below Min Area; another rule has filled room's Max-per-room slots | Try clearing the Room Filter box temporarily and re-previewing. Check Priority and Max-per-room. |
| Fixtures placed at wrong height | Mount Height set wrong; family origin in wrong location | Check rule Geometry group Mount Height; check family origin (see Chapter 2). |
| Fixtures facing wrong direction (e.g. socket facing wall) | Family `Center (Front/Back)` reference plane not set; `Always Vertical` not set | Fix reference planes in Family Editor (Chapter 2, Rule 2). |
| Fixtures stack on top of each other | Min Spacing too small; multiple rules running in same room at same anchor | Increase Min Spacing in the rule. Check Conflicts with field to suppress conflicting rules. |
| WC fixtures appear in walk-in closets or wardrobes | Room Filter regex too broad (old `(?i)wc` matches "walk-in closet") | Use word-boundary regex: `(?i)\b(wc|toilet|bathroom)\b` — the `\b` prevents partial word matches. |
| Emergency lights appear in plant rooms | Room Filter not excluding non-public areas | Add Exclude room regex: `(?i)\b(plant|riser|cupboard|store)\b` |
| Placement Centre is very slow | Large project + Project scope + all validators enabled | Switch to Active view scope. Disable validators during placement (turn off "Run validators after placement"), then validate separately. |
| "Undo last run" doesn't find the elements | Provenance stamping was disabled during the run, or elements were placed before the Centre was opened | Use Revit Ctrl+Z instead. In future runs, make sure "Stamp provenance on each placement" is On. |
| Import shows "0 rules imported, N skipped" | All rules in the imported file already exist (same RuleId) | Use a text editor to check the RuleIds in the imported file. If you want to overwrite existing rules, you must first delete them from the Centre (left-rail `-` button), then import. |
| Electrical fixtures land inside the wall | Family origin at back of back box, not at faceplate | Open the family in the Family Editor, move the geometry forward so the faceplate aligns with the origin reference plane. Re-save. Reload into project. Re-run. |
| Ceiling-hosted luminaire says "no ceiling found" | Family is OneLevelBased (not Ceiling-Hosted); or ceiling not modelled yet | Check the family's hosting type in the Family Editor (the `Family Placement Type` setting in `Create → Properties → Family Category and Parameters`). Change to Ceiling Hosted. |

---

# Quick Reference

---

## Placement Centre Commands — Complete Table

| Command | Where to find it | What it does |
|---|---|---|
| `PlaceFixturesCommand` | TAGS tab → Fixtures sub-tab → Place Fixtures | Opens the Placement Centre and runs rule-based placement for all fixture categories |
| `LightingGridCommand` | TAGS tab → Fixtures sub-tab → Lighting Grid | Runs the lighting grid algorithm for luminaires (BS EN 12464-1 lux calculation) |
| `LearnPlacementV4Command` | TAGS tab → Fixtures sub-tab → Learn Placement | Analyses existing placements and writes STING_PLACEMENT_RULES.learned.json |
| `AutoDropCommand` | TAGS tab → Routing sub-tab → Auto Drop | Routes conduit/pipe/duct drops from ceiling-level services to placed fixtures |
| `GenerateLayoutCommand` | TAGS tab → Routing sub-tab → Generate Layout | Generates the containment layout (cable tray routes, duct mains) for a floor |
| `ValidateFillsCommand` | TAGS tab → Routing sub-tab → Validate Fills | Checks conduit/cable tray fill against rated capacity; flags over-filled and orphaned elements |
| `RunAllValidatorsCommand` | TAGS tab → Routing sub-tab → Validate All | Runs all eight validators (clearance, maintenance, connectivity, fill, spec, termination, slope, separation) |
| `Placement_AuditSetup` | BIM tab or TEMP tab → Audit Setup | Cross-checks all family authoring requirements against the live model; writes a CSV report |

---

## MEP Symbol Authoring Checklist

Use this checklist before committing any new MEP symbol family to the library:

- [ ] Family template: `Generic Annotation.rft` (for most symbols) or a hosted template for fixed-host fixtures
- [ ] Two reference planes at origin: both have `Defines Origin = Yes` and `Is Reference = Strong Reference`
- [ ] Three subcategories created under `Manage → Object Styles → Annotation Objects`: `ISO_Symbols_[DISC]_IEC`, `ISO_Symbols_[DISC]_BS`, `ISO_Symbols_[DISC]_ANSI`
- [ ] Three Yes/No type parameters added: `SYMBOL_SHOW_IEC`, `SYMBOL_SHOW_BS`, `SYMBOL_SHOW_ANSI`
- [ ] All drawn geometry assigned to one of the three subcategories (not `<None>`)
- [ ] Visibility of each subcategory's geometry bound to its `SYMBOL_SHOW_*` parameter using the formula field in Properties → Visible
- [ ] `EMERGENCY_MODE` Yes/No parameter present for fire, emergency, or life-safety symbols
- [ ] At least one type created with `SYMBOL_SHOW_IEC = Yes` (the default IEC/ISO standard)
- [ ] File named per convention: `STING_[DISC]_[symbol_id].rfa` (e.g. `STING_E_E_SKT_13A_1.rfa`)
- [ ] File saved to `CompiledPlugin/Data/SymbolLibrary/[Discipline]/`
- [ ] Test load into a blank project: place one instance, switch through the three types, verify only the correct standard's geometry renders each time
- [ ] Origin position matches the `insertion_point` column in `mep_symbols.csv`
- [ ] Line weights set correctly: weight 1 for construction lines, weight 2 for standard symbols, weight 3 for fire/emergency/critical symbols

---

## Placement Family Authoring Checklist

Use this checklist before using any family with STING's Placement Centre:

- [ ] Family origin at the correct insertion point (see per-category table in Section 2.3)
- [ ] Reference plane `Center (Front/Back)` named correctly, `Is Reference = Strong Reference`, `Defines Origin = Yes`
- [ ] Reference plane `Center (Left/Right)` named correctly, `Is Reference = Strong Reference`, `Defines Origin = Yes`
- [ ] `Always Vertical = Yes` for vertical wall fittings (switches, sockets, sensors)
- [ ] `Cuts with Voids When Loaded = Yes` for sleeves and chase-through elements
- [ ] Family Placement Type set correctly (`One Level Based Hosted` for wall fixtures, `Ceiling Based` for ceiling fixtures, etc.)
- [ ] STING shared parameter file set to `StingTools/Data/MR_PARAMETERS.txt` before adding parameters
- [ ] `STING_BOX_LOCATION_ID` (Text, Instance) bound
- [ ] `STING_NOGGIN_REQUIRED` (Yes/No, Type) bound — for pendants and heavy ceiling fixtures
- [ ] `STING_AUTO_PLACED_BOOL` (Yes/No, Instance) bound
- [ ] `STING_FIXTURE_VARIANT_TXT` (Text, Type) bound and populated with the correct variant name (e.g. "FLUSH", "BASIN", "PENDANT")
- [ ] `MK_CATALOGUE_REF` (Text, Type) bound (manufacturer part number)
- [ ] Category-specific parameters bound (see per-category matrix in Section 2.3)
- [ ] `Placement_AuditSetup` run and no critical errors reported
- [ ] Test Auto Tag run: STING writes all 8 tag tokens successfully
- [ ] Test Placement Centre run: fixture places at the correct position and orientation

---

## Glossary

| Term | Plain English explanation |
|---|---|
| AFF | Above Finished Floor. The standard height datum for mounting heights. "1100 mm AFF" means 1.1 metres above the top of the floor finish. |
| Anchor | In the Placement Centre, the reference point from which offsets are measured. "DOOR_HINGE" means measure from the hinge side of the door. |
| Annotation family | A Revit family that is 2D only. Annotation families are visible in plan, section, and elevation views, but hidden in 3D views. Used for MEP symbols and wire annotations. |
| AVF | Analysis Visualization Framework. Revit's built-in heat-map system. The Placement Centre uses it to show a compliance heat-map (green = rules met, red = rules not met). |
| BMS | Building Management System. The computerised control system that monitors and controls HVAC, lighting, and other building services. |
| COBie | Construction-Operations Building Information Exchange. A spreadsheet format (BS 1192-4) for handing over building data to the FM team at project completion. |
| Defines Origin | A reference plane setting. A reference plane with `Defines Origin = Yes` contributes to the family's insertion point. |
| Family | A reusable Revit component stored as a `.rfa` file. Examples: a socket outlet, a basin, a smoke detector, a downlight. |
| Family Editor | The separate Revit mode (opened by clicking Edit on a family) where you create and modify the geometry, parameters, and properties of a family. |
| FamilyInstance | A copy of a family that has been placed in a Revit project model. Each placed copy is one instance. |
| FamilySymbol | A type variant within a family. One socket outlet `.rfa` might have types "1G FLUSH", "2G FLUSH", "1G SURFACE". Each is a FamilySymbol. |
| FFL | Finished Floor Level. The top of the floor finish. The reference datum for most mounting heights. |
| Filled Region | A closed 2D boundary with a fill pattern inside it. Used in families for solid-coloured shapes in symbols. |
| Homerun | In electrical drawings, the homerun is where a circuit exits the floor plan to return to the distribution board. Shown with an arrowhead annotation. |
| Hosting | The relationship between a hosted family and its host element. A ceiling-mounted downlight is "hosted by" the ceiling it sits in. |
| IEC | International Electrotechnical Commission. Publisher of IEC 60617, the international electrical symbol standard. |
| Insert point / Origin | The single point in a family that Revit snaps to the exact location you click when placing the family in a project. |
| ISO | International Organization for Standardization. Publisher of ISO 14617 (piping symbols), ISO 7010 (safety signs), and many others. |
| Label | In the Family Editor, a text element that displays the value of a linked parameter. When the parameter value changes, the label updates. |
| Lux | The unit of illuminance (light level on a surface). BS EN 12464-1 specifies minimum lux levels for different workplaces: 500 lux for office tasks, 300 lux for storage areas. |
| MCP | Manual Call Point. The red break-glass fire alarm device mounted on walls. Also called a "break glass unit" or "call point". |
| MEP | Mechanical, Electrical, Plumbing. The collective term for building services engineering. |
| Morphology | The shape and form of a symbol — how it looks. The morphology of a socket outlet symbol is two circles with a line. |
| P&ID | Piping and Instrumentation Diagram. A schematic drawing showing all pipes, valves, and instruments in a system. The symbols in P&IDs follow ISO 14617 and ANSI conventions. |
| Placement Centre | The STING tool that automatically places families in a Revit project based on written rules. |
| Provenance stamp | An invisible record written onto every element placed by the Placement Centre, recording which rule placed it, when, and by whom. |
| Reference plane | A construction aid in the Family Editor — an infinite plane (shown as a line in any view) used to define the family's origin, symmetry, and dimensional anchors. |
| Regex | Regular expression. A compact notation for matching text patterns. `(?i)office|workspace` matches any text containing "office" or "workspace", case-insensitively. |
| Rehosting | Assigning a new host element to a hosted family instance whose original host has been deleted or moved. |
| Rule | In the Placement Centre, a description of what to place, where to place it, in which rooms, per which standard. Each rule produces zero or more placed family instances. |
| Shared parameter | A parameter defined in a `.txt` file (the shared parameter file) that can be used across multiple families and projects. Shared parameters can be scheduled, exported, and read by STING. |
| Subcategory | A named sub-division of a Revit category (e.g. "ISO_Symbols_E_IEC" is a subcategory of "Generic Annotations"). Subcategories control line weights, colours, and visibility independently. |
| Symbolic lines | Lines drawn in the Family Editor's 2D annotation layer. They represent the symbol's visible shape on plan and elevation views. |
| Variant hint | A text value stored in `STING_FIXTURE_VARIANT_TXT` that tells the Placement Centre which type of a family to prefer (e.g. "FLUSH" prefers flush-plate types over surface-mount types). |

---

## Cross-references

> **This is the foundation guide. It references no other guides.**

> **Used by:** The following discipline-specific guides all assume you have read this guide first. When those guides say "make the family placement-ready" or "add the STING shared parameters" or "open the wire annotation family", they are referring to the steps described here.
>
> - `ELECTRICAL_WORKFLOW_GUIDE` — electrical fixture placement, panel schedules, wire annotation in context
> - `PLUMBING_WORKFLOW_GUIDE` — sanitary fixture placement, drainage routing
> - `HEALTHCARE_WORKFLOW_GUIDE` — medical gas outlets, bedhead trunking, HTM compliance rules
> - `DRAWING_PRODUCTION_SYSTEM_GUIDE` — how annotation families (including MEP symbols and wire annotations) appear on finished drawing deliverables, view template configuration, and the Drawing Template Manager

---

*Guide version 1.0. Covers: MEP symbol authoring for 516 symbols across 9 disciplines; wire annotation family creation and placement; placement-ready family authoring requirements; STING Placement Centre — all rules, buttons, and dialogs. For the phase history of the Placement Centre codebase, see `docs/CHANGELOG.md`. For open gaps and planned enhancements, see `docs/ROADMAP.md`.*
