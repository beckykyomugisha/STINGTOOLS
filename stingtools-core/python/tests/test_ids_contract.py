"""Every IDS property facet on a Pset_Sting* set names a declared property, with the
declared data type (DSCH-38 follow-up).

shared/ifc/ids/*.ids encode the contract in shared/ifc/psets/*.xml for IFC
checkers. An IDS that names a property the template does not declare, or a data
type the template does not give it, checks something no STING host writes - and
passes or fails for the wrong reason. Pure stdlib: runs without ifctester.
"""
from __future__ import annotations

from pathlib import Path
from xml.etree import ElementTree as ET

REPO = Path(__file__).resolve().parents[3]
PSETS = REPO / "shared" / "ifc" / "psets"
IDS = REPO / "shared" / "ifc" / "ids"
PNS = "{https://stingtools.io/schema/ifc/psets/v1}"
INS = "{http://standards.buildingsmart.org/IDS}"

# IDS files written before this check whose facets do not match their template.
# Found 2026-10-02: both say IFCLABEL where the template declares IfcText
# (Pset_StingProjectOrg CompanyName / ClientName; Pset_StingTag7 NarrativeFull and
# the six narrative parts). Which side is right depends on what the exporters
# write - settle that, fix the losing file, and remove it from this set.
# Shrink this list; never grow it.
KNOWN_DRIFT: set[str] = {"sting-project-org.ids", "sting-tag7.ids"}


def _contract() -> dict[str, dict[str, str]]:
    out = {}
    for f in PSETS.glob("Pset_Sting*.xml"):
        root = ET.parse(f).getroot()
        name = root.find(f"{PNS}Identity/{PNS}Name").text.strip()
        out[name] = {p.attrib["name"]: (p.findtext(f"{PNS}DataType") or "IfcLabel").strip().upper()
                     for p in root.find(f"{PNS}Properties").findall(f"{PNS}Property")}
    return out


def _facets(path: Path):
    root = ET.parse(path).getroot()
    for prop in root.iter(f"{INS}property"):
        pset = prop.findtext(f"{INS}propertySet/{INS}simpleValue")
        base = prop.findtext(f"{INS}baseName/{INS}simpleValue")
        if pset and base:
            yield pset.strip(), base.strip(), prop.attrib.get("dataType")


def _problems(path: Path, contract) -> list[str]:
    bad = []
    for pset, base, dt in _facets(path):
        if not pset.startswith("Pset_Sting"):
            continue
        if pset not in contract:
            bad.append(f"{path.name}: {pset} has no template in shared/ifc/psets")
        elif base not in contract[pset]:
            bad.append(f"{path.name}: {pset}.{base} is not declared in {pset}.xml")
        elif dt and dt != contract[pset][base]:
            bad.append(f"{path.name}: {pset}.{base} is {dt} in the IDS but {contract[pset][base]} in {pset}.xml")
    return bad


def test_ids_facets_match_the_pset_templates():
    contract = _contract()
    files = sorted(IDS.glob("*.ids"))
    assert len(files) >= 13, [f.name for f in files]
    bad = [p for f in files if f.name not in KNOWN_DRIFT for p in _problems(f, contract)]
    assert not bad, "\n".join(bad)


def test_every_pset_sting_template_has_an_ids():
    covered = {pset for f in IDS.glob("*.ids") for pset, _, _ in _facets(f)}
    missing = sorted(set(_contract()) - covered)
    assert not missing, f"Pset_Sting* templates with no IDS in shared/ifc/ids: {missing}"


def test_the_check_catches_an_undeclared_property(tmp_path):
    f = tmp_path / "x.ids"
    f.write_text(
        '<ids xmlns="http://standards.buildingsmart.org/IDS"><specifications><specification><requirements>'
        '<property dataType="IFCREAL"><propertySet><simpleValue>Pset_StingMEP</simpleValue></propertySet>'
        '<baseName><simpleValue>NO_SUCH_PROP</simpleValue></baseName></property>'
        '<property dataType="IFCLABEL"><propertySet><simpleValue>Pset_StingMEP</simpleValue></propertySet>'
        '<baseName><simpleValue>PLM_SUP_DN</simpleValue></baseName></property>'
        '</requirements></specification></specifications></ids>', encoding="utf-8")
    bad = _problems(f, _contract())
    assert len(bad) == 2, bad


def test_known_drift_is_still_drift():
    """An entry that no longer drifts must leave KNOWN_DRIFT, so the list only shrinks."""
    contract = _contract()
    healed = sorted(n for n in KNOWN_DRIFT if not _problems(IDS / n, contract))
    assert not healed, f"remove from KNOWN_DRIFT, they now match: {healed}"
