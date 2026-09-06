# -*- coding: utf-8 -*-
"""The MIDP baseline: every deliverable the project owes, with its provenance.

The register the workbook is built from. Data only -- no openpyxl -- so the
builder, the merge tool and the gate can all read it without being able to
write a spreadsheet.

WHERE THESE ROWS CAME FROM
Four MIDP workbooks existed by September 2026: a June "Comprehensive", a June
"Detailed", a June "Detailed v2" and the August issue. The August one had the
cleanest schema and the only merge tooling, so it became the base -- but it had
dropped 22 deliverables the June workbooks carried, and it collapsed the whole
construction stage into a single all-disciplines row, losing the per-discipline
record models that Deliverable D is actually assembled from.

The `source` field on every row records which of those it came from:

    'Both'      carried by both the June and the August issue
    'Aug P01'   August only
    'Jun P01'   in June, dropped by August, restored here
    'New P02'   identified as missing by the September reconciliation

`change` says what was done to the row and cites the finding that prompted it,
so the Change log sheet can be generated rather than written by hand. Together
they are why this file is 122 rows and the August issue was 78.

TWO FIELDS THAT ARE NOT DECORATION
`m_from` / `m_to` are month integers, not a display string. A deliverable is
usually a point, but a review window, a continuously maintained register or a
progressive as-built capture is a period, and the August workbook could only
express the first. They are validated against the Owner's programme below.

`isotype` is the ISO 19650 type code that appears in the container name. The
`type` field beside it is the plain-English word a reader needs. Both are here
because a register that gives only one of them either cannot be checked against
the naming convention or cannot be read by a consultant.
"""
from __future__ import annotations

R = []
def row(ref, disc, vol, deliv, typ, iso, stage, lod, fmt, suit, state, mf, mt, resp, tidp,
        rc=2, crit='N', ab='N', om='N', src='Aug P01', change='', notes=''):
    R.append(dict(ref=ref, discipline=disc, volume=vol, deliverable=deliv, type=typ, isotype=iso,
                  stage=stage, lod=lod, fmt=fmt, suit=suit, state=state, m_from=mf, m_to=mt,
                  responsible=resp, tidp=tidp, revclass=rc, crit=crit, asbuilt=ab, om=om,
                  source=src, change=change, notes=notes))

IM = 'Information Management'
MOB, DA, DB, DC, TEN, CON, C31, C32, DD = ('Mobilisation', '2.1 Deliverable A', '2.2 Deliverable B', '2.3 Deliverable C',
                                            '2.4 Tender', '2.5 Conformed set', '3.1 Construction', '3.2 FF&E', '3.3 Deliverable D')

# ── Mobilisation ──────────────────────────────────────────────────────────
row('Z-000', IM, 'ZZ', 'BIM Execution Plan', 'Document', 'RP', MOB, 'n/a', 'DOCX/PDF', 'A1', 'Published', 0, 0, 'Information Manager', 'TIDP-Z', 2, 'N', 'N', 'N', 'Both', 'Reference KUT-SMB-ZZ-ZZ-RP-Z-0001 (I-01)', 'Baselined at mobilisation; reissued per stage')
row('Z-001', IM, 'ZZ', 'Master Information Delivery Plan', 'Schedule', 'SC', MOB, 'n/a', 'XLSX', 'A1', 'Published', 0, 0, 'Information Manager', 'TIDP-Z', 2, 'N', 'N', 'N', 'Both', 'Type Document → Schedule; reference SC-Z-0001', 'Aggregated from all TIDPs; monthly and per drop')
row('Z-002', IM, 'ZZ', 'Project Delivery Playbook', 'Document', 'RP', MOB, 'n/a', 'DOCX/PDF', 'A1', 'Published', 0, 0, 'Information Manager', 'TIDP-Z', 2, 'N', 'N', 'N', 'Aug P01', '', 'Reference RP-Z-0002')
row('Z-003', IM, 'ZZ', 'Responsibility matrix (RACI)', 'Schedule', 'SC', MOB, 'n/a', 'XLSX', 'A1', 'Published', 0, 0, 'Information Manager', 'TIDP-Z', 2, 'N', 'N', 'N', 'Both', 'June ref Z-002; reissued with P02 codes', 'Reference SC-Z-0002')
row('Z-004', IM, 'ZZ', 'Project template, family library and title blocks', 'Model', 'M3', MOB, 'n/a', 'RVT/RFA', 'A1', 'Published', 0, 0, 'Information Manager', 'TIDP-Z', 2, 'Y', 'N', 'N', 'Both', 'June ref Z-006', 'Coordinates, levels, grids, worksets, shared parameters bound')
row('Z-005', IM, 'ZZ', 'Originator code register', 'Schedule', 'SC', MOB, 'n/a', 'XLSX', 'A1', 'Published', 0, 0, 'Lead Appointed Party', 'TIDP-Z', 2, 'Y', 'N', 'N', 'Aug P01', 'Responsible: Appointing Party → Lead Appointed Party (B-02)', 'Required before any container is numbered; 3-character codes')
row('Z-006', 'All disciplines', 'ZZ', 'Task Information Delivery Plan (one per appointed party)', 'Schedule', 'SC', MOB, 'n/a', 'XLSX', 'S3', 'Shared', 0, 0, 'Task Team Managers', 'TIDP-ALL', 2, 'N', 'N', 'N', 'Aug P01', '', 'Returned to the Information Manager; merged into this MIDP')
row('Z-007', IM, 'ZZ', 'Standards reconciliation schedule (BEP 4.1.3)', 'Schedule', 'SC', MOB, 'n/a', 'XLSX/PDF', 'S3', 'Shared', 0, 8, 'All disciplines', 'TIDP-Z', 2, 'Y', 'N', 'N', 'Aug P01', 'End month set to M8: closed before Technical Design', 'Opened at mobilisation; closed before Stage 2.3')
row('Z-008', IM, 'ZZ', 'Shared coordinates seed model', 'Model', 'M3', MOB, 'n/a', 'RVT', 'A1', 'Published', 0, 0, 'Information Manager', 'TIDP-Z', 2, 'Y', 'N', 'N', 'Aug P01', '', 'Survey point, project base point, true north, levels and grids')
row('Z-009', IM, 'ZZ', 'Level and grid register', 'Schedule', 'SC', MOB, 'n/a', 'XLSX/PDF', 'A1', 'Published', 0, 0, 'Lead Appointed Party', 'TIDP-Z', 2, 'Y', 'N', 'N', 'Aug P01', '', 'Reference SC-Z-0003')
row('Z-011', IM, 'ZZ', 'Software and version register', 'Schedule', 'SC', MOB, 'n/a', 'XLSX', 'A1', 'Published', 0, 0, 'All parties', 'TIDP-Z', 3, 'N', 'N', 'N', 'Aug P01', '', 'Revit 2025 locked project-wide')
row('Z-012', IM, 'ZZ', 'CDE folder structure and permission matrix', 'Document', 'RP', MOB, 'n/a', 'PDF', 'A1', 'Published', 0, 0, 'Information Manager', 'TIDP-Z', 3, 'N', 'N', 'N', 'Both', 'June ref Z-005 "CDE setup"', 'ACC provisioned; seats confirmed for every party (E-01)')
row('Z-013', IM, 'ZZ', 'Information classification and access schedule', 'Document', 'RP', MOB, 'n/a', 'PDF', 'A1', 'Published', 0, 0, 'Appointing Party', 'TIDP-Z', 2, 'Y', 'N', 'N', 'Aug P01', '', 'Restricted spaces and systems classified before modelling begins')
row('Z-014', IM, 'ZZ', 'Clash matrix and tolerance schedule', 'Document', 'RP', MOB, 'n/a', 'PDF', 'S3', 'Shared', 1, 1, 'MEP and structural leads', 'TIDP-Z', 2, 'Y', 'N', 'N', 'Aug P01', '', 'Required before the first coordination cycle of Stage 2.2')
row('Z-015', IM, 'ZZ', 'IFC export configuration', 'Document', 'RP', MOB, 'n/a', 'PDF/JSON', 'A1', 'Published', 0, 0, 'Information Manager', 'TIDP-Z', 3, 'N', 'N', 'N', 'Aug P01', '', 'Issued centrally')
row('Z-016', IM, 'ZZ', 'Shared parameter file', 'Document', 'RP', MOB, 'n/a', 'TXT', 'A1', 'Published', 0, 0, 'Information Manager', 'TIDP-Z', 2, 'Y', 'N', 'N', 'Aug P01', '', 'Central ownership')
row('Z-017', IM, 'ZZ', 'Capability and capacity assessment', 'Report', 'RP', MOB, 'n/a', 'XLSX', 'S3', 'Shared', 1, 1, 'Lead Appointed Party', 'TIDP-Z', 3, 'N', 'N', 'N', 'Aug P01', '', 'ISO 19650-2; the Stage 0 test model is its practical form')
row('Z-018', IM, 'ZZ', 'Standards localisation note (Owner standards to Ugandan practice)', 'Document', 'RP', MOB, 'n/a', 'DOCX/PDF', 'S3', 'Shared', 0, 1, 'Information Manager', 'TIDP-Z', 2, 'N', 'N', 'N', 'New P02', 'Added from proposal 4.1 (BEP 4.1.4)', 'Reference RP-Z-0005; weeks 2–3 after the Owner standards are received')
row('Z-019', IM, 'ZZ', 'Stage 0 gate record (test model shared by every appointed party)', 'Report', 'RP', MOB, 'n/a', 'PDF', 'S3', 'Shared', 1, 1, 'Information Manager', 'TIDP-Z', 2, 'Y', 'N', 'N', 'New P02', 'Added (A-02): the Stage 0 exit gate had no date', 'No discipline enters the fortnightly cycle before this')
row('Z-020', IM, 'ZZ', 'Monthly BIM status report (design stage)', 'Report', 'RP', MOB, 'n/a', 'PDF', 'S2', 'Shared', 1, 11, 'Information Manager', 'TIDP-Z', 3, 'N', 'N', 'N', 'New P02', 'Added (D-05): proposal 4.5 requires monthly reporting throughout; Aug MIDP started at M12', 'KPIs per BEP 10.5')
row('Z-021', IM, 'ZZ', 'Kickoff induction record and BEP acknowledgement', 'Document', 'RP', MOB, 'n/a', 'PDF', 'A1', 'Published', 0, 1, 'Information Manager', 'TIDP-Z', 3, 'N', 'N', 'N', 'Jun P01', 'June ref Z-007 "Kickoff training + signed BEP"; dropped in Aug', 'Attendance and signed acknowledgement from every task team')
row('Z-022', IM, 'ZZ', 'Document Control Standard (reissued to P02 codes)', 'Document', 'RP', MOB, 'n/a', 'DOCX/PDF', 'A1', 'Published', 0, 0, 'Information Manager', 'TIDP-Z', 3, 'N', 'N', 'N', 'New P02', 'Added (I-02); June document RP-Z-0001 renumbered RP-Z-0003. Absorbs the separate numbering convention (June RP-Z-0004), which would have been a second copy of the naming rules', 'KUT-SMB-ZZ-ZZ-RP-Z-0003. Container naming, sheet number bands, revision, authorisation, transmittal, superseded/cancelled/replaced notices, number retirement')
row('Z-024', IM, 'ZZ', 'Drawing register, transmittal and notice templates', 'Document', 'TR', MOB, 'n/a', 'XLSX/DOCX', 'A1', 'Published', 0, 0, 'Information Manager', 'TIDP-Z', 3, 'N', 'N', 'N', 'Jun P01', 'June templates carried; Uniclass column replaced by CSI section (E-05)', '')

# ── 2.1 Deliverable A ──────────────────────────────────────────────────────
row('Z-010', IM, 'ZZ', 'Federated coordination model', 'Model', 'M3', DA, '200', 'NWC/NWD/IFC', 'S1', 'Shared', 1, 1, 'Information Manager', 'TIDP-Z', 2, 'Y', 'Y', 'N', 'Both', 'June ref Z-003', 'First federation; container M3-Z-0001')
row('A-100', 'Architecture', 'ZZ', 'Architectural model', 'Model', 'M3', DA, '200', 'RVT/IFC', 'S2', 'Shared', 1, 1, 'Architecture lead', 'TIDP-A', 2, 'Y', 'Y', 'N', 'Both', '', 'Massing and generic systems; rooms placed')
row('A-101', 'Architecture', 'ZZ', 'Deliverable A drawing set', 'Drawing', 'SH', DA, '200', 'PDF/DWG', 'S3', 'Shared', 1, 1, 'Architecture lead', 'TIDP-A', 2, 'N', 'N', 'N', 'New P02', 'Added: Playbook 6.1 lists a Deliverable A drawing set that no MIDP carried', '')
row('S-100', 'Structure', 'ZZ', 'Structural model', 'Model', 'M3', DA, '200', 'RVT/IFC', 'S2', 'Shared', 1, 1, 'Structural lead', 'TIDP-S', 2, 'Y', 'Y', 'N', 'Both', '', 'Indicative frame and foundations')
row('M-100', 'Mechanical', 'ZZ', 'Mechanical model', 'Model', 'M3', DA, '200', 'RVT/IFC', 'S2', 'Shared', 1, 1, 'MEP lead', 'TIDP-M', 2, 'Y', 'Y', 'N', 'Both', '', 'Plant space allocation, primary routes')
row('E-100', 'Electrical', 'ZZ', 'Electrical model', 'Model', 'M3', DA, '200', 'RVT/IFC', 'S2', 'Shared', 1, 1, 'MEP lead', 'TIDP-E', 2, 'Y', 'Y', 'N', 'Both', '', 'Generic equipment and main containment')
row('P-100', 'Public Health', 'ZZ', 'Plumbing and drainage model', 'Model', 'M3', DA, '200', 'RVT/IFC', 'S2', 'Shared', 1, 1, 'MEP lead', 'TIDP-P', 2, 'Y', 'Y', 'N', 'Both', '', 'Primary routes; font systems indicated')
row('G-100', 'Civil and Site', '00', 'Site model and levels', 'Model', 'M3', DA, '200', 'RVT/IFC', 'S2', 'Shared', 1, 1, 'Civil lead', 'TIDP-G', 2, 'Y', 'Y', 'N', 'Both', 'Volume 00 (site-wide)', 'Site levels, access, drainage strategy; scope to confirm')
row('Z-110', IM, 'ZZ', 'Basis of design report', 'Document', 'RP', DA, '200', 'DOCX/PDF', 'S3', 'Shared', 1, 1, 'Lead Appointed Party', 'TIDP-Z', 3, 'N', 'N', 'N', 'Both', '', 'Design intent')
row('Z-111', IM, 'ZZ', 'Area schedule reconciled to the brief', 'Schedule', 'SC', DA, '200', 'XLSX/PDF', 'S3', 'Shared', 1, 1, 'Information Manager', 'TIDP-Z', 2, 'N', 'N', 'N', 'Aug P01', '', 'Gate condition')
row('Z-112', IM, 'ZZ', 'Deliverable A gate pack and compliance report', 'Document', 'RP', DA, '200', 'PDF', 'S4', 'Shared', 1, 1, 'Information Manager', 'TIDP-Z', 1, 'Y', 'N', 'N', 'Aug P01', 'Compliance report named explicitly (proposal 2.1)', 'Contents per Playbook Appendix B')
row('Z-113', IM, 'ZZ', 'Deliverable A Owner review set and comment close-out report', 'Document', 'RP', DA, '200', 'PDF', 'S3', 'Shared', 1, 2, 'Information Manager', 'TIDP-Z', 1, 'Y', 'N', 'N', 'New P02', 'Added (D-05): bookmarked, hyperlinked review set; two-week Owner review', 'Comments closed before the gate')
row('Z-114', IM, 'ZZ', 'Baseline model-health report', 'Report', 'RP', DA, '200', 'PDF', 'S2', 'Shared', 1, 1, 'Information Manager', 'TIDP-Z', 3, 'N', 'N', 'N', 'New P02', 'Added from proposal 4.1 (week 4 baseline audit)', '')

# ── 2.2 Deliverable B ──────────────────────────────────────────────────────
row('A-200', 'Architecture', 'ZZ', 'Architectural model', 'Model', 'M3', DB, '300', 'RVT/IFC', 'S2', 'Shared', 2, 4, 'Architecture lead', 'TIDP-A', 2, 'Y', 'Y', 'N', 'Both', '', 'Real geometry, correctly located; no placeholder families')
row('A-201', 'Architecture', 'ZZ', 'General arrangement plans, sections and elevations (50%)', 'Drawing', 'SH', DB, '300', 'PDF/DWG', 'S2', 'Shared', 4, 4, 'Architecture lead', 'TIDP-A', 2, 'N', 'Y', 'N', 'Both', '', '')
row('A-202', 'Architecture', 'ZZ', 'Door and window schedules', 'Schedule', 'SC', DB, '300', 'PDF/XLSX', 'S2', 'Shared', 4, 4, 'Architecture lead', 'TIDP-A', 2, 'N', 'N', 'N', 'Both', 'ISO type SH (schedule) → SC (B-04)', '')
row('A-203', 'Architecture', 'ZZ', 'Room data sheets (key spaces)', 'Room data sheet', 'RD', DB, '300', 'PDF', 'S2', 'Shared', 4, 4, 'Architecture lead', 'TIDP-A', 2, 'N', 'N', 'N', 'Both', '', 'Complete set at Deliverable C (A-302)')
row('A-204', 'Architecture', 'ZZ', 'Existing conditions and removals plan', 'Drawing', 'SH', DB, '300', 'PDF/DWG', 'S2', 'Shared', 4, 4, 'Architecture lead', 'TIDP-A', 2, 'N', 'N', 'N', 'Aug P01', '', 'Demolition classification is a manual task; BEP 10.4')
row('S-200', 'Structure', 'ZZ', 'Structural model', 'Model', 'M3', DB, '300', 'RVT/IFC', 'S2', 'Shared', 2, 4, 'Structural lead', 'TIDP-S', 2, 'Y', 'Y', 'N', 'Both', '', 'Sized members; penetrations coordinated')
row('S-201', 'Structure', 'ZZ', 'General arrangement drawings (50%)', 'Drawing', 'SH', DB, '300', 'PDF/DWG', 'S2', 'Shared', 4, 4, 'Structural lead', 'TIDP-S', 2, 'N', 'Y', 'N', 'Both', '', '')
row('M-200', 'Mechanical', 'ZZ', 'Mechanical model', 'Model', 'M3', DB, '300', 'RVT/IFC', 'S2', 'Shared', 2, 4, 'MEP lead', 'TIDP-M', 2, 'Y', 'Y', 'N', 'Both', '', 'Real equipment; risers fixed')
row('M-201', 'Mechanical', 'ZZ', 'HVAC layouts (50%)', 'Drawing', 'SH', DB, '300', 'PDF/DWG', 'S2', 'Shared', 4, 4, 'MEP lead', 'TIDP-M', 2, 'N', 'Y', 'N', 'Both', '', '')
row('E-200', 'Electrical', 'ZZ', 'Electrical model', 'Model', 'M3', DB, '300', 'RVT/IFC', 'S2', 'Shared', 2, 4, 'MEP lead', 'TIDP-E', 2, 'Y', 'Y', 'N', 'Both', '', 'Power, lighting and containment routed; boards placed')
row('E-201', 'Electrical', 'ZZ', 'Power and lighting layouts (50%)', 'Drawing', 'SH', DB, '300', 'PDF/DWG', 'S2', 'Shared', 4, 4, 'MEP lead', 'TIDP-E', 2, 'N', 'Y', 'N', 'Both', '', 'Lighting layouts under role E (G-03)')
row('P-200', 'Public Health', 'ZZ', 'Plumbing and drainage model', 'Model', 'M3', DB, '300', 'RVT/IFC', 'S2', 'Shared', 2, 4, 'MEP lead', 'TIDP-P', 2, 'Y', 'Y', 'N', 'Both', '', 'Real pipework with code falls; fixtures placed')
row('P-201', 'Public Health', 'ZZ', 'Plumbing and drainage layouts (50%)', 'Drawing', 'SH', DB, '300', 'PDF/DWG', 'S2', 'Shared', 4, 4, 'MEP lead', 'TIDP-P', 2, 'N', 'Y', 'N', 'New P02', 'Added: 50% set for P was missing while the drawing schedule plans P sheets', '')
row('FP-200', 'Fire Protection', 'ZZ', 'Fire strategy and suppression layout', 'Model/Document', 'M3', DB, '300', 'RVT/PDF', 'S2', 'Shared', 2, 4, 'Fire lead', 'TIDP-FP', 2, 'Y', 'Y', 'N', 'Aug P01', '', 'Sprinkler and detection layout')
row('LV-200', 'Low Voltage', 'ZZ', 'Low voltage containment and equipment rooms', 'Model', 'M3', DB, '300', 'RVT/IFC', 'S2', 'Shared', 2, 4, 'LV lead', 'TIDP-LV', 2, 'Y', 'Y', 'N', 'New P02', 'Added: LV appears in Aug only at Deliverable C', '')
row('G-200', 'Civil and Site', '00', 'Site model', 'Model', 'M3', DB, '300', 'RVT/IFC', 'S2', 'Shared', 2, 4, 'Civil lead', 'TIDP-G', 2, 'Y', 'Y', 'N', 'Jun P01', 'Restored from June (absent in Aug)', 'Real site model; drainage and access')
row('G-201', 'Civil and Site', '00', 'Site and drainage drawings (50%)', 'Drawing', 'SH', DB, '300', 'PDF/DWG', 'S2', 'Shared', 4, 4, 'Civil lead', 'TIDP-G', 2, 'N', 'Y', 'N', 'Jun P01', 'Restored from June (absent in Aug)', '')
row('Z-210', IM, 'ZZ', 'Coordination reports (fortnightly) and Deliverable B clash close-out', 'Report', 'CR', DB, '300', 'PDF/BCF', 'S2', 'Shared', 2, 4, 'Information Manager', 'TIDP-Z', 3, 'N', 'N', 'N', 'Both', 'ISO type CR', 'No unresolved high-priority clashes at the gate')
row('Z-211', IM, 'ZZ', 'Federated model', 'Model', 'M3', DB, '300', 'NWC/NWD/IFC', 'S2', 'Shared', 4, 4, 'Information Manager', 'TIDP-Z', 2, 'Y', 'Y', 'N', 'Both', '', 'Milestone NWD archived')
row('Z-212', 'QS / Cost', 'ZZ', 'Bill of quantities (preliminary)', 'Schedule', 'BQ', DB, '300', 'XLSX', 'S2', 'Shared', 4, 4, 'Quantity Surveyor', 'TIDP-Q', 2, 'Y', 'N', 'Y', 'Both', 'ISO type BQ; measurement standard [FILL] (D-03)', 'Derived from the coordinated models by the QS')
row('FF-200', 'FF&E', 'ZZ', 'FF&E and finishes register (first Fohlio exchange)', 'Schedule', 'SC', DB, '300', 'CSV/XLSX', 'S2', 'Shared', 4, 4, 'Interior Designer', 'TIDP-FF', 2, 'N', 'N', 'Y', 'Aug P01', '', 'Matched on room number')
row('Z-213', IM, 'ZZ', 'Deliverable B gate pack, LOD 300 verification and compliance report', 'Document', 'RP', DB, '300', 'PDF', 'S4', 'Shared', 4, 4, 'Information Manager', 'TIDP-Z', 1, 'Y', 'N', 'N', 'Aug P01', 'Verification and compliance report named', '')
row('Z-214', IM, 'ZZ', 'Deliverable B Owner review set and comment close-out report', 'Document', 'RP', DB, '300', 'PDF', 'S3', 'Shared', 4, 5, 'Information Manager', 'TIDP-Z', 1, 'Y', 'N', 'N', 'New P02', 'Added (D-05)', '')

# ── 2.3 Deliverable C ──────────────────────────────────────────────────────
row('A-300', 'Architecture', 'ZZ', 'Architectural model', 'Model', 'M3', DC, '350', 'RVT/IFC', 'S4', 'Shared', 5, 8, 'Architecture lead', 'TIDP-A', 2, 'Y', 'Y', 'N', 'Both', '', 'Interfaces resolved')
row('A-301', 'Architecture', 'ZZ', 'Full drawing set (100%) and details', 'Drawing', 'SH', DC, '350', 'PDF/DWG', 'S4', 'Shared', 8, 8, 'Architecture lead', 'TIDP-A', 2, 'Y', 'Y', 'N', 'Both', '', '')
row('A-302', 'Architecture', 'ZZ', 'Room data sheets (complete)', 'Room data sheet', 'RD', DC, '350', 'PDF', 'S4', 'Shared', 8, 8, 'Architecture lead', 'TIDP-A', 2, 'N', 'N', 'N', 'New P02', 'Added: Playbook 6.3 places the RDS set at 2.3', 'Generated from the reconciled Fohlio data')
row('S-300', 'Structure', 'ZZ', 'Structural model', 'Model', 'M3', DC, '350', 'RVT/IFC', 'S4', 'Shared', 5, 8, 'Structural lead', 'TIDP-S', 2, 'Y', 'Y', 'N', 'Both', '', 'Connections resolved')
row('S-301', 'Structure', 'ZZ', 'Structural drawing set (100%)', 'Drawing', 'SH', DC, '350', 'PDF/DWG', 'S4', 'Shared', 8, 8, 'Structural lead', 'TIDP-S', 2, 'Y', 'Y', 'N', 'Both', '', '')
row('M-300', 'Mechanical', 'ZZ', 'Mechanical model', 'Model', 'M3', DC, '350', 'RVT/IFC', 'S4', 'Shared', 5, 8, 'MEP lead', 'TIDP-M', 2, 'Y', 'Y', 'N', 'Both', 'Aug merged model and drawings; split restored', 'Systems complete and connected; equipment data')
row('M-301', 'Mechanical', 'ZZ', 'HVAC drawing set (100%)', 'Drawing', 'SH', DC, '350', 'PDF/DWG', 'S4', 'Shared', 8, 8, 'MEP lead', 'TIDP-M', 2, 'Y', 'Y', 'N', 'Both', '', '')
row('M-302', 'Mechanical', 'ZZ', 'HVAC load and sizing schedules', 'Schedule', 'SC', DC, '350', 'PDF/XLSX', 'S4', 'Shared', 8, 8, 'MEP lead', 'TIDP-M', 2, 'Y', 'N', 'N', 'Jun P01', 'June ref M-300b restored', '')
row('M-303', 'Mechanical', 'ZZ', 'BMS point-naming convention and point data on serviceable elements', 'Schedule', 'SC', DC, '350', 'XLSX', 'S3', 'Shared', 8, 8, 'MEP lead', 'TIDP-M', 2, 'N', 'N', 'N', 'New P02', 'Added (BEP 11.3)', 'Agreed with the controls contractor')
row('E-300', 'Electrical', 'ZZ', 'Electrical model and drawing set (100%)', 'Model/Drawing', 'M3', DC, '350', 'RVT/PDF', 'S4', 'Shared', 5, 8, 'MEP lead', 'TIDP-E', 2, 'Y', 'Y', 'N', 'Both', '', '')
row('E-301', 'Electrical', 'ZZ', 'Panel schedules and single-line diagrams', 'Schedule/Drawing', 'SH', DC, '350', 'PDF/XLSX', 'S4', 'Shared', 8, 8, 'MEP lead', 'TIDP-E', 2, 'Y', 'Y', 'N', 'Jun P01', 'June ref E-300b restored', 'Sheets in the 6xxx and 7xxx bands')
row('P-300', 'Public Health', 'ZZ', 'Plumbing model and drawing set (100%)', 'Model/Drawing', 'M3', DC, '350', 'RVT/PDF', 'S4', 'Shared', 5, 8, 'MEP lead', 'TIDP-P', 2, 'Y', 'Y', 'N', 'Both', '', '')
row('FP-300', 'Fire Protection', 'ZZ', 'Fire strategy and model (100%)', 'Model/Document', 'M3', DC, '350', 'RVT/PDF', 'S4', 'Shared', 5, 8, 'Fire lead', 'TIDP-FP', 2, 'Y', 'Y', 'N', 'Both', '', 'Transition to the design-build contractor agreed')
row('LV-300', 'Low Voltage', 'ZZ', 'Communications and security model and drawings', 'Model/Drawing', 'M3', DC, '350', 'RVT/PDF', 'S4', 'Shared', 5, 8, 'LV lead', 'TIDP-LV', 2, 'Y', 'Y', 'N', 'Aug P01', '', 'Restricted-information handling per BEP 8.1')
row('G-300', 'Civil and Site', '00', 'Civil and site model and drawings (100%)', 'Model/Drawing', 'M3', DC, '350', 'RVT/PDF', 'S4', 'Shared', 5, 8, 'Civil lead', 'TIDP-G', 2, 'Y', 'Y', 'N', 'Jun P01', 'Restored from June (absent in Aug)', '')
row('Z-310', 'QS / Cost', 'ZZ', 'Bill of quantities (tender)', 'Schedule', 'BQ', DC, '350', 'XLSX', 'S4', 'Shared', 8, 8, 'Quantity Surveyor', 'TIDP-Q', 2, 'Y', 'N', 'Y', 'Both', 'ISO type BQ', '')
row('Z-311', 'All disciplines', 'ZZ', 'Specifications (RIB SpecLink, CSI MasterFormat)', 'Specification', 'SP', DC, '350', 'PDF', 'S4', 'Shared', 8, 8, 'Task Team Managers', 'TIDP-ALL', 2, 'Y', 'N', 'Y', 'Both', 'Responsible: Lead AP → each discipline authors (RACI C12)', 'Issued sets archived with the milestone')
row('Z-312', IM, 'ZZ', 'Specification reconciliation report (model vs SpecLink)', 'Report', 'RP', DC, '350', 'XLSX/PDF', 'S4', 'Shared', 8, 8, 'Information Manager', 'TIDP-Z', 2, 'N', 'N', 'N', 'Aug P01', '', 'Gaps closed or formally accepted')
row('FF-300', 'FF&E', 'ZZ', 'FF&E specifications and schedule', 'Schedule', 'SC', DC, '350', 'XLSX/PDF', 'S4', 'Shared', 8, 8, 'Interior Designer', 'TIDP-FF', 2, 'Y', 'N', 'Y', 'Both', '', 'Reconciled to Fohlio')
row('Z-313', IM, 'ZZ', 'Deliverable C gate pack, LOD 350 verification and compliance report', 'Document', 'RP', DC, '350', 'PDF', 'S4', 'Shared', 8, 8, 'Information Manager', 'TIDP-Z', 1, 'Y', 'N', 'N', 'Aug P01', '', 'Includes FF&E currency report')
row('Z-314', IM, 'ZZ', 'Deliverable C Owner review set and comment close-out report', 'Document', 'RP', DC, '350', 'PDF', 'S3', 'Shared', 8, 9, 'Information Manager', 'TIDP-Z', 1, 'Y', 'N', 'N', 'New P02', 'Added (D-05)', '')

# ── 2.4 Tender / 2.5 Conformed ─────────────────────────────────────────────
row('Z-400', IM, 'ZZ', 'Tender documents', 'Document', 'RP', TEN, 'n/a', 'PDF', 'A1', 'Published', 9, 9, 'Lead Appointed Party', 'TIDP-Z', 2, 'N', 'N', 'N', 'Both', '', 'Issued from the CDE at A1')
row('Z-401', IM, 'ZZ', 'Tender query and addendum log', 'Report', 'RP', TEN, 'n/a', 'XLSX', 'S2', 'Shared', 9, 11, 'Lead Appointed Party', 'TIDP-Z', 3, 'N', 'N', 'N', 'Aug P01', '', 'Queries answered as formal RFIs')
row('Z-410', IM, 'ZZ', 'Conformed set', 'Drawing', 'SH', CON, '350', 'PDF/DWG', 'A1', 'Published', 11, 11, 'Lead Appointed Party', 'TIDP-Z', 2, 'Y', 'Y', 'N', 'Both', 'June ref Z-401', 'Addenda incorporated; superseded revisions archived')
row('Z-411', IM, 'ZZ', 'Conformed-set conformance audit and compliance report', 'Report', 'RP', CON, '350', 'PDF', 'S4', 'Shared', 11, 11, 'Information Manager', 'TIDP-Z', 1, 'Y', 'N', 'N', 'New P02', 'Added (D-05): the conformed set is one of the five contractual gates', 'Milestone NWD archive')

# ── 3.1 Construction ────────────────────────────────────────────────────────
for d, disc, resp, tidp, extra in [('A', 'Architecture', 'Architecture lead', 'TIDP-A', ''), ('S', 'Structure', 'Structural lead', 'TIDP-S', ''),
                                    ('M', 'Mechanical', 'MEP lead', 'TIDP-M', 'Maintenance type on plumbing fixtures from LOD 400'), ('E', 'Electrical', 'MEP lead', 'TIDP-E', ''),
                                    ('P', 'Public Health', 'MEP lead', 'TIDP-P', ''), ('FP', 'Fire Protection', 'Design-build fire contractor', 'TIDP-FP', 'D-B contractor becomes engineer of record'),
                                    ('LV', 'Low Voltage', 'LV lead', 'TIDP-LV', ''), ('G', 'Civil and Site', 'Civil lead', 'TIDP-G', '')]:
    row(f'{d}-500', disc, '00' if d == 'G' else 'ZZ', 'Construction-stage model (revisions tracked)', 'Model', 'M3', C31, '400', 'RVT/IFC', 'A1', 'Published', 12, 43, resp, tidp, 2, 'Y', 'Y', 'N',
        'Jun P01', 'Per-discipline rows restored; Aug collapsed them into ALL-500 (G-02)', extra)
row('C-500', 'Contractor', 'ZZ', 'Shop drawings and fabrication models (steel, MEP modules, façade)', 'Model/Drawing', 'M3', C31, '400', 'RVT/IFC/PDF', 'S2', 'Shared', 12, 43, 'Contractor', 'TIDP-C', 2, 'Y', 'Y', 'N', 'New P02', 'Added (Playbook 6.6; risk "late specialist models")', 'Linked into the federation')
row('Z-510', IM, 'ZZ', 'Request for information and submittal register', 'Report', 'RP', C31, '400', 'XLSX', 'S2', 'Shared', 12, 43, 'Information Manager', 'TIDP-Z', 3, 'N', 'N', 'N', 'Both', '', 'Maintained continuously')
row('Z-511', IM, 'ZZ', 'Monthly BIM status report (construction)', 'Report', 'RP', C31, '400', 'PDF', 'S2', 'Shared', 12, 49, 'Information Manager', 'TIDP-Z', 3, 'N', 'N', 'N', 'Both', 'End month M43 → M49: reporting continues through 3.2 and 3.3', '')
row('Z-512', 'Contractor', 'ZZ', 'As-built capture (progressive)', 'Model', 'M3', C31, '400', 'RVT/IFC', 'S2', 'Shared', 12, 43, 'Contractor', 'TIDP-C', 2, 'Y', 'Y', 'N', 'Aug P01', '', 'Clouded changes in official documentation; current to within one month')
row('Z-513', 'Contractor', 'ZZ', 'Asset data capture (tiered schedule, BEP 14)', 'Schedule', 'SC', C31, '400', 'XLSX', 'S2', 'Shared', 12, 47, 'Contractor', 'TIDP-C', 2, 'Y', 'Y', 'Y', 'Aug P01', 'End month M43 → M47: capture continues through FF&E installation', 'Tier A serialised plant, B maintainable devices, C warranted fabric')
row('Z-514', IM, 'ZZ', 'Asset data completeness report (monthly, by tier and volume)', 'Report', 'RP', C31, '400', 'XLSX/PDF', 'S2', 'Shared', 12, 47, 'Information Manager', 'TIDP-Z', 3, 'N', 'N', 'N', 'Aug P01', 'End month M43 → M47', 'A tier below 95% at Gate D is a gate failure')
row('Z-515', IM, 'ZZ', 'Quarterly model-health audit', 'Report', 'RP', C31, '400', 'PDF', 'S2', 'Shared', 12, 43, 'Information Manager', 'TIDP-Z', 3, 'N', 'N', 'N', 'Jun P01', 'June ref Z-514 restored; required by proposal 2.1 and 4.5', '')
row('Z-516', IM, 'ZZ', 'Revision register', 'Schedule', 'SC', C31, '400', 'XLSX', 'S2', 'Shared', 12, 43, 'Information Manager', 'TIDP-Z', 3, 'N', 'N', 'N', 'Jun P01', 'June ref Z-512 restored', 'Clouds and revision schedules')
row('Z-517', IM, 'ZZ', 'Contractor and specialist induction record', 'Document', 'RP', C31, 'n/a', 'PDF', 'S2', 'Shared', 12, 12, 'Information Manager', 'TIDP-Z', 3, 'N', 'N', 'N', 'New P02', 'Added (proposal 2.1; BEP 12)', 'Including the design-build fire and controls contractors')
row('Z-518', IM, 'ZZ', 'Construction-stage coordination reports (monthly)', 'Report', 'CR', C31, '400', 'PDF/BCF', 'S2', 'Shared', 12, 43, 'Information Manager', 'TIDP-Z', 3, 'N', 'N', 'N', 'New P02', 'Added: monthly federation and clash in construction (BEP 7.1)', '')
row('M-520', 'Mechanical', 'ZZ', 'Commissioning point list (Niagara)', 'Schedule', 'SC', C31, '400', 'CSV', 'S2', 'Shared', 40, 40, 'MEP lead', 'TIDP-M', 2, 'Y', 'N', 'Y', 'Both', '', 'Produced from the model for the controls contractor')

# ── 3.2 FF&E ────────────────────────────────────────────────────────────────
row('FF-600', 'FF&E', 'ZZ', 'FF&E final schedule and procurement record', 'Schedule', 'SC', C32, '400', 'XLSX', 'A1', 'Published', 44, 47, 'Interior Designer', 'TIDP-FF', 2, 'Y', 'N', 'Y', 'Both', 'Month M43 → M44–M47 (A-01)', 'Reconciled item by item')
row('FF-601', 'FF&E', 'ZZ', 'Installed FF&E and finishes reconciliation report (model vs Fohlio)', 'Report', 'RP', C32, '400', 'XLSX/PDF', 'S2', 'Shared', 47, 47, 'Information Manager', 'TIDP-FF', 2, 'Y', 'N', 'Y', 'New P02', 'Added (Playbook 6.7 exit criteria)', 'No unlinked items; O&M data collected in Fohlio')

# ── 3.3 Deliverable D ───────────────────────────────────────────────────────
for d, disc, resp, tidp, nm in [('A', 'Architecture', 'Architecture lead', 'TIDP-A', 'As-built architectural model'), ('S', 'Structure', 'Structural lead', 'TIDP-S', 'As-built structural model'),
                                 ('M', 'Mechanical', 'MEP lead', 'TIDP-M', 'As-built mechanical model'), ('E', 'Electrical', 'MEP lead', 'TIDP-E', 'As-built electrical model'),
                                 ('P', 'Public Health', 'MEP lead', 'TIDP-P', 'As-built plumbing and drainage model'), ('FP', 'Fire Protection', 'Design-build fire contractor', 'TIDP-FP', 'As-built fire protection model'),
                                 ('LV', 'Low Voltage', 'LV lead', 'TIDP-LV', 'As-built low voltage model'), ('G', 'Civil and Site', 'Civil lead', 'TIDP-G', 'As-built civil and site model')]:
    row(f'{d}-700', disc, '00' if d == 'G' else 'ZZ', nm + ' (LOD 500, verified)', 'Model', 'M3', DD, '500', 'RVT/IFC', 'A1', 'Published', 48, 49, resp, tidp, 1, 'Y', 'Y', 'Y',
        'Both' if d in ('A', 'S', 'M') else 'Jun P01', 'Month M45 → M48–M49 (A-01)' + ('' if d in ('A', 'S', 'M') else '; per-discipline row restored (G-02)'), 'Authored by the design consultant from the Contractor as-built documentation; verified by the Information Manager')
row('C-700', 'Contractor', 'ZZ', 'Final as-built documentation, commissioning records, warranties and O&M documents', 'Document', 'RP', DD, '500', 'PDF', 'A1', 'Published', 48, 48, 'Contractor', 'TIDP-C', 1, 'Y', 'Y', 'Y', 'New P02', 'Added (A1: within 60 days of substantial completion)', 'Input to the record model verification')
row('Z-700', IM, 'ZZ', 'COBie 2.4 handover dataset — CONDITIONAL on Owner instruction', 'Schedule', 'SC', DD, '500', 'XLSX', 'A1', 'Published', 48, 49, 'Information Manager', 'TIDP-Z', 1, 'N', 'N', 'Y', 'Both', 'Marked conditional (D-01): excluded from the base services in proposal 2.2', 'Produced only if instructed as an additional service')
row('Z-701', IM, 'ZZ', 'Operation and maintenance / asset data pack (Fohlio)', 'Document', 'RP', DD, '500', 'PDF', 'A1', 'Published', 48, 49, 'Information Manager', 'TIDP-Z', 1, 'Y', 'N', 'Y', 'Both', 'Month M45 → M48–M49', 'O&M manuals published through Fohlio by the Interior Designer (A1)')
row('Z-702', IM, 'ZZ', 'Reconciled building management system point register (digital-twin baseline)', 'Schedule', 'SC', DD, '500', 'CSV/PDF', 'A1', 'Published', 49, 49, 'Information Manager', 'TIDP-Z', 1, 'Y', 'N', 'Y', 'Both', 'Month M45 → M49', 'Model reconciled to the live Niagara station')
row('Z-703', IM, 'ZZ', 'Deliverable D gate pack: LOD 500 verification and asset data completeness', 'Document', 'RP', DD, '500', 'PDF', 'A1', 'Published', 49, 49, 'Information Manager', 'TIDP-Z', 1, 'Y', 'N', 'N', 'Aug P01', 'Month M45 → M49', 'Days 10 to 35 of the close-out plan')
row('Z-704', IM, 'ZZ', 'Final transmittal and project archive', 'Document', 'TR', DD, '500', 'PDF', 'A1', 'Archived', 49, 49, 'Information Manager', 'TIDP-Z', 1, 'Y', 'N', 'N', 'Both', 'June ref Z-703; ISO type TR', 'Days 50 to 60')
row('Z-705', 'All disciplines', 'ZZ', 'Native design, calculation, energy and system program files (A1 Deliverable D)', 'Document', 'RP', DD, '500', 'Native', 'A1', 'Published', 48, 49, 'Task Team Managers', 'TIDP-ALL', 1, 'Y', 'N', 'Y', 'New P02', 'Added (A1 Deliverable D contents; proposal 4.4 days 35 to 50)', '')
row('Z-706', IM, 'ZZ', 'Final drawing register', 'Schedule', 'SC', DD, '500', 'XLSX/PDF', 'A1', 'Published', 49, 49, 'Information Manager', 'TIDP-Z', 2, 'N', 'N', 'N', 'New P02', 'Added (Playbook 6.8 deliverables)', '')

# Retired or merged rows (recorded for the change log only)
RETIRED = [
    ('Z-023', 'New P02', 'Drawing and document numbering convention (reissued)', 'Absorbed into Z-022, the Document Control Standard. A separate numbering convention would have been a third statement of the container naming rules, beside BEP 4.2 and the standard itself, and the June pair had already drifted apart'),
    ('ALL-500', 'Aug P01', 'Construction stage models (all disciplines)', 'Replaced by per-discipline rows A-500 to G-500 (G-02)'),
    ('Z-004 (June)', 'Jun P01', 'Naming and tagging standard', 'Merged into BEP 4.2 and Playbook s4; no separate document'),
    ('Z-005 (June)', 'Jun P01', 'CDE setup (four states + permissions)', 'Became Z-012 CDE folder structure and permission matrix'),
    ('Z-006 (June)', 'Jun P01', 'Revit template + controlled family library', 'Became Z-004'),
    ('Z-007 (June)', 'Jun P01', 'Kickoff training + signed BEP', 'Became Z-021'),
    ('Z-003 (June)', 'Jun P01', 'Federated coordination model at 2.1', 'Kept as Aug ref Z-010'),
    ('Z-401 (June)', 'Jun P01', 'Conformed set', 'Kept as Aug ref Z-410'),
    ('Z-512 (June)', 'Jun P01', 'Revision register', 'Became Z-516 (Aug Z-512 is the Contractor as-built capture row)'),
    ('Z-514 (June)', 'Jun P01', 'Quarterly model-health audit', 'Became Z-515 (Aug Z-514 is the asset data completeness report)'),
    ('M-300b / E-300b (June)', 'Jun P01', 'HVAC load schedules; panel schedules and single-line diagrams', 'Became M-302 and E-301'),
    ('Z-703 (June)', 'Jun P01', 'Final handover transmittal + archive', 'Became Z-704 (Aug Z-703 is the Deliverable D gate pack)'),
]

TIDPS = [('TIDP-Z', 'Information Management', 'Symbion Consulting Group Studios — Information Manager', 'Z'),
         ('TIDP-A', 'Architecture and interiors', '[FILL — architecture practice]', 'A'),
         ('TIDP-S', 'Structural', '[FILL — structural engineer]', 'S'),
         ('TIDP-M', 'Mechanical', '[FILL — MEP engineer]', 'M'),
         ('TIDP-E', 'Electrical', '[FILL — MEP engineer]', 'E'),
         ('TIDP-P', 'Public health (plumbing and drainage)', '[FILL — MEP engineer]', 'P'),
         ('TIDP-FP', 'Fire protection', '[FILL — fire engineer / design-build contractor]', 'FP'),
         ('TIDP-LV', 'Low voltage and communications', '[FILL — LV engineer]', 'LV'),
         ('TIDP-G', 'Civil and site', '[FILL — civil engineer]', 'G'),
         ('TIDP-Q', 'Quantity surveying / cost', '[FILL — quantity surveyor]', 'Z'),
         ('TIDP-FF', 'FF&E and finishes (Interior Designer)', '[FILL — interior designer]', 'A'),
         ('TIDP-C', 'Contractor and specialists', '[FILL — main contractor]', 'ZZZ')]

STAGES = [MOB, DA, DB, DC, TEN, CON, C31, C32, DD]
ISS_STATUS = ['A1 authorised', 'B1 with comments', 'Rejected (reissue)', 'S2 information']

# The permitted values are NOT restated here. They live in midp_schema.LISTS,
# which is what the workbook's drop-downs offer and what merge_tidp.py validates
# a returned plan against. This module kept its own copies until they drifted by
# exactly one value -- 'Schedule/Drawing' was used by a row here and absent from
# the drop-down there, so the register carried a value its own workbook refused.
from midp_schema import LISTS as _L                                 # noqa: E402

DISCIPLINES = _L['Discipline']
TYPES = _L['Type']
ISOTYPES = _L['Type code']
LODS = _L['LOD']
SUITS = _L['Suitability']
STATES = _L['CDE State']
RAGS = _L['RAG']
VOLUMES = _L['Volume']

# Planned drawing schedule (June MIDP Detailed v2, lighting L mapped to E; volumes numeric)
DRAWINGS = [
 # volume, building, role, sheets, content
 ('01', 'Temple (2,449 m²)', 'A', 28, 'GA plans, RCPs, elevations, sections, details, door/window schedules'),
 ('01', 'Temple (2,449 m²)', 'S', 17, 'Foundations, framing plans, sections, details, schedules'),
 ('01', 'Temple (2,449 m²)', 'M', 14, 'HVAC plans, schematics, equipment schedules, details'),
 ('01', 'Temple (2,449 m²)', 'E', 24, 'Power, lighting (17 + 7 lighting sheets), single-line, panel schedules, details'),
 ('01', 'Temple (2,449 m²)', 'P', 11, 'Water, drainage, schematics, details'),
 ('01', 'Temple (2,449 m²)', 'FP', 6, 'Fire strategy, sprinkler and detection layout'),
 ('01', 'Temple (2,449 m²)', 'A (interiors)', 8, 'FF&E layouts, finishes, joinery'),
 ('02', 'Meetinghouse (1,312 m²)', 'A', 20, 'GA plans, RCPs, elevations, sections, details, door/window schedules'),
 ('02', 'Meetinghouse (1,312 m²)', 'S', 12, 'Foundations, framing plans, sections, details, schedules'),
 ('02', 'Meetinghouse (1,312 m²)', 'M', 10, 'HVAC plans, schematics, equipment schedules, details'),
 ('02', 'Meetinghouse (1,312 m²)', 'E', 17, 'Power, lighting (12 + 5), single-line, panel schedules, details'),
 ('02', 'Meetinghouse (1,312 m²)', 'P', 8, 'Water, drainage, schematics, details'),
 ('02', 'Meetinghouse (1,312 m²)', 'FP', 4, 'Fire strategy, sprinkler and detection layout'),
 ('02', 'Meetinghouse (1,312 m²)', 'A (interiors)', 6, 'FF&E layouts, finishes, joinery'),
 ('03', 'Housing / ancillary (2,554 m²)', 'A', 24, 'GA plans, RCPs, elevations, sections, details, door/window schedules'),
 ('03', 'Housing / ancillary (2,554 m²)', 'S', 14, 'Foundations, framing plans, sections, details, schedules'),
 ('03', 'Housing / ancillary (2,554 m²)', 'M', 12, 'HVAC plans, schematics, equipment schedules, details'),
 ('03', 'Housing / ancillary (2,554 m²)', 'E', 20, 'Power, lighting (14 + 6), single-line, panel schedules, details'),
 ('03', 'Housing / ancillary (2,554 m²)', 'P', 10, 'Water, drainage, schematics, details'),
 ('03', 'Housing / ancillary (2,554 m²)', 'FP', 5, 'Fire strategy, sprinkler and detection layout'),
 ('03', 'Housing / ancillary (2,554 m²)', 'A (interiors)', 7, 'FF&E layouts, finishes, joinery'),
 ('04', 'Grounds building (93 m²)', 'A', 7, 'GA plans, RCPs, elevations, sections, details'),
 ('04', 'Grounds building (93 m²)', 'S', 4, 'Foundations, framing, sections, details'),
 ('04', 'Grounds building (93 m²)', 'M', 4, 'HVAC plans, schematics, details'),
 ('04', 'Grounds building (93 m²)', 'E', 4, 'Power, lighting, single-line, details'),
 ('04', 'Grounds building (93 m²)', 'P', 3, 'Water, drainage, details'),
 ('05', 'Utility building (166 m²)', 'A', 9, 'GA plans, RCPs, elevations, sections, details'),
 ('05', 'Utility building (166 m²)', 'S', 5, 'Foundations, framing, sections, details'),
 ('05', 'Utility building (166 m²)', 'M', 4, 'HVAC plans, schematics, details'),
 ('05', 'Utility building (166 m²)', 'E', 7, 'Power, lighting (5 + 2), single-line, details'),
 ('05', 'Utility building (166 m²)', 'P', 4, 'Water, drainage, details'),
 ('05', 'Utility building (166 m²)', 'FP', 2, 'Fire strategy, sprinkler and detection layout'),
 ('05', 'Utility building (166 m²)', 'A (interiors)', 3, 'FF&E layouts, finishes'),
 ('06', 'Guard house (23 m²)', 'A', 4, 'GA plans, elevations, sections, details'),
 ('06', 'Guard house (23 m²)', 'S', 2, 'Foundations, framing'),
 ('06', 'Guard house (23 m²)', 'M', 2, 'HVAC plan, details'),
 ('06', 'Guard house (23 m²)', 'E', 2, 'Power and lighting, single-line'),
 ('06', 'Guard house (23 m²)', 'P', 2, 'Water, drainage'),
 ('00', 'Site-wide / external works', 'G', 4, 'Grading and levels'),
 ('00', 'Site-wide / external works', 'G', 4, 'Drainage and stormwater'),
 ('00', 'Site-wide / external works', 'G', 3, 'Utilities and services'),
 ('00', 'Site-wide / external works', 'G', 2, 'Roads, paving and parking'),
 ('00', 'Site-wide / external works', 'G', 3, 'External works and landscape'),
 ('00', 'Site-wide / external works', 'G', 4, 'Civil details and sections'),
]


# ── validation ──────────────────────────────────────────────────────────────
# Run on import, so a bad row fails the build rather than shipping a register
# that disagrees with the plan it implements.

import os as _os                                                    # noqa: E402
import sys as _sys                                                  # noqa: E402

_sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
import kut_docs_lib as _K                                           # noqa: E402


def _check():
    problems = []
    stages = _K.stage_months()
    tidp_refs = {t[0] for t in TIDPS} | {'TIDP-ALL'}
    seen = {}

    for r in R:
        ref = r['ref']
        if ref in seen:
            problems.append('%s appears twice' % ref)
        seen[ref] = r

        # A deliverable STARTS inside the stage that owns it. It may END later:
        # an Owner review window, a register maintained across stages and a
        # progressive as-built capture all legitimately run past the gate they
        # belong to. What is never legitimate is starting outside -- that is
        # the August defect, where FF&E deliverables were planned four months
        # before their own stage began.
        key = r['stage'].split()[0]
        if key in stages:
            start, end, _ = stages[key]
            if not start <= r['m_from'] <= end:
                problems.append('%s starts M%d, outside stage %s (M%d to M%d)'
                                % (ref, r['m_from'], key, start, end))
        elif r['stage'] != 'Mobilisation':
            problems.append('%s carries unknown stage %r' % (ref, r['stage']))

        if r['m_to'] < r['m_from']:
            problems.append('%s ends M%d before it starts M%d'
                            % (ref, r['m_to'], r['m_from']))
        if r['m_to'] > _K.TOTAL_MONTHS:
            problems.append('%s ends M%d, past the M%d programme'
                            % (ref, r['m_to'], _K.TOTAL_MONTHS))

        if r['isotype'] not in ISOTYPES:
            problems.append('%s carries type code %r, which BEP 4.2 does not permit'
                            % (ref, r['isotype']))
        if r['volume'] not in VOLUMES:
            problems.append('%s carries volume %r, which is not in the volume scheme'
                            % (ref, r['volume']))
        if r['tidp'] not in tidp_refs:
            problems.append('%s is assigned to %s, which no Task Information '
                            'Delivery Plan covers' % (ref, r['tidp']))
        for field, allowed in (('suit', SUITS), ('state', STATES),
                               ('lod', LODS), ('discipline', DISCIPLINES),
                               ('type', TYPES)):
            if r[field] not in allowed:
                problems.append('%s carries %s=%r, which is not a permitted value'
                                % (ref, field, r[field]))

    if problems:
        raise SystemExit('MIDP register is invalid:\n  ' + '\n  '.join(problems))


_check()
