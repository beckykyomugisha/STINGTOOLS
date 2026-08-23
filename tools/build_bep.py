# -*- coding: utf-8 -*-
"""Build the KUT BIM Execution Plan as a corporate .docx.

Source content: GUIDES/KUT_BEP_TEMPLATE.md, with three conflicts resolved (see
the CHANGELOG entry). House style comes from tools/corporate_docx.py, shared
with tools/build_team_playbook.py so the issued set looks like one set.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from corporate_docx import CorporateDoc, NAVY, SLATE, GREY  # noqa: E402

OUT = 'KUT_BIM_Execution_Plan.docx'
c = CorporateDoc()

c.title_page(
    title='BIM Execution Plan',
    eyebrow='Kampala Uganda Temple',
    strapline='Prepared in response to the Appointing Party exchange information requirements, '
              'in accordance with BS EN ISO 19650-2',
    control_rows=[
        ('Document reference', 'KUT-PLN-ZZ-ZZ-RP-Z-0001'),
        ('Revision', 'P01'),
        ('Status / suitability', '[FILL — S3 for review, or A1 on acceptance]'),
        ('BEP type', '[FILL — pre-appointment / post-appointment]'),
        ('Owned by', 'Symbion Consulting Group Studios (Lead Appointed Party)'),
        ('Prepared and maintained by', 'Planscape Consulting Engineers Ltd (Information Manager)'),
        ('Date of issue', '[FILL]'),
    ],
    note='Issued through the Common Data Environment. Uncontrolled when printed.')

c.footer('KUT BIM Execution Plan   |   Rev P01')

# ── document control ────────────────────────────────────────────────────────
c.h1('Document control')

c.h2('Approval')
c.table(['Role', 'Organisation', 'Name', 'Signature', 'Date'],
        [['Prepared by', 'Planscape', 'Mayanja Davis', '', ''],
         ['Checked by', '[FILL]', '[FILL]', '', ''],
         ['Approved by (Lead Appointed Party)', 'Symbion', '[FILL]', '', ''],
         ['Accepted by (Appointing Party)', 'The Church', '[FILL]', '', '']],
        widths=[4.6, 3.2, 3.2, 3.0, 2.6])

c.h2('Revision history')
c.table(['Rev', 'Date', 'Prepared', 'Checked', 'Summary of change'],
        [['P01', '[FILL]', '[FILL]', '[FILL]', 'First issue'],
         ['', '', '', '', ''],
         ['', '', '', '', '']],
        widths=[1.4, 2.4, 2.6, 2.6, 7.6])

c.h2('Status')
c.para('This BIM Execution Plan sets out how the project team will produce, manage, exchange and hand over '
       'information for the Kampala Uganda Temple project, in accordance with BS EN ISO 19650 and the '
       'exchange information requirements of the Appointing Party.')
c.para('This plan is the contractual statement of the information requirements. The KUT Project Delivery '
       'Playbook (KUT-PLN-ZZ-ZZ-RP-Z-0002) is the working procedure that delivers them. Where the two '
       'documents differ, this plan takes precedence.')
c.callout('This plan is reissued whenever the standards, the project team, the programme or the scope change. '
          'The current revision is the one published in the Common Data Environment.', 'Currency')

c.h2('Contents')
c.table(['Section', 'Title'],
        [['1', 'Introduction and project information'],
         ['2', 'Roles, responsibilities and authorities'],
         ['3', 'Information requirements'],
         ['4', 'Standards, methods and procedures'],
         ['5', 'Common Data Environment'],
         ['6', 'Software, exchange formats and coordinate system'],
         ['7', 'Collaboration and coordination'],
         ['8', 'Security-minded approach'],
         ['9', 'Information delivery planning'],
         ['10', 'Quality assurance and model validation'],
         ['11', 'FF&E, handover and operations'],
         ['12', 'Training and competence'],
         ['13', 'Risks and mitigation'],
         ['14', 'Asset data schedule'],
         ['15', 'Appendices']],
        widths=[2.6, 14.0], font=9)

# ── 1 ───────────────────────────────────────────────────────────────────────
c.h1('1  Introduction and project information')

c.h2('1.1  Purpose')
c.para('This plan sets out the information management arrangements for the project: what information is '
       'required, who produces it, to what standard, by when, and how it is checked and accepted. It responds '
       'to the exchange information requirements issued by the Appointing Party and forms part of the '
       'appointment of every appointed party.')

c.h2('1.2  Project details')
c.table(['Item', 'Detail'],
        [['Project name', 'Kampala Uganda Temple'],
         ['Project code', 'KUT'],
         ['Location', '[FILL — address, Kampala, Uganda]'],
         ['Description', '[FILL — temple and ancillary buildings, gross floor area, storeys]'],
         ['Procurement route', '[FILL]'],
         ['Form of contract', '[FILL]'],
         ['Programme', '49 months. Phase 2 (design) 11 months; Phase 3 (construction and close-out) 38 months. '
                       'See Section 9'],
         ['Key dates', '[FILL — appointment, Deliverables A to D, practical completion]']],
        widths=[4.4, 12.2])

c.h2('1.3  Volumes')
c.para('The project is divided into seven volumes. The volume governs how models are divided and federated, '
       'and forms a field in every container name and asset identifier.')
c.table(['Volume code', 'Volume', 'Numbering value'],
        [['BLD1', 'Temple', '01'], ['BLD2', 'Meetinghouse', '02'],
         ['BLD3', 'Housing and ancillary', '03'], ['BLD4', 'Grounds', '04'],
         ['BLD5', 'Utility', '05'], ['BLD6', 'Guard house', '06'],
         ['EXT', 'Site-wide and external works', '00']],
        widths=[3.4, 7.6, 5.6])

c.h2('1.4  Objectives and information uses')
c.table(['#', 'Objective', 'Information use', 'Success measure'],
        [['1', 'Coordinated design', 'Three-dimensional coordination and clash detection',
          'No unresolved high priority clashes at any data drop'],
         ['2', 'Reliable quantities and cost', 'Quantity take-off', 'Bill of quantities produced from the '
          'model at Deliverables B and C'],
         ['3', 'Efficient documentation', 'Drawing production', 'Drawings produced to the project standard '
          'from the coordinated models'],
         ['4', 'FF&E and handover', 'FF&E specification, structured handover data',
          'COBie and operation and maintenance information delivered at Deliverable D'],
         ['5', 'Operational readiness', 'Building management system integration',
          'Record model reconciled to the live building management system at handover'],
         ['6', '[FILL]', '[FILL]', '[FILL]']],
        widths=[1.0, 4.2, 5.2, 6.2])

# ── 2 ───────────────────────────────────────────────────────────────────────
c.h1('2  Roles, responsibilities and authorities')

c.h2('2.1  Information management roles')
c.table(['Role', 'Organisation', 'Name', 'Contact'],
        [['Appointing Party', 'The Church — Special Projects', '[FILL]', '[FILL]'],
         ['Lead Appointed Party', 'Symbion Consulting Group Studios', '[FILL]', '[FILL]'],
         ['Information Manager', 'Planscape Consulting Engineers Ltd', 'Mayanja Davis', '[FILL]'],
         ['BIM Coordinator', '[FILL]', '[FILL]', '[FILL]'],
         ['Task Team Manager — Architecture and interiors', '[FILL]', '[FILL]', '[FILL]'],
         ['Task Team Manager — Structural', '[FILL]', '[FILL]', '[FILL]'],
         ['Task Team Manager — Mechanical, electrical and plumbing', '[FILL]', '[FILL]', '[FILL]'],
         ['Task Team Manager — Fire protection', '[FILL]', '[FILL]', '[FILL]'],
         ['Task Team Manager — Civil and site', '[FILL]', '[FILL]', '[FILL]'],
         ['Quantity Surveyor', '[FILL]', '[FILL]', '[FILL]'],
         ['Contractor (from Stage 3.1)', '[FILL]', '[FILL]', '[FILL]']],
        widths=[5.6, 4.4, 3.4, 3.2])

c.h2('2.2  Authorities')
c.callout('The Information Manager is accountable for information management, coordination and verification. '
          'Design responsibility remains with the appointed party producing the design. Where coordination '
          'requires a design change, that change is made and signed by the responsible discipline.',
          'Limit of the Information Manager role')

c.h2('2.3  Responsibility matrix')
c.para('R — carries out the work.   A — accountable for the outcome.   C — consulted.   I — informed. '
       'The matrix is expanded per deliverable in the delivery plans referred to in Section 9.',
       size=9, italic=True)
c.table(['Activity', 'IM', 'Lead AP', 'Arch', 'Struct', 'MEP', 'QS', 'Contr'],
        [['Maintain the Common Data Environment', 'A/R', 'A', 'I', 'I', 'I', 'I', 'C'],
         ['Set standards and issue the project template', 'R', 'A', 'C', 'C', 'C', 'I', 'I'],
         ['Produce discipline models', 'C', 'A', 'R', 'R', 'R', 'I', 'C'],
         ['Perform the pre-share check', 'C', 'I', 'R', 'R', 'R', 'I', 'R'],
         ['Federate and run clash detection', 'R', 'A', 'C', 'C', 'C', 'I', 'C'],
         ['Resolve a clash', 'C', 'A', 'R', 'R', 'R', 'I', 'R'],
         ['Approve a data drop', 'C', 'A/R', 'C', 'C', 'C', 'C', 'I'],
         ['Quantities and cost', 'C', 'I', 'C', 'C', 'C', 'A/R', 'C'],
         ['FF&E and finishes information', 'R', 'A', 'R', 'I', 'C', 'C', 'C'],
         ['As-built capture', 'C', 'A', 'C', 'C', 'C', 'I', 'R'],
         ['Handover information', 'A/R', 'A', 'C', 'C', 'C', 'I', 'R']],
        widths=[7.0, 1.4, 1.6, 1.3, 1.4, 1.3, 1.2, 1.4], font=8)

# ── 3 ───────────────────────────────────────────────────────────────────────
c.h1('3  Information requirements')

c.h2('3.1  Requirement sources')
c.table(['Requirement', 'Reference', 'How it is satisfied'],
        [['Exchange information requirements', '[FILL — document reference]',
          'This plan and the delivery plans in Section 9'],
         ['Project information requirements', '[FILL]', '[FILL]'],
         ['Asset information requirements', '[FILL]',
          'Structured handover data, operation and maintenance information, and the reconciled building '
          'management system point register delivered at Deliverable D']],
        widths=[4.6, 4.0, 8.0])

c.h2('3.2  Level of information need by stage')
c.para('Level of information need comprises geometry, alphanumeric data and documentation. The detailed '
       'parameter requirements per category are set out in Section 7 of the Project Delivery Playbook and '
       'are verified automatically at each gate.')
c.table(['Stage', 'Geometry (LOD)', 'Alphanumeric data', 'Documentation'],
        [['2.1  Basis of Design (Deliverable A)', '200', 'Identity and spatial data', 'Basis of design report'],
         ['2.2  Developed Design (Deliverable B)', '300', 'Discipline data and classification',
          '50% drawing set, schedules'],
         ['2.3  Technical Design (Deliverable C)', '350', 'Full specification data',
          '100% set, bill of quantities'],
         ['2.5  Conformed set', '350', 'As Deliverable C, incorporating addenda', 'Conformed drawing set'],
         ['3.1  Construction', '400', 'Manufacturer and installation data',
          'Shop and fabrication information'],
         ['3.2  FF&E installation', '400', 'FF&E and finishes data', 'FF&E schedules and specifications'],
         ['3.3  Close-out (Deliverable D)', '500', 'Verified as-built data, including asset data',
          'Handover data, operation and maintenance information, as-built drawings']],
        widths=[5.0, 2.4, 5.0, 4.2])
c.callout('Deliverable D is delivered at LOD 500. LOD 500 requires verification that each element corresponds '
          'to the element installed, together with its asset data. For serviceable plant this includes serial '
          'number and installation date, captured progressively during Stage 3.1.', 'Level of development at close-out')

# ── 4 ───────────────────────────────────────────────────────────────────────
c.h1('4  Standards, methods and procedures')

c.h2('4.1  Standards adopted')
c.table(['Topic', 'Standard'],
        [['Information management', 'BS EN ISO 19650-1, -2, -3 and -5'],
         ['Classification', 'CSI MasterFormat, as the specifications are produced in RIB SpecLink. '
                            'Demolition (Division 02) is classified manually — see Section 10.3'],
         ['Container naming', 'ISO 19650 field-based convention — see Section 4.2'],
         ['Quantities and cost', '[FILL — NRM2 or other]'],
         ['Structured handover data', 'COBie 2.4'],
         ['Security', 'BS EN ISO 19650-5 — see Section 8']],
        widths=[4.0, 12.6])

c.h2('4.2  Container naming convention')
c.mono('KUT - PLN - 01 - GF - M3 - A - 0001')
c.table(['Field', 'Length', 'Permitted values'],
        [['Project', '3', 'KUT'],
         ['Originator', '[FILL — 3, subject to Section 4.2.1]', 'Per the originator register'],
         ['Volume', '2', '01 to 06 per Section 1.3; 00 site-wide; ZZ all volumes'],
         ['Level', '2', 'B1, GF, 01 upward, RF, ZZ all levels, XX not applicable'],
         ['Type', '2', 'M3, M2, DR, SH, SC, SP, RP, CA, RD, MS, PP, CR'],
         ['Role', '1 to 2', 'A, S, M, E, P, FP, LV, G. Z is used for multi-discipline and federated containers'],
         ['Number', '4', 'Sequential within the set']],
        widths=[2.6, 3.6, 10.4])

c.h3('4.2.1  Originator code length — to be confirmed')
c.callout('The automated compliance check enforces an originator code of exactly three characters. The '
          'Information Manager default code is four characters. Two options are available: issue '
          'three-character codes to every organisation, or extend the permitted range to three to six '
          'characters, which is closer to general ISO 19650 practice. The Appointing Party originator '
          'register determines which applies. **No container is to be numbered until this is confirmed**, '
          'because renumbering after Deliverable A affects every issued document.', 'Open item')

c.h3('4.2.2  Role code and asset discipline code')
c.para('The role field in a container name and the discipline field in an asset identifier are distinct. '
       'Container role codes may include Z for federated and multi-discipline containers. Asset discipline '
       'codes are limited to the eight discipline codes and are validated on every element.')

c.h2('4.3  Common Data Environment states and suitability')
c.para('WIP  →  Shared  →  Published  →  Archived. Revisions run P01 upward while preliminary and C01 upward '
       'once contractual.')
c.table(['Code', 'Meaning', 'State'],
        [['S0', 'Work in progress; not for use by others', 'WIP'],
         ['S1', 'Shared for coordination', 'Shared'],
         ['S2', 'Shared for information', 'Shared'],
         ['S3', 'Shared for review and comment', 'Shared'],
         ['S4', 'Shared for stage approval', 'Shared'],
         ['A1 to An', 'Published and authorised; contractual', 'Published'],
         ['B1 to Bn', 'Published and authorised with comments', 'Published']],
        widths=[3.0, 9.6, 4.0])

c.h2('4.4  Modelling standards')
c.bullet([
    'The shared coordinate system, project base point and true north are defined at mobilisation and are '
    'not altered thereafter.',
    'Units are millimetres. Level and grid names are taken from the issued project template.',
    'Content originates from the controlled family library. Imported CAD may be used as an underlay only '
    'and must not constitute deliverable geometry.',
    'Worksets follow the convention confirmed at mobilisation: per-volume worksets, or one model per volume.',
    'Rooms are placed and named before the first coordination share.',
    'Mechanical, electrical and plumbing elements are connected into real systems.',
    'Data completeness is verified before every share.',
    'No unresolved high priority clashes at any data drop.',
])

# ── 5 ───────────────────────────────────────────────────────────────────────
c.h1('5  Common Data Environment')
c.table(['Item', 'Arrangement'],
        [['Platform', 'Autodesk Construction Cloud. This is the single authoritative environment for project '
                      'information. No other environment holds project information of record'],
         ['Structure', 'WIP, Shared, Published and Archived, divided by volume and discipline'],
         ['Access control', 'Role-based permissions on the principle of least privilege, in accordance with '
                            'Section 8'],
         ['Issue and approval', 'ACC Issues and Reviews. Coordination issues are exchanged in BCF format'],
         ['Transmittals', 'A formal transmittal records every issue of information'],
         ['Review', 'Bluebeam Studio sessions for Appointing Party review. Comment close-out is a gate condition'],
         ['Backup and retention', '[FILL — retention period and archive arrangements]']],
        widths=[3.4, 13.2])

# ── 6 ───────────────────────────────────────────────────────────────────────
c.h1('6  Software, exchange formats and coordinate system')

c.h2('6.1  Software')
c.table(['Function', 'Software', 'Version'],
        [['Model authoring', 'Autodesk Revit', '[FILL — a single version is used across the project]'],
         ['Common Data Environment', 'Autodesk Construction Cloud', 'Current'],
         ['Coordination and clash detection', 'Navisworks and ACC Model Coordination', 'Current'],
         ['Design review', 'Bluebeam Studio', 'Current'],
         ['FF&E, finishes and O&M', 'Fohlio', 'Current'],
         ['Specifications', 'RIB SpecLink', 'Current'],
         ['Building management system', 'Tridium Niagara', '[FILL]']],
        widths=[5.0, 6.6, 5.0])
c.callout('All parties author in the same major version of Revit for the duration of a stage. Version changes '
          'are made only at a stage boundary and only by agreement of the Information Manager, as a version '
          'change is not reversible.', 'Version control')

c.h2('6.2  Exchange formats')
c.table(['Purpose', 'Format'],
        [['Native models', 'RVT'],
         ['Open exchange', 'IFC [FILL — 2x3 or 4]'],
         ['Coordination', 'NWC, IFC or ACC models'],
         ['Drawings', 'PDF, and DWG where required'],
         ['Schedules and quantities', 'XLSX'],
         ['Coordination issues', 'BCF 2.1'],
         ['Structured handover data', 'COBie 2.4 (XLSX)'],
         ['Operation and maintenance information', 'As exported from the FF&E and O&M system']],
        widths=[6.0, 10.6])

c.h2('6.3  Coordinate system')
c.para('[FILL — agreed survey datum, project base point, survey point, true north and shared coordinate '
       'origin.] The coordinate system is defined at mobilisation and locked thereafter. All models are '
       'linked by shared coordinates.')

# ── 7 ───────────────────────────────────────────────────────────────────────
c.h1('7  Collaboration and coordination')

c.h2('7.1  The coordination cycle')
c.table(['Cadence', 'Activity'],
        [['Daily', 'Authoring in the work in progress area'],
         ['Weekly', 'Progress note from each task team'],
         ['Fortnightly', 'Share, federation, clash detection, coordination report and coordination meeting'],
         ['Monthly', 'Status report, data quality audit and review of the delivery plan'],
         ['Per stage', 'Formal data drop, gate audit, transmittal and Appointing Party review']],
        widths=[3.0, 13.6])
c.callout('The coordination cycle is **fortnightly**. Earlier drafts of this plan described a weekly cycle. '
          'The fortnightly interval matches the appointment and gives task teams a working week between the '
          'issue of a coordination report and the next share, which a weekly cycle does not.', 'Confirmed cadence')

c.h2('7.2  Clash management')
c.table(['Item', 'Arrangement'],
        [['Tools', 'ACC Model Coordination is the system of record. Coordination issues are raised as ACC '
                   'Issues and exchanged in BCF format'],
         ['Clash matrix', '[FILL — which discipline models are tested against which]'],
         ['Priorities', 'P1 critical, P2 major, P3 minor, P4 observation, as defined in the Project Delivery '
                        'Playbook'],
         ['Clearances and tolerances', '[FILL — hard clash tolerance and clearance rules by interface]'],
         ['Grouping', 'Clashes are grouped so that each issue represents one physical condition rather than '
                      'one geometric intersection'],
         ['Acceptance', 'No unresolved high priority clashes at any data drop'],
         ['Reporting', 'A coordination report is issued 48 hours before each coordination meeting']],
        widths=[3.8, 12.8])

c.h2('7.3  Meetings')
c.table(['Meeting', 'Frequency', 'Chair', 'Attendees', 'Output'],
        [['Coordination', 'Fortnightly', 'Lead Appointed Party', 'Task Team Managers, Information Manager',
          'Issue assignments and dates'],
         ['Design team', 'Weekly', 'Lead Appointed Party', 'Design leads', 'Decisions log'],
         ['Information management', 'Monthly', 'Information Manager', 'Task Team Managers',
          'Standards and data quality actions'],
         ['Appointing Party review', 'Per gate', 'Appointing Party', 'All', 'Review comments'],
         ['Gate sign-off', 'Per gate', 'Lead Appointed Party', 'Appointing Party, Information Manager, leads',
          'Acceptance']],
        widths=[3.2, 2.2, 3.2, 4.4, 3.6], font=8)

# ── 8 ───────────────────────────────────────────────────────────────────────
c.h1('8  Security-minded approach')
c.para('The project is treated as sensitive. The following arrangements apply in accordance with '
       'BS EN ISO 19650-5.')
c.table(['Item', 'Arrangement'],
        [['Access control', 'Role-based permissions in the Common Data Environment, on the principle of '
                            'least privilege. Access is reviewed at each stage boundary'],
         ['Sensitive information', '[FILL — identify the information to be restricted and the parties '
                                  'permitted access]'],
         ['Data handling', '[FILL — storage, transfer and confidentiality arrangements, including '
                           'non-disclosure agreements]'],
         ['Publication', 'Project information, including images, plans and visualisations, must not be '
                         'published, circulated outside the project, or used in promotional or portfolio '
                         'material without the written permission of the Appointing Party'],
         ['Breach procedure', '[FILL — reporting route and timescale]']],
        widths=[3.4, 13.2])

# ── 9 ───────────────────────────────────────────────────────────────────────
c.h1('9  Information delivery planning')
c.para('Each task team submits a Task Information Delivery Plan setting out its deliverables, dates, level of '
       'information need and responsible person. The Information Manager aggregates these into the Master '
       'Information Delivery Plan, which is baselined at mobilisation and reissued at each stage and data drop.')
c.table(['Milestone', 'Stage', 'LOD', 'Planned (relative month)'],
        [['Basis of Design (Deliverable A)', '2.1', '200', 'M1'],
         ['Developed Design (Deliverable B)', '2.2', '300', 'M4'],
         ['Technical Design (Deliverable C)', '2.3', '350', 'M8'],
         ['Tender issue and award', '2.4', '—', 'M9 to M11'],
         ['Conformed set', '2.5', '350', 'M11'],
         ['Construction', '3.1', '400', 'M12 to M43'],
         ['FF&E installation', '3.2', '400', 'M40 to M43'],
         ['Close-out (Deliverable D)', '3.3', '500', 'M45']],
        widths=[6.4, 2.4, 2.0, 5.8])

# ── 10 ──────────────────────────────────────────────────────────────────────
c.h1('10  Quality assurance and model validation')

c.h2('10.1  Checks')
c.table(['Check', 'Acceptance criterion', 'When'],
        [['Container naming compliance', 'No errors', 'Before every share'],
         ['Data completeness for the stage', 'Not less than 95 per cent, and no critical omissions',
          'Before every share'],
         ['Coordinate and level integrity', 'Unchanged from the issued template', 'Every cycle'],
         ['Clash', 'No unresolved high priority clashes', 'Before every data drop'],
         ['Level of development against the stage', 'Verified for the stage milestone', 'At every gate'],
         ['Deliverables against the delivery plan', 'Complete for the stage', 'Monthly'],
         ['Model health', 'Within the agreed tolerance', 'Monthly'],
         ['Model classification against the specification', 'No unaccepted gaps', 'Before each specification issue'],
         ['Demolition classification (Division 02)', 'Complete — see Section 10.3',
          'Before Deliverable B and before each specification reconciliation'],
         ['Asset data capture', 'On programme against the capture plan', 'Monthly from Stage 3.1']],
        widths=[5.4, 6.6, 4.6])

c.h2('10.2  Reporting of scope')
c.callout('Every validation report states the number of elements examined. A percentage expressed without a '
          'population is not evidence of compliance. Where a check does not apply to a category, the report '
          'discloses the categories excluded from the run.', 'Evidence')

c.h2('10.3  Manual classification of demolition')
c.para('Deliverable B includes an existing conditions and removals plan. Demolition cannot be classified '
       'automatically: classification is resolved from category, family, type and system, whereas demolition '
       'is expressed in the authoring tool through the phase in which an element is demolished, which the '
       'classification process does not receive. Rules based on element naming were considered and rejected, '
       'as they would have presented as coverage while matching nothing.')
c.table(['Item', 'Arrangement'],
        [['Owner of this task', '[FILL — name and role]'],
         ['Method', 'Either add Division 02 entries to the project classification map, keyed on a demolition '
                    'type-naming convention agreed at kick-off, or write the classification directly onto the '
                    'demolition scope'],
         ['Timing', 'Before each specification reconciliation. If omitted, the Appointing Party Division 02 '
                    'specification sections report as over-specification and the reconciliation reads as '
                    'complete when it is not']],
        widths=[3.6, 13.0])

# ── 11 ──────────────────────────────────────────────────────────────────────
c.h1('11  FF&E, handover and operations')
c.table(['Item', 'Arrangement'],
        [['FF&E and finishes', 'Fohlio is the Appointing Party source of truth. The model carries a reference '
                              'to the Fohlio record and does not duplicate it. Information is exchanged each '
                              'cycle, matched on room number, with a difference report reviewed before any '
                              'data is written to the model'],
         ['Quantities and cost', 'Produced from the coordinated models at Deliverables B and C'],
         ['Structured handover data', 'COBie 2.4, produced from the record model at Deliverable D'],
         ['Operation and maintenance information', 'Compiled through the FF&E and O&M system and aligned to '
                                                   'the record model asset identifiers'],
         ['Building management system', 'The commissioning point list is produced from the model at Stage 3.1. '
                                        'At Stage 3.3 the model equipment and points are reconciled against '
                                        'the live station and differences resolved with the controls contractor'],
         ['As-built', 'The model is verified to LOD 500 at Deliverable D']],
        widths=[4.0, 12.6])

# ── 12 ──────────────────────────────────────────────────────────────────────
c.h1('12  Training and competence')
c.table(['Audience', 'Subject', 'When'],
        [['All task teams', 'Common Data Environment, container naming, level of information need, '
                            'coordination process and the pre-share check', 'Mobilisation'],
         ['Task Team Managers', 'Delivery planning and data requirements', 'Mobilisation and each stage'],
         ['Contractor and specialists', 'As-built capture and asset data requirements', 'Before Stage 3.1'],
         ['Operator and facilities management', 'Handover data, O&M and building management system '
                                                'requirements', 'Before handover']],
        widths=[4.4, 8.2, 4.0])
c.para('The kickoff agenda is set out in the Project Delivery Playbook, Appendix C. Competence of individuals '
       'nominated to task teams is confirmed by the appointed party.')

# ── 13 ──────────────────────────────────────────────────────────────────────
c.h1('13  Risks and mitigation')
c.table(['Risk', 'Impact', 'Mitigation', 'Owner'],
        [['Appointing Party standards issued after mobilisation, affecting naming or level of information need',
          'High', 'Standards are held as project configuration rather than embedded in content, so adoption '
                  'is a configuration change rather than rework', 'Information Manager'],
         ['Originator code length unresolved (Section 4.2.1)', 'High',
          'Confirmed in Week 1 of mobilisation, before any container is numbered', 'Information Manager'],
         ['Uncoordinated information', 'High',
          'CDE governance, the fortnightly cycle, and automated validation before every share',
          'Information Manager'],
         ['Late deliverables', 'High', 'Delivery plan tracking with monthly status reporting',
          'Information Manager'],
         ['Weak volume attribution through absent rooms or worksets', 'Medium',
          'Rooms placed before the first share; attribution confidence audited at every gate',
          'Task Team Managers'],
         ['Asset data deferred to close-out', 'High',
          'Asset data capture begins at Stage 3.1 and is reported monthly', 'Contractor'],
         ['Divergence between the model and the FF&E record', 'Medium',
          'Monthly currency check reported in the status report', 'Information Manager'],
         ['Demolition classification omitted (Section 10.3)', 'Medium',
          'Named owner and a defined method, checked before each specification reconciliation', '[FILL]'],
         ['[FILL]', '', '', '']],
        widths=[5.4, 1.8, 6.4, 3.0], font=8)

# ── 14 ──────────────────────────────────────────────────────────────────────
c.h1('14  Asset data schedule')
c.para('This schedule defines the asset information to be captured during construction and delivered with '
       'the record model at Deliverable D. It is tiered by what the asset is, because a requirement applied '
       'uniformly to every element cannot be met. Requiring a serial number on each of several thousand '
       'luminaires produces a register completed to perhaps forty per cent, which the facilities team cannot '
       'rely on for any of it. A smaller requirement, met in full, is worth more than a complete one met in '
       'part.')
c.para('Capture begins at Stage 3.1 and is reported monthly. It is verified at the Deliverable D gate.')

c.h2('14.1  Tiers')
c.table(['Tier', 'What it covers', 'Categories'],
        [['A  Serialised plant',
          'Individually commissioned equipment carrying a nameplate, held under a service contract or '
          'connected to the building management system',
          'Mechanical equipment, electrical equipment, specialty equipment (including baptistry plant, '
          'audio-visual equipment and lift equipment)'],
         ['B  Maintainable devices',
          'High quantity devices with a maintenance regime but no meaningful individual serial number',
          'Lighting fixtures, plumbing fixtures, air terminals, sprinklers, fire alarm devices, electrical '
          'fixtures'],
         ['C  Warranted fabric',
          'No serial number and no maintenance regime, but a warranty the Appointing Party will need to '
          'claim against',
          'Roofs, curtain panels and mullions, doors, windows, casework and joinery'],
         ['FF&E', 'Furniture and loose equipment, reconciled to the FF&E record',
          'Furniture, furniture systems'],
         ['D  All other elements', 'Identified but not asset-managed', 'Every other category']],
        widths=[3.4, 6.2, 7.0])

c.h2('14.2  Data required by tier')
c.table(['Data', 'A', 'B', 'C', 'FF&E', 'D'],
        [['Asset identifier (the eight-field tag)', 'Yes', 'Yes', 'Yes', 'Yes', 'Yes'],
         ['Product code', 'Yes', 'Yes', 'Yes', 'Yes', 'From LOD 350'],
         ['Manufacturer and model reference', 'Yes', 'Yes', '—', 'Yes', 'From LOD 400'],
         ['Unique asset reference', 'Yes', '—', '—', '—', '—'],
         ['Serial number', 'Yes', '—', '—', '—', '—'],
         ['Loop and address', '—', 'Fire alarm devices only', '—', '—', '—'],
         ['Installation date', 'Yes', 'Yes', 'Yes', 'Yes', '—'],
         ['Supplier', 'Yes', 'Yes', 'Yes', 'Yes', '—'],
         ['Warranty guarantor', 'Yes', '—', 'Yes', '—', '—'],
         ['Warranty duration', 'Yes', 'Yes', 'Yes', 'Yes', '—'],
         ['Warranty expiry date', 'Yes', '—', 'Yes', '—', '—'],
         ['Expected service life', 'Yes', 'Yes', '—', '—', '—'],
         ['Maintenance interval', 'Yes', '—', '—', '—', '—'],
         ['Recommended spares', 'Yes', '—', '—', '—', '—'],
         ['Commissioning date', 'Yes', '—', '—', '—', '—'],
         ['FF&E reference', '—', '—', '—', 'Yes', '—']],
        widths=[6.2, 2.2, 3.6, 2.2, 1.6, 2.4], font=8)
c.callout('Fire alarm devices carry loop and address in place of a serial number. That is the identifier the '
          'cause-and-effect schedule, the panel and any future maintenance actually use; a device serial '
          'number is not used by anyone once the device is installed.', 'Fire alarm devices')

c.h2('14.3  Conventions')
c.table(['Item', 'Requirement'],
        [['Date format', 'YYYY-MM-DD throughout. Date fields are held as text, so nothing enforces the '
                         'format automatically; a mixed convention is only discovered at close-out'],
         ['Warranty duration', 'Whole years. Where parts and labour differ, both are recorded'],
         ['Warranty expiry', 'The date the warranty period ends. Expiry rather than start is recorded '
                             'because it is the date that triggers action, and the duration is captured '
                             'alongside it so the start remains derivable'],
         ['Expected service life', 'Whole years, as stated by the manufacturer'],
         ['Maintenance interval', 'Whole months'],
         ['Unique asset reference', 'Allocated by the Appointing Party numbering convention where one is '
                                    'issued; otherwise the asset identifier is used'],
         ['Responsibility', 'The Contractor captures the data at installation and commissioning. The '
                            'Information Manager verifies completeness monthly and at the gate']],
        widths=[4.0, 12.6])

c.h2('14.4  Reporting')
c.para('Completeness is reported monthly from the first month of construction, by tier and by volume, so a '
       'shortfall is visible while the installer is still on site. A tier reported below ninety-five per cent '
       'at the Deliverable D gate is a gate failure, not an observation.')
c.callout('This schedule is the project position pending issue of the Appointing Party asset information '
          'requirements. On receipt, it is reconciled against them and reissued. It is deliberately narrower '
          'than the standard corporate requirement, which applies serial numbers to categories where they '
          'cannot realistically be captured.', 'Status')

c.h1('15  Appendices')
c.table(['Appendix', 'Content', 'Status'],
        [['A', 'Exchange information requirements (Appointing Party)', 'Attached / referenced'],
         ['B', 'Master Information Delivery Plan', 'Attached'],
         ['C', 'Task Information Delivery Plans, per appointed party', 'Attached'],
         ['D', 'Responsibility matrix, expanded', 'Attached'],
         ['E', 'Clash matrix', 'Attached'],
         ['F', 'Project Delivery Playbook (KUT-PLN-ZZ-ZZ-RP-Z-0002)', 'Issued separately'],
         ['G', 'Originator code register', 'To be confirmed — Section 4.2.1'],
         ['H', 'Appointing Party asset information requirements', 'Awaited — see Section 14.4']],
        widths=[2.2, 10.4, 4.0])

c.end_mark()

c.properties(
    title='KUT BIM Execution Plan',
    subject='Kampala Uganda Temple — BIM Execution Plan in accordance with BS EN ISO 19650-2',
    category='Project procedure',
    comments='Rev P01. Issued through the Common Data Environment. Uncontrolled when printed.')

c.save(OUT, generator='tools/build_bep.py')
print('saved:', OUT)
