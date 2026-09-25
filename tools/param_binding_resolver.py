import io,os,re,csv,json,collections
# csv.writer's default lineterminator is CRLF whatever open(newline="") does, so
# the terminator has to be set on the WRITER. All three outputs are pinned to LF
# and to `text eol=lf` in .gitattributes: the drift gate regenerates on Linux and
# diffs, so a Windows-authored CRLF file would fail CI on a tree nobody edited.
LF = "\n"
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
S={"HVAC":"Mechanical Equipment|Air Terminals|Ducts|Duct Fittings|Duct Accessories|Duct Insulation|Flex Ducts",
"HVAC_TERM":"Air Terminals","PLUMB":"Pipes|Pipe Fittings|Pipe Accessories|Flex Pipes|Pipe Insulation|Plumbing Fixtures",
"FIRE":"Sprinklers|Fire Alarm Devices","FIRE_COMPARTMENT":"Fire Alarm Devices|Rooms|Sprinklers","ELEC":"Electrical Equipment|Electrical Fixtures|Cable Trays|Cable Tray Fittings|Conduits|Conduit Fittings|Electrical Circuits",
"CABLE_TRAY":"Cable Trays|Cable Tray Fittings","LIGHT":"Lighting Fixtures|Lighting Devices",
"ELEC_EQUIP":"Electrical Equipment|Electrical Circuits",
"ELEC_FIXTURE":"Electrical Fixtures",
"ELEC_CONDUIT":"Conduits|Conduit Fittings",
"ELEC_TRAY":"Cable Trays|Cable Tray Fittings",
"ELEC_CABLE":"Cable Trays|Conduits|Electrical Circuits",
"ELEC_CIRCUIT":"Electrical Circuits",
"SPACE_ROOM":"MEP Spaces|Rooms",
"ELEC_LPS":"Electrical Equipment|Generic Models","LIGHT_FIX":"Lighting Fixtures","LIGHT_DEV":"Lighting Devices",
"DATA":"Data Devices|Communication Devices|Telephone Devices|Security Devices|Nurse Call Devices",
"STRUCT":"Structural Framing|Structural Columns|Structural Foundations|Structural Rebar|Floors",
"DOOR":"Doors","WINDOW":"Windows","WALL":"Walls|Curtain Panels|Curtain Wall Mullions","FLOOR":"Floors","CEILING":"Ceilings",
"ROOF":"Roofs","STAIR":"Stairs|Railings","RAMP":"Ramps","RAILING":"Railings","CASEWORK":"Casework","FURN":"Furniture|Furniture Systems",
"PARK":"Parking","COLUMN":"Columns|Structural Columns","ROOM":"Rooms","FINISH":"Walls|Floors|Ceilings|Roofs|Rooms",
"MATERIAL":"Materials","SHEET":"Sheets","TITLEBLOCK":"Title Blocks","PROJECT_INFO":"Project Information","HEALTH":"Specialty Equipment|Mechanical Equipment|Plumbing Fixtures|Medical Equipment","MGS_PIPEWORK":"Specialty Equipment|Mechanical Equipment|Plumbing Fixtures|Medical Equipment|Pipes|Pipe Fittings|Pipe Accessories","UNIVERSAL":"<ALL>","NONE":"","MEP_ALL":"Mechanical Equipment|Air Terminals|Ducts|Duct Fittings|Duct Accessories|Flex Ducts|Pipes|Pipe Fittings|Pipe Accessories|Flex Pipes|Plumbing Fixtures|Electrical Equipment|Electrical Fixtures|Cable Trays|Conduits","PEN":"Walls|Floors|Ceilings|Roofs|Generic Models","ARCH":"Walls|Floors|Ceilings|Roofs|Doors|Windows|Columns|Stairs|Ramps|Casework|Furniture|Curtain Panels|Railings|Generic Models|Specialty Equipment","FABX":"Ducts|Duct Fittings|Pipes|Pipe Fittings|Structural Framing|Cable Trays"}
SAFE={"HVC":"HVAC","PLM":"PLUMB","ELC":"ELEC","LTG":"LIGHT","ICT":"DATA","COM":"DATA","MGS":"HEALTH","CLN":"HEALTH","CEQ":"HEALTH","RAD":"HEALTH","FLS":"FIRE"}
BLE={"DOOR":"DOOR","WINDOW":"WINDOW","WALL":"WALL","FACADE":"WALL","CW":"WALL","PANEL":"WALL","MULLION":"WALL","FLR":"FLOOR","FLOOR":"FLOOR","SLAB":"FLOOR","CEILING":"CEILING","CEIL":"CEILING","ROOF":"ROOF","STAIR":"STAIR","RAMP":"RAMP","RAILING":"RAILING","RAIL":"RAILING","CASEWORK":"CASEWORK","FURN":"FURN","FURNITURE":"FURN","PARK":"PARK","PARKING":"PARK","COLUMN":"COLUMN","ROOM":"ROOM","HEADROOM":"ROOM","STRUCT":"STRUCT","LOAD":"STRUCT","LIVE":"STRUCT","FINISH":"FINISH","TILE":"FINISH","PAINT":"FINISH","PLASTER":"FINISH","MORTAR":"FINISH","BRICK":"FINISH","BLOCK":"FINISH","SURFACE":"FINISH","MAT":"MATERIAL","MATERIAL":"MATERIAL","CBL":"CABLE_TRAY","SIGN":"ARCH"}
CST_ROLLUP=set("UNIT TOTAL RATE SUP LABOUR BOQ DUTY FX UG INTL PROC INSTALL FORMWORK EMBODIED TITLE".split()); CST={"CALC":"FINISH","S":"STRUCT"}
catb=collections.defaultdict(set)
# Rows whose Is_Shared column reads "Yes" are HAND-AUTHORED statements of where a
# parameter lives (LPS Wave 1, the Uganda regional defaults, the room/space result
# stamps). The generated bulk of the file says "True". The derivation below never
# looked at a curated row for a parameter a prefix rule could place, so every one
# of these was dropped: the LPS class, mesh size, rolling-sphere radius and Kc that
# LpsClassSetup writes to ProjectInformation went to Electrical Equipment and
# Generic Models only, and the write found no parameter on ProjectInformation.
# "<ALL>" cannot carry them either -- it is the 143 element categories, which hold
# neither Project Information, Rooms' results nor Views.
#
# The rest of the curated file is NOT honoured wholesale: it is polluted (hundreds
# of BLE_*/CST_* rows on Plumbing Equipment, Medical Equipment, Flex Pipes...),
# which is why the derivation exists. The Yes marker is the line between the two.
explicit=collections.defaultdict(set)
for row in csv.reader(open("StingTools/Data/CATEGORY_BINDINGS.csv",encoding="utf-8",errors="replace")):
    if row: row[0]=row[0].lstrip(chr(0xFEFF))
    if row and not row[0].startswith("#") and row[0]!="Parameter_Name" and len(row)>=2:
        catb[row[0]].add(row[1])
        if len(row)>=4 and row[3].strip()=="Yes": explicit[row[0]].add(row[1])
ELC={"PNL":"ELEC_EQUIP","PANEL":"ELEC_EQUIP","PWR":"ELEC_EQUIP","ARC":"ELEC_EQUIP","BUSBAR":"ELEC_EQUIP","ATS":"ELEC_EQUIP","GEN":"ELEC_EQUIP","UPS":"ELEC_EQUIP","SEL":"ELEC_EQUIP","EQP":"ELEC_EQUIP","ENERGY":"ELEC_EQUIP","PHOTO":"LIGHT","LPD":"LIGHT","LIGHTING":"LIGHT","FIX":"ELEC_FIXTURE","JB":"ELEC_FIXTURE","VOLTAGE":"ELEC_FIXTURE","RECEPT":"ELEC_FIXTURE","IT":"ELEC_FIXTURE","SOCKET":"ELEC_FIXTURE","OUTLET":"ELEC_FIXTURE","SPUR":"ELEC_FIXTURE","CDT":"ELEC_CONDUIT","CTR":"ELEC_TRAY","CBT":"ELEC_TRAY","WIRE":"ELEC_CABLE","CBL":"ELEC_CABLE","FEEDER":"ELEC_CABLE","CKT":"ELEC_CIRCUIT","CIR":"ELEC_CIRCUIT","CIRCUIT":"ELEC_CIRCUIT","VLT":"ELEC_CIRCUIT","LPS":"ELEC_LPS","LP":"ELEC_LPS","CONDUIT":"ELEC_CONDUIT","CABLE":"ELEC_CABLE"}
LTG={"CTRL":"LIGHT_DEV","CONTROLS":"LIGHT_DEV","CKT":"ELEC_CIRCUIT"}
CST_MATERIAL=set("ADHESIVE AGGREGATE BLOCK BLOCKS CEMENT GROUT PAINT PRIMER SAND SHEET STEEL TILE FASTENER PLASTER PUTTY MORTAR WATER RIDGE DPC BRICK CONC CONCRETE SCREED RENDER REBAR TIMBER PLYWOOD WATERPROOF NAILS HARDCORE".split())
MGS_PIPEWORK={"MGS_GAS_TYPE_TXT","MGS_ZVB_REF_TXT","MGS_NOM_PRESS_KPA_NR","MGS_DESIGN_FLOW_LPM_NR","MGS_PIPE_BRAZED_BOOL"}
def resolve(n,desc,depth=0):
    p=n.split("_"); pre=p[0]; sub=p[1] if len(p)>1 else ""
    if pre=="ASS" and ("TAG" in n or sub in("DISCIPLINE","LOC","ZONE","LVL","SYSTEM","SYS","FUNC","PRODCT","PROD","SEQ","STATUS","DISPLAY","CAT","DESCRIPTION","SYSTEMS","MODEL","MANUFACTURER","ID")): return "UNIVERSAL","universal"
    if pre=="IFC": return "UNIVERSAL","universal"
    if pre=="TAG": return "NONE","annotation-only"
    # SHT_* binds to Sheets. It sat in the excluded tuple beside the genuinely
    # unbindable prefixes (Qto quantity sets, view/title-block metadata), so every
    # regeneration silently dropped ten sheet parameters that the committed spec
    # bound -- and "absent from the spec" means intentionally UNBOUND, not
    # broad-bound, so they go dark rather than wrong. Only ONE of the ten has a
    # CATEGORY_BINDINGS.csv row to fall back on; the other nine bind nowhere.
    # This must come before the _TAG_ rule so SHT_TAG_1_TXT / SHT_TAG_7_TXT land
    # on Sheets rather than being treated as element tag containers.
    if pre=="SHT": return "SHEET","sheet"
    # TB_* stays excluded, and NOT for the reason the SHT_ note above describes.
    # This was tried: mapping TB_ to "Title Blocks" was committed, regenerated,
    # deployed and run. All 33 parameters came back skipped, because Revit
    # refuses the binding -- OST_TitleBlocks answers false to
    # Category.AllowsBoundParameters, so BuildCategorySet logged "0/1 categories
    # resolved" 33 times and LoadSharedParams reported them under Skipped/failed.
    # No project parameter can ever live on that category; it is a Revit rule,
    # not a gap in this file. (Sheet data belongs on Sheets, which is why SHT_
    # above works and this does not.)
    #
    # Per-title-block state lives in Extensible Storage instead --
    # Core/Storage/StingQrAnchorSchema.cs, the same answer StingViewCropSchema
    # reached for crop stamps. A family CAN still carry TB_QR_ANCHOR_JSON_TXT as
    # a FAMILY parameter authored into the .rfa, and the stamper still prefers
    # that; it just cannot come from here.
    if pre in("Qto","VT","TB","TBL","VIEW"): return "NONE","excluded"
    # A classification code is a property of the thing, not of a discipline, so every
    # classification axis binds universally. CSI was here alone; UNICLASS (Pr/Ss/EF),
    # NBS and the per-element RFI URL are the other four ClassificationReader.Read()
    # consults, and without a rule they fell through to UNRESOLVED and bound NOWHERE.
    if pre in ("CSI","UNICLASS","NBS"): return "UNIVERSAL","classification"
    if n=="ASSET_RFI_URL_TXT": return "UNIVERSAL","classification"
    if pre=="STRUCT":
        if sub=="COL": return "COLUMN","struct-col"
        return "STRUCT","struct"
    if ("_TAG_1_TXT" in n) or ("_TAG_7_PARA" in n) or n.endswith("_TAG"):
        if pre=="ELE" or n.startswith("ELE_FIX"): return "ELEC","tag-elec"
        if pre=="PIP" or "PIPE" in n: return "PLUMB","tag-pipe"
        if pre=="SPK" or "SPRINKLER" in n.upper(): return "FIRE","tag-fire"
        if pre=="SLV" or "SLEEVE" in n.upper(): return "PEN","tag-sleeve"
        if pre=="BLE": return "ARCH","tag-arch"
        return "UNIVERSAL","tag-container"
    if pre in("FOHLIO","PROJECT","MOUNTING","USAGE","INS"): return "UNIVERSAL","misc-meta"
    if pre=="WARN" and len(p)>1 and depth<3:
        return resolve("_".join(p[1:]),desc,depth+1)[0],"warn-mirror"
    # A fire compartment is a property of the SPACE first. FLS_ alone binds to
    # sprinklers and detectors, so FLS_COMPARTMENT_ID_TXT reached every device in a
    # compartment and no room in it -- while the fls-compartment-id filter (OST_Rooms),
    # the RDS validator and the Fire Compartment Tag all read it from Rooms.
    if n.startswith("FLS_COMPARTMENT_") and depth==0: return "FIRE_COMPARTMENT","fls-compartment"
    # The installation's earthing arrangement and MET location are facts about the
    # whole supply, read off ProjectInformation by the earthing diagram. Named
    # exactly, and ahead of the ELC_ prefix rule: a sub-token rule (EARTHING ->
    # project) would also move the WARN_ELC_EARTHING_* mirrors, which describe
    # equipment.
    if n in ("ELC_EARTHING_SYSTEM_TXT","ELC_MET_LOCATION_TXT"): return "PROJECT_INFO","project-level"
    # Board-level facts read only off Electrical Equipment (Dual-Source / SLD feed
    # type, IPS Validation's LIM flag). The broad ELEC set would put them on every
    # conduit and tray.
    if n in ("ELC_FEED_TYPE_TXT","ELC_IPS_LIM_BOOL"): return "ELEC_EQUIP","board-level"
    # A load-profile space type describes a space, not HVAC plant: Block Load and
    # the cross-talk audit read it on MEP Spaces, Block Load and ComCheck on Rooms.
    if n=="HVC_SPACE_TYPE_TXT": return "SPACE_ROOM","space-level"
    # Medical gas travels in pipes. MgasNetwork builds each gas's network from
    # pipes, fittings and accessories keyed on MGS_GAS_TYPE_TXT (and finds zone
    # valve boxes by MGS_ZVB_REF_TXT on an accessory); MgasFlowValidator checks
    # pressure and flow on the same elements. Under the HEALTH set none of them
    # reached a pipe, so every network was terminal units with nothing between.
    if n in MGS_PIPEWORK: return "MGS_PIPEWORK","mgs-pipework"
    if pre in SAFE:
        if pre=="HVC" and sub=="TERMINAL": return "HVAC_TERM","prefix+sub"
        if pre=="ELC" and sub in ELC: return ELC[sub],"elc-sub"
        if pre=="LTG": return LTG.get(sub,"LIGHT_FIX"),"ltg-sub"
        return SAFE[pre],"safe-prefix"
    if pre=="STR": return "STRUCT","safe-prefix"
    if pre=="MAT": return "MATERIAL","safe-prefix"
    if pre=="BLE":
        if n.startswith("BLE_APP-"): return "MATERIAL","ble-material"
        if sub in BLE: return BLE[sub],"ble-sub"
    if pre=="CST":
        if sub in CST: return CST[sub],"cst-sub"
        if set(n.split("_")) & CST_MATERIAL: return "FINISH","cst-material"
        return "UNIVERSAL","cost-meta"
    # PRJ_ORG_* are project-level facts -- originator code, project code,
    # appointing party, RIBA stage -- and they live on ProjectInformation. They
    # were caught by the blanket PRJ rule below and marked <ALL>.
    #
    # <ALL> does not mean "every category". It means PARAMETER_REGISTRY.json's
    # universal_categories: 143 ELEMENT categories, containing neither Project
    # Information nor Sheets. (Sheets reaches the core set only because
    # LoadSharedParams explicitly inserts OST_Sheets; nothing inserts
    # OST_ProjectInformation.) So PRJ_ORG_ORIGINATOR_CODE_TXT -- whose own
    # description reads "Originator code from Project Information" -- was bound to
    # walls, ducts and doors and to no ProjectInformation element anywhere. The
    # dialog that is supposed to hold it showed nothing, it could not be typed in,
    # and every reader of it got an empty string and fell back to a guess.
    if n.startswith("PRJ_ORG_"): return "PROJECT_INFO","project-level"
    # Title-block and sheet-identity parameters belong on Sheets. Marked <ALL>
    # they landed on 143 ELEMENT categories -- every wall, duct and door carried
    # PRJ_TB_DRAWN_BY_TXT in its Properties palette -- and reached Sheets only
    # because LoadSharedParams inserts OST_Sheets into the core set by hand.
    #
    # Sheets is the home that matters and the only one any of these is read from.
    # This is a NARROWING, so it changes nothing in a project that has already
    # bound them: the loader adds missing categories and never removes one, since
    # taking a parameter off elements could break a schedule or tag that depends
    # on it. New projects and templates get the tight set.
    if n.startswith(("PRJ_TB_","PRJ_SHEET_","PRJ_DWG_")) or n == "PRJ_STATUS_COD_TXT":
        return "SHEET","sheet-identity"
    if pre in("PER","RGL","PRJ","STING","MNT","PMT","VAR","CBN"): return "UNIVERSAL","universal-meta"
    if pre=="ASS": return "UNIVERSAL","asset-universal"
    if pre in("ACC","AC","Pset","PST","COBIE","COB"): return "UNIVERSAL","interop-universal"
    if pre=="MEP": return "MEP_ALL","mep-generic"
    if pre in("QTO",): return "NONE","excluded"
    if pre=="FAB":
        if sub=="DCT": return "HVAC","fab-duct"
        if sub=="PIPE": return "PLUMB","fab-pipe"
        return "FABX","fabrication"
    if pre=="PEN": return "PEN","penetration"
    if pre in("SYS","BLD","WS","PH","NRG","SPC","ZON","GEN","CLS","CLASH","ASBUILT","COMM","SUST","HANDOVER","EV","MEC","RNV","PV","RMP","ARC"): return "UNIVERSAL","meta-universal"
    if n.startswith("BLE_ELE_") or n=="AREA_SQ_M": return "UNIVERSAL","generic-geom"
    if pre=="ARCH":
        parts=n.split("_")
        for tk in parts:
            if tk in BLE: return BLE[tk],"arch-sub"
        return "ARCH","arch-generic"
    # curated fallback (tight only). Tag-family keys are stripped from what the
    # curated row contributes -- see TAG_FAMILY_KEYS below.
    cc=catb.get(n)
    if cc and len(cc)<=12: return None,"curated-fallback"  # keep raw curated
    # CODE-USAGE tier for the still-unresolved
    cs=code_single(n)
    if cs: return cs,"code-usage"
    if cc: return "NONE","UNRESOLVED(polluted-curated)"
    return "NONE","UNRESOLVED"
# ── The Materials cross-check ───────────────────────────────────────────────
# "Materials" in RESOLVED_BINDINGS.csv is DROPPED by SharedParamGuids when it loads
# the spec -- OST_Materials is a pseudo-category there. Material binding happens by
# a different mechanism entirely: LoadSharedParamsCommand.IsMaterialRelevantParam
# selects parameters BY NAME PREFIX and CleanMaterialBindings binds those.
#
# So a Materials row is honoured only if that C# rule also recognises the parameter.
# One it does not recognise sits in the spec looking bound and binds to NOTHING --
# and "absent from the spec" means intentionally unbound, so nothing downstream
# reports it. That is the same failure the C# comment already records for
# BLE_MATERIAL_TXT, which is why that name is an explicit exception there.
#
# The prefixes are READ OUT OF THE C# SOURCE, not copied here. A second copy of the
# list is the defect class this generator exists to remove, and a mirror would rot
# the first time someone edits the C# and not this file.
def material_prefixes():
    src_path = "StingTools/Tags/LoadSharedParamsCommand.cs"
    text = io.open(src_path, encoding="utf-8", errors="replace").read()
    # Anchored on the opening paren. A bare name find() PREFIX-MATCHES a renamed
    # method -- IsMaterialRelevantParamRenamed still contains it -- so the reader
    # would parse a method that no longer exists under that name and report a
    # confident prefix list from it. Found by sabotaging this check with exactly
    # that rename, which passed until the anchor was added.
    start = text.find("private static bool IsMaterialRelevantParam(")
    if start < 0:
        raise SystemExit(
            "IsMaterialRelevantParam not found in " + src_path + " -- fix this reader "
            "rather than copying the prefix list, or the two will drift.")
    end = text.index("\n        }", start)
    body = text[start:end]
    prefixes = re.findall(r'StartsWith\("([^"]+)"', body)
    exact = re.findall(r'paramName == "([^"]+)"', body)
    if not prefixes:
        raise SystemExit(
            "no StartsWith prefixes parsed out of IsMaterialRelevantParam -- the "
            "method shape changed. Fix this reader; an empty prefix list would let "
            "the check below pass by recognising nothing.")
    return prefixes, exact

MAT_PREFIXES, MAT_EXACT = material_prefixes()
def material_relevant(name):
    return any(name.startswith(px) for px in MAT_PREFIXES) or name in MAT_EXACT

# Every explicit category must be one the plugin can resolve. SharedParamGuids
# drops a name missing from category_enum_map without a word, so a typo here would
# look bound in the spec and bind nowhere -- the exact failure the marker exists to
# end. Materials is exempt: it is bound by name prefix, and checked further down.
_enum_map = json.load(open("StingTools/Data/PARAMETER_REGISTRY.json", encoding="utf-8-sig"))["category_enum_map"]
_bad_explicit = sorted("%s -> %s" % (n, c) for n, cs in explicit.items() for c in cs
                       if c != "Materials" and c not in _enum_map)
if _bad_explicit:
    raise SystemExit("explicit (Yes) CATEGORY_BINDINGS rows name categories that "
                     "PARAMETER_REGISTRY.json category_enum_map does not know, so they "
                     "would bind nowhere:\n  " + "\n  ".join(_bad_explicit[:20]))
_orphan_explicit = sorted(n for n in explicit if n not in params)
if _orphan_explicit:
    raise SystemExit("explicit (Yes) CATEGORY_BINDINGS rows name parameters that are not "
                     "defined in MR_PARAMETERS.txt:\n  " + "\n  ".join(_orphan_explicit[:20]))

# ── Tag-family keys are not categories ──────────────────────────────────────
# CATEGORY_BINDINGS.csv names some LABEL_DEFINITIONS.json tag-family keys in its
# category column -- "MEP Sleeve", "Anti-Ligature (Door)" -- because
# tools/check_tag_row_bindings.py checks a tag family's label rows against the
# family's key. They are not Revit categories: SharedParamGuids.EnsureResolved
# finds no BuiltInCategory for them and drops them. Every row that names one also
# names the real category (Generic Models, Doors, ...), so dropping the key from
# the SPEC loses no binding; keeping it in the spec made a row look bound to a
# category nothing could deliver (ROADMAP PARAM-9). Only keys that ARE tag
# families are stripped -- any other unknown name fails below.
_label_keys = set((json.load(open("StingTools/Data/LABEL_DEFINITIONS.json", encoding="utf-8-sig"))
                   .get("category_labels") or {}).keys())
TAG_FAMILY_KEYS = {k for k in _label_keys if k not in _enum_map and k != "Materials"}
def _real(names):
    return [c for c in names if c not in TAG_FAMILY_KEYS]

# ── "<ALL> plus" cells ──────────────────────────────────────────────────────
# A categories cell may read "<ALL>|Project Information": the universal set (the
# 143 element categories in universal_categories, plus Sheets, which the loader
# inserts) AND categories outside it. SharedParamGuids.EnsureResolved reads the
# first token as the universal marker and the rest as extra categories. Plain
# "<ALL>" still means exactly what it did.
def cell_parse(c):
    toks = c.split("|")
    return ("<ALL>" in toks), [t for t in toks if t and t != "<ALL>"]
def cell_fmt(uni, extras):
    ex = sorted(set(extras))
    return "|".join((["<ALL>"] if uni else []) + ex)

# ── Project-level parameters reach Project Information (ROADMAP PARAM-6) ──────
# PRJ_* are facts about the project -- name, address, phase, climate site,
# refrigerant defaults -- and their readers go to doc.ProjectInformation. Marked
# <ALL> they bound to 143 element categories and not to Project Information, which
# is not one of them, so every one of those reads returned nothing. The sheet
# identity family (PRJ_TB_*, PRJ_SHEET_*, PRJ_DWG_*, PRJ_STATUS_COD_TXT) stays on
# Sheets. Additive: an <ALL> parameter keeps <ALL> and gains Project Information,
# because a project that has put a PRJ_ value on elements keeps it.
def is_project_level(n):
    return (n.startswith("PRJ_")
            and not n.startswith(("PRJ_TB_", "PRJ_SHEET_", "PRJ_DWG_"))
            and n != "PRJ_STATUS_COD_TXT")
_project_info_added = []

out=[]; src=collections.Counter(); _explicit_added=0
for n,(g,d) in params.items():
    dom,s=resolve(n,d)
    cats = "|".join(sorted(_real(catb[n]))) if dom is None else S[dom]
    ex = explicit.get(n)
    if ex:
        if cats == "<ALL>":
            # An explicit home beats a blanket prefix rule: <ALL> cannot be
            # combined with a category outside the universal set (the loader reads
            # the cell as one token), and a parameter someone placed by hand on
            # Views or Project Information has no business on 143 element categories.
            cats = "|".join(sorted(ex)); s = "explicit"
        else:
            have = [c for c in cats.split("|") if c]
            extra = sorted(ex - set(have))
            if extra:
                cats = "|".join(have + extra); _explicit_added += len(extra)
    if is_project_level(n) and cats:
        _u, _ex = cell_parse(cats)
        if "Project Information" not in _ex:
            cats = cell_fmt(_u, _ex + ["Project Information"]) if _u else "|".join(
                [c for c in cats.split("|")] + ["Project Information"])
            _project_info_added.append(n)
    out.append((n,g,s,cats,d)); src[s]+=1
# FAIL rather than write a row that claims a binding nothing delivers.
mat_orphans=[o[0] for o in out if o[3]=="Materials" and not material_relevant(o[0])]
if mat_orphans:
    raise SystemExit(
        "%d parameter(s) resolve to Materials but IsMaterialRelevantParam does not "
        "recognise them, so they would bind to NOTHING while the spec says they are "
        "bound:\n  %s\n"
        "Either give them a real category in resolve(), or add their prefix to "
        "IsMaterialRelevantParam in StingTools/Tags/LoadSharedParamsCommand.cs."
        % (len(mat_orphans), "\n  ".join(sorted(mat_orphans)[:20])))
# The same lie in the INPUT. A "Materials" row in CATEGORY_BINDINGS.csv for a parameter
# IsMaterialRelevantParam does not recognise is dropped at load (CleanMaterialBindings
# removes Materials from every non-material parameter), so the row claims a binding that
# never happens. 56 such rows sat there - mostly WARN_* parameters bound to a broad list
# that happened to include Materials - and the check above missed them because those
# parameters resolve to more than Materials. A material TAG reading one prints blank.
stray_mat=sorted(n for n,cs in catb.items() if "Materials" in cs and not material_relevant(n))
if stray_mat:
    raise SystemExit(
        "%d CATEGORY_BINDINGS.csv row(s) bind a non-material parameter to Materials, which "
        "CleanMaterialBindings strips at load:\n  %s\n"
        "Remove the Materials row, or add the prefix to IsMaterialRelevantParam in "
        "StingTools/Tags/LoadSharedParamsCommand.cs if it really is a material property."
        % (len(stray_mat), "\n  ".join(stray_mat[:20])))
scoped=sum(1 for o in out if o[3]!="" and not cell_parse(o[3])[0]); univ=sum(1 for o in out if cell_parse(o[3])[0]); unb=sum(1 for o in out if o[3]=="")
gaps=[o for o in out if o[2].startswith("UNRESOLVED")]
print("resolution source:")
for s,c in src.most_common(): print("  %-26s %5d"%(s,c))
print("\nSCOPED:%d  UNIVERSAL:%d  UNBOUND:%d"%(scoped,univ,unb))
print("remaining true gaps:",len(gaps))

# ── never narrow what a gate has widened ────────────────────────────────────
#
# This script DERIVES bindings. It does not know the two rules that other gates
# enforce, and both of them WIDEN a parameter's category list:
#
#   * every parameter a tag label displays must be bound to the category that
#     family tags, or the label renders blank (095c9c8eb: 128 of 206 families
#     had at least one such row);
#   * every schedule field must be bound to its schedule's category, or the
#     column renders empty (304a132f2).
#
# Regenerating from scratch therefore DROPS those widenings. Measured
# 2026-09-22: it removed 251 rows' worth of categories, and the Revit-free
# gates immediately reported 340 label parameters across 120 families unable to
# reach their own category and 197 empty schedule columns. A green
# regenerate-is-a-noop bought by deleting those is worse than a red one.
#
# So a category present in the committed file is KEPT. The derivation may add,
# never remove. The drift gate still catches a resolver that invents a binding,
# which is the direction that needs catching - a wrongly ADDED binding is a
# parameter on a category that should not carry it, and nothing else would see
# it.
_prev = {}
try:
    with open("StingTools/Data/RESOLVED_BINDINGS.csv", newline="", encoding="utf-8") as _f:
        for _row in csv.reader(_f):
            if len(_row) >= 2 and not _row[0].startswith("#"):
                # A tag-family key in the committed spec is not a category (see
                # TAG_FAMILY_KEYS); carrying it forward would re-add it forever.
                _prev[_row[0]] = "|".join(_real(_row[1].split("|")))
except FileNotFoundError:
    pass

_widened = 0
for _i, _o in enumerate(out):
    _n, _g, _srcx, _cats, _d = _o
    _was = _prev.get(_n)
    if not _was or _was == _cats:
        continue
    if set(_cats.split("|")) <= set(_was.split("|")):
        # Nothing new, possibly in a different order: keep the committed
        # spelling, so a change to HOW a set is derived does not show up as a
        # binding diff when the binding itself has not moved.
        if set(_cats.split("|")) != set(_was.split("|")): _widened += 1
        out[_i] = (_n, _g, _srcx, _was, _d)
        continue
    _wu, _we = cell_parse(_was); _cu, _ce = cell_parse(_cats)
    if _wu or _cu:
        # <ALL> is the widest there is; never trade it for a list. The list
        # side's categories are dropped as before (they are element
        # categories <ALL> already covers), except that the extras of an
        # "<ALL>|..." cell on EITHER side are kept.
        _merged = cell_fmt(True, (_we if _wu else []) + (_ce if _cu else []))
        if _merged != _cats:
            out[_i] = (_n, _g, _srcx, _merged, _d)
            _widened += 1
        continue
    _union = sorted(set(_was.split("|")) | set(_cats.split("|")))
    if len(_union) > len(_cats.split("|")):
        out[_i] = (_n, _g, _srcx, "|".join(_union), _d)
        _widened += 1

print("kept wider committed bindings on %d parameter(s)" % _widened)

# Regression gate for the Yes marker: every hand-authored home must be in the
# row that ships. This is what went missing for the LPS / regional project-level
# parameters, and nothing reported it, because "absent" reads as "unbound on
# purpose". A later rule that drops one fails here instead.
_final = {o[0]: o[3] for o in out}
_lost = sorted("%s -> %s" % (n, c) for n, cs in explicit.items() for c in cs
               if c != "Materials" and c not in _final.get(n, "").split("|"))
if _lost:
    raise SystemExit("explicit (Yes) CATEGORY_BINDINGS homes missing from the generated "
                     "spec:\n  " + "\n  ".join(_lost[:20]))

# Every category in every row that ships must be one the plugin can resolve.
# SharedParamGuids.EnsureResolved skips an unknown name (it now logs it), so a row
# naming only an unknown category binds nowhere while the spec says it is bound.
# Checked on the OUTPUT, whatever route put the name there -- derivation, a
# curated fallback, an explicit row, or the keep-wider-committed step.
# Materials is bound by name prefix (checked above); an empty token is ignored by
# the loader.
_unknown = sorted("%s -> %s" % (o[0], c) for o in out for c in o[3].split("|")
                  if c and c not in ("<ALL>", "Materials") and c not in _enum_map)
if _unknown:
    raise SystemExit("%d emitted binding(s) name a category PARAMETER_REGISTRY.json "
                     "category_enum_map does not know, so they would bind nowhere:\n  %s"
                     % (len(_unknown), "\n  ".join(_unknown[:20])))
_misplaced_all = sorted(o[0] for o in out if "<ALL>" in o[3].split("|")[1:])
if _misplaced_all:
    raise SystemExit("<ALL> must be the first token of a categories cell (the loader "
                     "reads it there):\n  " + "\n  ".join(_misplaced_all[:20]))

with open("docs/RESOLVED_BINDINGS.csv","w",newline="",encoding="utf-8") as f:
    w=csv.writer(f, lineterminator=LF); w.writerow(["param","group","source","categories","desc"]); w.writerows(sorted(out))
with open("docs/binding_gaps.csv","w",newline="",encoding="utf-8") as f:
    w=csv.writer(f, lineterminator=LF); w.writerow(["param","group","desc"]); [w.writerow((o[0],o[1],o[4])) for o in sorted(gaps)]
with open("StingTools/Data/RESOLVED_BINDINGS.csv","w",newline="",encoding="utf-8") as f:
    w=csv.writer(f, lineterminator=LF); w.writerow(["# Parameter_Name","Categories(pipe)|<ALL>=universal"])
    for n,g,srcx,cats,d in sorted(out):
        if cats!="": w.writerow([n,cats])
print("material rows cross-checked against IsMaterialRelevantParam: "
      "%d prefix(es), %d exact, %d row(s), 0 orphans"
      % (len(MAT_PREFIXES), len(MAT_EXACT),
         sum(1 for o in out if o[3]=="Materials")))
print("code-usage recovered:",src["code-usage"])
print("explicit (Yes) categories added on top of the derivation:",_explicit_added)
print("project-level (PRJ_) parameters given Project Information:",len(_project_info_added))
print("tag-family keys stripped from category lists:",len(TAG_FAMILY_KEYS),"known")
print("wrote StingTools/Data/RESOLVED_BINDINGS.csv (deployable)")
