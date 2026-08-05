"""IFC entity type → (DISC, SYS) inference for STING tags.

Pure lookup — no IFC API calls, no bpy dependency. Importable headlessly.
Matches the mappings in StingTools/Core/TagConfig.cs (TagConfig.SysMap /
TagConfig.GetMepSystemAwareSysCode) and the STING ISO 19650 tag grammar v5.
"""
from __future__ import annotations

# Maps uppercase IFC entity name (with IFC prefix) → (DISC code, SYS code).
# Discipline codes: A=Arch, S=Struct, M=Mechanical, E=Electrical,
#                   P=Plumbing, F=Fire, H=Healthcare, RP=Radiation
# System codes match TagConfig.SysMap keys.
_MAP: dict[str, tuple[str, str]] = {
    # ── Mechanical — HVAC ──────────────────────────────────────────────────
    "IFCDUCTSEGMENT":            ("M", "HVAC"),
    "IFCDUCTFITTING":            ("M", "HVAC"),
    "IFCDUCTSILENCER":           ("M", "HVAC"),
    "IFCAIRTERMINAL":            ("M", "HVAC"),
    "IFCAIRTERMINALBOX":         ("M", "HVAC"),
    "IFCAIRTOAIRHEATRECOVERY":   ("M", "HVAC"),
    "IFCCOIL":                   ("M", "HVAC"),
    "IFCFAN":                    ("M", "HVAC"),
    "IFCFILTERCHAMBER":          ("M", "HVAC"),
    "IFCHUMIDIFIER":             ("M", "HVAC"),
    "IFCUNITARYEQUIPMENT":       ("M", "HVAC"),
    "IFCMEDICALDEVICE":          ("M", "HVAC"),
    "IFCCOMPRESSOR":             ("M", "HVAC"),
    "IFCEVAPORATOR":             ("M", "HVAC"),
    "IFCCONDENSER":              ("M", "HVAC"),
    "IFCEVAPORATIVECOOLER":      ("M", "HVAC"),

    # ── Mechanical — Heating / Cooling ────────────────────────────────────
    "IFCBOILER":                 ("M", "HWS"),
    "IFCCHILLER":                ("M", "CHW"),
    "IFCCOOLINGTOWER":           ("M", "CHW"),
    "IFCHEATEXCHANGER":          ("M", "HVAC"),
    "IFCPUMP":                   ("M", "HWS"),
    "IFCTUBEBUNDLE":             ("M", "CHW"),

    # ── Mechanical — Pipe ─────────────────────────────────────────────────
    "IFCPIPESEGMENT":            ("M", "DCW"),
    "IFCPIPEFITTING":            ("M", "DCW"),
    "IFCFLOWCONTROLLER":         ("M", "DCW"),
    "IFCVALVE":                  ("M", "DCW"),
    "IFCFLOWMETER":              ("M", "DCW"),
    "IFCFLOWSTORAGEDEVICE":      ("M", "DCW"),
    "IFCTANK":                   ("M", "DCW"),

    # ── Plumbing / Sanitary ───────────────────────────────────────────────
    "IFCSANITARYTERMINAL":       ("P", "SAN"),
    "IFCWASTETERMINAL":          ("P", "SAN"),
    "IFCINTERCEPTOR":            ("P", "SAN"),
    "IFCSTACK":                  ("P", "SAN"),

    # ── Electrical — LV ───────────────────────────────────────────────────
    "IFCCABLESEGMENT":               ("E", "LV"),
    "IFCCABLECARRIERSEGMENT":        ("E", "LV"),
    "IFCCABLECARRIERFITTING":        ("E", "LV"),
    "IFCELECTRICAPPLIANCE":          ("E", "LV"),
    "IFCELECTRICDISTRIBUTIONBOARD":  ("E", "LV"),
    "IFCELECTRICTIMECONTROL":        ("E", "LV"),
    "IFCPROTECTIVEDEVICE":           ("E", "LV"),
    "IFCSWITCHINGDEVICE":            ("E", "LV"),
    "IFCJUNCTIONBOX":                ("E", "LV"),
    "IFCLAMP":                       ("E", "LV"),
    "IFCMOTOR":                      ("E", "LV"),
    "IFCOUTLET":                     ("E", "LV"),
    "IFCSOLARDEVICE":                ("E", "LV"),
    "IFCPROTECTIVEDEVICETRIPPINGUNIT":("E", "LV"),

    # ── Electrical — HV ───────────────────────────────────────────────────
    "IFCTRANSFORMER":                ("E", "HV"),
    "IFCENERGYCONVERSIONDEVICE":     ("E", "LV"),

    # ── Lighting ──────────────────────────────────────────────────────────
    "IFCLIGHTFIXTURE":               ("E", "LTG"),

    # ── Fire ──────────────────────────────────────────────────────────────
    "IFCFIRESUPPRESSIONTERMINAL":    ("F", "FPS"),
    "IFCALARM":                      ("F", "FPS"),
    "IFCACTUATOR":                   ("F", "FPS"),

    # ── Electrical — energy storage (batteries, UPS, capacitor banks) ─────
    # IfcElectricFlowStorageDevice is an electrical asset, not a fire-protection
    # device. Aligned with stingtools-core inference.py (E) and TagConfig.cs.
    "IFCELECTRICFLOWSTORAGEDEVICE":  ("E", "LV"),

    # ── Communications / BMS ──────────────────────────────────────────────
    "IFCCOMMUNICATIONSAPPLIANCE":    ("E", "COM"),
    "IFCSENSOR":                     ("M", "BMS"),
    "IFCCONTROLLER":                 ("M", "BMS"),
    "IFCFLOWINSTRUMENT":             ("M", "BMS"),
    "IFCUNITARYCONTROLELEMENT":      ("M", "BMS"),

    # ── Structure ─────────────────────────────────────────────────────────
    "IFCBEAM":                   ("S", "STR"),
    "IFCBEAMSTANDARDCASE":       ("S", "STR"),
    "IFCCOLUMN":                 ("S", "STR"),
    "IFCCOLUMNSTANDARDCASE":     ("S", "STR"),
    "IFCFOOTING":                ("S", "STR"),
    "IFCPILE":                   ("S", "STR"),
    "IFCPLATE":                  ("S", "STR"),
    "IFCPLATESTANDARDCASE":      ("S", "STR"),
    "IFCSLAB":                   ("S", "STR"),
    "IFCSLABSTANDARDCASE":       ("S", "STR"),
    "IFCMEMBER":                 ("S", "STR"),
    "IFCMEMBERSTANDARDCASE":     ("S", "STR"),
    "IFCRAILING":                ("S", "STR"),
    "IFCSTAIR":                  ("S", "STR"),
    "IFCSTAIRFLIGHT":            ("S", "STR"),
    "IFCRAMP":                   ("S", "STR"),
    "IFCRAMPFLIGHT":             ("S", "STR"),
    "IFCTENDON":                 ("S", "STR"),
    "IFCTENDONANCHOR":           ("S", "STR"),
    "IFCREINFORCINGBAR":         ("S", "STR"),
    "IFCREINFORCINGELEMENT":     ("S", "STR"),

    # ── Architecture ──────────────────────────────────────────────────────
    "IFCWALL":                   ("A", "ARC"),
    "IFCWALLSTANDARDCASE":       ("A", "ARC"),
    "IFCCURTAINWALL":            ("A", "ARC"),
    "IFCDOOR":                   ("A", "ARC"),
    "IFCWINDOW":                 ("A", "ARC"),
    "IFCROOF":                   ("A", "ARC"),
    "IFCCOVERING":               ("A", "ARC"),
    "IFCSPACE":                  ("A", "ARC"),
    "IFCZONE":                   ("A", "ARC"),
    "IFCFURNITURE":              ("A", "ARC"),
    "IFCFURNISHINGELEMENT":      ("A", "ARC"),
    "IFCBUILDINGELEMENTPROXY":   ("A", "ARC"),
}


def infer_disc_sys(ifc_type: str) -> tuple[str | None, str | None]:
    """Return (DISC, SYS) for an IFC entity type string.

    Args:
        ifc_type: IFC entity type, with or without 'Ifc' prefix, any case.
                  Examples: 'IfcDuctSegment', 'IFCDUCTSEGMENT', 'DuctSegment'.

    Returns:
        (disc_code, sys_code) or (None, None) if no mapping exists.
    """
    key = ifc_type.upper()
    if not key.startswith("IFC"):
        key = "IFC" + key
    return _MAP.get(key, (None, None))


def infer_disc(ifc_type: str) -> str | None:
    """Return only the DISC code, or None."""
    disc, _ = infer_disc_sys(ifc_type)
    return disc


def infer_sys(ifc_type: str) -> str | None:
    """Return only the SYS code, or None."""
    _, sys = infer_disc_sys(ifc_type)
    return sys
