# -*- coding: utf-8 -*-
"""Build the KUT Master Information Delivery Plan as a corporate .xlsx workbook.

Source content: GUIDES/KUT_MIDP_TEMPLATE.csv, with the tooling references removed
(this is a project document), the originator column reset pending the code
register, and the Stage 3.1 asset-data capture rows added.

Palette and register conventions match the issued Word documents produced by
tools/corporate_docx.py.
"""
import datetime
import pathlib
import sys

from openpyxl import Workbook
from openpyxl.formatting.rule import CellIsRule, FormulaRule
from openpyxl.styles import Alignment, Border, Font, NamedStyle, PatternFill, Side
from openpyxl.utils import get_column_letter
from openpyxl.worksheet.datavalidation import DataValidation

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import kut_docs_lib as K                                          # noqa: E402
import midp_rows as D                                             # noqa: E402
import midp_schema as S                                           # noqa: E402
from midp_schema import COLS, COL_NAMES, LIST_COL, LISTS          # noqa: E402

OUT = 'KUT_DOCS_WORKING/issued/KUT_Master_Information_Delivery_Plan.xlsx'

NAVY = '1F3654'
SLATE = '444F5C'
BAND = 'E8EDF3'
SHADE = 'F4F6F9'
LINE = 'C6CEDA'
RED = 'F8D7DA'
AMBER = 'FFF0CC'
GREEN = 'DDF0DD'

thin = Side(style='thin', color=LINE)
box = Border(left=thin, right=thin, top=thin, bottom=thin)

wb = Workbook()

title_f = Font(name='Calibri', size=22, bold=True, color=NAVY)
eyebrow_f = Font(name='Calibri', size=10, bold=True, color=SLATE)
h_f = Font(name='Calibri', size=11, bold=True, color=NAVY)
lbl_f = Font(name='Calibri', size=9, bold=True, color=SLATE)
body_f = Font(name='Calibri', size=9.5)
small_f = Font(name='Calibri', size=8.5, italic=True, color=SLATE)
hdr_fill = PatternFill('solid', fgColor=BAND)
shade_fill = PatternFill('solid', fgColor=SHADE)

# Validation lists and register columns live in tools/midp_schema.py so that
# tools/merge_tidp.py validates a returned TIDP against exactly what this
# workbook offered. See that module for why.

# ── register rows ───────────────────────────────────────────────────────────
# The 122 deliverables, their provenance and the Task Information Delivery Plans
# they belong to all live in tools/midp_rows.py, which validates itself on
# import: every row must start inside its own stage, carry a permitted type
# code, volume, suitability and state, and name a Task Information Delivery Plan
# that exists. A bad row fails the build.
R = D.R


def style_header(ws, row, ncols):
    for i in range(1, ncols + 1):
        c = ws.cell(row=row, column=i)
        c.font = Font(name='Calibri', size=9, bold=True, color=NAVY)
        c.fill = hdr_fill
        c.border = box
        c.alignment = Alignment(vertical='center', wrap_text=True)
    ws.row_dimensions[row].height = 30


# ═══ 1. Cover ═══════════════════════════════════════════════════════════════
ws = wb.active
ws.title = 'Cover'
ws.sheet_view.showGridLines = False
ws.column_dimensions['A'].width = 3
ws.column_dimensions['B'].width = 30
ws.column_dimensions['C'].width = 76

ws['B3'] = 'KAMPALA UGANDA TEMPLE'
ws['B3'].font = eyebrow_f
ws['B5'] = 'Master Information Delivery Plan'
ws['B5'].font = title_f
ws['B7'] = ('The aggregated schedule of information deliverables for the project, compiled from the Task '
            'Information Delivery Plan of every appointed party.')
ws['B7'].font = Font(name='Calibri', size=11, color=SLATE)
ws['B7'].alignment = Alignment(wrap_text=True, vertical='top')
ws.merge_cells('B7:C7')
ws.row_dimensions[7].height = 32

meta = [
    ('Document reference', 'KUT-SMB-ZZ-ZZ-SH-Z-0001'),
    ('Revision', 'P01'),
    ('Status / suitability', 'DRAFT — not issued (S0, work in progress)'),
    ('Prepared by', 'Symbion Consulting Group Studios (Information Manager)'),
    ('Baselined', '[FILL — date]'),
    ('Last updated', '[FILL — date]'),
    ('Update frequency', 'Monthly, and at every data drop'),
]
r = 10
for k, v in meta:
    ws.cell(row=r, column=2, value=k).font = lbl_f
    ws.cell(row=r, column=3, value=v).font = body_f
    r += 1

r += 1
ws.cell(row=r, column=2, value='How to use this workbook').font = h_f
r += 1
for line in [
    ('Programme', 'Enter the appointment date here, once. Every calendar date in the workbook is calculated from it.'),
    ('MIDP', 'The project register. One row per deliverable. Filter by discipline, stage or status. '
             'The Information Manager maintains this sheet.'),
    ('TIDP-…', 'One Task Information Delivery Plan per appointed party, pre-filled with the deliverables '
               'already assigned to it. Confirm the dates, add anything your scope requires that is not '
               'listed, and return the sheet. TIDP-ALL carries the deliverables every party owes.'),
    ('Summary', 'Counts by stage, discipline and delivery plan, calculated from the MIDP sheet. '
                'Nothing is typed here.'),
    ('Drawing schedule', 'Indicative sheet counts by volume and role, for production planning.'),
    ('Change log', 'What changed since the previous issue of this register, and why, including the '
                   'references that were retired.'),
    ('Lists', 'The permitted values behind the drop-down lists. Amend here to change the drop-downs.'),
]:
    ws.cell(row=r, column=2, value=line[0]).font = lbl_f
    c = ws.cell(row=r, column=3, value=line[1])
    c.font = body_f
    c.alignment = Alignment(wrap_text=True, vertical='top')
    ws.row_dimensions[r].height = 28
    r += 1

r += 1
ws.cell(row=r, column=2, value='Before this plan is baselined').font = h_f
r += 1
note = ('The Originator column is deliberately empty. The originator code register has not been confirmed, '
        'and the container naming rule requires a code of a fixed length that is still to be agreed. Complete '
        'the Originator column only once the register is issued. Nothing on this project is to be numbered '
        'before that point.')
c = ws.cell(row=r, column=2, value=note)
c.font = Font(name='Calibri', size=9.5)
c.alignment = Alignment(wrap_text=True, vertical='top')
ws.merge_cells(start_row=r, start_column=2, end_row=r + 2, end_column=3)
for rr in range(r, r + 3):
    for cc in (2, 3):
        ws.cell(row=rr, column=cc).fill = shade_fill
r += 4

ws.cell(row=r, column=2, value=('Not yet issued through the Common Data Environment. Uncontrolled when printed.')).font = small_f

# ═══ 1b. Programme ══════════════════════════════════════════════════════════
# ONE input: the appointment date. Every calendar date in this workbook derives
# from it by formula. Until now the pack carried month offsets and the only
# calendar dates anywhere were illustrative ones in a guide, assuming an
# appointment in September 2026 -- so nobody could answer "what date is
# Deliverable B" without doing the arithmetic themselves and getting a different
# answer from the next person. Enter the date once here on appointment.
ps = wb.create_sheet('Programme')
ps.sheet_view.showGridLines = False
ps.column_dimensions['A'].width = 3
for col, w in (('B', 34), ('C', 10), ('D', 10), ('E', 16), ('F', 16), ('G', 10)):
    ps.column_dimensions[col].width = w

ps['B2'] = 'Programme'
ps['B2'].font = Font(name='Calibri', size=16, bold=True, color=NAVY)
ps['B4'] = 'Appointment date (M0)'
ps['B4'].font = lbl_f
APPT = 'C4'
ps[APPT] = None
ps[APPT].number_format = 'dd mmm yyyy'
ps[APPT].fill = PatternFill('solid', fgColor=AMBER)
ps[APPT].border = box
ps['E4'] = '← the only date entered by hand. Everything else is calculated from it.'
ps['E4'].font = small_f

ps['B6'] = ('Stage durations are the Appointing Party Work Program: Phase 2 totals %d months and Phase 3 '
            'totals %d months, %d in all, read in sequence.'
            % (K.PHASE_SUBTOTALS['2'], K.PHASE_SUBTOTALS['3'], K.TOTAL_MONTHS))
ps['B6'].font = small_f
ps['B6'].alignment = Alignment(wrap_text=True, vertical='top')
ps.merge_cells('B6:G7')

r = 9
for i, hh in enumerate(['Stage', 'From', 'To', 'Starts', 'Ends', 'Months'], start=2):
    ps.cell(row=r, column=i, value=hh)
style_header(ps, r, 7)
r += 1
_months = K.stage_months()
for stage, title, lod, months, _kind in K.WORK_PROGRAMME:
    start, end, _ = _months[stage]
    vals = [
        '%s  %s' % (stage, title),
        'M%d' % start, 'M%d' % end,
        '=IF($%s="","",EDATE($%s,%d))' % (APPT, APPT, start - 1),
        '=IF($%s="","",EOMONTH($%s,%d))' % (APPT, APPT, end - 1),
        months,
    ]
    for i, v in enumerate(vals, start=2):
        c = ps.cell(row=r, column=i, value=v)
        c.font = body_f
        c.border = box
        if i in (5, 6):
            c.number_format = 'dd mmm yyyy'
        if i in (3, 4, 7):
            c.alignment = Alignment(horizontal='center')
    r += 1
ps.cell(row=r, column=2, value='Total').font = lbl_f
ps.cell(row=r, column=7, value=K.TOTAL_MONTHS).font = lbl_f
ps.cell(row=r, column=7).alignment = Alignment(horizontal='center')
for i in range(2, 8):
    ps.cell(row=r, column=i).border = box
    ps.cell(row=r, column=i).fill = shade_fill

r += 2
ps.cell(row=r, column=2, value=(
    'Calendar dates are indicative until the appointment date is entered and the plan is rebaselined. '
    'A stage runs to the end of its final month, so a stage ending M8 ends on the last day of the eighth '
    'month after appointment.')).font = small_f
ps.cell(row=r, column=2).alignment = Alignment(wrap_text=True, vertical='top')
ps.merge_cells(start_row=r, start_column=2, end_row=r + 1, end_column=7)

# ═══ 2. MIDP register ═══════════════════════════════════════════════════════
# Every column below is addressed BY NAME. The previous version wrote literal
# letters and indices -- 'Q2:Q%d' for the RAG band, `i in (12, 13)` for the date
# formats -- which are silently wrong the moment a column is inserted, and this
# workbook has just gained six. L() and C() resolve a name to a letter and an
# index from midp_schema.COLS, so the layout is stated once.
FIRST = S.MIDP_FIRST_COL


def L(name):
    return S.letter_of(name, FIRST)


def C(name):
    return COL_NAMES.index(name) + FIRST


ms = wb.create_sheet('MIDP')
ms.sheet_view.showGridLines = False
for i, (name, width) in enumerate(COLS, start=FIRST):
    ms.cell(row=1, column=i, value=name)
    ms.column_dimensions[get_column_letter(i)].width = width
style_header(ms, 1, len(COLS))

WRAP = {'Deliverable', 'Notes'}
CENTRE = {'Volume', 'Type code', 'LOD', 'Suitability', 'CDE State', 'Month from',
          'Month to', 'Critical', 'As-built', 'O&M', 'RAG'}
DATES = {'Planned date', 'Actual date'}

for n, r in enumerate(R, start=2):
    vals = {
        'Ref': r['ref'], 'Discipline': r['discipline'], 'Originator': '',
        'Volume': r['volume'], 'Deliverable': r['deliverable'], 'Type': r['type'],
        'Type code': r['isotype'], 'Stage': r['stage'], 'LOD': r['lod'],
        'Format': r['fmt'], 'Suitability': r['suit'], 'CDE State': r['state'],
        'Month from': r['m_from'], 'Month to': r['m_to'],
        # Derived from the one appointment date on the Programme sheet, at the
        # month the deliverable is due. A blank appointment date leaves it blank
        # rather than showing a date computed from zero.
        'Planned date': '=IF(Programme!$C$4="","",EOMONTH(Programme!$C$4,%d))' % r['m_to'],
        'Actual date': None,
        'Variance (days)': '=IF(AND({p}{n}<>"",{a}{n}<>""),{a}{n}-{p}{n},"")'.format(
            p=L('Planned date'), a=L('Actual date'), n=n),
        'Responsible': r['responsible'], 'TIDP ref': r['tidp'],
        'Critical': r['crit'], 'As-built': r['asbuilt'], 'O&M': r['om'],
        'RAG': '', 'Notes': r['notes'],
    }
    for name, _w in COLS:
        c = ms.cell(row=n, column=C(name), value=vals[name])
        c.font = body_f
        c.border = box
        c.alignment = Alignment(vertical='top', wrap_text=name in WRAP)
        if name in DATES:
            c.number_format = 'dd mmm yyyy'
        elif name == 'Variance (days)':
            c.number_format = '0;-0;""'
            c.alignment = Alignment(horizontal='center', vertical='top')
        elif name in CENTRE:
            c.alignment = Alignment(horizontal='center', vertical='top')

last = len(R) + 1
ms.freeze_panes = '%s2' % L('Deliverable')
ms.auto_filter.ref = 'A1:%s%d' % (L(COL_NAMES[-1]), last)

# Drop-downs. The source column on the Lists sheet is NOT the same letter as the
# column being validated -- LIST_COL maps them explicitly, in midp_schema.py.
# Pointing a list at its own column letter silently offers the wrong values,
# which a reader would take as correct.
for col_letter, key in S.validated(FIRST):
    src = LIST_COL[key]
    dv = DataValidation(type='list', formula1="'Lists'!$%s$2:$%s$30" % (src, src),
                        allow_blank=True, showDropDown=False)
    dv.error = 'Select a value from the list. To add a permitted value, amend the Lists sheet.'
    dv.errorTitle = 'Value not permitted'
    ms.add_data_validation(dv)
    dv.add('%s2:%s%d' % (col_letter, col_letter, last))

# RAG colouring
rag = '{c}2:{c}{n}'.format(c=L('RAG'), n=last)
for word, colour in (('Red', RED), ('Amber', AMBER), ('Green', GREEN), ('Complete', GREEN)):
    ms.conditional_formatting.add(rag, CellIsRule(
        operator='equal', formula=['"%s"' % word], fill=PatternFill('solid', fgColor=colour)))

# Critical-path deliverables are banded so they read at a glance.
ms.conditional_formatting.add(
    'A2:%s%d' % (L(COL_NAMES[-1]), last),
    FormulaRule(formula=['${c}2="Y"'.format(c=L('Critical'))],
                fill=PatternFill('solid', fgColor=SHADE), stopIfTrue=False))

# overdue: planned date passed and no actual date
ms.conditional_formatting.add(
    'A2:%s%d' % (L(COL_NAMES[-1]), last),
    FormulaRule(formula=['AND(${p}2<>"",${a}2="",${p}2<TODAY())'.format(
        p=L('Planned date'), a=L('Actual date'))],
        fill=PatternFill('solid', fgColor=RED), stopIfTrue=False))

ms.page_setup.orientation = 'landscape'
ms.page_setup.fitToWidth = 1
ms.page_setup.fitToHeight = 0
ms.sheet_properties.pageSetUpPr.fitToPage = True
ms.print_title_rows = '1:1'

# ═══ 3. Task Information Delivery Plans, one sheet per appointed party ══════
# The August workbook carried a single blank TIDP template. A Task Information
# Delivery Plan is per appointed party, so a blank template made every party
# transcribe their own rows out of the register before they could confirm a
# date -- and transcription is where the register and the plans drift apart.
#
# Each party now gets a sheet pre-filled with the deliverables already assigned
# to them, plus blank rows for anything the register has missed. They confirm
# dates and add rows; they do not retype the scope.
TFIRST = S.TIDP_FIRST_COL


def TL(name):
    return S.letter_of(name, TFIRST)


TIDP_BLANKS = 15
tidp_sheets = []

for tidp_ref, scope, org, _role in D.TIDPS + [('TIDP-ALL', 'Every appointed party',
                                               'All parties', 'ZZ')]:
    mine = [r for r in R if r['tidp'] == tidp_ref]
    ts = wb.create_sheet(tidp_ref)
    tidp_sheets.append(tidp_ref)
    ts.sheet_view.showGridLines = False
    ts.column_dimensions['A'].width = 3

    ts['B2'] = 'Task Information Delivery Plan — %s' % scope
    ts['B2'].font = Font(name='Calibri', size=16, bold=True, color=NAVY)
    ts['B4'] = ('The deliverables below are those already assigned to you in the Master Information '
                'Delivery Plan. Confirm the planned dates, add any deliverable your scope requires '
                'that is not listed, and return this sheet to the Information Manager. Rows are '
                'merged back into the register by reference. Use the drop-down lists; if a value '
                'you need is not offered, raise it rather than typing a variant.')
    ts['B4'].font = Font(name='Calibri', size=9.5)
    ts['B4'].alignment = Alignment(wrap_text=True, vertical='top')
    ts.merge_cells('B4:H5')

    hdr = [('Task Information Delivery Plan', tidp_ref),
           ('Scope', scope),
           ('Appointed party', org),
           ('Originator code', '[FILL — once the register is issued]'),
           ('Task Team Manager', '[FILL]'), ('Contact', '[FILL]'),
           ('Revision', 'P01'), ('Date', '[FILL]')]
    r = 7
    for k, v in hdr:
        ts.cell(row=r, column=2, value=k).font = lbl_f
        ts.cell(row=r, column=3, value=v).font = body_f
        r += 1

    r += 1
    for i, (name, width) in enumerate(COLS, start=TFIRST):
        ts.cell(row=r, column=i, value=name)
        ts.column_dimensions[get_column_letter(i)].width = width
    style_header(ts, r, len(COLS) + 1)
    header_row = r

    for n, row in enumerate(mine, start=header_row + 1):
        vals = {
            'Ref': row['ref'], 'Discipline': row['discipline'], 'Originator': '',
            'Volume': row['volume'], 'Deliverable': row['deliverable'], 'Type': row['type'],
            'Type code': row['isotype'], 'Stage': row['stage'], 'LOD': row['lod'],
            'Format': row['fmt'], 'Suitability': row['suit'], 'CDE State': row['state'],
            'Month from': row['m_from'], 'Month to': row['m_to'],
            'Planned date': None, 'Actual date': None, 'Variance (days)': None,
            'Responsible': row['responsible'], 'TIDP ref': row['tidp'],
            'Critical': row['crit'], 'As-built': row['asbuilt'], 'O&M': row['om'],
            'RAG': '', 'Notes': row['notes'],
        }
        for name, _w in COLS:
            c = ts.cell(row=n, column=COL_NAMES.index(name) + TFIRST, value=vals[name])
            c.font = body_f
            c.border = box
            c.alignment = Alignment(vertical='top', wrap_text=name in WRAP)
            if name in DATES:
                c.number_format = 'dd mmm yyyy'
            elif name in CENTRE:
                c.alignment = Alignment(horizontal='center', vertical='top')

    blank_start = header_row + 1 + len(mine)
    for n in range(blank_start, blank_start + TIDP_BLANKS):
        for i in range(TFIRST, len(COLS) + TFIRST):
            c = ts.cell(row=n, column=i)
            c.border = box
            c.font = body_f
            if COL_NAMES[i - TFIRST] in DATES:
                c.number_format = 'dd mmm yyyy'

    for col_letter, key in S.validated(TFIRST):
        src = LIST_COL[key]
        dv = DataValidation(type='list', formula1="'Lists'!$%s$2:$%s$30" % (src, src),
                            allow_blank=True, showDropDown=False)
        ts.add_data_validation(dv)
        dv.add('%s%d:%s%d' % (col_letter, header_row + 1, col_letter,
                              blank_start + TIDP_BLANKS - 1))

    ts.freeze_panes = 'A%d' % (header_row + 1)
    ts.page_setup.orientation = 'landscape'
    ts.page_setup.fitToWidth = 1
    ts.sheet_properties.pageSetUpPr.fitToPage = True
    ts.print_title_rows = '%d:%d' % (header_row, header_row)

# ═══ 4. Summary ═════════════════════════════════════════════════════════════
# Counts are COUNTIF over the register, and every column in every formula is
# resolved through L() by name. The previous version hard-coded $F for Stage and
# $Q for RAG; both moved when the register gained six columns, and a COUNTIF
# against the wrong column returns 0 rather than an error -- a summary that
# reads "no deliverables outstanding" because it is counting the Type column.
ss = wb.create_sheet('Summary')
ss.sheet_view.showGridLines = False
ss.column_dimensions['A'].width = 3
ss.column_dimensions['B'].width = 30
for col in 'CDEFG':
    ss.column_dimensions[col].width = 14

ss['B2'] = 'Delivery summary'
ss['B2'].font = Font(name='Calibri', size=16, bold=True, color=NAVY)
ss['B3'] = 'Calculated from the MIDP sheet. Do not type in this sheet.'
ss['B3'].font = small_f

C_STAGE, C_DISC, C_RAG, C_TIDP = L('Stage'), L('Discipline'), L('RAG'), L('TIDP ref')
HEADS = ['Deliverables', 'Complete', 'Red', 'Amber', 'Outstanding']


def block(r, title, key_head, key_col, values):
    ss.cell(row=r, column=2, value=title).font = h_f
    r += 1
    for i, hh in enumerate([key_head] + HEADS, start=2):
        ss.cell(row=r, column=i, value=hh)
    style_header(ss, r, 7)
    r += 1
    first = r
    for v in values:
        ss.cell(row=r, column=2, value=v).font = body_f
        ss.cell(row=r, column=3,
                value='=COUNTIF(MIDP!${c}:${c},$B{r})'.format(c=key_col, r=r)).font = body_f
        for i, word in ((4, 'Complete'), (5, 'Red'), (6, 'Amber')):
            ss.cell(row=r, column=i,
                    value='=COUNTIFS(MIDP!${c}:${c},$B{r},MIDP!${g}:${g},"{w}")'.format(
                        c=key_col, g=C_RAG, r=r, w=word)).font = body_f
        ss.cell(row=r, column=7, value='=C%d-D%d' % (r, r)).font = body_f
        for i in range(2, 8):
            ss.cell(row=r, column=i).border = box
            if i > 2:
                ss.cell(row=r, column=i).alignment = Alignment(horizontal='center')
        r += 1
    ss.cell(row=r, column=2, value='Total').font = lbl_f
    for i, col in enumerate('CDEFG', start=3):
        ss.cell(row=r, column=i, value='=SUM(%s%d:%s%d)' % (col, first, col, r - 1)).font = lbl_f
        ss.cell(row=r, column=i).alignment = Alignment(horizontal='center')
    for i in range(2, 8):
        ss.cell(row=r, column=i).border = box
        ss.cell(row=r, column=i).fill = shade_fill
    return r + 3


r = block(5, 'By stage', 'Stage', C_STAGE, LISTS['Stage'])
r = block(r, 'By discipline', 'Discipline', C_DISC, LISTS['Discipline'])
r = block(r, 'By Task Information Delivery Plan', 'TIDP', C_TIDP, tidp_sheets)

# Deliverables that carry the project: critical path, as-built and O&M feeds.
ss.cell(row=r, column=2, value='Deliverable classes').font = h_f
r += 1
for i, hh in enumerate(['Class', 'Deliverables'], start=2):
    ss.cell(row=r, column=i, value=hh)
style_header(ss, r, 3)
r += 1
for label, colname in (('On the critical path', 'Critical'),
                       ('Feeding the as-built model', 'As-built'),
                       ('Feeding the O&M / asset data pack', 'O&M')):
    ss.cell(row=r, column=2, value=label).font = body_f
    ss.cell(row=r, column=3,
            value='=COUNTIF(MIDP!${c}:${c},"Y")'.format(c=L(colname))).font = body_f
    for i in (2, 3):
        ss.cell(row=r, column=i).border = box
    ss.cell(row=r, column=3).alignment = Alignment(horizontal='center')
    r += 1

ss.page_setup.orientation = 'portrait'
ss.page_setup.fitToWidth = 1
ss.sheet_properties.pageSetUpPr.fitToPage = True

# ═══ 4b. Drawing schedule ═══════════════════════════════════════════════════
# The planned sheet count per volume and role. It is an estimate, and it is
# labelled as one -- but a drawing register with no expected total cannot tell
# anybody at Deliverable B whether production is behind.
ds = wb.create_sheet('Drawing schedule')
ds.sheet_view.showGridLines = False
ds.column_dimensions['A'].width = 3
for col, w in (('B', 10), ('C', 30), ('D', 16), ('E', 10), ('F', 58)):
    ds.column_dimensions[col].width = w

ds['B2'] = 'Planned drawing schedule'
ds['B2'].font = Font(name='Calibri', size=16, bold=True, color=NAVY)
ds['B3'] = ('Indicative sheet counts by volume and role, carried forward from the June information '
            'delivery plan. Confirmed against each Task Information Delivery Plan at mobilisation '
            'and rebaselined at Deliverable B.')
ds['B3'].font = small_f
ds['B3'].alignment = Alignment(wrap_text=True, vertical='top')
ds.merge_cells('B3:F4')

r = 6
for i, hh in enumerate(['Volume', 'Building', 'Role', 'Sheets', 'Content'], start=2):
    ds.cell(row=r, column=i, value=hh)
style_header(ds, r, 6)
r += 1
first_draw = r
for vol, bld, role, sheets, content in D.DRAWINGS:
    for i, v in enumerate((vol, bld, role, sheets, content), start=2):
        c = ds.cell(row=r, column=i, value=v)
        c.font = body_f
        c.border = box
        c.alignment = Alignment(vertical='top', wrap_text=(i == 6),
                                horizontal='center' if i in (2, 5) else 'left')
    r += 1
ds.cell(row=r, column=3, value='Total sheets').font = lbl_f
ds.cell(row=r, column=5, value='=SUM(E%d:E%d)' % (first_draw, r - 1)).font = lbl_f
ds.cell(row=r, column=5).alignment = Alignment(horizontal='center')
for i in range(2, 7):
    ds.cell(row=r, column=i).border = box
    ds.cell(row=r, column=i).fill = shade_fill
ds.freeze_panes = 'A%d' % first_draw
ds.auto_filter.ref = 'B%d:F%d' % (first_draw - 1, r - 1)
ds.page_setup.orientation = 'landscape'
ds.page_setup.fitToWidth = 1
ds.sheet_properties.pageSetUpPr.fitToPage = True

# ═══ 4c. Change log ═════════════════════════════════════════════════════════
# Where every row came from, and what happened to the ones that went. Generated
# from the `source` and `change` fields on the register, so it cannot describe a
# change that was not actually made -- which is the failure mode of a change log
# maintained by hand beside the thing it describes.
cl = wb.create_sheet('Change log')
cl.sheet_view.showGridLines = False
cl.column_dimensions['A'].width = 3
for col, w in (('B', 12), ('C', 12), ('D', 44), ('E', 62)):
    cl.column_dimensions[col].width = w

cl['B2'] = 'Change log'
cl['B2'].font = Font(name='Calibri', size=16, bold=True, color=NAVY)
cl['B3'] = ('What changed between the previous issue of this register and this one, and why. '
            '"Restored" rows were carried by the June plan and dropped by the August issue.')
cl['B3'].font = small_f
cl['B3'].alignment = Alignment(wrap_text=True, vertical='top')
cl.merge_cells('B3:E4')

SRC_LABEL = {'Both': 'Carried forward', 'Aug P01': 'Carried forward',
             'Jun P01': 'Restored', 'New P02': 'Added'}

r = 6
cl.cell(row=r, column=2, value='Counts').font = h_f
r += 1
for i, hh in enumerate(['Change', 'Deliverables'], start=2):
    cl.cell(row=r, column=i, value=hh)
style_header(cl, r, 3)
r += 1
import collections as _collections
counts = _collections.Counter(SRC_LABEL[x['source']] for x in R)
for label in ('Carried forward', 'Restored', 'Added'):
    cl.cell(row=r, column=2, value=label).font = body_f
    cl.cell(row=r, column=3, value=counts.get(label, 0)).font = body_f
    for i in (2, 3):
        cl.cell(row=r, column=i).border = box
    cl.cell(row=r, column=3).alignment = Alignment(horizontal='center')
    r += 1
cl.cell(row=r, column=2, value='Total').font = lbl_f
cl.cell(row=r, column=3, value=len(R)).font = lbl_f
cl.cell(row=r, column=3).alignment = Alignment(horizontal='center')
for i in (2, 3):
    cl.cell(row=r, column=i).border = box
    cl.cell(row=r, column=i).fill = shade_fill

r += 3
cl.cell(row=r, column=2, value='Rows added or restored').font = h_f
r += 1
for i, hh in enumerate(['Ref', 'Change', 'Deliverable', 'Reason'], start=2):
    cl.cell(row=r, column=i, value=hh)
style_header(cl, r, 5)
r += 1
for x in R:
    label = SRC_LABEL[x['source']]
    if label == 'Carried forward' and not x['change']:
        continue
    for i, v in enumerate((x['ref'], label, x['deliverable'], x['change']), start=2):
        c = cl.cell(row=r, column=i, value=v)
        c.font = body_f
        c.border = box
        c.alignment = Alignment(vertical='top', wrap_text=(i in (4, 5)))
    r += 1

r += 2
cl.cell(row=r, column=2, value='Retired references').font = h_f
r += 1
for i, hh in enumerate(['Ref', 'From', 'Deliverable', 'Disposition'], start=2):
    cl.cell(row=r, column=i, value=hh)
style_header(cl, r, 5)
r += 1
for ref, src, deliv, disp in D.RETIRED:
    for i, v in enumerate((ref, src, deliv, disp), start=2):
        c = cl.cell(row=r, column=i, value=v)
        c.font = body_f
        c.border = box
        c.alignment = Alignment(vertical='top', wrap_text=(i in (4, 5)))
    r += 1
cl.page_setup.orientation = 'landscape'
cl.page_setup.fitToWidth = 1
cl.sheet_properties.pageSetUpPr.fitToPage = True

# ═══ 5. Lists ═══════════════════════════════════════════════════════════════
# The column order is DERIVED from LIST_COL, not restated. A hand-written order
# beside a hand-written letter map is two copies of the same fact, and the first
# time they disagreed every drop-down on the register offered the wrong values
# while looking entirely correct. Deriving it means a list added to the schema
# lands in the column its drop-downs already point at.
ls = wb.create_sheet('Lists')
ls.sheet_view.showGridLines = False

order = sorted(LIST_COL, key=lambda k: (len(LIST_COL[k]), LIST_COL[k]))
for key in order:
    i = sum((ord(ch) - 64) * 26 ** n
            for n, ch in enumerate(reversed(LIST_COL[key])))
    col = get_column_letter(i)
    ls.column_dimensions[col].width = 22
    c = ls.cell(row=1, column=i, value=key)
    c.font = Font(name='Calibri', size=9, bold=True, color=NAVY)
    c.fill = hdr_fill
    c.border = box
    # 'Originator' has no fixed list: the codes come from the register the Lead
    # Appointed Party issues, and inventing them here would pre-empt it.
    vals = LISTS.get(key, ['[FILL — from the originator code register]'])
    for j, v in enumerate(vals, start=2):
        cc = ls.cell(row=j, column=i, value=v)
        cc.font = body_f
        cc.border = box

# The Ref prefix groups the register; it is NOT the container role code, and
# the two deliberately differ (FF for FF&E, C for the contractor, ALL for
# project-wide). Stated here because a reader who assumes they are the same
# builds a container name out of a register key.
_ref_row = 20
ls.cell(row=_ref_row, column=1, value='Reference prefix').font = Font(
    name='Calibri', size=9, bold=True, color=NAVY)
ls.cell(row=_ref_row, column=2, value='Groups').font = Font(
    name='Calibri', size=9, bold=True, color=NAVY)
ls.cell(row=_ref_row, column=3, value='Container role code').font = Font(
    name='Calibri', size=9, bold=True, color=NAVY)
for _c in (1, 2, 3):
    ls.cell(row=_ref_row, column=_c).fill = hdr_fill
    ls.cell(row=_ref_row, column=_c).border = box
_REF_PREFIXES = [
    ('A', 'Architecture and interiors', 'A'),
    ('S', 'Structure', 'S'),
    ('M', 'Mechanical', 'M'),
    ('E', 'Electrical, including lighting', 'E'),
    ('P', 'Public health', 'P'),
    ('FP', 'Fire protection', 'FP'),
    ('LV', 'Low voltage and communications', 'LV'),
    ('G', 'Civil and site', 'G'),
    ('FF', 'FF&E and finishes', 'A — issued under architecture'),
    ('C', 'Contractor and specialists', 'per the discipline of the container'),
    ('Z', 'Information management, cost and project-wide', 'Z'),
]
for _i, (_p, _g, _role) in enumerate(_REF_PREFIXES, start=_ref_row + 1):
    for _c, _v in ((1, _p), (2, _g), (3, _role)):
        _cell = ls.cell(row=_i, column=_c, value=_v)
        _cell.font = body_f
        _cell.border = box

# Every validated column must point at a populated list column. Checked rather
# than assumed, because a drop-down sourced from an empty column silently
# offers nothing and reads as "no constraint".
for _sheet_first in (S.MIDP_FIRST_COL, S.TIDP_FIRST_COL):
    for _letter, _key in S.validated(_sheet_first):
        if _key not in LIST_COL:
            raise SystemExit('drop-down on column %s wants list %r, which has no '
                             'column on the Lists sheet' % (_letter, _key))
        if _key not in LISTS and _key != 'Originator':
            raise SystemExit('list %r has a column but no permitted values' % _key)

wb.properties.title = 'KUT Master Information Delivery Plan'
wb.properties.subject = 'Kampala Uganda Temple — aggregated information delivery schedule'
wb.properties.creator = 'Symbion Consulting Group Studios'
wb.properties.lastModifiedBy = 'Symbion Consulting Group Studios'
wb.properties.category = 'Project procedure'

# Determinism. openpyxl stamps dcterms:created and dcterms:modified from the wall
# clock, so an unpinned build produced a different binary every run -- and that
# was true of the committed workbook until this was added. An always-dirty binary
# diff trains reviewers to ignore `git status` on exactly the file a hand-edit
# would show up in. Pinned, regeneration is a genuine no-op.
_EPOCH = datetime.datetime(*K.EPOCH)
wb.properties.created = _EPOCH
wb.properties.modified = _EPOCH

# The staleness stamp rides in dc:description, which openpyxl already writes, so
# the gate can read it back with plain `zipfile` and stay stdlib-only.
_ROOT = pathlib.Path(__file__).resolve().parent.parent
wb.properties.description = K.with_provenance(
    'Rev P01 — DRAFT. Not yet issued through the Common Data Environment. Uncontrolled when printed.',
    # GENERATED is keyed by BASENAME, not by path: corporate_docx.save() looks
    # a document up by basename to decide whether to stamp provenance, and the
    # two must agree or the stamp is silently skipped.
    'tools/build_midp.py', K.inputs_digest(_ROOT, pathlib.Path(OUT).name))

wb.active = 0
wb.save(OUT)
K.finalise(pathlib.Path(OUT))
print('saved:', OUT, '|', len(R), 'deliverables')
