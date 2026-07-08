import csv, re, collections
DATA="StingTools/Data/"
# ---- category sets (Revit display names, as CATEGORY_BINDINGS.csv uses) ----
S={
"HVAC":["Mechanical Equipment","Air Terminals","Ducts","Duct Fittings","Duct Accessories","Duct Insulations","Flex Ducts"],
"HVAC_TERM":["Air Terminals"],
"PLUMB":["Pipes","Pipe Fittings","Pipe Accessories","Flex Pipes","Pipe Insulations","Plumbing Fixtures"],
"FIRE":["Sprinklers","Fire Alarm Devices"],
"ELEC":["Electrical Equipment","Electrical Fixtures","Cable Trays","Cable Tray Fittings","Conduits","Conduit Fittings","Electrical Circuits"],
"CABLE_TRAY":["Cable Trays","Cable Tray Fittings"],"CONDUIT":["Conduits","Conduit Fittings"],
"LIGHT":["Lighting Fixtures","Lighting Devices"],
"DATA":["Data Devices","Communication Devices","Telephone Devices","Security Devices","Nurse Call Devices"],
"STRUCT":["Structural Framing","Structural Columns","Structural Foundations","Structural Rebar","Floors"],
"DOOR":["Doors"],"WINDOW":["Windows"],"WALL":["Walls","Curtain Panels","Curtain Wall Mullions"],
"FLOOR":["Floors"],"CEILING":["Ceilings"],"ROOF":["Roofs"],"STAIR":["Stairs","Railings"],"RAMP":["Ramps"],
"RAILING":["Railings"],"CASEWORK":["Casework"],"FURN":["Furniture","Furniture Systems"],"PARK":["Parking"],
"COLUMN":["Columns","Structural Columns"],"ROOM":["Rooms"],
"FINISH":["Walls","Floors","Ceilings","Roofs","Rooms"],"MATERIAL":["Materials"],
"HEALTH":["Specialty Equipment","Mechanical Equipment","Plumbing Fixtures"],
"UNIVERSAL":["<ALL>"],"SHEET":["Sheets"],"NONE":[],
}
# ---- safe single-discipline prefixes -> set ----
SAFE={"HVC":"HVAC","PLM":"PLUMB","ELC":"ELEC","LTG":"LIGHT","ICT":"DATA","COM":"DATA","MGS":"HEALTH","CLN":"HEALTH","CEQ":"HEALTH","RAD":"HEALTH","FLS":"FIRE"}
# STR handled below (structural)
# ---- BLE sub-prefix -> set (with confirmed remaps) ----
BLE={"DOOR":"DOOR","WINDOW":"WINDOW","WALL":"WALL","FACADE":"WALL","CW":"WALL","PANEL":"WALL","MULLION":"WALL",
"FLR":"FLOOR","FLOOR":"FLOOR","SLAB":"FLOOR","CEILING":"CEILING","CEIL":"CEILING","ROOF":"ROOF","STAIR":"STAIR",
"RAMP":"RAMP","RAILING":"RAILING","RAIL":"RAILING","CASEWORK":"CASEWORK","FURN":"FURN","FURNITURE":"FURN",
"PARK":"PARK","PARKING":"PARK","COLUMN":"COLUMN","ROOM":"ROOM","HEADROOM":"ROOM","STRUCT":"STRUCT","LOAD":"STRUCT","LIVE":"STRUCT",
"FINISH":"FINISH","TILE":"FINISH","PAINT":"FINISH","PLASTER":"FINISH","MORTAR":"FINISH","BRICK":"FINISH","BLOCK":"FINISH","SURFACE":"FINISH",
"MAT":"MATERIAL","MATERIAL":"MATERIAL","CBL":"CABLE_TRAY","ELE":"NONE","ELES":"NONE","SIGN":"NONE"}
CST={"CALC":"FINISH","S":"STRUCT"}  # rollup handled as universal
CST_ROLLUP=set("UNIT TOTAL RATE SUP LABOUR BOQ DUTY FX UG INTL PROC INSTALL FORMWORK EMBODIED TITLE".split())
UNIVERSAL_PREFIX_EXACT=set()  # decided by name pattern below
# ---- description keywords (corroboration only) ----
KW=[(re.compile(p,re.I),d) for p,d in [
 (r"cable tray","CABLE_TRAY"),(r"\bconduit","CONDUIT"),(r"air ?terminal|diffuser|grille","HVAC_TERM"),
 (r"\bdoor\b","DOOR"),(r"\bwindow","WINDOW"),(r"\bwall\b","WALL"),(r"ceiling","CEILING"),(r"\broof|ridge cap","ROOF"),
 (r"\bstair","STAIR"),(r"\bramp","RAMP"),(r"casework|cabinet|worktop","CASEWORK"),(r"parking|car ?park","PARK"),
 (r"plaster|\btile\b|screed|skirting","FINISH"),(r"sprinkler","FIRE"),(r"luminaire|\blux\b","LIGHT")]]
def descdom(d):
    hs={dm for rx,dm in KW if rx.search(d or "")}
    return next(iter(hs)) if len(hs)==1 else None
# ---- load ----
params={}
for line in open(DATA+"MR_PARAMETERS.txt",encoding="utf-8",errors="replace"):
    f=line.rstrip("\n").split("\t")
    if len(f)>=8 and f[0]=="PARAM": params[f[2]]=(f[5],f[7])
catb=collections.defaultdict(set)
for row in csv.reader(open(DATA+"CATEGORY_BINDINGS.csv",encoding="utf-8",errors="replace")):
    if row and not row[0].startswith("#") and row[0]!="Parameter_Name" and len(row)>=2: catb[row[0]].add(row[1])
def resolve(n,desc):
    p=n.split("_"); pre=p[0]; sub=p[1] if len(p)>1 else ""
    # universal identity/tag/ifc/status
    if pre=="ASS" and (sub in ("TAG","DISCIPLINE","LOC","ZONE","LVL","SYSTEM","SYS","FUNC","PRODCT","PROD","SEQ","STATUS","DISPLAY","CAT","DESCRIPTION","SYSTEMS","MODEL","MANUFACTURER") or "TAG" in n): return S["UNIVERSAL"],"universal","HIGH"
    if pre in ("IFC",): return S["UNIVERSAL"],"universal","HIGH"
    if pre=="TAG": return S["NONE"],"annotation-only","HIGH"
    if pre in ("Qto","VT","TB","TBL","SHT"): return S["NONE"],"excluded(revit/view/sheet)","HIGH"
    if pre=="WARN" and len(p)>1:  # mirror the 2nd-token domain
        inner="_".join(p[1:]); return resolve(inner,desc)[0],"warn-mirror","MED"
    if pre in SAFE: 
        if pre=="HVC" and sub=="TERMINAL": return S["HVAC_TERM"],"prefix+sub","HIGH"
        return S[SAFE[pre]],"safe-prefix","HIGH"
    if pre=="STR": return S["STRUCT"],"safe-prefix","HIGH"
    if pre=="MAT": return S["MATERIAL"],"safe-prefix","HIGH"
    if pre=="BLE":
        if sub in BLE: 
            dom=BLE[sub]; return (S[dom],"ble-sub"+("(remap)" if dom in("CABLE_TRAY",) else ""),"MED" if dom!="NONE" else "LOW")
        if n.startswith("BLE_APP-"): return S["MATERIAL"],"ble-material","HIGH"
    if pre=="CST":
        if sub in CST_ROLLUP: return S["UNIVERSAL"],"cost-rollup","MED"
        if sub in CST: return S[CST[sub]],"cst-sub","MED"
    if pre in ("PER","RGL","PRJ","STING","MNT","PMT","VAR","CBN"): return S["UNIVERSAL"],"universal-meta","MED"
    dd=descdom(desc)
    if dd: return S[dd],"description","MED"
    cc=catb.get(n)
    if cc and len(cc)<=12:   # trust curated ONLY when it is a tight, non-polluted set
        return sorted(cc),"curated-fallback","MED"
    if cc:                    # curated exists but is polluted (>12 cats) -> distrust
        return S["NONE"],"UNRESOLVED(polluted-curated)","LOW"
    return S["NONE"],"UNRESOLVED","LOW"
out=[]; conf=collections.Counter(); srcc=collections.Counter()
for n,(g,d) in params.items():
    cats,src,c=resolve(n,d); out.append((n,g,src,c,"|".join(cats),d)); conf[c]+=1; srcc[src]+=1
bound=[o for o in out if o[3] in("HIGH","MED") and o[4] not in("","<ALL>") ] 
univ=[o for o in out if o[4]=="<ALL>"]; unb=[o for o in out if o[3]=="LOW" or o[4]==""]
print("CONFIDENCE:", dict(conf))
print("\nby source:")
for s,c in srcc.most_common(): print("  %-22s %5d"%(s,c))
print("\n=> scoped-bound:%d  universal:%d  UNBOUND(gap/annotation/excluded):%d"%(len(bound),len(univ),len(unb)))
with open("docs/RESOLVED_BINDINGS.csv","w",newline="",encoding="utf-8") as f:
    w=csv.writer(f); w.writerow(["param","group","source","confidence","categories","desc"]); w.writerows(sorted(out))
gaps=[o for o in out if o[2]=="UNRESOLVED"]
with open("docs/binding_gaps.csv","w",newline="",encoding="utf-8") as f:
    w=csv.writer(f); w.writerow(["param","group","desc"]); [w.writerow((o[0],o[1],o[5])) for o in sorted(gaps)]
print("\nUNRESOLVED gaps (need a human domain call, written to docs/binding_gaps.csv):",len(gaps))
print("wrote docs/RESOLVED_BINDINGS.csv (full audit)")
