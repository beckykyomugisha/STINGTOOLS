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

# ── project identity ─────────────────────────────────────────────────────────
# One definition, because it appears in the title page, the roles table, the
# RACI, the document references and the worked naming examples. The Information
# Manager works under Symbion rather than as Planscape, so the originator code is
# SMB and the containers this role produces are KUT-SMB-... The exact role TITLE
# is still to be confirmed by Symbion -- whether the appointment names an
# Information Manager or a BIM Coordinator discharging Symbion's information
# management function -- so it is a placeholder rather than a guess.
ORIGINATOR = 'SMB'
IM_ORG = 'Symbion Consulting Group Studios'
IM_ROLE = '[CONFIRM — Information Manager, or BIM Coordinator within Symbion’s information management function]'
IM_NAME = 'Mayanja Davis'


def ref(type_role_number):
    """A container reference for information this role produces."""
    return 'KUT-%s-ZZ-ZZ-%s' % (ORIGINATOR, type_role_number)


c = CorporateDoc()

c.title_page(
    title='BIM Execution Plan',
    eyebrow='Kampala Uganda Temple',
    strapline='Prepared in response to the Appointing Party exchange information requirements, '
              'in accordance with BS EN ISO 19650-2',
    control_rows=[
        ('Document reference', ref('RP-Z-0001')),
        ('Revision', 'P01'),
        ('Status / suitability', '[FILL — S3 for review, or A1 on acceptance]'),
        ('BEP type', '[FILL — pre-appointment / post-appointment]'),
        ('Owned by', 'Symbion Consulting Group Studios (Lead Appointed Party)'),
        ('Prepared and maintained by', '%s (%s)' % (IM_ORG, IM_NAME)),
        ('Date of issue', '[FILL]'),
    ],
    note='Issued through the Common Data Environment. Uncontrolled when printed.')

c.footer('KUT BIM Execution Plan   |   Rev P01')

# ── document control ────────────────────────────────────────────────────────
c.h1('Document control')

c.h2('Approval')
c.table(['Role', 'Organisation', 'Name', 'Signature', 'Date'],
        [['Prepared by', IM_ORG, IM_NAME, '', ''],
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
       'Playbook (' + ref('RP-Z-0002') + ') is the working procedure that delivers them. Where the two '
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
         ['15', 'Mobilisation checklist'],
         ['16', 'Appendices']],
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
         ['Information Manager', IM_ORG, IM_NAME, '[FILL]'],
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

c.h2('4.1  Information management standards')
c.table(['Topic', 'Standard'],
        [['Information management', 'BS EN ISO 19650-1, -2, -3 and -5'],
         ['Classification', 'CSI MasterFormat, as the specifications are produced in RIB SpecLink. '
                            'Demolition (Division 02) is classified manually — see Section 10.4'],
         ['Container naming', 'ISO 19650 field-based convention — see Section 4.2'],
         ['Quantities and cost', '[FILL — NRM2 or other]'],
         ['Structured handover data', 'COBie 2.4'],
         ['Security', 'BS EN ISO 19650-5 — see Section 8']],
        widths=[4.0, 12.6])
c.para('These govern how information is structured, named and exchanged. The standards that govern what the '
       'information must SAY — the technical and statutory codes the design is produced against — are set out '
       'in Sections 4.1.1 to 4.1.3.')

c.h3('4.1.1  Standards hierarchy')
c.para('The project is procured to an Appointing Party standards package derived from United States practice '
       'and is constructed in Uganda under Ugandan law. Three layers of requirement therefore apply, and they '
       'are not of equal standing.')
c.table(['Layer', 'Comprises', 'Standing'],
        [['Statutory', 'Building Control Act 2013 and the National Building Code; UNBS and adopted EAS '
                       'standards; Electricity Act and ERA regulations; Public Health Act; Occupational '
                       'Safety and Health Act; NEMA requirements; KCCA planning and fire requirements '
                       '[FILL — confirm the applicable list with the Lead Appointed Party]',
          '**Legal floor. Cannot be waived by contract or by agreement**'],
         ['Appointing Party', 'CSI MasterFormat; NFPA 13 and 72; ASHRAE 90.1 and 62.1; ADA / ANSI A117.1; '
                              'AWI; and the Owner’s own design standards [FILL — confirm against the issued '
                              'exchange information requirements]',
          'Binding through the appointment'],
         ['Design practice', 'BS, EN and IEC standards as normally applied by the appointed parties in Uganda',
          'Applies where the layers above are silent']],
        widths=[3.0, 9.0, 4.6])
c.callout('**The governing rule.** Statutory requirements always apply. Where an Appointing Party standard is '
          '**stricter** than the statutory requirement, the Appointing Party standard governs. Where the two '
          '**conflict** rather than differ in strictness, the conflict is recorded in Section 4.1.3 and a '
          'written derogation is obtained from the Appointing Party before the affected information is '
          'produced. No appointed party resolves such a conflict privately.', 'Precedence')

c.h3('4.1.2  Technical standards by discipline')
c.para('The standard each discipline designs to, and the standard against which the information is checked. '
       'Where a cell reads [CONFIRM] the reconciliation in Section 4.1.3 has not yet been closed and the '
       'discipline must not proceed to detailed calculation.')
c.table(['Discipline', 'Appointing Party expectation', 'Local / statutory position', 'Governs'],
        [['Architecture', 'IBC-derived planning and life safety; AWI for architectural woodwork',
          'National Building Code; KCCA planning', '[CONFIRM]'],
         ['Accessibility', 'ADA / ANSI A117.1',
          'Persons with Disabilities Act; UNBS accessibility provisions', '[CONFIRM]'],
         ['Structural', 'ACI 318; ASCE 7',
          'Eurocodes or BS as adopted; **Ugandan wind, seismic and soil values regardless of code**',
          '[CONFIRM] — but loading values are local without exception'],
         ['Mechanical', 'ASHRAE 90.1 and 62.1', 'CIBSE and BS EN practice; National Building Code ventilation',
          '[CONFIRM]'],
         ['Electrical', 'NEC-derived calculation and protection',
          '**240 V single / 415 V three-phase, 50 Hz, BS 7671 / IEC 60364, BS 1363 accessories**',
          '**Local governs** — see the note below'],
         ['Public health', 'IPC / UPC fixture-unit method',
          'BS EN 12056 and BS 8558; National Building Code drainage', '[CONFIRM]'],
         ['Fire protection', 'NFPA 13 (suppression) and NFPA 72 (detection and alarm)',
          'National Building Code fire provisions; KCCA fire approval', '[CONFIRM]'],
         ['Energy', 'ASHRAE 90.1 with ComCheck evidence', 'No directly equivalent local mandate',
          'Appointing Party'],
         ['Units', 'Imperial-derived dimensions in source standards', 'Metric (millimetres) throughout',
          '**Metric, without exception**']],
        widths=[2.8, 4.6, 5.4, 3.8], font=8)
c.callout('**Electrical is the one that cannot be transferred.** Uganda operates at 240 V single phase, 415 V '
          'three phase, 50 Hz, with BS 7671 / IEC 60364 protection philosophy and BS 1363 accessories. NEC '
          'assumes 120/208 V at 60 Hz with AWG conductors and a different protection basis. Conductor sizing, '
          'protective device selection and earthing arrangement do not convert between the two, and equipment '
          'is procured to the local supply. The electrical design is produced to the local standard; the '
          'Appointing Party standard is satisfied where it is stricter and does not conflict.',
          'Electrical supply and protection')

c.h3('4.1.3  Standards reconciliation schedule')
c.para('One row per topic where the layers in Section 4.1.1 differ. This schedule is opened at mobilisation, '
       'closed before Technical Design begins, and reissued with this plan whenever a row changes. An open '
       'row is a live risk: it means two appointed parties may be designing to different bases.')
c.table(['#', 'Topic', 'Difference to resolve', 'Resolution', 'Approved by / date'],
        [['SR-01', 'Electrical supply, protection and accessories',
          'NEC basis versus 240/415 V 50 Hz BS 7671 practice', 'Local governs (Section 4.1.2)',
          '[FILL]'],
         ['SR-02', 'Fire suppression design density and hydrant provision',
          'NFPA 13 versus National Building Code provisions', '[FILL]', '[FILL]'],
         ['SR-03', 'Fire detection zoning, audibility and cause and effect',
          'NFPA 72 versus local practice', '[FILL]', '[FILL]'],
         ['SR-04', 'Sanitary and rainwater drainage sizing method',
          'IPC/UPC fixture units versus BS EN 12056 discharge units', '[FILL]', '[FILL]'],
         ['SR-05', 'Accessibility dimensions and provision',
          'ADA / ANSI A117.1 versus the Persons with Disabilities Act and UNBS', '[FILL]', '[FILL]'],
         ['SR-06', 'Structural design code and loading',
          'ACI 318 / ASCE 7 versus Eurocode or BS; loading values must be Ugandan', '[FILL]', '[FILL]'],
         ['SR-07', 'Ventilation rates and indoor air quality',
          'ASHRAE 62.1 versus National Building Code and CIBSE', '[FILL]', '[FILL]'],
         ['SR-08', 'Energy performance and evidence',
          'ASHRAE 90.1 with ComCheck; no local equivalent', '[FILL]', '[FILL]'],
         ['SR-09', 'Units and product sizing',
          'Imperial-derived sizes versus metric procurement; hard versus soft metric', '[FILL]', '[FILL]'],
         ['SR-10', 'Product equivalence and substitution',
          'Specified US products that are not procurable in Uganda; the approval route for an equivalent',
          '[FILL]', '[FILL]'],
         ['SR-11', 'Lightning protection', '[CONFIRM — NFPA 780 versus BS EN 62305]', '[FILL]', '[FILL]'],
         ['SR-12', '[FILL — add rows as differences are identified]', '', '', '']],
        widths=[1.4, 4.0, 5.6, 3.4, 2.2], font=8)
c.callout('Until a row is closed, the affected discipline works to the **statutory** position, because that is '
          'the only layer that cannot be waived. Recording the row is what prevents each discipline resolving '
          'the same conflict differently and the difference surfacing at tender.', 'Working position while a row is open')

c.h2('4.2  Container naming convention')
c.mono('KUT - ' + ORIGINATOR + ' - 01 - GF - M3 - A - 0001')
c.table(['Field', 'Length', 'Permitted values'],
        [['Project', '3', 'KUT'],
         ['Originator', '[FILL — 3, subject to Section 4.2.1]', 'Per the originator register'],
         ['Volume', '2', '01 to 06 per Section 1.3; 00 site-wide; ZZ all volumes'],
         ['Level', '2', 'B1, GF, 01 upward, RF, ZZ all levels, XX not applicable'],
         ['Type', '2', 'M3, M2, DR, SH, SC, SP, RP, CA, RD, MS, PP, CR'],
         ['Role', '1 to 2', 'A, S, M, E, P, FP, LV, G. Z is used for multi-discipline and federated containers'],
         ['Number', '4', 'Sequential within the set']],
        widths=[2.6, 3.6, 10.4])

c.h3('4.2.1  Originator register')
c.para('The originator identifies the ORGANISATION that produced the container, not the discipline. Discipline '
       'is carried separately in the role field. Where one organisation produces information for two '
       'disciplines, it uses one originator code and two role codes; where two organisations work in the same '
       'discipline, they use two originator codes. Coding originators by discipline is not permitted, because '
       'it duplicates the role field and fails the first time either case arises.')
c.table(['Organisation', 'Code', 'Status'],
        [['Symbion Consulting Group Studios', ORIGINATOR, 'Provisional — pending the register'],
         ['Architecture', '[FILL]', ''],
         ['Interiors', '[FILL]', ''],
         ['Structural engineering', '[FILL]', ''],
         ['Mechanical, electrical and plumbing', '[FILL]', ''],
         ['Fire protection', '[FILL]', ''],
         ['Low voltage and communications', '[FILL]', ''],
         ['Civil and site', '[FILL]', ''],
         ['Quantity surveying', '[FILL]', ''],
         ['Contractor', '[FILL]', 'Reserve now; allocate before Stage 3.1'],
         ['Specialist subcontractors', '[FILL]', 'Reserve a block now'],
         ['Not known or not applicable', 'ZZZ', 'Fixed']],
        widths=[7.0, 3.0, 6.6])
c.callout('**Open item.** The register is issued by the Lead Appointed Party and is not yet received. The '
          'compliance check enforces exactly three characters; a register of three-letter organisation codes '
          'therefore needs no change to the rule. **No container is to be numbered until the register is '
          'issued**, because renumbering after Deliverable A affects every issued document. The register must '
          'also state who allocates a code to a party joining mid-project, and how.', 'Open item')

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

c.h2('4.5  In-model naming')
c.para('Container naming governs what leaves a model. These conventions govern what is inside one. They are '
       'set in the issued project template; a task team does not define its own.')
c.table(['Item', 'Convention', 'Example'],
        [['Workset', '[Volume]_[Discipline] — where per-volume worksets are the adopted convention',
          'BLD2_Mechanical'],
         ['Level', 'As issued in the template; never renamed locally, because the level code appears in every '
                   'container name', 'GF, 01, RF'],
         ['Grid', 'Numeric one way, alphabetic the other, as issued', '1–24, A–P'],
         ['View — working', 'WIP_[Discipline]_[Purpose]_[Level] — not placed on sheets, may be deleted at any '
                            'time by its owner', 'WIP_M_Coordination_01'],
         ['View — issued', '[Discipline]_[Type]_[Level]_[Description] — placed on a sheet, never deleted '
                           'without checking the sheet', 'A_GA Plan_GF_Temple'],
         ['Loadable family', '[Discipline]_[Category]_[Description]', 'M_AHU_Packaged'],
         ['Family type', 'Manufacturer-neutral until LOD 350; manufacturer and model reference from LOD 400',
          '1200x600, then Daikin FXSQ'],
         ['Material', '[Class]_[Description]_[Finish]', 'Concrete_C30_Fair Face'],
         ['Sheet', 'Container name (Section 4.2); the sheet number IS the container number', 'KUT-…-SH-A-0100'],
         ['View template', 'VT_[Discipline]_[Purpose]', 'VT_A_GA Plan'],
         ['Filter', 'F_[Discipline]_[Purpose]', 'F_M_Supply Air']],
        widths=[3.2, 8.6, 4.8], font=8)
c.callout('These are proposals in this revision. Confirm against Symbion house standards before issue — where '
          'the Lead Appointed Party already has a convention its teams know, that convention should prevail. '
          'What matters is that one convention exists and is issued in the template, not which one.',
          '[CONFIRM] with the Lead Appointed Party')

c.h2('4.6  Shared parameters')
c.table(['Item', 'Arrangement'],
        [['Ownership', 'The Information Manager owns the shared parameter file. No appointed party adds, '
                       'renames or re-GUIDs a shared parameter'],
         ['Distribution', 'Issued with the project template and re-issued whenever it changes'],
         ['Requests', 'A discipline needing a new parameter requests it; it is added centrally and reissued, '
                      'so every model binds the same GUID'],
         ['Why it matters', 'A parameter added locally has a different GUID. It looks identical in the user '
                            'interface, schedules separately, and is invisible to every automated check']],
        widths=[3.4, 13.2])

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


c.h2('5.1  Folder structure')
c.para('The state is the top level; volume and discipline divide it. A container lives in exactly one place, '
       'and its location matches its name.')
c.table(['Level', 'Folders'],
        [['State', '00_WIP · 01_SHARED · 02_PUBLISHED · 03_ARCHIVED'],
         ['Within WIP', 'One folder per appointed party. A party sees only its own WIP folder'],
         ['Within SHARED and PUBLISHED', 'By volume (BLD1 … BLD6, EXT), then by information type '
                                         '(Models · Drawings · Schedules · Documents · Reports)'],
         ['Within ARCHIVED', 'Mirrors PUBLISHED, by issue date'],
         ['Outside the states', 'Incoming (survey, Appointing Party issues) and Templates (project template, '
                                'family library, title blocks, seed model)']],
        widths=[4.2, 12.4])

c.h2('5.2  Permission matrix')
c.para('Least privilege. A party that cannot publish cannot publish by accident.')
c.table(['Role', 'Own WIP', 'Other WIP', 'Shared', 'Published', 'Archived'],
        [['Task team member', 'Read / write', 'None', 'Read', 'Read', 'Read'],
         ['Task Team Manager', 'Read / write', 'None', 'Read / write', 'Read', 'Read'],
         ['Information Manager', 'Read', 'Read', 'Read / write', 'Read / write', 'Read / write'],
         ['Lead Appointed Party', 'Read', 'Read', 'Read / write', 'Read / approve', 'Read'],
         ['Appointing Party', 'None', 'None', 'Read', 'Read / accept', 'Read'],
         ['Contractor (from 3.1)', 'Read / write (own)', 'None', 'Read / write', 'Read', 'Read'],
         ['[FILL — other parties]', '', '', '', '', '']],
        widths=[4.0, 2.8, 2.4, 2.4, 2.6, 2.4], font=8)
c.callout('Nobody deletes. Superseded information moves to ARCHIVED and remains retrievable. The ability to '
          'delete is withheld from every project role and retained by the platform administrator.', 'Deletion')

c.h2('5.3  Retention and archive')
c.table(['Item', 'Arrangement'],
        [['Retention period', '[FILL — from the appointment; state the period after practical completion]'],
         ['Archive format', 'Native and PDF for documents and drawings; native and IFC for models; XLSX for '
                            'schedules and handover data'],
         ['Archive trigger', 'Each gate, and at project close'],
         ['Custody at close', '[FILL — who holds the archive, and in what environment, once the CDE '
                              'subscription ends]'],
         ['Handover of the CDE itself', '[FILL — whether the Appointing Party takes over the environment or '
                                        'receives an export]']],
        widths=[4.4, 12.2])
c.callout('Decide the exit arrangement at mobilisation, not at close-out. A CDE subscription that lapses with '
          'the project information inside it is a real and common way to lose an archive.', 'Exit')

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
c.para('The coordinate system is established once at mobilisation from the site survey and locked thereafter. '
       'It is the single setting on this project that cannot be corrected later without re-issuing every '
       'model and every drawing, so it is recorded here in full rather than by reference.')
c.table(['Item', 'Value'],
        [['Survey datum', '[FILL — vertical datum and its origin, e.g. Uganda national datum, or a stated '
                          'site benchmark with its reduced level]'],
         ['Site benchmark', '[FILL — description, location and reduced level]'],
         ['Coordinate system / projection', '[FILL — e.g. UTM Zone 36N, or a stated site grid]'],
         ['Survey point — easting, northing, elevation', '[FILL]'],
         ['Project base point — easting, northing, elevation', '[FILL]'],
         ['Angle to true north', '[FILL — degrees, and the direction of measurement]'],
         ['Internal origin offset', '[FILL — must remain within 10 km of the internal origin to avoid '
                                    'geometry and display faults]'],
         ['Units', 'Millimetres. Project units set in the template and not altered'],
         ['Source survey', '[FILL — surveyor, drawing reference, date]']],
        widths=[6.4, 10.2])
c.h3('6.3.1  How coordinates are adopted')
c.numlist([
    'The Information Manager issues a **shared coordinates seed model** containing the survey point, project '
    'base point, true north and the levels and grids. It contains no other geometry.',
    'Each appointed party starts its model from the issued project template and **acquires coordinates** from '
    'the seed model. Nobody types coordinates in.',
    'Models are linked to one another by **shared coordinates**, never by origin-to-origin or by hand '
    'positioning.',
    'The project base point and survey point are **pinned**. Unpinning either is a change requiring the '
    'Information Manager’s agreement.',
    'A model that reports a different survey point from the seed is rejected at the pre-share check, because '
    'every downstream federation and clash result would be wrong by that offset.',
])
c.callout('Do not defer this. A coordinate error found at Deliverable B costs a remodelling exercise across '
          'every discipline; found at mobilisation it costs an afternoon.', 'Sequence')

c.h2('6.4  Level and grid register')
c.para('Levels and grids are issued in the template and in the seed model. Level codes appear in every '
       'container name, so a renamed level invalidates issued references.')
c.table(['Level code', 'Level name', 'Reduced level (m)', 'Applies to volumes'],
        [['[FILL]', '[FILL]', '[FILL]', '[FILL]'],
         ['B1', 'Basement 1', '[FILL]', '[FILL]'],
         ['GF', 'Ground Floor', '[FILL]', '[FILL]'],
         ['01', 'Level 01', '[FILL]', '[FILL]'],
         ['RF', 'Roof', '[FILL]', '[FILL]']],
        widths=[2.6, 5.0, 3.6, 5.4])
c.callout('Volumes may have different floor-to-floor arrangements. Where a level exists in one volume and not '
          'another, it is still named once, project-wide, and simply not used where it does not apply. Two '
          'volumes must never use the same level code for different elevations.', 'Levels across volumes')

c.h2('6.5  Software and version register')
c.para('All appointed parties author in the same major version for the duration of a stage. A version change '
       'is made only at a stage boundary and only by agreement, because an upgraded model cannot be reopened '
       'in the earlier version.')
c.table(['Function', 'Software and version', 'Party'],
        [['Model authoring', '[FILL — one Revit version, project-wide]', 'All'],
         ['Coordination', '[FILL — Navisworks version]', 'Information Manager'],
         ['Common Data Environment', 'Autodesk Construction Cloud (evergreen)', 'All'],
         ['Design review', 'Bluebeam [FILL]', 'All'],
         ['Specifications', 'RIB SpecLink [FILL]', 'Specification lead'],
         ['FF&E and O&M', 'Fohlio (evergreen)', 'Interior Designer'],
         ['Building management', 'Tridium Niagara [FILL]', 'Controls contractor'],
         ['Analysis and calculation', '[FILL — per discipline, with the standard each is configured to]',
          'Each discipline']],
        widths=[4.0, 8.0, 4.6])

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

c.h2('7.2  Model breakdown and federation')
c.para('How the project is divided into models, and how they are recombined. The division follows the volumes '
       'in Section 1.3 and the disciplines in Section 4.2, so that a model can always be identified from its '
       'container name alone.')
c.table(['Item', 'Arrangement'],
        [['Division rule', 'One model per volume per discipline. A discipline covering several volumes '
                           'produces several models, not one large one'],
         ['Naming', 'Per Section 4.2; the volume and role fields identify the model'],
         ['Maximum file size', '[FILL — a working ceiling, typically 300 MB. State the action when exceeded: '
                               'split by level, or by system]'],
         ['Linking', 'By shared coordinates, pinned, never bound into the host'],
         ['Site and shared model', 'The seed model (Section 6.3) carries survey point, levels and grids. '
                                   'Every model links it; nothing else is modelled in it'],
         ['Federated model', 'Assembled by the Information Manager each cycle from the shared models. It is '
                             'a derived container and is never authored in'],
         ['Federation container', ref('M3-Z-0001')],
         ['Cross-discipline hosting', 'An element is hosted in the model of the discipline responsible for it. '
                                      'A discipline does not host elements in another discipline’s model'],
         ['Copy/monitor', '[FILL — state which elements are copy/monitored between models: levels and grids '
                          'always; state whether structural elements are]']],
        widths=[3.8, 12.8])
c.callout('The largest cause of unusable federated models is a discipline modelling outside its own container '
          '— a mechanical contractor placing builders-work openings in the structural model, for example. '
          'Where an element crosses responsibility, the owning discipline models it and the other coordinates '
          'against it.', 'Ownership of elements')

c.h2('7.3  Clash management')
c.table(['Item', 'Arrangement'],
        [['Tools', 'ACC Model Coordination is the system of record. Coordination issues are raised as ACC '
                   'Issues and exchanged in BCF format'],
         ['Clash matrix', 'Section 7.3.1'],
         ['Priorities', 'P1 critical, P2 major, P3 minor, P4 observation, as defined in the Project Delivery '
                        'Playbook'],
         ['Clearances and tolerances', 'Section 7.3.2'],
         ['Grouping', 'Clashes are grouped so that each issue represents one physical condition rather than '
                      'one geometric intersection'],
         ['Acceptance', 'No unresolved high priority clashes at any data drop'],
         ['Reporting', 'A coordination report is issued 48 hours before each coordination meeting']],
        widths=[3.8, 12.8])

c.h3('7.3.1  Clash matrix')
c.para('Which discipline model is tested against which. **T** = tested every cycle · **G** = tested at gates '
       'only · **–** = not tested. Complete at the mobilisation coordination workshop.')
c.table(['', 'Arch', 'Struct', 'Mech', 'Elec', 'PH', 'Fire', 'LV', 'Civil'],
        [['Architecture', '–', 'T', 'T', 'T', 'T', 'T', 'G', 'G'],
         ['Structure', '', '–', 'T', 'T', 'T', 'T', 'G', 'T'],
         ['Mechanical', '', '', '–', 'T', 'T', 'T', 'G', '–'],
         ['Electrical', '', '', '', '–', 'T', 'T', 'T', '–'],
         ['Public health', '', '', '', '', '–', 'T', '–', 'T'],
         ['Fire protection', '', '', '', '', '', '–', 'G', '–'],
         ['Low voltage', '', '', '', '', '', '', '–', '–'],
         ['Civil and site', '', '', '', '', '', '', '', '–']],
        widths=[3.4, 1.7, 1.7, 1.7, 1.7, 1.7, 1.7, 1.6, 1.7], font=8,
        caption='Proposed. [CONFIRM] at the mobilisation coordination workshop — the disciplines decide what '
                'is worth testing, not the Information Manager.')

c.h3('7.3.2  Clearances and tolerances')
c.para('Clash detection needs two numbers per interface: the overlap that counts as a hard clash, and the '
       'clear space that must exist even where nothing collides. The second is the one that protects '
       'maintainability, and geometry alone will never reveal it.')
c.table(['Interface', 'Hard clash tolerance', 'Required clearance', 'Set by'],
        [['Structure to MEP', '[FILL — typically 0 mm]', '[FILL]', 'Structural and MEP leads'],
         ['MEP to MEP', '[FILL]', '[FILL]', 'MEP lead'],
         ['MEP to architecture (ceiling void, risers)', '[FILL]', '[FILL]', 'Architecture and MEP leads'],
         ['Maintenance access to plant', 'n/a', '[FILL — manufacturer requirement, with a stated minimum]',
          'MEP lead, against manufacturer data'],
         ['Access for replacement (largest removable part)', 'n/a', '[FILL]', 'MEP lead'],
         ['Fire compartment penetrations', '[FILL]', '[FILL]', 'Fire and MEP leads'],
         ['Structural zone reserved in ceiling void', 'n/a', '[FILL]', 'Structural lead'],
         ['Services zoning in ceiling void', 'n/a', 'Per the agreed services zoning drawing [FILL — reference]',
          'MEP lead'],
         ['Site services to structures and trees', '[FILL]', '[FILL]', 'Civil lead']],
        widths=[5.0, 3.4, 4.4, 3.8], font=8)
c.callout('**Open item, with a deadline.** These are engineering judgements with cost consequences — too '
          'tight and the building cannot be maintained, too generous and ceiling void and floor-to-floor '
          'height are lost. They must be agreed before the first coordination cycle of Stage 2.2. Until they '
          'are, the clash run reports geometric collisions only and nothing about access or maintainability '
          'is being checked at all.', 'Required before Stage 2.2')

c.h2('7.4  Meetings')
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


c.h2('8.1  Information classification and access')
c.para('Not all project information carries the same sensitivity. Classify it once, and let the classification '
       'drive access rather than deciding case by case.')
c.table(['Classification', 'Examples', 'Who may access', 'Handling'],
        [['Open', 'General arrangement drawings, room schedules', 'All appointed parties',
          'Normal CDE handling'],
         ['Restricted', 'Security system layouts, access control, CCTV coverage, alarm zoning',
          '[FILL — named individuals only]', 'Separate CDE folder with explicit permissions; not included in '
          'the general federated model'],
         ['Confidential', '[FILL — identify: sacred or ritual spaces, and any area the Appointing Party '
                          'designates]', '[FILL]', '[FILL — state whether these are modelled at all, and by whom]'],
         ['Personal data', 'Team contact details, site personnel records',
          'Information Manager and Lead Appointed Party', 'Not published; retained only as long as needed']],
        widths=[2.8, 5.0, 4.0, 4.8], font=8)
c.callout('**A temple carries sensitivities beyond the usual.** The Appointing Party must state which spaces '
          'and systems are restricted before modelling begins, because retrofitting a restriction to '
          'information already federated and shared is not achievable. This is an [FILL] that should be closed '
          'at the first Appointing Party meeting.', 'To be established before modelling')

c.h2('8.2  Publication and personal use')
c.bullet([
    'Project information — including images, plans, renders and visualisations — must not be published, '
    'circulated outside the project, or used in promotional or portfolio material without the written '
    'permission of the Appointing Party.',
    'This applies during and after the appointment, and to individuals as well as organisations.',
    'Site photography is [FILL — permitted / restricted / prohibited], per the Appointing Party.',
    'Breaches are reported to the Lead Appointed Party within [FILL] hours of discovery.',
])

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
         ['Demolition classification (Division 02)', 'Complete — see Section 10.4',
          'Before Deliverable B and before each specification reconciliation'],
         ['Asset data capture', 'On programme against the capture plan', 'Monthly from Stage 3.1']],
        widths=[5.4, 6.6, 4.6])


c.h2('10.2  Model health thresholds')
c.para('"Within tolerance" means nothing without numbers. These are the thresholds a model is measured '
       'against at each share; a model outside them is not rejected automatically, but the exceedance is '
       'reported and the Task Team Manager states the remedy.')
c.table(['Metric', 'Threshold', 'Measured'],
        [['File size', '[FILL — working ceiling, typically 300 MB]', 'Every share'],
         ['Revit warnings — total', '[FILL — typically 500]', 'Every share'],
         ['Revit warnings — critical', '0 at a gate', 'Every share, enforced at gates'],
         ['Unplaced rooms', '0 at a gate', 'Every share'],
         ['Rooms not enclosed / redundant', '0 at a gate', 'Every share'],
         ['Duplicate elements in the same place', '0', 'Every cycle'],
         ['Elements outside the model extents', '0 — a stray element inflates extents and slows every open',
          'Every share'],
         ['In-place families', '[FILL — a stated maximum; they do not schedule reliably]', 'Every share'],
         ['Imported CAD instances in the model', '0 in a shared model; underlays are linked, not imported',
          'Every share'],
         ['Unused / unpurged content', 'Purged before every share', 'Every share'],
         ['Model open time', '[FILL — a stated ceiling, as an early warning of a model becoming unworkable]',
          'Monthly'],
         ['Workset count and naming', 'Per Section 4.5', 'Every share']],
        widths=[5.6, 6.6, 4.4], font=8)
c.callout('Thresholds are a conversation trigger, not an automatic rejection. A model 20 per cent over the '
          'size ceiling because a volume is genuinely large is fine and stated; the same model at three times '
          'the ceiling because nothing has been purged since Deliverable A is not.', 'How thresholds are used')

c.h2('10.3  Reporting of scope')
c.callout('Every validation report states the number of elements examined. A percentage expressed without a '
          'population is not evidence of compliance. Where a check does not apply to a category, the report '
          'discloses the categories excluded from the run.', 'Evidence')

c.h2('10.4  Manual classification of demolition')
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

c.h2('12.1  Capability and capacity assessment')
c.para('BS EN ISO 19650-2 expects each prospective appointed party to be assessed for capability and capacity '
       'before appointment, and the assessment to be revisited when the team changes. It is recorded here so '
       'that a shortfall is addressed by training or resourcing at mobilisation rather than discovered at the '
       'first data drop.')
c.table(['Assessed', 'Evidence sought', 'Party', 'Outcome'],
        [['Authoring capability', 'Competence in the agreed software version; a sample model to the project '
                                  'conventions', '[FILL]', '[FILL]'],
         ['Capacity', 'Named individuals, allocated time, and cover for absence', '[FILL]', '[FILL]'],
         ['Information management competence', 'Understanding of the CDE, suitability codes and the '
                                               'pre-share check', '[FILL]', '[FILL]'],
         ['IT capability', 'Hardware, network and CDE access adequate for the model sizes in Section 7.2',
          '[FILL]', '[FILL]'],
         ['Security', 'Acceptance of the security-minded requirements in Section 8', '[FILL]', '[FILL]']],
        widths=[4.0, 5.8, 3.2, 3.6], font=8)
c.callout('The Stage 0 exit gate — every appointed party produces a test model from the issued template and '
          'shares it once — is the practical form of this assessment. A party that cannot complete it has a '
          'capability or capacity gap that must be closed before Deliverable A, not during it.',
          'How capability is actually demonstrated')

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
         ['Demolition classification omitted (Section 10.4)', 'Medium',
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


c.h1('15  Mobilisation checklist')
c.para('Everything that must exist before the project can proceed, and the order it must exist in. An item '
       'marked OPEN is a live dependency: work downstream of it is either blocked or is being done on an '
       'assumption that may have to be undone.')

c.h2('15.1  Before the kickoff')
c.table(['#', 'Item', 'Reference', 'Owner', 'Status'],
        [['1', 'Exchange information requirements received', 'Section 3.1', 'Appointing Party', '[OPEN]'],
         ['2', 'Originator register issued', 'Section 4.2.1', 'Lead Appointed Party', '[OPEN]'],
         ['3', 'Standards hierarchy confirmed', 'Section 4.1.1', 'Lead Appointed Party', '[OPEN]'],
         ['4', 'Coordinate system established and seed model issued', 'Section 6.3', 'Information Manager',
          '[OPEN — needs the survey]'],
         ['5', 'Level and grid register agreed', 'Section 6.4', 'Lead Appointed Party', '[OPEN]'],
         ['6', 'Software versions agreed and recorded', 'Section 6.5', 'All parties', '[OPEN]'],
         ['7', 'Volume and workset convention decided', 'Sections 1.3 and 4.5', 'Information Manager',
          '[OPEN]'],
         ['8', 'Project template, family library and title blocks issued', 'Section 4.4',
          'Information Manager', '[OPEN]'],
         ['9', 'CDE provisioned, folders created, permissions applied', 'Sections 5.1 and 5.2',
          'Information Manager', '[OPEN]'],
         ['10', 'Restricted information classified', 'Section 8.1', 'Appointing Party', '[OPEN]'],
         ['11', 'This plan and the Project Delivery Playbook issued', '—', 'Information Manager', '[OPEN]']],
        widths=[1.0, 6.6, 3.4, 3.4, 2.2], font=8)

c.h2('15.2  At or immediately after the kickoff')
c.table(['#', 'Item', 'Reference', 'Owner', 'Status'],
        [['12', 'Clash matrix agreed', 'Section 7.3.1', 'All disciplines', '[OPEN]'],
         ['13', 'Clearances and tolerances agreed', 'Section 7.3.2', 'MEP and structural leads', '[OPEN]'],
         ['14', 'Model health thresholds set', 'Section 10.2', 'Information Manager', '[OPEN]'],
         ['15', 'TIDPs returned by every appointed party', 'Section 9', 'Task Team Managers', '[OPEN]'],
         ['16', 'MIDP baselined from the TIDPs', 'Section 9', 'Information Manager', '[OPEN]'],
         ['17', 'Capability and capacity assessed', 'Section 12.1', 'Lead Appointed Party', '[OPEN]'],
         ['18', 'Standards reconciliation schedule opened', 'Section 4.1.3', 'All disciplines', '[OPEN]']],
        widths=[1.0, 6.6, 3.4, 3.4, 2.2], font=8)

c.h2('15.3  Before the first coordination share')
c.table(['#', 'Item', 'Reference', 'Owner', 'Status'],
        [['19', 'Every party has produced a test model and shared it once', 'Stage 0 gate', 'Task teams',
          '[OPEN]'],
         ['20', 'Every model verified against the seed coordinates', 'Section 6.3.1', 'Information Manager',
          '[OPEN]'],
         ['21', 'IFC export configuration issued', 'Section 6.2', 'Information Manager', '[OPEN]'],
         ['22', 'Rooms placed in every architectural model', 'Section 4.4', 'Architecture', '[OPEN]'],
         ['23', 'Shared parameter file issued and bound', 'Section 4.6', 'Information Manager', '[OPEN]']],
        widths=[1.0, 6.6, 3.4, 3.4, 2.2], font=8)

c.h2('15.4  Before Technical Design (Stage 2.3)')
c.table(['#', 'Item', 'Reference', 'Owner', 'Status'],
        [['24', 'Standards reconciliation schedule CLOSED', 'Section 4.1.3', 'All disciplines',
          '[OPEN — blocks detailed calculation]'],
         ['25', 'Product equivalence route agreed', 'Section 4.1.3 row SR-10', 'Lead Appointed Party',
          '[OPEN]'],
         ['26', 'Asset data capture method agreed with the contractor', 'Section 14', 'Contractor',
          '[OPEN — Stage 3.1]']],
        widths=[1.0, 6.6, 3.4, 3.4, 2.2], font=8)
c.callout('Items 1 to 11 gate the kickoff itself. Attempting to train a team on standards that are not yet '
          'decided produces a team that has to be retrained, and models that have to be redone.',
          'Sequence matters')

c.h1('16  Appendices')
c.table(['Appendix', 'Content', 'Status'],
        [['A', 'Exchange information requirements (Appointing Party)', 'Attached / referenced'],
         ['B', 'Master Information Delivery Plan', 'Attached'],
         ['C', 'Task Information Delivery Plans, per appointed party', 'Attached'],
         ['D', 'Responsibility matrix, expanded', 'Attached'],
         ['E', 'Clash matrix', 'Attached'],
         ['F', 'Project Delivery Playbook (' + ref('RP-Z-0002') + ')', 'Issued separately'],
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
