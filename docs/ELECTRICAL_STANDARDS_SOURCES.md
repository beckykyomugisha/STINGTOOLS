# Electrical standards data — sources and verification (2026-09-27)

Where the BS 7671 Appendix 4 tables in `StingTools/Data/STING_WIRE_TABLES.json` and the
IEEE 1584-2018 coefficients in `StingTools/Commands/Electrical/ArcFlash/Ieee1584_2018.cs`
came from, and how far each figure has been checked.

**Neither standard was read in print.** Both are paywalled, and the research session could
reach only public GitHub repositories (manufacturer datasheets, IET pages and vendor
application notes were blocked by its network policy). Every figure below was read from a
public transcription; none was recalled or interpolated. Check the rows marked VERIFY, and
the IEEE coefficients, against the printed standards before relying on them for issue.

## BS 7671 Appendix 4

### What "verified" means

Each row carries two flags:

- `verified`: It_1ph and It_3ph agree exactly between two independent transcriptions.
- `mvVerified`: the same, for mV/A/m.

A sizing result that lands on a row with either flag false says VERIFY and names what is
unchecked. A size whose mV/A/m is not carried cannot be selected; the sizer refuses and
names the missing table rather than estimate voltage drop.

For most non-armoured columns the second transcription is IEC 60364-5-52 Annex B, whose
values BS 7671 adopts. Two sources agreeing is strong evidence, but it is still evidence about
the IEC values, not a second copy of BS 7671. The IEC tables start at 1.5 mm², so 1.0 mm²
rows compared against an NBR-only row carry less weight.

### Sources

| Id | Source | Used for |
|---|---|---|
| A | [Elec-Mate `appendix4CurrentCapacity.ts`](https://github.com/Gangoo91/Elec-Mate-Merge/blob/main/src/lib/calculators/bs7671-data/appendix4CurrentCapacity.ts) | It for every table. The only source that transcribes BS 7671 itself (header: read page by page from BS 7671:2018+A4). |
| A-vd | [Elec-Mate `voltageDropTables.ts`](https://github.com/Gangoo91/Elec-Mate-Merge/blob/main/src/lib/calculators/bs7671-data/voltageDropTables.ts) | mV/A/m ≤ 16 mm². Its own header calls its ≥ 25 mm² figures unverified, so those were not taken. No 4E2B / 4E4B. |
| A-tf | [Elec-Mate `temperatureFactors.ts`](https://github.com/Gangoo91/Elec-Mate-Merge/blob/main/src/lib/calculators/bs7671-data/temperatureFactors.ts) | Tables 4B1 and 4C1 |
| B | [nbr-5410-calculator `copper-pvc.json5`](https://github.com/Marcelotsvaz/nbr-5410-calculator/blob/master/share/data/wireTypes/copper-pvc.json5), `copper-epr.json5` and the correction-factor files | IEC 60364-5-52 Annex B as adopted by NBR 5410: the second source for It and the correction factors |
| C | [NBR5410_dimencionamento `script.js`](https://github.com/WerideMarcondes/NBR5410_dimencionamento/blob/main/script.js) | Second source for 4C1 wall, tray and ladder rows |
| D | [calEng `cop_tables.tsx`](https://github.com/llano1025/calEng/blob/main/src/data/cop_tables.tsx) | The Hong Kong EMSD Code of Practice for the Electricity (Wiring) Regulations 2020, Appendix 6, Tables A6(1)–A6(8), which reproduce the BS 7671 Appendix 4 layout. It was committed on 2025-04-21, before source A existed, so it is independent of A. **Mapping:** A6(1)=4D1, A6(2)=4D2, A6(4)=4D4, A6(6)=4E2, A6(8)=4E4. Used as the second source for It, and as the only full transcription of the multicore voltage-drop tables (z at every size). |
| E | [HansEJC `lvcalc.js`](https://github.com/HansEJC/hansejc.github.io/blob/master/js/lvcalc.js) | BS 7671:2018 Table 4E4A/4E4B, 1.5–120 and 400 mm² (first committed 2021). Its It column "D" matches D1. |
| F | [Elec-Mate `useEVChargingSmartForm.ts`](https://github.com/Gangoo91/Elec-Mate-Merge/blob/main/src/hooks/inspection/useEVChargingSmartForm.ts) | 4E4B 1.5–25 mm². |
| G | [Pedroaaandrade `index.html`](https://github.com/Pedroaaandrade/bs7671-Cable-sizing-calculator/blob/main/index.html) | 4D1A B/C/F and 4D4A (including D1/D2), plus 4D4B r and x. It was committed after A, so it only corroborates and never counts as the second source on its own. |
| H | IEC 60364-5-52 Tables B.52.2–B.52.5 in two transcriptions: [Ali-3427/ElektroPlan](https://github.com/Ali-3427/ElektroPlan/tree/16f96142691e6455339c54ccd58b3f2a63e2c0a3/packages/calculation-data/src/iec/cable-ampacity) @16f9614 and [m0000hamad/CableSizer](https://github.com/m0000hamad/CableSizer/blob/4ac0a7b0e7d3a8ff89ae7d421033886e5d71d102/index.html) @4ac0a7b ([AjmalJamseeth/ajpowerapps](https://github.com/AjmalJamseeth/ajpowerapps/blob/79943a536a53aa0925f9ed5fef281feecc561efb/src/lib/cable.ts) corroborates D1) | Second source for the armoured D1 and D2 columns (4D4A, 4E4A). |
| I | Scans of the printed BS 7671 Tables 4D1B and 4E1B embedded in [amoadel1/IDP1-Electrical-Installation-Design](https://github.com/amoadel1/IDP1-Electrical-Installation-Design/blob/1c11bde/Final/IDP1-Final-Report.pdf) @1c11bde (report pp. 118–119), transcribed by eye. The scans are about 930×600 px, so a single digit could be misread; only cells where they agree with D are used. | Second source for 4D1B at 25 mm² and above. |
| J | Three BS 7671 student design reports that quote Table 4E2B: [Deelaka8](https://github.com/Deelaka8/Electrical_System_Design_for_MultiStorey_Apartment_Building-/tree/eeffd8b) @eeffd8b, [mushrif-rila](https://github.com/mushrif-rila/Electrical-System-Design-for-Three-story-Building/tree/8151c4f) @8151c4f, [ThisaraDhathiya](https://github.com/ThisaraDhathiya) @0986d07. They likely share course notes, so they count as one source. | Second source for 4E2B at 1, 1.5, 2.5, 4 and 16 mm². |
| — | IEC 60364-5-52 B.52.17 transcriptions ([FabriNeves](https://github.com/FabriNeves/Dimensionamento-de-cabos-BT), [llosinskas](https://github.com/llosinskas/Circuits-FreeCAD), [electrical-dev](https://github.com/electrical-dev/WebSite)) | Table 4C1 ladder row, including the 2-circuit factor 0.87. |

Rejected: three repositories whose values were fabricated, called "representative" by their
authors, or scrambled; one OCR of the On-Site Guide that was unreadable; one field-kit
repository listing SWA ratings for methods 4D4A does not have. Neither standard was read in
print; everything here comes from public transcriptions reachable through GitHub.

### Coverage — current ratings (It)

| Table / method | Rows two-source checked | Second source(s) |
|---|---|---|
| 4D1A A / B / C / F | all | IEC via B, and D (F: B) |
| 4D2A A / B / C / E | all | IEC via B, and D |
| 4E2A A / B / C / E | all | IEC via B, and D |
| 4D4A C / E | all | D (and G) |
| 4E4A C / E | all | D, and E for 1.5–120 and 400 mm² |
| 4E4A D1 | all | E for 1.5–120 mm², H for every size |
| 4D4A D1 / D2, 4E4A D2 | all | H |

Every current rating in the file is now two-source checked, and a test
(`Every_capacity_row_is_two_source_checked`) fails if a single-source row is added.

The armoured D1 and D2 values in source A equal the IEC non-armoured multicore D1 and D2
columns cell for cell (104 cells, no mismatch), and the pre-2026 single "D" column in E equals
IEC D1. So for these columns the check confirms that BS 7671 repeats the IEC figures, not that a
second copy of BS 7671 agrees. The D2 column is new in BS 7671 A4:2026 and no earlier BS
transcription can exist for it; that A4 took IEC D2 unchanged rests on source A alone. No BS
transcription independent of A was reachable (manufacturer, distributor, IET and EMSD sites are
blocked by the research network policy). A shared-typo check ruled out two further BS
repositories (G and hiufsitake/EEE both print 472 A for 4D4A D2 300 mm², where A and IEC give
427 A), so neither counts as independent.

One cell disagrees: 4D2A method E, 400 mm², single-phase. D gives 705 A, while A and IEC give
715 A. The row keeps 715 and carries a note.

### Coverage — voltage drop (mV/A/m)

The multicore tables carry the z value (single-phase 2-core, three-phase 3/4-core) at every size.
Multicore voltage drop is not split by reference method, so each value applies to every method
of its capacity table.

| Table | Values from | Two-source checked (`mvVerified`) |
|---|---|---|
| 4D2B | ≤ 16 mm²: earlier data, A and D. ≥ 25 mm²: D only. | ≤ 16 mm² |
| 4D4B | D (≤ 16 mm² also A; ≥ 25 mm² r and x also G) | ≤ 16 mm² (at 25 mm² and above only r and x are two-source, not z) |
| 4E2B | D (J for some cells) | 1, 1.5, 2.5, 4 and 16 mm² (J also agrees on 3-phase z at 10, 25, 35, 50 and 95 mm², but a row needs both columns) |
| 4E4B | D, E, F | ≤ 16 mm², and z at 25–120 and 400 mm² |
| 4D1B | ≤ 16 mm²: A, checked against D. ≥ 25 mm²: D, checked against I. | every size (≤ 16 mm²: A and D agree; ≥ 25 mm²: I and D agree on each value used) |

**Single-core 4D1B.** At 25 mm² and above the table splits by arrangement. Methods A and B take
the enclosed columns (A&B). Methods C and F take single-phase "cables touching" and three-phase
"flat touching"; flat touching is at or above trefoil at every size, so that choice is
conservative. The scan (I) agrees with D on 308 of 318 comparable 4D1B cells and on every 4E1B
cell. It settles the earlier disagreement: A's figures at 25 mm² and above match the printed
table in 1 of 20 cells and are not used. The scan also shows three D typos, none in a column
STING uses (trefoil 400 and 500 mm², flat-spaced 150 mm²).

**Other second sources looked at and not counted.** hiufsitake/EEE agrees with 4D4B z at 25–300
mm², but it shares a typo with G and so is not independent. Osmoore/p2-cable-calculator agrees
with 4D2B at 25–70 mm², but from 95 mm² its r column equals D's d.c. column, which suggests it
was built from D. EEE gives 4D4B 400 mm² single-phase z as 0.185 against D's 0.186;
√(0.115² + 0.145²) = 0.185, so 0.186 is likely a D slip. It is kept because it is the higher
value. No second source was found for 4E4B at 150–300 mm², 4E2B at 70 mm² and at 120 mm² and
above, or 4D2B z at 95 mm² and above.

The 4E2B and 4E4B values up to 16 mm² briefly shipped as **derived** figures: 2 or √3 × the
BS EN 60228 resistance at 90 °C, rounded up. The transcriptions replaced them. Every
transcribed value is at or below the derived bound, and a test keeps it that way
(`Transcribed_xlpe_values_up_to_16mm2_sit_at_or_below_the_resistance_bound`). The 4D2B figures
at 25 mm² and above, entered before any source was found, were replaced by D's values; they had
differed by 0.01–0.02 at 95–240 mm².

### Correction factors

- **Table 4B1:** not changed. Below 30 °C the sizer still takes no uplift. PVC at 25 °C is 1.03
  in BS 7671 (A and D). The 1.06 figure is the IEC 60364-5-52 value, not a transcription error.
- **Table 4C1:** carries the bunched, wall, perforated-tray and ladder rows. The ladder
  2-circuit factor is 0.87 (A and three IEC transcriptions). The 0.88 once reported against it
  is the perforated-tray row.

## IEEE 1584-2018

### Sources

Tables 1, 3, 4 and 5 agree digit for digit across four implementations:

- [LiaungYip/arcflash](https://github.com/LiaungYip/arcflash) (MIT). Its author states the tables
  were copied from the standard.
- [jgrimard/arc-flash-calculator](https://github.com/jgrimard/arc-flash-calculator)
- [Rush2088/PowerSystems_Tools](https://github.com/Rush2088/PowerSystems_Tools)
- [ESYSingenieria/Calculadora-de-Arco-Electrico](https://github.com/ESYSingenieria/Calculadora-de-Arco-Electrico)

Table 2 agrees across four of them. Tables 6 and 7 agree across five.

Two implementations were found with errors, and nothing was taken from them:

- JJ-Trejo/Arco-Electrico has wrong cells in Table 1 and Table 3 VCBB k7.
- BitsOfMehdi rounds seven Table 1 values.

### Functional checks (in `StingTools.Tags.Tests/Ieee1584_2018Tests.cs`)

- **Annex D.1 (4.16 kV) and D.2 (0.48 kV):** every intermediate value printed in the
  standard's worked examples. The published figures come from LiaungYip's transcription.
- **600 rows of the official IEEE 1584-2018 spreadsheet's results:** full and reduced cases,
  60 per electrode configuration at LV and at MV
  (`StingTools.Tags.Tests/TestData/ieee1584_2018_spreadsheet_sample.csv`, from LiaungYip's
  144,000-row set, MIT). The research implementation matched all 105,120 in-range rows.
- **The reduced-case point LiaungYip reported to IEEE DataPort:** there the spreadsheet's
  version 2.6.6 does not apply VarCf to the intermediate currents (7.0153 against
  4.7638 J/cm²). The engine follows the equations, not the spreadsheet bug.
- **Red check:** planting JJ-Trejo's wrong Table 3 value fails 64 of these tests.

### Implementation rules taken from the sources

- In the ≤ 600 V energy and boundary equations, the k3 term uses the full 600 V
  intermediate current in both cases.
- An enclosure is "shallow" only below 0.6 kV (strictly), and only when height and width
  are under 508 mm and depth is at most 203.2 mm.
- Table 6 converts mm to inches with the printed 0.03937; Eq. 11/12 divide by 25.4.
- At exactly 2.7 kV the lower-range interpolation (X3) applies.
- Both cases are computed with their own arc duration; the larger energy and the larger
  boundary are kept.

### Not found

- The standard's own text.
- A maximum enclosure opening area.
- A model limit on arc duration. STING keeps its 2 s cap and says so on the label.
- A typical value for "LV switchboard".
