# SLD Symbols — Layman's Catalogue Guide

**What this covers**: Every symbol used on Single Line Diagrams (SLDs) — the schematic drawings that show how electrical, lighting, and power distribution systems are connected. This is a _symbol catalogue_ guide focused on what each shape means and why. For the workflow of creating and managing SLD drawings in STING, see `docs/SLD_SYMBOL_WORKFLOW_AND_GAPS.md`.

**Standards covered**:
- **IEC 60617** — International Electrotechnical Commission standard (used in UK, Europe, Africa, most of world)
- **IEEE 315 / ANSI Y32.2** — American / North American standard (US projects)

---

## Part 1 — What Is a Single Line Diagram?

A **Single Line Diagram** (SLD) is a simplified drawing of an electrical system. Unlike a full wiring diagram (which shows every individual wire), an SLD represents three-phase cables as a single line and uses standardised symbols to show what each component is and how they connect.

**Think of it like a map of the London Underground**: the map doesn't show every rail, every sleeper, or the actual curves of the tunnels. It just shows which stations exist and how they connect. An SLD is the same — it shows which electrical components exist and how power flows between them, without showing every wire.

SLDs are used for:
- Electrical panel schedules and distribution boards
- Motor control centres (MCCs)
- Generator and UPS systems
- Cable routes from source to load
- Protection coordination studies

---

## Part 2 — IEC 60617 Symbol Catalogue

IEC 60617 is organised into 11 parts. Parts relevant to building services MEP:

| IEC 60617 Part | Covers |
|---|---|
| Part 1 | General symbols (lines, connections, crossings) |
| Part 4 | Passive components (resistors, capacitors, inductors) |
| Part 6 | Production and conversion of electrical energy (generators, transformers, batteries) |
| Part 7 | Switchgear and controlgear |
| Part 8 | Measuring instruments and signal devices |
| Part 11 | Architectural and topographic installation plans (THIS is building services MEP) |

STING uses mainly **Part 6**, **Part 7**, and **Part 11**.

---

### 2.1 Power Sources (IEC 60617 Part 6)

#### Generators
| Symbol code | What it is | Symbol description | When you see it |
|---|---|---|---|
| `SLD_GEN_3PH` | 3-phase generator | Circle with a wavy sine-wave line inside | Main standby generator (diesel gen-set) |
| `SLD_GEN_1PH` | 1-phase generator | Circle with single sine wave | Small portable generator |
| `SLD_GEN_EMRG` | Emergency generator | Generator circle with "E" or diagonal hatching | Life-safety generator (must power fire alarm, emergency lighting) |

**Plain English**: A generator makes electricity by spinning a coil of wire inside a magnetic field. On an SLD it's always a circle — the sine wave inside shows it produces alternating current (AC), the type that comes from the grid.

#### Transformers
| Symbol code | What it is | Symbol description | When you see it |
|---|---|---|---|
| `SLD_XFMR_2W` | 2-winding transformer | Two interlocking sine-wave loops side by side | Steps voltage up or down (e.g. 11kV from street to 400V in building) |
| `SLD_XFMR_3W` | 3-winding transformer | Three loops | Central electrical substation supplying multiple voltage levels |
| `SLD_XFMR_AUTO` | Auto-transformer | Single loop with tapped connection | Voltage adjustment without full isolation |
| `SLD_XFMR_ISOL` | Isolation transformer | Two loops with gap between them | Electrical isolation (medical equipment, swimming pools) |
| `SLD_XFMR_CURR` | Current transformer (CT) | Small loop around the single line | Measures current for metering / protection — does NOT change the voltage |

**Plain English**: A transformer changes voltage. If your street supply is 11,000 volts and your building needs 400 volts, a transformer steps it down. The symbol looks like two coils of wire facing each other (because that's literally what a transformer is — two coils with magnetic coupling between them).

#### Batteries and UPS
| Symbol code | What it is | Symbol description |
|---|---|---|
| `SLD_BATT` | Battery | Alternating long and short parallel lines (the cells) |
| `SLD_BATT_BANK` | Battery bank | Multiple battery symbols in parallel |
| `SLD_UPS` | Uninterruptible Power Supply | Rectangle labelled "UPS" with battery symbol inside or adjacent |
| `SLD_CHARGER` | Battery charger | Rectifier symbol with battery |

---

### 2.2 Switchgear and Protection (IEC 60617 Part 7)

This is the largest and most important section for SLDs.

#### Circuit Breakers
| Symbol code | What it is | Symbol description | What it does |
|---|---|---|---|
| `SLD_CB` | Generic circuit breaker | Square box on the single line (or diagonal slash with box) | Automatically breaks the circuit when current is too high |
| `SLD_CB_3P` | 3-pole circuit breaker | Three squares or three parallel lines through the main symbol | Protects all three phases simultaneously |
| `SLD_CB_MCCB` | Moulded-case circuit breaker | Square box with "MCCB" label | Higher-capacity breaker for main distribution |
| `SLD_CB_ACB` | Air circuit breaker | Labelled box or specific IEC symbol | Very high current capacity, opens in air |
| `SLD_CB_RCCB` | Residual-current circuit breaker | CB symbol with small earth symbol | Detects leakage current to earth (protects people) |
| `SLD_CB_RCBO` | RCBO (combined CB + RCD) | Combined symbol | Single device does both overcurrent and residual current protection |
| `SLD_MCB` | Miniature circuit breaker | Diagonal line through a square | Small breaker for individual circuits in a consumer unit |

**Plain English**: A circuit breaker is like a clever fuse that resets. When too much current flows (a fault or overload), it trips open and breaks the circuit. You can reset it by flipping it back. An RCCB/RCBO also detects tiny currents leaking to earth (which could give someone an electric shock) and trips even when the main current is within limits.

#### Fuses
| Symbol code | What it is | Symbol description |
|---|---|---|
| `SLD_FUSE` | Generic fuse | Small rectangle or horizontal line with a rectangle around it |
| `SLD_FUSE_HRC` | High-rupture-capacity fuse | Labelled rectangle — can safely break very large fault currents |
| `SLD_FUSE_DISC` | Fuse-disconnector | Fuse with a switch symbol — lets you isolate the fuse to replace it safely |

#### Switches and Disconnectors
| Symbol code | What it is | Symbol description | What it does |
|---|---|---|---|
| `SLD_SW_DISC` | Disconnector / isolator | Two horizontal lines with a gap and a diagonal line that bridges them | Isolates a section of circuit for safe maintenance. Does NOT interrupt current — circuit must be dead first |
| `SLD_SW_LOAD` | Load-break switch | Diagonal bridging line with an arc showing it can break under load | Can be opened under normal load current (unlike a plain isolator) |
| `SLD_SW_EARTH` | Earth switch | Diagonal line dropping to earth symbol (three descending lines) | Safely earths a circuit to guarantee it's de-energised during maintenance |
| `SLD_SW_TRANS` | Transfer switch | Double-throw symbol | Switches load between two sources (mains → generator) |
| `SLD_SW_ATS` | Automatic transfer switch | Transfer switch with automatic control symbol | Automatically switches to generator when mains fails |
| `SLD_SW_MAN` | Manual change-over switch | Transfer switch labelled "MANUAL" | Operator manually selects source |
| `SLD_CONTACTOR` | Contactor | Switch symbol with control coil indicated | Remotely-operated heavy-duty switch for motors and large loads |
| `SLD_STARTER_DOL` | Direct-on-line starter | Contactor with overload relay | Starts a motor by connecting directly to full voltage |
| `SLD_STARTER_SD` | Star-delta starter | Three contactors shown | Starts motor in "star" (lower voltage/torque) then switches to "delta" (full power) |
| `SLD_STARTER_VFD` | Variable frequency drive (inverter) | Rectangle labelled "VFD" or "VSD" | Changes motor speed by varying the frequency of the supply |

**Plain English**: Switches control whether current can flow. A disconnector is purely for isolation — the electrical equivalent of pulling a plug — and can only be operated when no current is flowing. A contactor is a remotely-controlled switch for motors — it closes when a control coil is energised (like an electromagnet pulling the contacts together). A VFD is like a volume control for a motor, letting it run at any speed instead of just full speed.

#### Busbars
| Symbol code | What it is | Symbol description |
|---|---|---|
| `SLD_BUSBAR` | Busbar (3-phase) | Bold horizontal line with vertical lines dropping to circuits |
| `SLD_BUSBAR_COUP` | Busbar coupler | Switch connecting two separate busbars |
| `SLD_BUSBAR_SEC` | Section switch | Isolates one section of a busbar from another |

**Plain English**: A busbar is like a multi-socket extension lead but for high-power electrical distribution — a solid copper bar that multiple circuits tap off. On an SLD, it's shown as a heavy horizontal line with the breakers or fuses hanging off it.

---

### 2.3 Meters and Measuring Instruments (IEC 60617 Part 8)

| Symbol code | What it is | Symbol description |
|---|---|---|
| `SLD_METER_ENER` | Energy meter / kWh meter | Circle with "kWh" inside |
| `SLD_METER_AMM` | Ammeter | Circle with "A" |
| `SLD_METER_VOLT` | Voltmeter | Circle with "V" |
| `SLD_METER_POW` | Power meter (kW) | Circle with "W" or "P" |
| `SLD_METER_PF` | Power factor meter | Circle with "PF" or "cosφ" |
| `SLD_METER_HARM` | Power quality / harmonics analyser | Labelled instrument symbol |
| `SLD_CT` | Current transformer (for metering) | Small loop around single line |
| `SLD_VT` | Voltage transformer (potential transformer) | Small transformer symbol on the line |

---

### 2.4 Lighting and Power Outlets (IEC 60617 Part 11)

Part 11 is what most building services engineers use on electrical floor plans:

#### Lighting
| Symbol code | What it is | Symbol on plan | Notes |
|---|---|---|---|
| `SLD_LUM_GEN` | General lighting point / luminaire | Circle (open) | Generic ceiling light |
| `SLD_LUM_PEND` | Pendant luminaire | Circle with vertical line below | Hanging light fitting |
| `SLD_LUM_WALL` | Wall luminaire | Half-circle against a wall | Bulkhead / wall bracket light |
| `SLD_LUM_EMRG` | Emergency luminaire | Circle with "EM" or asterisk / filled circle | Must stay on in a power cut |
| `SLD_LUM_EXIT` | Exit sign luminaire | Rectangle with arrows | "EXIT" or running man signs |
| `SLD_LUM_FLOOD` | Floodlight | Circle with directional arrow | External floodlight |
| `SLD_LUM_DOWN` | Downlight / recessed | Filled circle | Recessed ceiling light |
| `SLD_LUM_STRIP` | Fluorescent / LED strip | Rectangle | Batten or strip light fitting |
| `SLD_LUM_BATTEN` | Batten | Narrow rectangle | Bare-tube fluorescent |

#### Switches (room-level)
| Symbol code | What it is |
|---|---|
| `SLD_SW_1G1W` | 1-gang 1-way switch (simple on/off) |
| `SLD_SW_1G2W` | 1-gang 2-way switch (two switches controlling same light) |
| `SLD_SW_2G` | 2-gang switch (two independent switches in one plate) |
| `SLD_SW_DIMMER` | Dimmer switch |
| `SLD_SW_PIR` | PIR occupancy detector switch |
| `SLD_SW_PULL` | Pull-cord switch (bathrooms) |
| `SLD_SW_TIMER` | Time switch |
| `SLD_SW_PHOTO` | Photocell / daylight sensor switch |

#### Power Outlets (Sockets)
| Symbol code | What it is |
|---|---|
| `SLD_SOC_13A` | 13A switched socket outlet (UK standard) |
| `SLD_SOC_13A_DP` | 13A socket with double-pole switch |
| `SLD_SOC_RCD` | Socket with integral RCD |
| `SLD_SOC_USB` | Socket with USB charger |
| `SLD_SOC_IND` | Industrial socket (CEEform / IP44+) |
| `SLD_SOC_3PH` | 3-phase socket (e.g. for large equipment) |
| `SLD_SOC_SHVR` | Shaver socket (bathroom) |

#### Data and Communications
| Symbol code | What it is |
|---|---|
| `SLD_DATA_CAT6` | Cat6 data outlet |
| `SLD_DATA_FIBRE` | Fibre optic outlet |
| `SLD_DATA_COAX` | Coaxial TV / aerial outlet |
| `SLD_PHONE` | Telephone outlet |
| `SLD_PA` | Public address / audio speaker outlet |
| `SLD_ACCESS_RDR` | Access control reader |

#### Fire Alarm (on electrical plans)
| Symbol code | What it is |
|---|---|
| `SLD_FA_SMOKE` | Optical smoke detector |
| `SLD_FA_HEAT` | Heat detector |
| `SLD_FA_MCP` | Manual call point (break glass) |
| `SLD_FA_SOUND` | Sounder / bell |
| `SLD_FA_SOUNDER_BEACON` | Sounder with visual beacon |
| `SLD_FA_PANEL` | Fire alarm control panel |

---

## Part 3 — IEEE 315 / ANSI Y32.2 Symbol Catalogue

Used on projects that follow US / North American electrical standards. The shapes differ from IEC 60617 but represent the same components.

### Key differences from IEC

| Component | IEC 60617 | IEEE 315 |
|---|---|---|
| Circuit breaker | Square box on line | S-curve or zigzag line |
| Disconnect switch | Diagonal bridging line | Open-jaw symbol |
| Ground / earth | Three descending lines (long-medium-short) | Three lines, longest on top |
| Transformer | Two interlocked loops | Two circles |
| Generator | Circle with sine wave | Circle with "G" |
| Motor | Circle with "M" | Circle with "M" |
| Battery | Alternating long/short lines | Same (internationally common) |

### IEEE 315 Symbols (partial list)

| Symbol code | IEC equivalent | What it is |
|---|---|---|
| `SLD_IEEE_CB` | `SLD_CB` | Circuit breaker (S-curve symbol) |
| `SLD_IEEE_DISC` | `SLD_SW_DISC` | Knife-switch disconnector |
| `SLD_IEEE_XFMR` | `SLD_XFMR_2W` | Two-winding transformer (two circles) |
| `SLD_IEEE_GEN` | `SLD_GEN_3PH` | Generator (circle with G) |
| `SLD_IEEE_MOTOR` | `SLD_MOT` | Motor (circle with M) |
| `SLD_IEEE_CAP` | — | Capacitor (two parallel lines) |
| `SLD_IEEE_IND` | — | Inductor / reactor (semicircle loops) |
| `SLD_IEEE_RES` | — | Resistor (zigzag line) |
| `SLD_IEEE_GND` | `SLD_EARTH` | Earth / ground |

---

## Part 4 — Reading an SLD: A Step-by-Step Example

Imagine you open a main distribution board SLD. Here is how to read it from top to bottom:

**1. Incoming supply (top of drawing)**
The heavy line at the top represents the incoming 400V 3-phase supply from the DNO (Distribution Network Operator — i.e. the electricity company). It arrives at the:

**2. Main incomer**
A large circuit breaker symbol (square box) labelled "800A MCCB" or similar. This is the main switch that can cut power to the entire board.

**3. Main busbar**
The bold horizontal line below the main breaker. All the outgoing circuits tap off this bar.

**4. Outgoing circuits (below the busbar)**
Each vertical line dropping from the busbar represents one circuit. Each has:
- A breaker or fuse symbol (the protection device)
- A label showing the circuit number, load name, and rating
- Sometimes a meter symbol if energy monitoring is installed
- An earth symbol at the bottom showing it connects to earth

**5. Sub-boards (downstream distribution)**
If a circuit feeds another board (e.g. a sub-distribution board on floor 3), you'll see an arrow or the SLD continues to a separate sheet with the sub-board's own diagram.

**6. Motor loads**
A circuit feeding a motor shows the breaker → contactor → overload relay → motor symbol (a circle with "M") → the motor tag number.

**7. Transformers**
If a circuit feeds a transformer to a lower voltage (e.g. 230V UPS from a 400V supply), the transformer symbol appears between the breaker and the load.

---

## Part 5 — Colour Conventions on SLDs

IEC 60617 is a **monochrome** standard (black on white). However, STING supports optional colour highlighting:

| Colour | Meaning in STING SLD drawings |
|---|---|
| Red | Emergency / life-safety circuits (fire alarm, emergency lighting, exit signs) |
| Orange | UPS / uninterruptible supply (battery-backed) |
| Yellow | Generator supply |
| Green | Normal mains supply |
| Blue | Control circuits (lower voltage, 24V DC etc.) |
| Purple | Data / communications circuits |
| Black | Monochrome (IEC default) |

These colours are applied by setting `SymbolColorScheme = IEC60617` in the placement options — which actually means monochrome symbols, with IEC 60617's own colour conventions applied to cable route annotations rather than the symbols themselves.

---

## Part 6 — SLD Symbols in STING: How They're Placed

SLD symbols are placed on **Drafting Views** (the Revit equivalent of a blank sheet of tracing paper). The STING SLD Generator (`SLD_Generate` button in the TAGS tab → Fabrication subtab) creates a dedicated drafting view for each distribution board and populates it with:

1. The busbar line (a Detail Line)
2. Circuit breaker symbols for each outgoing circuit
3. Load labels (circuit number, load name, rating)
4. Current transformer (CT) symbols for metered circuits
5. Earth symbols at each connection to earth

SLD symbol families live in `Families/SLD/` and are referenced by `MepSymbolEngine` using the `symbol_code` values prefixed `SLD_` in `STING_MEP_SYMBOLS_INDEX.csv`.

Since SLD drafting views have no "scale" in the traditional sense (they're not cut from a building model), the `Symbol Scale` parameter is set to **1** and linework is authored at its plotted size (e.g. 5 mm wide busbar symbol is drawn as 5 mm in model space).

---

## Part 7 — Quick Symbol Look-up Table

| You see on an SLD | It is | IEC code |
|---|---|---|
| Circle with sine wave | AC generator | `SLD_GEN_3PH` |
| Two interlocked loops | Transformer | `SLD_XFMR_2W` |
| Circle with "M" | Motor | `SLD_MOT` |
| Square box on the line | Circuit breaker | `SLD_CB` |
| Diagonal line bridging two horizontal lines | Isolator / disconnector | `SLD_SW_DISC` |
| Diagonal line with arc | Load-break switch | `SLD_SW_LOAD` |
| Diagonal line down to three short bars | Earth connection | `SLD_EARTH` |
| Diagonal line with a box around it | Fuse-disconnector | `SLD_FUSE_DISC` |
| Rectangle labelled "VFD" | Variable frequency drive (speed controller) | `SLD_STARTER_VFD` |
| Circle labelled "kWh" | Energy meter | `SLD_METER_ENER` |
| Small loop around the main line | Current transformer | `SLD_CT` |
| Alternating long-short lines | Battery | `SLD_BATT` |
| Bold horizontal line with verticals | Busbar | `SLD_BUSBAR` |

---

*Guide version 1.0 — 2026-05-17*
*Standards: IEC 60617-6/7/8/11 (2010–2020), IEEE 315-1975 / ANSI Y32.2*
*See also: SLD_SYMBOL_WORKFLOW_AND_GAPS.md, MEP_SYMBOL_COLOUR_SCALE_GUIDE.md*
