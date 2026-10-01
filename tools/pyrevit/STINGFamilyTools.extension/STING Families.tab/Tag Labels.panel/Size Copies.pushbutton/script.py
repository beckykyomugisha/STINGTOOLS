# -*- coding: utf-8 -*-
"""Size copies + door boxes for the hand-built STING tag families.

For every OPEN family document named "STING - <Fire Door|Accessible Door|Room Finish|Fire Compartment> Tag":
  * the 2.5 mm label's Visible is tied to TXT_2_5;
  * a 3.5 mm copy of it is made at the same place, typed "3.5mm", Visible tied to TXT_3_5;
  * door tags get a Tag Box rectangle 1 mm clear of each label, each line tied to the same TXT_* switch;
  * the family is saved.
Idempotent: a label or box already tied to a switch is left alone.
"""
from Autodesk.Revit.DB import (FilteredElementCollector, TextElement, TextNote, TextElementType,
                               ElementTransformUtils, XYZ, Line, Transaction, BuiltInParameter,
                               View, GraphicsStyleType, CurveElement, ElementId)

MM = 1.0 / 304.8
TARGETS = ["Fire Door", "Accessible Door", "Room Finish", "Fire Compartment"]
SIZES = [("2.5", "TXT_2_5", "2.5mm"), ("3.5", "TXT_3_5", "3.5mm")]

def log(msg):
    print(msg)

def fparam(fm, name):
    for p in fm.Parameters:
        if p.Definition.Name == name:
            return p
    return None

def associated_ids(fp):
    ids = set()
    if fp is None:
        return ids
    for ep in fp.AssociatedParameters:
        ids.add(ep.Element.Id.IntegerValue)
    return ids

def labels_of(doc):
    out = []
    for e in FilteredElementCollector(doc).WhereElementIsNotElementType():
        if isinstance(e, TextElement) and not isinstance(e, TextNote):
            out.append(e)
    return out

def size_mm(doc, lab):
    t = doc.GetElement(lab.GetTypeId())
    p = t.get_Parameter(BuiltInParameter.TEXT_SIZE) if t else None
    return round(p.AsDouble() / MM, 2) if p else None

def type_name(t):
    # IronPython cannot read ElementType.Name (AttributeError: Name - the property is
    # hidden by the ElementType overload); go through the base getter, then the parameter.
    try:
        from Autodesk.Revit.DB import Element
        return Element.Name.GetValue(t)
    except Exception:
        p = t.get_Parameter(BuiltInParameter.SYMBOL_NAME_PARAM)
        return p.AsString() if p else None

def label_type(doc, base_type, name, mm):
    for t in FilteredElementCollector(doc).OfClass(TextElementType):
        if type_name(t) == name and t.Category is not None and base_type.Category is not None \
           and t.Category.Id == base_type.Category.Id:
            return t
    nt = base_type.Duplicate(name)
    nt.get_Parameter(BuiltInParameter.TEXT_SIZE).Set(mm * MM)
    return nt

def family_view(doc):
    for v in FilteredElementCollector(doc).OfClass(View):
        if not v.IsTemplate:
            return v
    return None

def tag_box_style(doc):
    cat = doc.OwnerFamily.FamilyCategory
    sub = None
    for s in cat.SubCategories:
        if s.Name == "Tag Box":
            sub = s
    if sub is None:
        sub = doc.Settings.Categories.NewSubcategory(cat, "Tag Box")
        sub.SetLineWeight(1, GraphicsStyleType.Projection)
    return sub.GetGraphicsStyle(GraphicsStyleType.Projection)

def associate(fm, elem, fp):
    vp = elem.get_Parameter(BuiltInParameter.IS_VISIBLE_PARAM)
    if vp is not None and fp is not None and fm.CanElementParameterBeAssociated(vp):
        fm.AssociateElementParameterToFamilyParameter(vp, fp)
        return True
    return False

def process(doc):
    fm = doc.FamilyManager
    sw = dict((s, fparam(fm, n)) for s, n, _ in SIZES)
    if sw["2.5"] is None or sw["3.5"] is None:
        log("  SKIP: no TXT_2_5 / TXT_3_5 switch (not made by Create Tag Fams)")
        return False
    labs = labels_of(doc)
    if not labs:
        log("  SKIP: no label - build the label first")
        return False
    by_size = {}
    for l in labs:
        by_size.setdefault(str(size_mm(doc, l)), []).append(l)
    log("  labels by size: " + ", ".join("%s mm x%d" % (k, len(v)) for k, v in by_size.items()))
    base = (by_size.get("2.5") or labs)[0]
    is_door = doc.OwnerFamily.FamilyCategory.Name.startswith("Door")
    view = family_view(doc)
    t = Transaction(doc, "STING size copies + tag box")
    t.Start()
    try:
        done = {}
        changed = []
        # 2.5 mm: the base label
        if base.Id.IntegerValue not in associated_ids(sw["2.5"]):
            associate(fm, base, sw["2.5"])
            log("  2.5 mm label -> TXT_2_5")
            changed.append(1)
        done["2.5"] = base
        # 3.5 mm: reuse an existing 3.5 label, else copy the base
        l35 = (by_size.get("3.5") or [None])[0]
        if l35 is None:
            new_id = list(ElementTransformUtils.CopyElement(doc, base.Id, XYZ.Zero))[0]
            l35 = doc.GetElement(new_id)
            l35.ChangeTypeId(label_type(doc, doc.GetElement(base.GetTypeId()), "3.5mm", 3.5).Id)
            log("  3.5 mm copy created")
            changed.append(1)
        if l35.Id.IntegerValue not in associated_ids(sw["3.5"]):
            associate(fm, l35, sw["3.5"])
            log("  3.5 mm label -> TXT_3_5")
            changed.append(1)
        done["3.5"] = l35
        # Door box
        if is_door and view is not None:
            style = tag_box_style(doc)
            doc.Regenerate()
            for s, name, _ in SIZES:
                fp = sw[s]
                have = [e for e in FilteredElementCollector(doc).OfClass(CurveElement)
                        if e.Id.IntegerValue in associated_ids(fp)]
                if have:
                    log("  box %s mm already present" % s)
                    continue
                # measure with only this size visible
                bb = done[s].get_BoundingBox(view)
                if bb is None:
                    log("  box %s mm: no bounding box - skipped" % s)
                    continue
                pad = 1.0 * MM
                x0, y0, x1, y1 = bb.Min.X - pad, bb.Min.Y - pad, bb.Max.X + pad, bb.Max.Y + pad
                pts = [XYZ(x0, y0, 0), XYZ(x1, y0, 0), XYZ(x1, y1, 0), XYZ(x0, y1, 0)]
                for i in range(4):
                    c = doc.FamilyCreate.NewDetailCurve(view, Line.CreateBound(pts[i], pts[(i + 1) % 4]))
                    c.LineStyle = style
                    associate(fm, c, fp)
                log("  box %s mm drawn (%.1f x %.1f mm) -> %s" % (s, (x1 - x0) / MM, (y1 - y0) / MM, name))
                changed.append(1)
        if not changed:
            # Nothing to add: do not re-save, so the file (and its manifest checksum) is unchanged.
            t.RollBack()
            log("  already complete - not saved")
            return True
        t.Commit()
    except Exception as ex:
        t.RollBack()
        log("  FAILED, rolled back: %s" % ex)
        return False
    doc.Save()
    log("  saved")
    return True

app = __revit__.Application
count = 0
for d in list(app.Documents):
    if not d.IsFamilyDocument:
        continue
    title = d.Title.replace(".rfa", "")
    if not any(title == "STING - %s Tag" % n for n in TARGETS):
        continue
    log(title)
    if process(d):
        count += 1
log("Done: %d family/families updated." % count)
