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

Rejected: three repositories whose values were fabricated, called "representative" by their
authors, or scrambled; one OCR of the On-Site Guide that was unreadable.

### Coverage

| Table / method | It rows two-source checked | Notes |
|---|---|---|
| 4D2A C | 18/18 | Already shipped. 1.5–400 mm² agree with IEC C; 1.0 mm² agrees with the previously checked data (IEC has no 1.0 mm² row). The ≤ 16 mm² 4D2B values match A exactly. |
| 4D2A A | 17/17 | Matches IEC A2. |
| 4D2A B | 12/18 | 1.0 mm² and 150–400 mm² differ: A is lower than IEC. |
| 4D2A E | 16/18 | 1.0 and 1.5 mm² three-phase differ by 0.5 A. |
| 4D1A A | 15/17 | 2.5 mm² single-phase: A 20, IEC 19.5. |
| 4D1A B | 12/18 | 1.0 mm² and 150–400 mm² differ. |
| 4D1A C | 0/18 | No IEC single-core C column: single source. |
| 4D1A F | 11/11 | The three-phase column is the IEC flat-touching one (114 A at 25 mm²), not trefoil. |
| 4E2A A / B / C / E | 16/17, 13/18, 17/18, 17/18 | Differences at 1.0 mm², 150–400 mm² (B) and 400 mm². |
| 4D4A, 4E4A (C, E, D1, D2) | 0 | IEC has no armoured tables: single source, every result VERIFY. |

Voltage drop:

- **Two-source checked:** 4D2B ≤ 16 mm² only (the existing data and A agree).
- **Single source:** 4D1B and 4D4B ≤ 16 mm².
- **Derived, not transcribed (2026-09-27):** 4E2B and 4E4B ≤ 16 mm². No source for either
  table was reachable, so each value is calculated as 2 (single-phase) or √3 (three-phase) ×
  the BS EN 60228 conductor resistance at 20 °C (the `VoltageDropEngine` table), corrected to
  90 °C with α = 0.00393 /K, and rounded **up** to two significant figures.
  - Evidence that this errs on the safe side: the same rule at 70 °C never falls below the
    two-source-checked 4D2B. Of those 14 values, 9 are equal and the rest are up to 9 % higher
    (4 mm² single-phase: 12 against 11).
  - A result on one of these rows says "DERIVED, NOT TRANSCRIBED" and VERIFY.
  - `Shipped_XLPE_voltage_drop_is_exactly_the_derivation_and_never_verified` pins every value
    to the rule. Replace them with the printed tables when available.
- **Not carried:** every value ≥ 25 mm² for the new tables. Above 16 mm² the tables depend
  on reactance, which is not derived, so those sizes are refused.

Table 4B1 is unchanged; the sub-30 °C rows are still not carried, and PVC at 25 °C
disagrees between sources (1.03 against 1.06). Table 4C1 gains the perforated-tray and
ladder rows. The 2-circuit ladder factor is omitted because the sources give 0.87 and 0.88,
so 2 circuits read the lower 3-circuit value.

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
