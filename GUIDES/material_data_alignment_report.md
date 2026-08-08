# Material data alignment — APPLIED

**Totals — 580 class fixes, 34 name normalisations, 1066 cost units inferred.**


## BLE_MATERIALS.csv — 815 rows

- identity class changed: **472**
- identity class still unresolved: **3**
- names normalised: **34**
- `MAT_COST_UNIT_OF_MEASURE` populated: **723**  _(column added)_
- cost unit unresolved: **92**
- empty MAT_ELEMENT_TYPE filled: **0**

**Class moves**

| from | to | rows |
|---|---|---|
| Generic | Ceramic | 67 |
| Paint | Liquid | 56 |
| Generic | Plastic | 50 |
| Generic | Gypsum | 39 |
| Generic | Concrete | 36 |
| Flooring | Plastic | 35 |
| Plaster | Gypsum | 25 |
| Generic | Masonry | 16 |
| Ceiling | Metal | 16 |
| Flooring | Wood | 15 |
| Generic | Insulation | 14 |
| Ceiling | Wood | 13 |
| Ceiling | Plastic | 12 |
| Ceiling | Ceramic | 10 |
| Generic | Metal | 10 |
| Generic | Stone | 9 |
| Ceiling | Insulation | 8 |
| Generic | Wood | 7 |
| Fabric | Plastic | 6 |
| Ceiling | Gypsum | 6 |
| Carpet | Plastic | 4 |
| Ceiling | Liquid | 4 |
| Masonry | Concrete | 3 |
| Flooring | Stone | 3 |
| Wood | Concrete | 2 |
| Generic | Liquid | 2 |
| Flooring | Concrete | 2 |
| Metal | Concrete | 1 |
| Carpet | Ceramic | 1 |

**Sample class decisions**

| material | from | to | why |
|---|---|---|---|
| GYPSUM BOARD STANDARD 9.5MM | Generic | Gypsum | gypsum-based board or plaster |
| GYPSUM BOARD STANDARD 12.5MM | Generic | Gypsum | gypsum-based board or plaster |
| GYPSUM BOARD STANDARD 15MM | Generic | Gypsum | gypsum-based board or plaster |
| FIRE-RATED GYPSUM TYPE X 12.5MM | Generic | Gypsum | gypsum-based board or plaster |
| FIRE-RATED GYPSUM TYPE X 15MM | Generic | Gypsum | gypsum-based board or plaster |
| MOISTURE RESISTANT GYPSUM 12.5MM | Generic | Gypsum | gypsum-based board or plaster |
| MOISTURE RESISTANT GYPSUM 15MM | Generic | Gypsum | gypsum-based board or plaster |
| MOLD RESISTANT GYPSUM 12.5MM | Generic | Gypsum | gypsum-based board or plaster |
| IMPACT RESISTANT GYPSUM 12.5MM | Generic | Gypsum | gypsum-based board or plaster |
| ACOUSTIC GYPSUM BOARD 12.5MM | Generic | Gypsum | gypsum-based board or plaster |

**Sample name normalisations**

| before | after |
|---|---|
| `CLAY BRICK STANDARD (225×112.5×75MM)` | `CLAY BRICK STANDARD (225X112.5X75MM)` |
| `CLAY BRICK VILLAGE-ARTISAN (220×110×65MM)` | `CLAY BRICK VILLAGE-ARTISAN (220X110X65MM)` |
| `CLAY BRICK VILLAGE LARGE (295×150×130MM)` | `CLAY BRICK VILLAGE LARGE (295X150X130MM)` |
| `CLAY BRICK FACING (225×112.5×75MM)` | `CLAY BRICK FACING (225X112.5X75MM)` |
| `CLAY BRICK ENGINEERING (225×112.5×75MM)` | `CLAY BRICK ENGINEERING (225X112.5X75MM)` |
| `CLAY BRICK FIRE-REFRACTORY (225×112.5×75MM)` | `CLAY BRICK FIRE-REFRACTORY (225X112.5X75MM)` |
| `CLAY BRICK HALF (112.5×112.5×75MM)` | `CLAY BRICK HALF (112.5X112.5X75MM)` |
| `CLAY BRICK PERFORATED (225×112.5×75MM)` | `CLAY BRICK PERFORATED (225X112.5X75MM)` |

**Cost units inferred**: `m2` 454, `m` 70, `each` 68, `L` 66, `m3` 53, `kg` 12

**UNRESOLVED classes — left untouched, decide by hand**

| material | current class | category |
|---|---|---|
| MOISTURE RESISTANT BOARD 12.5MM | Generic | MOISTURE RESISTANT |
| KITCHEN SPLASH BACK 20MM | Generic | WATERPROOF SYSTEM |
| TERRACOTTA RAIN SCREEN 30MM | Generic | TERRACOTTA PANELS |

**UNRESOLVED cost units — left blank** (92). First 15:

| material | category |
|---|---|
| PERFORATED METAL ACOUSTIC 15MM | PERFORATED METAL |
| FABRIC WRAPPED PANEL 25MM | FABRIC WRAPPED |
| FABRIC WRAPPED PANEL 50MM | FABRIC WRAPPED |
| ACOUSTIC BAFFLES 50MM | ACOUSTIC BAFFLES |
| ACOUSTIC CLOUDS 50MM | ACOUSTIC CLOUDS |
| METAL GRID ACOUSTIC 15MM | METAL GRID ACOUSTIC |
| PERFORATED ALUMINUM PANEL 0.7MM | PERFORATED METAL |
| CLIP-IN METAL PANEL 0.6MM | CLIP-IN METAL |
| PVC TONGUE & GROOVE 8MM | PVC T&G |
| PVC LAMINATED PANEL 8MM | PVC LAMINATED |
| TRANSLUCENT PVC PANEL 10MM | TRANSLUCENT PVC |
| WOOD VENEER PANEL 6MM | WOOD VENEER |
| EXPOSED T-GRID 25MM | EXPOSED GRID |
| EXPOSED T-GRID 38MM | EXPOSED GRID |
| CONCEALED SPLINE GRID 15MM | CONCEALED GRID |

_Written. Backup at `BLE_MATERIALS.csv.bak`._

## MEP_MATERIALS.csv — 464 rows

- identity class changed: **108**
- identity class still unresolved: **19**
- names normalised: **0**
- `MAT_COST_UNIT_OF_MEASURE` populated: **343**  _(column added)_
- cost unit unresolved: **121**
- empty MAT_ELEMENT_TYPE filled: **5**

**Class moves**

| from | to | rows |
|---|---|---|
| Generic | Metal | 82 |
| Generic | Plastic | 23 |
| Lining | Insulation | 2 |
| Generic | Liquid | 1 |

**Sample class decisions**

| material | from | to | why |
|---|---|---|---|
| INSULATED FLEXIBLE DUCT 152MM | Lining | Insulation | thermal or acoustic insulation, or a mineral board |
| ACOUSTIC MINERAL WOOL 50MM | Lining | Insulation | thermal or acoustic insulation, or a mineral board |
| LED PANEL-600X600 WHITE | Generic | Metal | metal product or predominantly metal fitting |
| LED PANEL-600X600 BLACK | Generic | Metal | metal product or predominantly metal fitting |
| LED PANEL-600X600 CHROME | Generic | Metal | metal product or predominantly metal fitting |
| LED PANEL-600X600 BRUSHED-STEEL | Generic | Metal | metal product or predominantly metal fitting |
| LED PANEL-600X600 BRUSHED-NICKEL | Generic | Metal | metal product or predominantly metal fitting |
| LED PANEL-600X600 MATT-BLACK | Generic | Metal | metal product or predominantly metal fitting |
| LED PANEL-600X600 BRONZE | Generic | Metal | metal product or predominantly metal fitting |
| LED DOWNLIGHT-12W WHITE | Generic | Metal | metal product or predominantly metal fitting |

**Cost units inferred**: `m` 189, `each` 137, `m2` 17

**UNRESOLVED classes — left untouched, decide by hand**

| material | current class | category |
|---|---|---|
| FLEXIBLE DUCT 152MM (6 INCH) | Lining | FLEXIBLE DUCT |
| FLEXIBLE DUCT 203MM (8 INCH) | Lining | FLEXIBLE DUCT |
| FLEXIBLE DUCT 100MM (4 INCH) | Lining | FLEXIBLE DUCT |
| FLEXIBLE DUCT 125MM (5 INCH) | Lining | FLEXIBLE DUCT |
| FLEXIBLE DUCT 250MM (10 INCH) | Lining | FLEXIBLE DUCT |
| BASIN-TAP CHROME | Generic | TAP-BASIN-TAP |
| BASIN-TAP BRUSHED-NICKEL | Generic | TAP-BASIN-TAP |
| BASIN-TAP MATT-BLACK | Generic | TAP-BASIN-TAP |
| BASIN-TAP GOLD | Generic | TAP-BASIN-TAP |
| BATH-TAP CHROME | Generic | TAP-BATH-TAP |
| BATH-TAP BRUSHED-NICKEL | Generic | TAP-BATH-TAP |
| BATH-TAP MATT-BLACK | Generic | TAP-BATH-TAP |
| BATH-TAP GOLD | Generic | TAP-BATH-TAP |
| SHOWER-TAP CHROME | Generic | TAP-SHOWER-TAP |
| SHOWER-TAP BRUSHED-NICKEL | Generic | TAP-SHOWER-TAP |
| SHOWER-TAP MATT-BLACK | Generic | TAP-SHOWER-TAP |
| SHOWER-TAP GOLD | Generic | TAP-SHOWER-TAP |
| HVAC DUCT STAINLESS KITCHEN EXTRACT | Generic | DUCT |
| PIPE SERVICE MARKED HOT WATER | Generic | PIPE |

**UNRESOLVED cost units — left blank** (121). First 15:

| material | category |
|---|---|
| PVC WIRE 1.5MM² SINGLE CORE | PVC BUILDING WIRE |
| PVC WIRE 2.5MM² SINGLE CORE | PVC BUILDING WIRE |
| PVC WIRE 4MM² SINGLE CORE | PVC BUILDING WIRE |
| PVC WIRE 6MM² SINGLE CORE | PVC BUILDING WIRE |
| PVC WIRE 10MM² SINGLE CORE | PVC BUILDING WIRE |
| PVC WIRE 16MM² SINGLE CORE | PVC BUILDING WIRE |
| PVC WIRE 25MM² SINGLE CORE | PVC BUILDING WIRE |
| ACOUSTIC PVC DRAINAGE 110MM | ACOUSTIC DRAINAGE |
| MDB 630A 400V 3-PHASE | MAIN DISTRIBUTION BOARD |
| MDB 800A 400V 3-PHASE | MAIN DISTRIBUTION BOARD |
| DB 250A 400V 3-PHASE | DISTRIBUTION BOARD |
| DB 400A 400V 3-PHASE | DISTRIBUTION BOARD |
| FDB 63A 230V SINGLE PHASE | FINAL DISTRIBUTION BOARD |
| FDB 100A 400V 3-PHASE | FINAL DISTRIBUTION BOARD |
| MCC 630A 400V 3-PHASE | MOTOR CONTROL CENTER |

_Written. Backup at `MEP_MATERIALS.csv.bak`._

## MATERIAL_SCHEMA.json

- declared columns before: **70** (real files carry 72)
- adding: `PROP_CARBON_FOSSIL_KG_M3`, `PROP_CARBON_BIOGENIC_KG_M3`, `MAT_COST_UNIT_OF_MEASURE`
- declared columns after: **73**

_Written. Backup at `MATERIAL_SCHEMA.json.bak`._
