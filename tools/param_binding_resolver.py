import os,re,csv,collections
# ---- code scan: param -> set(code domains) ----
params={}
for line in open("StingTools/Data/MR_PARAMETERS.txt",encoding="utf-8",errors="replace"):
    f=line.rstrip("\n").split("\t")
    if len(f)>=8 and f[0]=="PARAM": params[f[2]]=(f[5],f[7])
pset=set(params); tok=re.compile(r"[A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+")
def cdomain(p):
    p=p.replace("\\","/").lower()
    if "/commands/hvac" in p or "/core/hvac" in p or "/core/mep/" in p: return "HVAC"
    if "/electrical" in p or "/core/sld" in p or "/lightning" in p: return "ELEC"
    if "/plumbing" in p: return "PLUMB"
    if "/healthcare" in p or "/medgas" in p or "/radiation" in p: return "HEALTH"
    if "/structural" in p or "/model/" in p: return "STRUCT"
    if "/materials" in p or "/core/materials" in p: return "MATERIAL"
    if "/boq/" in p or "/costplan" in p: return "COST"
    return "OTHER"
pcode=collections.defaultdict(collections.Counter)
for dp,_,fs in os.walk("StingTools"):
    if "/obj/" in dp.replace("\\","/") or "/bin/" in dp.replace("\\","/"): continue
    for fn in fs:
        if not fn.endswith(".cs"): continue
        fp=os.path.join(dp,fn)
        try: txt=open(fp,encoding="utf-8",errors="replace").read()
        except: continue
        d=cdomain(fp)
        if d=="OTHER": continue
        for t in set(tok.findall(txt)):
            if t in pset: pcode[t][d]+=1
def code_single(p):
    c=pcode.get(p)
    if not c: return None
    disc={k for k in c if k in ("HVAC","PLUMB","ELEC","HEALTH","STRUCT","MATERIAL")}
    return next(iter(disc)) if len(disc)==1 else None
# ---- category sets ----
S={"HVAC":"Mechanical Equipment|Air Terminals|Ducts|Duct Fittings|Duct Accessories|Duct Insulations|Flex Ducts",
"HVAC_TERM":"Air Terminals","PLUMB":"Pipes|Pipe Fittings|Pipe Accessories|Flex Pipes|Pipe Insulations|Plumbing Fixtures",
"FIRE":"Sprinklers|Fire Alarm Devices","ELEC":"Electrical Equipment|Electrical Fixtures|Cable Trays|Cable Tray Fittings|Conduits|Conduit Fittings|Electrical Circuits",
"CABLE_TRAY":"Cable Trays|Cable Tray Fittings","LIGHT":"Lighting Fixtures|Lighting Devices",
"DATA":"Data Devices|Communication Devices|Telephone Devices|Security Devices|Nurse Call Devices",
"STRUCT":"Structural Framing|Structural Columns|Structural Foundations|Structural Rebar|Floors",
"DOOR":"Doors","WINDOW":"Windows","WALL":"Walls|Curtain Panels|Curtain Wall Mullions","FLOOR":"Floors","CEILING":"Ceilings",
"ROOF":"Roofs","STAIR":"Stairs|Railings","RAMP":"Ramps","RAILING":"Railings","CASEWORK":"Casework","FURN":"Furniture|Furniture Systems",
"PARK":"Parking","COLUMN":"Columns|Structural Columns","ROOM":"Rooms","FINISH":"Walls|Floors|Ceilings|Roofs|Rooms",
"MATERIAL":"Materials","HEALTH":"Specialty Equipment|Mechanical Equipment|Plumbing Fixtures","UNIVERSAL":"<ALL>","NONE":"","MEP_ALL":"Mechanical Equipment|Air Terminals|Ducts|Duct Fittings|Duct Accessories|Flex Ducts|Pipes|Pipe Fittings|Pipe Accessories|Flex Pipes|Plumbing Fixtures|Electrical Equipment|Electrical Fixtures|Cable Trays|Conduits","PEN":"Walls|Floors|Ceilings|Roofs|Generic Models","ARCH":"Walls|Floors|Ceilings|Roofs|Doors|Windows|Columns|Stairs|Ramps|Casework|Furniture|Curtain Panels|Railings|Generic Models|Specialty Equipment","FABX":"Ducts|Duct Fittings|Pipes|Pipe Fittings|Structural Framing|Cable Trays"}
SAFE={"HVC":"HVAC","PLM":"PLUMB","ELC":"ELEC","LTG":"LIGHT","ICT":"DATA","COM":"DATA","MGS":"HEALTH","CLN":"HEALTH","CEQ":"HEALTH","RAD":"HEALTH","FLS":"FIRE"}
BLE={"DOOR":"DOOR","WINDOW":"WINDOW","WALL":"WALL","FACADE":"WALL","CW":"WALL","PANEL":"WALL","MULLION":"WALL","FLR":"FLOOR","FLOOR":"FLOOR","SLAB":"FLOOR","CEILING":"CEILING","CEIL":"CEILING","ROOF":"ROOF","STAIR":"STAIR","RAMP":"RAMP","RAILING":"RAILING","RAIL":"RAILING","CASEWORK":"CASEWORK","FURN":"FURN","FURNITURE":"FURN","PARK":"PARK","PARKING":"PARK","COLUMN":"COLUMN","ROOM":"ROOM","HEADROOM":"ROOM","STRUCT":"STRUCT","LOAD":"STRUCT","LIVE":"STRUCT","FINISH":"FINISH","TILE":"FINISH","PAINT":"FINISH","PLASTER":"FINISH","MORTAR":"FINISH","BRICK":"FINISH","BLOCK":"FINISH","SURFACE":"FINISH","MAT":"MATERIAL","MATERIAL":"MATERIAL","CBL":"CABLE_TRAY"}
CST_ROLLUP=set("UNIT TOTAL RATE SUP LABOUR BOQ DUTY FX UG INTL PROC INSTALL FORMWORK EMBODIED TITLE".split()); CST={"CALC":"FINISH","S":"STRUCT"}
catb=collections.defaultdict(set)
for row in csv.reader(open("StingTools/Data/CATEGORY_BINDINGS.csv",encoding="utf-8",errors="replace")):
    if row and not row[0].startswith("#") and row[0]!="Parameter_Name" and len(row)>=2: catb[row[0]].add(row[1])
def resolve(n,desc,depth=0):
    p=n.split("_"); pre=p[0]; sub=p[1] if len(p)>1 else ""
    if pre=="ASS" and ("TAG" in n or sub in("DISCIPLINE","LOC","ZONE","LVL","SYSTEM","SYS","FUNC","PRODCT","PROD","SEQ","STATUS","DISPLAY","CAT","DESCRIPTION","SYSTEMS","MODEL","MANUFACTURER","ID")): return "UNIVERSAL","universal"
    if pre=="IFC": return "UNIVERSAL","universal"
    if pre=="TAG": return "NONE","annotation-only"
    if pre in("Qto","VT","TB","TBL","SHT"): return "NONE","excluded"
    if pre=="WARN" and len(p)>1 and depth<3:
        return resolve("_".join(p[1:]),desc,depth+1)[0],"warn-mirror"
    if pre in SAFE:
        if pre=="HVC" and sub=="TERMINAL": return "HVAC_TERM","prefix+sub"
        return SAFE[pre],"safe-prefix"
    if pre=="STR": return "STRUCT","safe-prefix"
    if pre=="MAT": return "MATERIAL","safe-prefix"
    if pre=="BLE":
        if n.startswith("BLE_APP-"): return "MATERIAL","ble-material"
        if sub in BLE: return BLE[sub],"ble-sub"
    if pre=="CST":
        if sub in CST_ROLLUP: return "UNIVERSAL","cost-rollup"
        if sub in CST: return CST[sub],"cst-sub"
    if pre in("PER","RGL","PRJ","STING","MNT","PMT","VAR","CBN"): return "UNIVERSAL","universal-meta"
    if pre=="ASS": return "UNIVERSAL","asset-universal"
    if pre in("ACC","AC","Pset","PST","COBIE","COB"): return "UNIVERSAL","interop-universal"
    if pre=="MEP": return "MEP_ALL","mep-generic"
    if pre in("QTO",): return "NONE","excluded"
    if pre=="FAB": return "FABX","fabrication"
    if pre=="PEN": return "PEN","penetration"
    if pre in("SYS","BLD","WS","PH","NRG","SPC","ZON","GEN","CLS","CLASH"): return "UNIVERSAL","meta-universal"
    if pre=="ARCH":
        parts=n.split("_")
        for tk in parts:
            if tk in BLE: return BLE[tk],"arch-sub"
        return "ARCH","arch-generic"
    # curated fallback (tight only)
    cc=catb.get(n)
    if cc and len(cc)<=12: return None,"curated-fallback"  # keep raw curated
    # CODE-USAGE tier for the still-unresolved
    cs=code_single(n)
    if cs: return cs,"code-usage"
    if cc: return "NONE","UNRESOLVED(polluted-curated)"
    return "NONE","UNRESOLVED"
out=[]; src=collections.Counter()
for n,(g,d) in params.items():
    dom,s=resolve(n,d)
    cats = "|".join(sorted(catb[n])) if dom is None else S[dom]
    out.append((n,g,s,cats,d)); src[s]+=1
scoped=sum(1 for o in out if o[3] not in("","<ALL>")); univ=sum(1 for o in out if o[3]=="<ALL>"); unb=sum(1 for o in out if o[3]=="")
gaps=[o for o in out if o[2].startswith("UNRESOLVED")]
print("resolution source:")
for s,c in src.most_common(): print("  %-26s %5d"%(s,c))
print("\nSCOPED:%d  UNIVERSAL:%d  UNBOUND:%d"%(scoped,univ,unb))
print("remaining true gaps:",len(gaps))
with open("docs/RESOLVED_BINDINGS.csv","w",newline="",encoding="utf-8") as f:
    w=csv.writer(f); w.writerow(["param","group","source","categories","desc"]); w.writerows(sorted(out))
with open("docs/binding_gaps.csv","w",newline="",encoding="utf-8") as f:
    w=csv.writer(f); w.writerow(["param","group","desc"]); [w.writerow((o[0],o[1],o[4])) for o in sorted(gaps)]
print("code-usage recovered:",src["code-usage"])
