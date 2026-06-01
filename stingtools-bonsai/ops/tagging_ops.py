"""TAGS operators — auto-tag, token writers, sequence assignment, full-tag builder."""

from __future__ import annotations

import bpy

# Module-level sequence counters keyed by (ifc_path, disc, sys, level)
# so we don't re-scan the whole model on every small batch.
_SEQ_COUNTERS: dict[tuple, int] = {}


# ---------------------------------------------------------------------------
# helpers
# ---------------------------------------------------------------------------

def _active_ifc():
    from ..core.bonsai import bonsai
    return bonsai.active_ifc()


def _get_pset(element, pset_name: str) -> dict:
    try:
        import ifcopenshell.util.element as ifc_util  # type: ignore
        return ifc_util.get_pset(element, pset_name) or {}
    except Exception:
        return {}


def _write_pset(element, pset_name: str, props: dict) -> bool:
    from ..core.bonsai import bonsai
    return bonsai.add_pset(element, pset_name, props)


def _ifc_path(model) -> str:
    return getattr(model, "path", "") or ""


def _infer_discipline(element) -> str:
    """Map IFC class to STING discipline code."""
    cls = element.is_a()
    mapping = {
        "IfcFlowTerminal": "M",
        "IfcAirTerminal": "M",
        "IfcUnitaryEquipment": "M",
        "IfcCoil": "M",
        "IfcDamper": "M",
        "IfcDuctFitting": "M",
        "IfcDuctSegment": "M",
        "IfcDuctSilencer": "M",
        "IfcFilter": "M",
        "IfcSpaceHeater": "M",
        "IfcPipeFitting": "P",
        "IfcPipeSegment": "P",
        "IfcSanitaryTerminal": "P",
        "IfcValve": "P",
        "IfcElectricAppliance": "E",
        "IfcElectricDistributionBoard": "E",
        "IfcElectricFlowStorageDevice": "E",
        "IfcElectricGenerator": "E",
        "IfcElectricMotor": "E",
        "IfcLamp": "E",
        "IfcLightFixture": "E",
        "IfcOutlet": "E",
        "IfcProtectiveDevice": "E",
        "IfcSwitchingDevice": "E",
        "IfcTransformer": "E",
        "IfcCableCarrierFitting": "E",
        "IfcCableCarrierSegment": "E",
        "IfcCableFitting": "E",
        "IfcCableSegment": "E",
        "IfcWall": "A",
        "IfcWallStandardCase": "A",
        "IfcWindow": "A",
        "IfcDoor": "A",
        "IfcSlab": "A",
        "IfcRoof": "A",
        "IfcCovering": "A",
        "IfcCurtainWall": "A",
        "IfcColumn": "S",
        "IfcBeam": "S",
        "IfcMember": "S",
        "IfcPile": "S",
        "IfcFooting": "S",
        "IfcFireSuppressionTerminal": "FP",
        "IfcAlarm": "FP",
    }
    return mapping.get(cls, "XX")


def _infer_level(element) -> str:
    """Derive short level code from containing IfcBuildingStorey."""
    try:
        import ifcopenshell.util.element as ifc_util  # type: ignore
        container = ifc_util.get_container(element)
        if container is None or not container.is_a("IfcBuildingStorey"):
            return "XX"
        name = (container.Name or "").strip().upper()
        if "ROOF" in name or "ROOFTOP" in name:
            return "RF"
        if "MEZZANINE" in name or "MEZ" in name:
            return "MZ"
        if "PLANT" in name:
            return "PR"
        if "BASEMENT" in name or name.startswith("B") and any(c.isdigit() for c in name):
            digit = next((c for c in name if c.isdigit()), "1")
            return f"B{digit}"
        if "GROUND" in name or name in ("GF", "G", "GROUND FLOOR", "0"):
            return "GF"
        for token in name.split():
            if token.isdigit():
                return f"L{int(token):02d}"
        if container.Elevation is not None:
            try:
                elev = float(container.Elevation)
                if elev < 0:
                    return "B1"
                if elev < 0.5:
                    return "GF"
                return f"L{max(1, round(elev / 3)):02d}"
            except (ValueError, TypeError):
                pass
        return "XX"
    except Exception:
        return "XX"


def _infer_system(element) -> str:
    """Derive system code from IfcSystem group membership."""
    try:
        import ifcopenshell.util.element as ifc_util  # type: ignore
        model = element.wrapped_data.file
        for rel in model.get_inverse(element):
            if rel.is_a("IfcRelAssignsToGroup"):
                grp = rel.RelatingGroup
                if grp.is_a("IfcSystem"):
                    name = (grp.Name or "").upper()
                    if any(k in name for k in ("HVAC", "AIR", "VENT", "DUCT")):
                        return "HVAC"
                    if any(k in name for k in ("DRAIN", "SANIT", "WASTE", "SEWAGE")):
                        return "SAN"
                    if any(k in name for k in ("COLD", "DCW", "CWS")):
                        return "DCW"
                    if any(k in name for k in ("HOT", "DHW", "HWS")):
                        return "DHW"
                    if any(k in name for k in ("ELECTRIC", "POWER", "LV")):
                        return "ELC"
                    if any(k in name for k in ("FIRE", "SPRINKLER", "FP")):
                        return "FP"
                    if any(k in name for k in ("GAS",)):
                        return "GAS"
        return "XX"
    except Exception:
        return "XX"


def _infer_product(element) -> str:
    """Derive product code from element type name (first 3 uppercase chars)."""
    try:
        el_type = element.IsDefinedBy
        for rel in (element.wrapped_data.file.get_inverse(element)):
            if rel.is_a("IfcRelDefinesByType"):
                type_obj = rel.RelatingType
                if type_obj and type_obj.Name:
                    clean = "".join(c for c in type_obj.Name.upper() if c.isalpha())
                    if clean:
                        return clean[:3]
        return "XX"
    except Exception:
        return "XX"


def _next_seq(path: str, disc: str, sys: str, level: str) -> str:
    key = (path, disc, sys, level)
    _SEQ_COUNTERS[key] = _SEQ_COUNTERS.get(key, 0) + 1
    return str(_SEQ_COUNTERS[key]).zfill(4)


def _seed_counters(model) -> None:
    """Scan existing Pset_StingTags.Sequence values to initialise counters."""
    path = _ifc_path(model)
    try:
        for el in model.by_type("IfcElement"):
            pset = _get_pset(el, "Pset_StingTags")
            if not pset:
                continue
            disc = pset.get("Discipline", "XX")
            sys = pset.get("System", "XX")
            level = pset.get("Level", "XX")
            seq_str = pset.get("Sequence", "0000")
            try:
                seq_val = int(seq_str)
            except (ValueError, TypeError):
                continue
            key = (path, disc, sys, level)
            if seq_val > _SEQ_COUNTERS.get(key, 0):
                _SEQ_COUNTERS[key] = seq_val
    except Exception:
        pass


# ---------------------------------------------------------------------------
# 5 — Auto Tag
# ---------------------------------------------------------------------------

class StingAutoTagOperator(bpy.types.Operator):
    """Infer and write Pset_StingTags on ALL IFC elements in the active file."""

    bl_idname = "sting.auto_tag"
    bl_label = "Auto Tag"
    bl_description = "Infer discipline, level, system, product and sequence for all IFC elements"
    bl_options = {"REGISTER", "UNDO"}

    overwrite: bpy.props.BoolProperty(  # type: ignore[valid-type]
        name="Overwrite existing",
        description="Replace already-set tokens; leave off to only fill missing/XX values",
        default=False,
    )

    def invoke(self, context: bpy.types.Context, event):
        return context.window_manager.invoke_props_dialog(self)

    def execute(self, context: bpy.types.Context) -> set[str]:
        model = _active_ifc()
        if model is None:
            self.report({"ERROR"}, "No active IFC file — open one via Bonsai first")
            return {"CANCELLED"}

        _seed_counters(model)
        path = _ifc_path(model)

        try:
            elements = list(model.by_type("IfcElement"))
        except Exception as e:
            self.report({"ERROR"}, f"Cannot query IFC: {e}")
            return {"CANCELLED"}

        tagged = 0
        skipped = 0
        for el in elements:
            existing = _get_pset(el, "Pset_StingTags")

            def pick(key: str, inferred: str) -> str:
                cur = existing.get(key, "")
                if cur and cur != "XX" and not self.overwrite:
                    return cur
                return inferred

            disc = pick("Discipline", _infer_discipline(el))
            level = pick("Level", _infer_level(el))
            sys = pick("System", _infer_system(el))
            prod = pick("Product", _infer_product(el))

            # Sequence: only auto-assign if missing / 0000
            cur_seq = existing.get("Sequence", "0000")
            if (cur_seq in ("", "0000", "XX") or self.overwrite):
                seq = _next_seq(path, disc, sys, level)
            else:
                seq = cur_seq

            props = {
                "Discipline": disc,
                "Location": existing.get("Location", "XX"),
                "Zone": existing.get("Zone", "XX"),
                "Level": level,
                "System": sys,
                "Function": existing.get("Function", "XX"),
                "Product": prod,
                "Sequence": seq,
            }

            ok = _write_pset(el, "Pset_StingTags", props)
            if ok:
                tagged += 1
            else:
                skipped += 1

        self.report({"INFO"}, f"Tagged {tagged} element(s), {skipped} skipped")
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# 6 — Tag Selected
# ---------------------------------------------------------------------------

class StingTagSelectedOperator(bpy.types.Operator):
    """Infer and write Pset_StingTags on the currently selected Blender objects."""

    bl_idname = "sting.tag_selected"
    bl_label = "Tag Selected"
    bl_description = "Auto-tag only the currently selected objects"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context: bpy.types.Context) -> set[str]:
        model = _active_ifc()
        if model is None:
            self.report({"ERROR"}, "No active IFC file — open one via Bonsai first")
            return {"CANCELLED"}

        _seed_counters(model)
        path = _ifc_path(model)

        elements = []
        for obj in context.selected_objects:
            props = getattr(obj, "BIMObjectProperties", None)
            ifc_id = getattr(props, "ifc_definition_id", 0) if props else 0
            if ifc_id:
                try:
                    el = model.by_id(ifc_id)
                    if el and el.is_a("IfcElement"):
                        elements.append(el)
                except Exception:
                    pass

        if not elements:
            self.report({"WARNING"}, "No IFC elements found in selection")
            return {"CANCELLED"}

        tagged = 0
        for el in elements:
            existing = _get_pset(el, "Pset_StingTags")
            disc = _infer_discipline(el) if not existing.get("Discipline", "XX").replace("XX","") else existing["Discipline"]
            level = _infer_level(el) if not existing.get("Level", "XX").replace("XX","") else existing["Level"]
            sys = _infer_system(el) if not existing.get("System", "XX").replace("XX","") else existing["System"]
            prod = _infer_product(el) if not existing.get("Product", "XX").replace("XX","") else existing["Product"]
            seq = existing.get("Sequence", "0000")
            if seq in ("", "0000"):
                seq = _next_seq(path, disc, sys, level)

            props = {
                "Discipline": disc,
                "Location": existing.get("Location", "XX"),
                "Zone": existing.get("Zone", "XX"),
                "Level": level,
                "System": sys,
                "Function": existing.get("Function", "XX"),
                "Product": prod,
                "Sequence": seq,
            }
            if _write_pset(el, "Pset_StingTags", props):
                tagged += 1

        self.report({"INFO"}, f"Tagged {tagged} of {len(elements)} selected element(s)")
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# 7 — Set Discipline
# ---------------------------------------------------------------------------

_DISCIPLINE_ITEMS = [
    ("M",  "Mechanical",            ""),
    ("E",  "Electrical",            ""),
    ("P",  "Plumbing / Public Health", ""),
    ("A",  "Architectural",         ""),
    ("S",  "Structural",            ""),
    ("H",  "Healthcare",            ""),
    ("MG", "Medical Gas",           ""),
    ("RP", "Radiation Protection",  ""),
    ("FP", "Fire Protection",       ""),
    ("LV", "LV / Comms",            ""),
    ("G",  "Civil",                 ""),
]


class StingSetDisciplineOperator(bpy.types.Operator):
    """Set Pset_StingTags.Discipline on all selected IFC elements."""

    bl_idname = "sting.set_discipline"
    bl_label = "Set Discipline"
    bl_description = "Write a chosen discipline code on selected elements"
    bl_options = {"REGISTER", "UNDO"}

    discipline: bpy.props.EnumProperty(  # type: ignore[valid-type]
        name="Discipline",
        items=_DISCIPLINE_ITEMS,
        default="M",
    )

    def invoke(self, context: bpy.types.Context, event):
        return context.window_manager.invoke_props_dialog(self)

    def execute(self, context: bpy.types.Context) -> set[str]:
        model = _active_ifc()
        if model is None:
            self.report({"ERROR"}, "No active IFC file")
            return {"CANCELLED"}

        count = 0
        for obj in context.selected_objects:
            props = getattr(obj, "BIMObjectProperties", None)
            ifc_id = getattr(props, "ifc_definition_id", 0) if props else 0
            if not ifc_id:
                continue
            try:
                el = model.by_id(ifc_id)
            except Exception:
                continue
            if el and el.is_a("IfcElement"):
                existing = _get_pset(el, "Pset_StingTags")
                existing["Discipline"] = self.discipline
                _write_pset(el, "Pset_StingTags", existing)
                count += 1

        self.report({"INFO"}, f"Set Discipline={self.discipline} on {count} element(s)")
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# 8 — Set System
# ---------------------------------------------------------------------------

_SYSTEM_ITEMS = [
    ("HVAC", "HVAC", ""),
    ("DCW",  "Cold Water (DCW)", ""),
    ("DHW",  "Hot Water (DHW)", ""),
    ("HWS",  "Heating Water (HWS)", ""),
    ("SAN",  "Sanitary / Drainage", ""),
    ("RWD",  "Rainwater / Stormwater", ""),
    ("GAS",  "Natural Gas", ""),
    ("FP",   "Fire Protection", ""),
    ("ELC",  "Electrical / LV", ""),
    ("ICT",  "ICT / Data", ""),
    ("COM",  "Communications", ""),
    ("SEC",  "Security / Access", ""),
]


class StingSetSystemOperator(bpy.types.Operator):
    """Set Pset_StingTags.System on all selected IFC elements."""

    bl_idname = "sting.set_system"
    bl_label = "Set System"
    bl_description = "Write a chosen system code on selected elements"
    bl_options = {"REGISTER", "UNDO"}

    system: bpy.props.EnumProperty(  # type: ignore[valid-type]
        name="System",
        items=_SYSTEM_ITEMS,
        default="HVAC",
    )

    def invoke(self, context: bpy.types.Context, event):
        return context.window_manager.invoke_props_dialog(self)

    def execute(self, context: bpy.types.Context) -> set[str]:
        model = _active_ifc()
        if model is None:
            self.report({"ERROR"}, "No active IFC file")
            return {"CANCELLED"}

        count = 0
        for obj in context.selected_objects:
            props = getattr(obj, "BIMObjectProperties", None)
            ifc_id = getattr(props, "ifc_definition_id", 0) if props else 0
            if not ifc_id:
                continue
            try:
                el = model.by_id(ifc_id)
            except Exception:
                continue
            if el and el.is_a("IfcElement"):
                existing = _get_pset(el, "Pset_StingTags")
                existing["System"] = self.system
                _write_pset(el, "Pset_StingTags", existing)
                count += 1

        self.report({"INFO"}, f"Set System={self.system} on {count} element(s)")
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# 9 — Assign Sequence Numbers
# ---------------------------------------------------------------------------

class StingAssignSequenceOperator(bpy.types.Operator):
    """Assign 4-digit sequence numbers grouped by Discipline / System / Level."""

    bl_idname = "sting.assign_sequence"
    bl_label = "Assign Sequence Numbers"
    bl_description = "Renumber sequence in each (Discipline, System, Level) group starting from max+1"
    bl_options = {"REGISTER", "UNDO"}

    overwrite: bpy.props.BoolProperty(  # type: ignore[valid-type]
        name="Overwrite existing sequences",
        default=False,
    )

    def invoke(self, context: bpy.types.Context, event):
        return context.window_manager.invoke_props_dialog(self)

    def execute(self, context: bpy.types.Context) -> set[str]:
        model = _active_ifc()
        if model is None:
            self.report({"ERROR"}, "No active IFC file")
            return {"CANCELLED"}

        path = _ifc_path(model)
        # Find max existing per group
        group_max: dict[tuple, int] = {}
        try:
            elements = list(model.by_type("IfcElement"))
        except Exception as e:
            self.report({"ERROR"}, f"Cannot query IFC: {e}")
            return {"CANCELLED"}

        for el in elements:
            pset = _get_pset(el, "Pset_StingTags")
            if not pset:
                continue
            key = (pset.get("Discipline","XX"), pset.get("System","XX"), pset.get("Level","XX"))
            try:
                val = int(pset.get("Sequence", "0") or "0")
                if val > group_max.get(key, 0):
                    group_max[key] = val
            except (ValueError, TypeError):
                pass

        group_counters = dict(group_max)
        reassigned = 0

        for el in elements:
            pset = _get_pset(el, "Pset_StingTags")
            if not pset:
                continue
            cur_seq = pset.get("Sequence", "")
            if cur_seq and cur_seq not in ("0000", "XX") and not self.overwrite:
                continue

            key = (pset.get("Discipline","XX"), pset.get("System","XX"), pset.get("Level","XX"))
            group_counters[key] = group_counters.get(key, 0) + 1
            pset["Sequence"] = str(group_counters[key]).zfill(4)
            _write_pset(el, "Pset_StingTags", pset)
            reassigned += 1

        # Sync to module counter cache
        for (disc, sys, level), val in group_counters.items():
            _SEQ_COUNTERS[(path, disc, sys, level)] = val

        self.report({"INFO"}, f"Assigned sequence to {reassigned} element(s)")
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# 10 — Build Full Tags
# ---------------------------------------------------------------------------

class StingBuildFullTagsOperator(bpy.types.Operator):
    """Assemble and write Pset_StingTags.FullTag from the 8 segment values."""

    bl_idname = "sting.build_full_tags"
    bl_label = "Build Full Tags"
    bl_description = "Write the FullTag string (DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ) on all elements"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context: bpy.types.Context) -> set[str]:
        model = _active_ifc()
        if model is None:
            self.report({"ERROR"}, "No active IFC file")
            return {"CANCELLED"}

        try:
            from stingtools_core.tag_grammar import Tag  # type: ignore
        except ImportError:
            self.report({"ERROR"}, "stingtools_core not available")
            return {"CANCELLED"}

        built = 0
        incomplete = 0
        try:
            for el in model.by_type("IfcElement"):
                pset = _get_pset(el, "Pset_StingTags")
                if not pset:
                    continue
                tag = Tag.from_pset(pset)
                full = tag.to_full_tag()
                pset["FullTag"] = full
                _write_pset(el, "Pset_StingTags", pset)
                if tag.is_complete():
                    built += 1
                else:
                    incomplete += 1
        except Exception as e:
            self.report({"ERROR"}, f"Error building tags: {e}")
            return {"CANCELLED"}

        self.report({"INFO"}, f"Built {built} complete tag(s), {incomplete} partial tag(s) written")
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# registration
# ---------------------------------------------------------------------------

CLASSES = (
    StingAutoTagOperator,
    StingTagSelectedOperator,
    StingSetDisciplineOperator,
    StingSetSystemOperator,
    StingAssignSequenceOperator,
    StingBuildFullTagsOperator,
)
