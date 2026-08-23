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

# -- permitted values behind the drop-downs ---------------------------------
# These are written to the 'Lists' sheet, one per column, in this key order.

LISTS = {
    'Discipline': ['Information Management', 'Architecture', 'Interiors', 'Structure', 'Mechanical',
                   'Electrical', 'Public Health', 'Fire Protection', 'Low Voltage', 'Civil and Site',
                   'QS / Cost', 'FF&E', 'Contractor', 'All disciplines'],
    'Type': ['Model', 'Drawing', 'Schedule', 'Document', 'Specification', 'Report', 'Calculation',
             'Room data sheet', 'Model/Drawing', 'Model/Document'],
    'Stage': ['Mobilisation', '2.1 Deliverable A', '2.2 Deliverable B', '2.3 Deliverable C',
              '2.4 Tender', '2.5 Conformed set', '3.1 Construction', '3.2 FF&E', '3.3 Deliverable D'],
    'LOD': ['n/a', '100', '200', '300', '350', '400', '500'],
    'Suitability': ['S0', 'S1', 'S2', 'S3', 'S4', 'A1', 'B1'],
    'CDE State': ['WIP', 'Shared', 'Published', 'Archived'],
    'RAG': ['Green', 'Amber', 'Red', 'Complete'],
}

# -- the register columns, in order, with their display widths ---------------

COLS = [
    ('Ref', 10), ('Discipline', 20), ('Originator', 12), ('Deliverable', 44), ('Type', 15),
    ('Stage', 20), ('LOD', 7), ('Format', 15), ('Suitability', 11), ('CDE State', 12),
    ('Planned month', 14), ('Planned date', 13), ('Actual date', 13), ('Variance (days)', 14),
    ('Responsible', 20), ('TIDP ref', 10), ('RAG', 11), ('Notes', 40),
]

COL_NAMES = [name for name, _w in COLS]

# The source column on the Lists sheet is NOT the same letter as the column being
# validated -- they are mapped explicitly. Pointing a list at its own column
# letter silently offers the wrong values, which a reader takes as correct: that
# was a real defect in this workbook, across all seven drop-downs.
LIST_COL = {'Discipline': 'A', 'Originator': 'B', 'Type': 'C', 'Stage': 'D',
            'LOD': 'E', 'Suitability': 'F', 'CDE State': 'G', 'RAG': 'H'}

# (column letter on the sheet, permitted-value key) for each validated column.
MIDP_VALIDATED = (('B', 'Discipline'), ('E', 'Type'), ('F', 'Stage'), ('G', 'LOD'),
                  ('I', 'Suitability'), ('J', 'CDE State'), ('Q', 'RAG'))
TIDP_VALIDATED = (('C', 'Discipline'), ('F', 'Type'), ('G', 'Stage'), ('H', 'LOD'),
                  ('J', 'Suitability'), ('K', 'CDE State'), ('R', 'RAG'))

# -- sheet geometry ---------------------------------------------------------
# The MIDP register starts at A1; the TIDP return template is indented one
# column and carries a header block above it. merge_tidp.py locates the header
# row by searching for 'Ref' rather than trusting these, so a layout change
# degrades to a clear error instead of a silent column shift -- but they are
# recorded here because the builder needs them and because a reader deserves to
# know where the rows are meant to be.

MIDP_SHEET = 'MIDP'
TIDP_SHEET = 'TIDP'
LISTS_SHEET = 'Lists'

MIDP_HEADER_ROW = 1
MIDP_FIRST_COL = 1                # column A
TIDP_HEADER_ROW = 16
TIDP_FIRST_COL = 2                # column B
TIDP_BLANK_ROWS = 40

# The column a returned TIDP is matched on when merging into the register.
KEY_COL = 'Ref'

# Columns the Information Manager owns, not the returning party: they are
# maintained in the register and a returned value never overwrites them.
# 'Variance (days)' is a formula in the register, so a literal from a return
# would replace the calculation with a stale number.
IM_OWNED = ('Variance (days)',)
