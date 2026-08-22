# -*- coding: utf-8 -*-
"""Generate the KUT LOD matrix overlay with the tiered asset data schedule.

The overlay REPLACES a category wholesale, so every category it names must carry
the complete ladder (200 to 500), not only the rung being changed. A partial
category would resolve to nothing at the other rungs and its elements would be
skipped in silence -- reported as 100% over an empty scope. This script therefore
copies each category's corporate ladder and edits only rung 500, rather than
hand-writing the JSON.

Run from the repository root; writes project-templates/KUT/_BIM_COORD/lod_matrix.json.
"""
import collections
import io
import json

CORPORATE = 'StingTools/Data/STING_LOD_MATRIX.json'
OVERLAY = 'project-templates/KUT/_BIM_COORD/lod_matrix.json'

# ── the tiers ───────────────────────────────────────────────────────────────
# Tier A  serialised plant: individually commissioned, carries a nameplate, and
#         sits under a service contract or a BMS point.
# Tier B  maintainable devices: high count, no meaningful serial number. Type
#         level data plus installation date. Requiring a serial per luminaire is
#         the classic unachievable requirement.
# Tier C  warranted fabric: no serial, no maintenance regime, but a warranty the
#         Owner will need to claim against.
# Tier D  everything else: identifier only. Not listed here; inherits rung 400.

TIER_A = ['Mechanical Equipment', 'Electrical Equipment', 'Specialty Equipment']
TIER_B = ['Lighting Fixtures', 'Plumbing Fixtures', 'Air Terminals', 'Sprinklers',
          'Fire Alarm Devices', 'Electrical Fixtures']
TIER_C = ['Roofs', 'Curtain Panels', 'Curtain Wall Mullions', 'Doors', 'Windows', 'Casework']
TIER_FFE = ['Furniture', 'Furniture Systems']

A_FIELDS = ['+ASS_ASSET_ID_TXT', '+ASS_SERIAL_NR_TXT', '+ASS_INSTALLATION_DATE_TXT',
            '+ASS_SUPPLIER_TXT', '+ASS_WARRANTY_PARTS_TXT', '+ASS_WARRANTY_DURATION_PARTS_YRS',
            '+COM_WARRANTY_START_TXT', '+ASS_EXPECTED_LIFE_YEARS_YRS',
            '+ASS_MAINTENANCE_FREQUENCY_MONTHS', '+MNT_SPARE_PARTS_TXT',
            '+COM_COMMISSION_DATE_TXT']

B_FIELDS = ['+ASS_INSTALLATION_DATE_TXT', '+ASS_SUPPLIER_TXT',
            '+ASS_WARRANTY_DURATION_PARTS_YRS', '+ASS_EXPECTED_LIFE_YEARS_YRS']

# Fire alarm devices are identified by loop and address, not by serial number.
B_EXTRA = {'Fire Alarm Devices': ['+FLS_SFTY_DEV_LOOP_TXT', '+FLS_SFTY_DEV_ADDRESS_TXT']}

C_FIELDS = ['+ASS_INSTALLATION_DATE_TXT', '+ASS_SUPPLIER_TXT', '+ASS_WARRANTY_PARTS_TXT',
            '+ASS_WARRANTY_DURATION_PARTS_YRS', '+COM_WARRANTY_START_TXT']

FFE_FIELDS = ['+ASS_INSTALLATION_DATE_TXT', '+FOHLIO_REF_TXT', '+ASS_SUPPLIER_TXT',
              '+ASS_WARRANTY_DURATION_PARTS_YRS']

TIERS = [('A', TIER_A, A_FIELDS), ('B', TIER_B, B_FIELDS),
         ('C', TIER_C, C_FIELDS), ('FF&E', TIER_FFE, FFE_FIELDS)]

DESCRIPTION = (
    "Kampala Uganda Temple (KUT) PROJECT OVERLAY for the STING LOD verification matrix. "
    "Merged by id/category over the corporate baseline (StingTools/Data/STING_LOD_MATRIX.json) -- "
    "project milestones and categoryRules win. Deploy to <project>/_BIM_COORD/lod_matrix.json. "
    "\n\n"
    "THIS OVERLAY DELIBERATELY DIVERGES FROM THE CORPORATE BASELINE AT RUNG 500. The baseline "
    "requires a serial number and installation date on ten categories, including Lighting Fixtures, "
    "Air Terminals, Sprinklers and Fire Alarm Devices. On a project of this size that is thousands "
    "of devices, most of which carry no meaningful serial number, and an asset information "
    "requirement that cannot be met is worse than a smaller one that can: it is completed at forty "
    "per cent and the facilities team cannot rely on any of it. The overlay therefore tiers the "
    "requirement by what the asset actually is. "
    "\n\n"
    "Tier A -- serialised plant (Mechanical Equipment, Electrical Equipment, Specialty Equipment). "
    "Individually commissioned, carries a nameplate, sits under a service contract. Full asset "
    "record including serial number, warranty, expected life, maintenance interval, spares and "
    "commissioning date. "
    "\n\n"
    "Tier B -- maintainable devices (Lighting Fixtures, Plumbing Fixtures, Air Terminals, "
    "Sprinklers, Fire Alarm Devices, Electrical Fixtures). High count, type-level data plus "
    "installation date, supplier, warranty duration and expected life. NO serial number. Fire Alarm "
    "Devices instead carry loop and address, which is what the fire alarm cause-and-effect and any "
    "future maintenance actually uses. "
    "\n\n"
    "Tier C -- warranted fabric (Roofs, Curtain Panels, Curtain Wall Mullions, Doors, Windows, "
    "Casework). No serial number and no maintenance regime, but a warranty the Owner will need to "
    "claim against, and on this project a significant quantity of bespoke joinery and specialist "
    "envelope. Supplier, warranty guarantor, duration and start date. "
    "\n\n"
    "Tier D -- every other category inherits rung 400 unchanged and carries the asset identifier "
    "only. "
    "\n\n"
    "Each category below carries its COMPLETE ladder, not only rung 500, because the registry "
    "replaces a category wholesale. A category listing rung 500 alone would resolve to nothing at "
    "the other rungs and its elements would drop silently out of every earlier gate. Regenerate "
    "with tools/build_kut_lod_overlay.py rather than editing by hand."
)


def main():
    corp = json.load(io.open(CORPORATE, encoding='utf-8'))
    by_cat = {c['category']: c for c in corp['categoryRules']}
    star = by_cat['*']

    tier_of = {}
    rules = []
    for tier, cats, fields in TIERS:
        for cat in cats:
            src = by_cat.get(cat, star)
            checks = json.loads(json.dumps(src['checks']))   # deep copy
            extra = list(fields) + B_EXTRA.get(cat, [])
            base500 = checks.get('500') or {'inherit': '400'}
            base500 = dict(base500)
            base500['inherit'] = base500.get('inherit', '400')
            base500['requiredParams'] = extra
            checks['500'] = base500
            rules.append({'category': cat, 'checks': checks})
            tier_of[cat] = tier

    overlay = {
        'version': '1.1',
        'description': DESCRIPTION,
        'milestones': corp['milestones'],
        'categoryRules': rules,
    }
    io.open(OVERLAY, 'w', encoding='utf-8', newline='\n').write(
        json.dumps(overlay, indent=2, ensure_ascii=False) + '\n')

    # ── verify: every category resolves at every rung, and 500 is what we meant
    def resolve(checks, key, seen=None):
        seen = seen or set()
        c = checks.get(key)
        if not c or key in seen:
            return None
        seen.add(key)
        base = resolve(checks, c['inherit'], seen) if c.get('inherit') else {}
        base = base or {}
        out = dict(base)
        lvl = c.get('requiredParams')
        if lvl is not None:
            plus = [s[1:] for s in lvl if s.startswith('+')]
            plain = [s for s in lvl if not s.startswith('+')]
            out['params'] = (plain if plain else list(base.get('params', []))) + plus
        return out

    print('overlay written: %s' % OVERLAY)
    print('categories: %d\n' % len(rules))
    problems = 0
    for r in rules:
        cat = r['category']
        for rung in ['200', '300', '350', '400', '500']:
            if resolve(r['checks'], rung) is None:
                print('  MISSING RUNG %s on %s' % (rung, cat))
                problems += 1
    counts = collections.Counter(tier_of.values())
    for tier, cats, _f in TIERS:
        n500 = len(resolve(by_cat.get(cats[0], star)['checks'], '500').get('params', []))
        got = resolve([r for r in rules if r['category'] == cats[0]][0]['checks'], '500')
        print('  Tier %-4s %2d categories  |  rung 500 fields: %d  (e.g. %s)'
              % (tier, counts[tier], len(got['params']), cats[0]))
    print('\n%s' % ('all categories resolve at every rung' if not problems
                    else 'PROBLEMS: %d' % problems))
    return 1 if problems else 0


if __name__ == '__main__':
    raise SystemExit(main())
