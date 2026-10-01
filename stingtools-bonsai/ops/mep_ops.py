"""MEP engineering calculation operators — pipe flow, drainage units, conduit fill."""

from __future__ import annotations

import math

import bpy


# ---------------------------------------------------------------------------
# Shared helpers
# ---------------------------------------------------------------------------

def _get_ifc():
    try:
        from ..core.bonsai import bonsai as _bridge
        return _bridge.active_ifc()
    except Exception:
        return None


def _get_mep_pset(element) -> dict:
    """Return Pset_StingMEP values for element (may be empty dict)."""
    try:
        import ifcopenshell.util.element as ifc_util
        return ifc_util.get_psets(element).get("Pset_StingMEP", {})
    except Exception:
        return {}


def _positive(mep: dict, key: str):
    """The value of ``key`` as a positive float, or None when it is missing, blank,
    not a number or not positive. Callers refuse the element on None; nothing is
    ever substituted for a missing input."""
    raw = mep.get(key)
    if raw is None or (isinstance(raw, str) and not raw.strip()):
        return None
    try:
        v = float(raw)
    except (TypeError, ValueError):
        return None
    return v if v > 0 and math.isfinite(v) else None


def _describe(el) -> str:
    """How a refused element is named in the report: GlobalId, else #id, plus Name."""
    gid = getattr(el, "GlobalId", None)
    ident = gid if isinstance(gid, str) and gid else f"#{el.id()}" if hasattr(el, "id") else "?"
    name = getattr(el, "Name", None)
    return f"{ident} ({name})" if isinstance(name, str) and name else ident


def _report_refused(op, what: str, refused: list) -> None:
    """One WARNING naming every refused element (first 10 in full) and its missing input."""
    if not refused:
        return
    shown = "; ".join(f"{d}: {why}" for d, why in refused[:10])
    more = f"; and {len(refused) - 10} more" if len(refused) > 10 else ""
    op.report({"WARNING"}, f"{len(refused)} {what} skipped, nothing written: {shown}{more}")


def _write_mep_pset(element, props: dict) -> bool:
    """Write props into Pset_StingMEP on element. Returns True on success.

    Pset_StingMEP is declared in shared/ifc/psets/Pset_StingMEP.xml (DSCH-38
    follow-up). Numbers are passed as int / float so ifcopenshell writes
    IfcInteger / IfcReal, as declared; before 5.8.0 they were text (IfcLabel),
    which the readers here still accept (float() / int(float())).

    BonsaiBridge.add_pset takes (element, pset_name, properties) and resolves the
    active model itself — passing the model as a fourth argument raises TypeError.
    """
    from ..core.bonsai import bonsai as _bridge
    return _bridge.add_pset(element, "Pset_StingMEP", props)


# ---------------------------------------------------------------------------
# Hazen-Williams pipe flow / sizing
# ---------------------------------------------------------------------------

class StingCalcPipeFlowOperator(bpy.types.Operator):
    """Size pipe and calculate velocity using the Hazen-Williams formula."""

    bl_idname = "sting.calc_pipe_flow"
    bl_label = "Calc Pipe Flow (H-W)"
    bl_description = (
        "Apply Hazen-Williams formula to every IfcPipeSegment: select "
        "the smallest standard DN whose full-bore capacity carries the design "
        "flow PLM_SUP_FLOW_LS at no more than 3.0 m/s. Writes PLM_SUP_DN and "
        "PLM_SUP_VEL_MS to Pset_StingMEP. A segment with no design flow is "
        "skipped and reported, never sized for an assumed flow."
    )
    bl_options = {"REGISTER", "UNDO"}

    # Standard metric DN bore diameters in mm
    _DN_SERIES = [15, 20, 25, 32, 40, 50, 65, 80, 100, 125, 150, 200, 250, 300]
    _HW_C = 140        # Hazen-Williams C — copper/plastic
    _MAX_VEL = 3.0     # m/s maximum velocity
    _SLOPE = 0.01      # assumed hydraulic gradient (m/m) for preliminary sizing

    @classmethod
    def _hw_velocity(cls, diameter_m: float, slope: float = None) -> float:
        """Return velocity in m/s for a full-bore circular pipe."""
        s = slope or cls._SLOPE
        r = diameter_m / 4.0   # hydraulic radius for full circular pipe
        return 0.8492 * cls._HW_C * (r ** 0.63) * (s ** 0.54)

    @classmethod
    def _select_dn(cls, flow_ls: float):
        """(DN_mm, velocity_m_s) for the smallest DN whose full-bore capacity at the
        design gradient carries ``flow_ls`` with the design-flow velocity at or below
        _MAX_VEL. The velocity is the design flow over the bore area. None when no DN
        in the series qualifies - the caller refuses rather than writing the largest."""
        for dn in cls._DN_SERIES:
            d_m = dn / 1000.0
            area = math.pi * (d_m / 2.0) ** 2
            capacity_ls = cls._hw_velocity(d_m) * area * 1000.0
            velocity = (flow_ls / 1000.0) / area
            if capacity_ls >= flow_ls and velocity <= cls._MAX_VEL:
                return dn, velocity
        return None

    def execute(self, context: bpy.types.Context) -> set[str]:
        ifc = _get_ifc()
        if ifc is None:
            self.report({"ERROR"}, "No IFC file loaded")
            return {"CANCELLED"}

        try:
            import ifcopenshell
        except ImportError as exc:
            self.report({"ERROR"}, f"ifcopenshell unavailable: {exc}")
            return {"CANCELLED"}

        processed = 0
        refused = []
        for el in ifc.by_type("IfcPipeSegment"):
            mep = _get_mep_pset(el)
            # The design flow is an input. It is not assumed when missing, and it is
            # not overwritten: the old code wrote the selected DN's capacity back
            # into PLM_SUP_FLOW_LS, so a second run sized for the wrong flow.
            flow_ls = _positive(mep, "PLM_SUP_FLOW_LS")
            if flow_ls is None:
                refused.append((_describe(el), "no design flow PLM_SUP_FLOW_LS"))
                continue
            pick = self._select_dn(flow_ls)
            if pick is None:
                refused.append((_describe(el), f"{flow_ls:g} l/s needs more than DN{self._DN_SERIES[-1]}"))
                continue
            dn, vel = pick
            _write_mep_pset(el, {
                "PLM_SUP_DN": int(dn),
                "PLM_SUP_VEL_MS": round(vel, 3),
            })
            processed += 1

        _report_refused(self, "pipe segment(s)", refused)
        self.report({"INFO"}, f"Hazen-Williams sizing applied to {processed} pipe segment(s)")
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# BS EN 12056-2 Drainage Unit assignment
# ---------------------------------------------------------------------------

class StingCalcDrainageUnitsOperator(bpy.types.Operator):
    """Assign BS EN 12056-2 drainage unit (DU) values to sanitary terminals."""

    bl_idname = "sting.calc_drainage_units"
    bl_label = "Calc Drainage Units (DU)"
    bl_description = (
        "Look up BS EN 12056-2 / BS EN 806 DU per IfcSanitaryTerminal sub-type "
        "and write PLM_DRN_DU to Pset_StingMEP on each element."
    )
    bl_options = {"REGISTER", "UNDO"}

    # BS EN 12056-2 Table 1 — Discharge Unit values
    _DU_TABLE = {
        "WC": 2.0,
        "WATERCLOSETWITHCISTERN": 2.0,
        "WATERCLOSETWITHFLUSHOMETER": 2.0,
        "WASHHANDBASIN": 0.5,
        "BASIN": 0.5,
        "SINK": 1.0,
        "BATH": 3.0,
        "SHOWER": 0.6,
        "BIDET": 0.5,
        "URINAL": 0.3,
        "KITCHENSINK": 1.0,
        "SHOWERBASE": 0.6,
    }

    def _resolve_du(self, el):
        """DU from PredefinedType, else from a table key in the Name; None when
        neither identifies the fixture (the caller refuses - no default DU)."""
        try:
            pt = (el.PredefinedType or "").upper().replace(" ", "")
        except AttributeError:
            pt = ""
        if pt in self._DU_TABLE:
            return self._DU_TABLE[pt]
        # Fallback: scan Name
        name = (el.Name or "").upper()
        for key, du in self._DU_TABLE.items():
            if key in name:
                return du
        return None

    def execute(self, context: bpy.types.Context) -> set[str]:
        ifc = _get_ifc()
        if ifc is None:
            self.report({"ERROR"}, "No IFC file loaded")
            return {"CANCELLED"}

        try:
            import ifcopenshell
        except ImportError as exc:
            self.report({"ERROR"}, f"ifcopenshell unavailable: {exc}")
            return {"CANCELLED"}

        processed = 0
        total_du = 0.0
        refused = []
        for el in ifc.by_type("IfcSanitaryTerminal"):
            du = self._resolve_du(el)
            if du is None:
                refused.append((_describe(el), "fixture type not in the BS EN 12056-2 table "
                                               "(PredefinedType / Name)"))
                continue
            _write_mep_pset(el, {"PLM_DRN_DU": round(du, 1)})
            processed += 1
            total_du += du

        _report_refused(self, "sanitary terminal(s)", refused)
        self.report(
            {"INFO"},
            f"Drainage units assigned to {processed} sanitary terminal(s) — total DU: {total_du:.1f}",
        )
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# BS 7671 Conduit fill check
# ---------------------------------------------------------------------------

class StingCalcConduitFillOperator(bpy.types.Operator):
    """Evaluate BS 7671 40% conduit fill for each IfcCableCarrierSegment."""

    bl_idname = "sting.calc_conduit_fill"
    bl_label = "Calc Conduit Fill"
    bl_description = (
        "Check BS 7671 40% fill rule for each IfcCableCarrierSegment. "
        "Reads ELC_CONDUIT_DN_MM, ELC_CABLE_COUNT and ELC_CABLE_OD_MM from "
        "Pset_StingMEP; writes ELC_FILL_PCT and ELC_FILL_STATUS (OK / OVERLOADED). "
        "A segment missing any of the three is skipped and reported."
    )
    bl_options = {"REGISTER", "UNDO"}

    _MAX_FILL_PCT = 40.0   # BS 7671 Appendix 5 maximum

    def execute(self, context: bpy.types.Context) -> set[str]:
        ifc = _get_ifc()
        if ifc is None:
            self.report({"ERROR"}, "No IFC file loaded")
            return {"CANCELLED"}

        try:
            import ifcopenshell
        except ImportError as exc:
            self.report({"ERROR"}, f"ifcopenshell unavailable: {exc}")
            return {"CANCELLED"}

        processed = overloaded = 0
        refused = []
        for el in ifc.by_type("IfcCableCarrierSegment"):
            mep = _get_mep_pset(el)
            conduit_d = _positive(mep, "ELC_CONDUIT_DN_MM")   # internal diameter, mm
            cable_n = _positive(mep, "ELC_CABLE_COUNT")
            cable_d = _positive(mep, "ELC_CABLE_OD_MM")       # one cable's overall diameter, mm
            missing = [k for k, v in (("ELC_CONDUIT_DN_MM", conduit_d), ("ELC_CABLE_COUNT", cable_n),
                                      ("ELC_CABLE_OD_MM", cable_d)) if v is None]
            if cable_n is not None and cable_n != int(cable_n):
                missing.append("ELC_CABLE_COUNT (not a whole number)")
            if missing:
                refused.append((_describe(el), "missing " + ", ".join(missing)))
                continue

            conduit_area = math.pi * (conduit_d / 2.0) ** 2   # mm²
            total_cable_area = int(cable_n) * math.pi * (cable_d / 2.0) ** 2
            fill_pct = total_cable_area / conduit_area * 100.0
            status = "OK" if fill_pct <= self._MAX_FILL_PCT else "OVERLOADED"

            _write_mep_pset(el, {
                "ELC_FILL_PCT": round(fill_pct, 1),
                "ELC_FILL_STATUS": status,
            })
            processed += 1
            if status == "OVERLOADED":
                overloaded += 1

        _report_refused(self, "cable carrier segment(s)", refused)
        msg = f"Conduit fill checked on {processed} segment(s)"
        if overloaded:
            msg += f" — {overloaded} OVERLOADED (>40%)"
            self.report({"WARNING"}, msg)
        else:
            self.report({"INFO"}, msg)

        return {"FINISHED"}


CLASSES = (
    StingCalcPipeFlowOperator,
    StingCalcDrainageUnitsOperator,
    StingCalcConduitFillOperator,
)
