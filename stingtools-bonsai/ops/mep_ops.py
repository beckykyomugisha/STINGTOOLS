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


def _write_mep_pset(element, props: dict) -> bool:
    """Write props into Pset_StingMEP on element. Returns True on success.

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
        "the smallest standard DN that keeps velocity ≤ 3.0 m/s. "
        "Writes PLM_SUP_VEL_MS, PLM_SUP_FLOW_LS, PLM_SUP_DN to Pset_StingMEP."
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
    def _select_dn(cls, flow_ls: float) -> tuple[int, float, float]:
        """Return (DN_mm, velocity_m_s, flow_L_s) for the smallest passing DN."""
        for dn in cls._DN_SERIES:
            d_m = dn / 1000.0
            area = math.pi * (d_m / 2.0) ** 2
            v = cls._hw_velocity(d_m)
            q = v * area * 1000.0   # L/s
            if q >= flow_ls:
                return dn, min(v, cls._MAX_VEL), q
        dn = cls._DN_SERIES[-1]
        d_m = dn / 1000.0
        v = cls._hw_velocity(d_m)
        return dn, v, v * math.pi * (d_m / 2.0) ** 2 * 1000.0

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
        for el in ifc.by_type("IfcPipeSegment"):
            mep = _get_mep_pset(el)
            # Use existing flow if stamped, else default 0.5 L/s for a domestic branch
            flow_ls = float(mep.get("PLM_SUP_FLOW_LS", 0.5) or 0.5)
            dn, vel, actual_flow = self._select_dn(flow_ls)
            _write_mep_pset(el, {
                "PLM_SUP_DN": str(dn),
                "PLM_SUP_VEL_MS": f"{vel:.3f}",
                "PLM_SUP_FLOW_LS": f"{actual_flow:.3f}",
            })
            processed += 1

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

    def _resolve_du(self, el) -> float:
        """Resolve DU from PredefinedType or Name heuristics."""
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
        return 0.5  # conservative default

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
        for el in ifc.by_type("IfcSanitaryTerminal"):
            du = self._resolve_du(el)
            _write_mep_pset(el, {"PLM_DRN_DU": f"{du:.1f}"})
            processed += 1
            total_du += du

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
        "Reads cable count + diameter from Pset_StingMEP; writes "
        "ELC_FILL_PCT and ELC_FILL_STATUS (OK / OVERLOADED)."
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
        for el in ifc.by_type("IfcCableCarrierSegment"):
            mep = _get_mep_pset(el)

            # Conduit internal diameter (mm); default 25 mm if not set
            conduit_d = float(mep.get("ELC_CONDUIT_DN_MM", 25) or 25)
            conduit_area = math.pi * (conduit_d / 2.0) ** 2   # mm²

            # Cable count and individual cable outer diameter (mm)
            cable_count = int(float(mep.get("ELC_CABLE_COUNT", 1) or 1))
            cable_d = float(mep.get("ELC_CABLE_OD_MM", 6) or 6)   # typical 6 mm²/1.5 mm² T&E
            cable_area_each = math.pi * (cable_d / 2.0) ** 2
            total_cable_area = cable_count * cable_area_each

            fill_pct = (total_cable_area / conduit_area * 100.0) if conduit_area > 0 else 0.0
            status = "OK" if fill_pct <= self._MAX_FILL_PCT else "OVERLOADED"

            _write_mep_pset(el, {
                "ELC_FILL_PCT": f"{fill_pct:.1f}",
                "ELC_FILL_STATUS": status,
            })
            processed += 1
            if status == "OVERLOADED":
                overloaded += 1

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
