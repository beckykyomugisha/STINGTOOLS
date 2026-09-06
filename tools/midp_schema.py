# -*- coding: utf-8 -*-
"""The MIDP / TIDP schema: columns, permitted values, and the sheet geometry.

Extracted from tools/build_midp.py so that the builder and tools/merge_tidp.py
work from one definition. A returned TIDP is validated against the same lists
the workbook offered in its drop-downs; if the merge tool restated them, the two
would drift and the merge would start rejecting values the workbook itself had
just handed the consultant -- or worse, accepting values it had not.

Stdlib-only: openpyxl is needed to WRITE the workbook, not to describe it.
"""
from __future__ import annotations

from kut_naming import TYPE_CODES as _TYPE_CODES

# -- permitted values behind the drop-downs ---------------------------------
# These are written to the 'Lists' sheet, one per column, in this key order.

LISTS = {
    'Discipline': ['Information Management', 'Architecture', 'Interiors', 'Structure', 'Mechanical',
                   'Electrical', 'Public Health', 'Fire Protection', 'Low Voltage', 'Civil and Site',
                   'QS / Cost', 'FF&E', 'Contractor', 'All disciplines'],
    'Type': ['Model', 'Drawing', 'Schedule', 'Document', 'Specification', 'Report', 'Calculation',
             'Room data sheet', 'Model/Drawing', 'Model/Document', 'Schedule/Drawing'],
    'Stage': ['Mobilisation', '2.1 Deliverable A', '2.2 Deliverable B', '2.3 Deliverable C',
              '2.4 Tender', '2.5 Conformed set', '3.1 Construction', '3.2 FF&E', '3.3 Deliverable D'],
    'LOD': ['n/a', '100', '200', '300', '350', '400', '500'],
    'Suitability': ['S0', 'S1', 'S2', 'S3', 'S4', 'A1', 'B1'],
    'CDE State': ['WIP', 'Shared', 'Published', 'Archived'],
    'RAG': ['Green', 'Amber', 'Red', 'Complete'],
    'Volume': ['00', '01', '02', '03', '04', '05', '06', 'ZZ'],
    # From kut_naming, so the drop-down cannot offer a code the naming
    # convention does not define -- it had already drifted once.
    'Type code': list(_TYPE_CODES),
    'Yes/No': ['Y', 'N'],
}

# -- the register columns, in order, with their display widths ---------------
# 'Month from' / 'Month to' rather than a single 'Planned month': a deliverable
# is usually a point, but an Owner review window, a continuously maintained
# register and a progressive as-built capture are periods, and one column could
# not say so. 'Type code' sits beside 'Type' because the register has to be both
# checkable against the naming convention and readable by a consultant.

COLS = [
    ('Ref', 10), ('Discipline', 20), ('Originator', 12), ('Volume', 8), ('Deliverable', 44),
    ('Type', 15), ('Type code', 10), ('Stage', 20), ('LOD', 7), ('Format', 15),
    ('Suitability', 11), ('CDE State', 12), ('Month from', 11), ('Month to', 10),
    ('Planned date', 13), ('Actual date', 13), ('Variance (days)', 14),
    ('Responsible', 20), ('TIDP ref', 10), ('Critical', 9), ('As-built', 9), ('O&M', 7),
    ('RAG', 11), ('Notes', 40),
]

COL_NAMES = [name for name, _w in COLS]

# The source column on the Lists sheet is NOT the same letter as the column being
# validated -- they are mapped explicitly. Pointing a list at its own column
# letter silently offers the wrong values, which a reader takes as correct: that
# was a real defect in this workbook, across all seven drop-downs.
LIST_COL = {'Discipline': 'A', 'Originator': 'B', 'Type': 'C', 'Stage': 'D',
            'LOD': 'E', 'Suitability': 'F', 'CDE State': 'G', 'RAG': 'H',
            'Volume': 'I', 'Type code': 'J', 'Yes/No': 'K'}

# Which register column takes which list. Stated by COLUMN NAME, never by
# letter: the letters are computed below from the position of the name in COLS,
# so inserting a column cannot leave a drop-down pointing one column off. The
# original defect in this workbook was a hand-maintained letter mapping, and a
# second hand-maintained letter mapping is not the fix for the first.
VALIDATED_COLUMNS = (
    ('Discipline', 'Discipline'), ('Volume', 'Volume'), ('Type', 'Type'),
    ('Type code', 'Type code'), ('Stage', 'Stage'), ('LOD', 'LOD'),
    ('Suitability', 'Suitability'), ('CDE State', 'CDE State'),
    ('Critical', 'Yes/No'), ('As-built', 'Yes/No'), ('O&M', 'Yes/No'),
    ('RAG', 'RAG'),
)


def _letter(n: int) -> str:
    """1 -> 'A', 26 -> 'Z', 27 -> 'AA'. openpyxl is not importable here."""
    out = ''
    while n > 0:
        n, rem = divmod(n - 1, 26)
        out = chr(65 + rem) + out
    return out


def validated(first_col: int):
    """((column letter, list key), ...) for a register starting at `first_col`."""
    return tuple((_letter(COL_NAMES.index(name) + first_col), key)
                 for name, key in VALIDATED_COLUMNS)


def letter_of(name: str, first_col: int) -> str:
    """The sheet column letter for a register column, by name."""
    return _letter(COL_NAMES.index(name) + first_col)

# -- sheet geometry ---------------------------------------------------------
# The MIDP register starts at A1; the TIDP return template is indented one
# column and carries a header block above it. merge_tidp.py locates the header
# row by searching for 'Ref' rather than trusting these, so a layout change
# degrades to a clear error instead of a silent column shift -- but they are
# recorded here because the builder needs them and because a reader deserves to
# know where the rows are meant to be.

MIDP_SHEET = 'MIDP'
LISTS_SHEET = 'Lists'

MIDP_HEADER_ROW = 1
MIDP_FIRST_COL = 1                # column A
TIDP_FIRST_COL = 2                # column B

# TIDP_SHEET is now a PREFIX, not a sheet name. The workbook carries one plan
# per appointed party -- 'TIDP-A', 'TIDP-M', 'TIDP-ALL' -- because a single blank
# template made every party transcribe their own rows out of the register before
# they could confirm a date, and transcription is where a register and its
# delivery plans drift apart. merge_tidp.py matches on this prefix.
TIDP_SHEET = 'TIDP'

# There is no fixed TIDP header row any more: each plan sheet's table starts
# below a header block and however many pre-filled rows that party has. Both the
# builder and the merge tool locate it by searching for the key column, which is
# what they did anyway -- a constant here would have been a second answer to a
# question already answered correctly elsewhere.
TIDP_BLANK_ROWS = 15              # spare rows offered for additions

# The column a returned TIDP is matched on when merging into the register.
KEY_COL = 'Ref'

# Columns the Information Manager owns, not the returning party: they are
# maintained in the register and a returned value never overwrites them.
# 'Variance (days)' is a formula in the register, so a literal from a return
# would replace the calculation with a stale number.
IM_OWNED = ('Variance (days)',)
