# -*- coding: utf-8 -*-
"""The naming and numbering convention: volumes, levels, roles, types, bands.

ONE source for how a container is named, rendered into two documents that need
it for different reasons. The BIM Execution Plan states it as a requirement; the
Document Control Standard states it as a procedure, with the tables a drafter
reads at the desk. Both render from here.

WHY IT IS NOT WRITTEN TWICE
It already was. The June pack carried a Document Control Standard and a
Numbering Convention; the August pack put the same fields in BEP 4.2 and left
the June documents alone. They drifted immediately -- June assumed four-character
originators and alphabetic volume codes, August three characters and numeric,
and each was internally consistent, so nothing looked wrong from inside either
one. The type codes then drifted a third time into the delivery-plan schema.

A container name is the one string every other artefact depends on: the
register, the transmittal, the coordination report, the asset identifier and the
handover data all carry it. It cannot have three definitions.

Stdlib only. midp_schema imports the type-code list from here, so the workbook's
drop-down cannot offer a code the convention does not define.
"""
from __future__ import annotations

PROJECT_CODE = 'KUT'

# ── volumes ─────────────────────────────────────────────────────────────────
# (numbering value, code, name, gross internal area in m2 or None)
# The numbering value is what appears in a container name. The alphabetic code
# is the ELEMENT location token, which is a different field in a different
# string -- BEP 4.2.2 keeps container fields and asset identifier fields apart,
# and this table is where the two are related rather than confused.
VOLUMES = (
    ('01', 'BLD1', 'Temple', 2449),
    ('02', 'BLD2', 'Meetinghouse', 1312),
    ('03', 'BLD3', 'Housing and ancillary', 2554),
    ('04', 'BLD4', 'Grounds building', 93),
    ('05', 'BLD5', 'Utility building', 166),
    ('06', 'BLD6', 'Guard house', 23),
    ('00', 'EXT', 'Site-wide and external works', None),
    ('ZZ', 'ZZ', 'All volumes', None),
)

GROSS_AREA_M2 = sum(a for _n, _c, _v, a in VOLUMES if a)

# ── levels ──────────────────────────────────────────────────────────────────
LEVELS = (
    ('B1', 'Basement'),
    ('GF', 'Ground floor'),
    ('01', 'First floor, and upward in sequence'),
    ('RF', 'Roof'),
    ('ZZ', 'All levels'),
    ('XX', 'Not applicable'),
)

# ── roles ───────────────────────────────────────────────────────────────────
# The role field of a container name. ROLES are NOT the element discipline codes:
# those are ASSET_DISCIPLINES, below, and the two are related there rather than
# confused -- the same split as volume 01 (container) and location BLD1 (element).
# ── role codes ──────────────────────────────────────────────────────────────
# BS EN ISO 19650-2 UK National Annex, Table NA.3. The role field states the
# DISCIPLINE OF THE ORIGINATOR, not the subject of the container, and the codes
# are the standard's -- not this project's.
#
# Two earlier project inventions were withdrawn when the project adopted the
# standard set, because each collided with a standard meaning:
#   G was used for Civil.        In the standard G is Geographical/Land Surveyor;
#                                Civil Engineer is C.
#   Interiors were folded into A. The standard separates Interior Designer as I,
#                                which is also who owns the FF&E record.
# FP (fire protection) and LV (low voltage) are not in the standard at all. Both
# are issued under Y, Specialist Designer, and distinguished by the Volume/System
# field rather than by inventing a role code -- an invented code is invalid on
# every downstream system that reads the standard, and silently so.
ROLES = (
    ('A', 'Architect'),
    ('C', 'Civil Engineer'),
    ('E', 'Electrical Engineer'),
    ('I', 'Interior Designer — including FF&E and finishes'),
    ('M', 'Mechanical Engineer'),
    ('P', 'Public Health Engineer — plumbing and drainage'),
    ('Q', 'Quantity Surveyor'),
    ('S', 'Structural Engineer'),
    ('W', 'Contractor'),
    ('X', 'Sub-contractor'),
    ('Y', 'Specialist Designer — fire protection, low voltage and communications'),
)
CONTAINER_ONLY_ROLES = (
    ('Z', 'General — multi-discipline, federated and management containers'),
)
ALL_ROLES = ROLES + CONTAINER_ONLY_ROLES

# ── element (asset) discipline codes ───────────────────────────────────────
# The DISCIPLINE field of the eight-field element tag. ISO 19650 does not define
# this field: the standard's role code (above) states the discipline of the
# ORGANISATION that produced a container, and says nothing about what letter an
# element inside it carries. This project once declared the role codes to be the
# element codes too. That was a project choice, not the standard, and it could
# not be met: the tagging that produces these identifiers writes FP for fire
# protection, LV for low voltage and communications, and G for general elements
# (generic models, specialty equipment, model groups), and the owner-standards
# audit then failed every sprinkler, detector, data outlet and item of specialty
# equipment -- baptistry plant included.
#
# (code, meaning, the container role such elements are normally issued under)
# The third field is guidance for a reader, never a rule: a container's role is
# its originator's discipline, so a sprinkler head drawn in the mechanical
# engineer's model is in a container with role M. None means "whichever model the
# element is in".
#
# owner_standards.json's discipline-code-valid list is derived from this table by
# tools/build_kut_owner_standards.py, and StingTools.Tags.Tests holds it to the
# codes the tagger can actually write.
ASSET_DISCIPLINES = (
    ('A', 'Architecture -- including interiors, FF&E and finishes', 'A'),
    ('S', 'Structure', 'S'),
    ('M', 'Mechanical -- including heating, cooling and ventilation pipework', 'M'),
    ('E', 'Electrical -- power and lighting', 'E'),
    ('P', 'Public health -- plumbing, drainage and gas', 'P'),
    ('FP', 'Fire protection -- sprinklers, suppression and fire alarm devices', 'Y'),
    ('LV', 'Low voltage -- data, communications, security and audio-visual', 'Y'),
    ('G', 'General -- generic models, specialty equipment and model groups', None),
)
ASSET_DISCIPLINE_CODES = [c for c, _m, _r in ASSET_DISCIPLINES]

# ── type codes ──────────────────────────────────────────────────────────────
# BS EN ISO 19650-2 UK National Annex, Table NA.2.
#
# SH IS A SCHEDULE, NOT A SHEET. The project previously read SH as "Sheet" and
# invented SC for a schedule, which inverted the standard: a container named
# ...-SH-A-0100 was valid under both readings and meant a sheet to one reader
# and a schedule to the other. A sheet is not a type in the standard -- a sheet
# is a DRAWING, DR.
#
# CA (calculation), MS (method statement) and DC (document) were project
# inventions and are withdrawn; each is a report, RP. BQ is withdrawn in favour
# of the standard's CP, Cost plan.
TYPES = (
    ('CO', 'Correspondence'),
    ('CP', 'Cost plan — including bills of quantities'),
    ('CR', 'Clash rendition'),
    ('DR', 'Drawing — including sheets'),
    ('IE', 'Information exchange — including transmittals'),
    ('M2', '2D model'),
    ('M3', '3D model'),
    ('MI', 'Minutes or action list'),
    ('PP', 'Presentation'),
    ('PR', 'Programme'),
    ('RD', 'Room data sheet'),
    ('RI', 'Request for information'),
    ('RP', 'Report — including calculations and method statements'),
    ('SH', 'Schedule'),
    ('SN', 'Snagging list'),
    ('SP', 'Specification'),
    ('SU', 'Survey'),
    ('VS', 'Visualisation'),
)
TYPE_CODES = [c for c, _m in TYPES]

# ── sheet number bands ──────────────────────────────────────────────────────
# The first digit of the four-digit Number field on a SHEET carries meaning.
# Three schemes were live across the pack: a plain sequence from 0001, a
# per-discipline sequence, and this banding. Banding is proposed because it
# survives insertion -- a section added late takes the next free number in its
# own band instead of renumbering everything after it -- and because the Owner's
# reviewers read it directly. NOT YET CONFIRMED: BEP 15.2 carries it as a
# kickoff decision. Non-sheet containers stay sequential from 0001.
SHEET_BANDS = (
    ('0xxx', 'General — cover, drawing list, notes, legends, key plans'),
    ('1xxx', 'Plans — general arrangement, reflected ceiling, roof, setting out'),
    ('2xxx', 'Elevations'),
    ('3xxx', 'Sections'),
    ('4xxx', 'Large-scale plans and enlarged areas'),
    ('5xxx', 'Details'),
    ('6xxx', 'Schedules'),
    ('7xxx', 'Diagrams and schematics — single line, riser, system'),
    ('8xxx', 'Three-dimensional views and presentation'),
    ('9xxx', 'Reserved'),
)
SHEET_BANDS_CONFIRMED = False

# ── suitability ─────────────────────────────────────────────────────────────
# (code, meaning, CDE state)
SUITABILITY = (
    ('S0', 'Work in progress; not for use by others', 'WIP'),
    ('S1', 'Shared for coordination', 'Shared'),
    ('S2', 'Shared for information', 'Shared'),
    ('S3', 'Shared for review and comment', 'Shared'),
    ('S4', 'Shared for stage approval', 'Shared'),
    ('A1 to An', 'Published and authorised; contractual', 'Published'),
    ('B1 to Bn', 'Published and authorised with comments', 'Published'),
)

# ── the container name ──────────────────────────────────────────────────────
ORIGINATOR_LENGTH = 3
EXAMPLE_ORIGINATOR = 'SMB'


def container_fields(originator_note):
    """The Field / Length / Permitted values table, as both documents show it."""
    return [
        ['Project', '3', PROJECT_CODE],
        ['Originator', originator_note, 'Per the originator register'],
        ['Volume', '2', ', '.join(n for n, _c, _v, _a in VOLUMES)],
        ['Level', '2', ', '.join('%s %s' % (c, m.lower()) for c, m in LEVELS)],
        ['Type', '2', ', '.join(TYPE_CODES)],
        ['Role', '1 to 2', '%s. %s is used for multi-discipline and federated containers'
            % (', '.join(c for c, _m in ROLES), CONTAINER_ONLY_ROLES[0][0])],
        ['Number', '4', 'Sequential within the set'],
    ]


def example_name():
    return '%s - %s - 01 - GF - M3 - A - 0001' % (PROJECT_CODE, EXAMPLE_ORIGINATOR)


def _check():
    """The convention must be internally coherent before anything renders it."""
    problems = []
    seen = set()
    for group, name in ((VOLUMES, 'volume'), (LEVELS, 'level'),
                        (ALL_ROLES, 'role'), (TYPES, 'type')):
        codes = [row[0] for row in group]
        dupes = {c for c in codes if codes.count(c) > 1}
        if dupes:
            problems.append('%s code(s) %s defined twice' % (name, ', '.join(sorted(dupes))))
    for code, _m in TYPES:
        if len(code) != 2:
            problems.append('type code %r is not two characters' % code)
    for code, _m in ALL_ROLES:
        if not 1 <= len(code) <= 2:
            problems.append('role code %r is not one or two characters' % code)
    for num, _c, _v, _a in VOLUMES:
        if len(num) != 2:
            problems.append('volume numbering value %r is not two characters' % num)
    if len(SHEET_BANDS) != 10:
        problems.append('sheet bands must cover every first digit 0 to 9')
    if problems:
        raise SystemExit('naming convention is invalid:\n  ' + '\n  '.join(problems))
    return seen


_check()
