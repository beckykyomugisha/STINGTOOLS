# -*- coding: utf-8 -*-
"""Build the mobilisation information request to the Lead Appointed Party.

Everything asked for here is an item from Section 15.1 of the BIM Execution Plan
— the things that gate the kickoff. One document rather than a series of emails,
so the answers arrive together and the dependencies between them are visible.

House style from tools/corporate_docx.py, so it matches the issued pack.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from corporate_docx import CorporateDoc  # noqa: E402
import kut_naming as N  # noqa: E402

OUT = 'KUT_Mobilisation_Information_Request.docx'
ORIGINATOR = 'SMB'

c = CorporateDoc()

c.title_page(
    title='Mobilisation Information Request',
    eyebrow='Kampala Uganda Temple',
    strapline='Information required from the Lead Appointed Party and the Appointing Party '
              'before mobilisation can complete',
    control_rows=[
        ('Document reference', 'KUT-%s-ZZ-ZZ-RP-Z-0004' % ORIGINATOR),
        ('Revision', 'P01'),
        ('Status / suitability', 'S3 — for review and response'),
        ('From', 'Mayanja Davis, Information Management'),
        ('To', '[FILL — name], Symbion Consulting Group Studios (Lead Appointed Party)'),
        ('Copied to', '[FILL]'),
        ('Date', '[FILL]'),
        ('Response requested by', '[FILL — suggest five working days]'),
    ],
    note='Issued through the Common Data Environment. Uncontrolled when printed.')

c.footer('KUT Mobilisation Information Request   |   Rev P01')

# ── 1 ───────────────────────────────────────────────────────────────────────
c.h1('1  Purpose', page_break=False)
c.para('The mobilisation set is drafted and ready to issue: the BIM Execution Plan, the Project Delivery '
       'Playbook, the Document Control Standard and the Master Information Delivery Plan. Eleven items in '
       'Section 15.1 of the plan must be settled before the kickoff, because they determine the project '
       'template every appointed party starts from. Training a team on standards that are not yet decided '
       'produces a team that has to be retrained, and models that have to be redone.')
c.para('Those eleven items are asked for here as nine requests — several of them are answered by the same '
       'document, and asking twice for one answer wastes your time. Section 5 lists the nine and what each '
       'unblocks. Where an answer sits with the Appointing Party rather than the Lead Appointed Party, it '
       'says so; nothing here is a decision for information management to take alone.')
c.callout('Two items have consequences that cannot be undone later and are marked accordingly: the site '
          'survey, which sets the coordinate system, and the classification of restricted information. Both '
          'are dealt with in Section 2.', 'Two items that cannot be corrected later')

# ── 2 ───────────────────────────────────────────────────────────────────────
c.h1('2  Items that cannot be corrected later')

c.h2('2.1  Site survey and coordinate system')
c.para('The shared coordinate system is established once from the site survey and is then locked. It is the '
       'only setting on this project that cannot be corrected without re-issuing every model and every '
       'drawing produced up to that point.')
c.table(['Required', 'Detail'],
        [['Site survey', 'Surveyor’s drawing and data, with the vertical datum and the site benchmark stated'],
         ['Coordinate system', 'The projection or site grid to be used'],
         ['Setting-out point', 'The agreed project base point, with easting, northing and elevation'],
         ['True north', 'Angle and the direction in which it is measured']],
        widths=[4.4, 12.2])
c.para('On receipt, a shared coordinates seed model is issued to every appointed party, and all models '
       'acquire their coordinates from it rather than having them entered by hand.')
c.callout('**This is the longest lead item in mobilisation.** If the survey is not yet commissioned, that is '
          'the critical path and should be treated ahead of everything else in this request. Modelling can '
          'begin without several of the items below; it cannot sensibly begin without this one.', 'Priority')

c.h2('2.2  Classification of restricted information')
c.para('The project is a temple, and some spaces and systems will carry sensitivities that ordinary project '
       'information does not. Access restrictions must be applied before information is created and '
       'federated, because a restriction cannot be applied retrospectively to information already shared '
       'across the team.')
c.table(['Required from the Appointing Party', 'Detail'],
        [['Restricted spaces', 'Which spaces, if any, are restricted, and to whom'],
         ['Restricted systems', 'Whether security, access control and CCTV information is to be separated '
                                'from the general federated model'],
         ['Modelling extent', 'Whether any space is to be modelled in outline only, or not at all, and by whom'],
         ['Publication', 'Whether site photography and the use of project imagery are permitted, restricted '
                         'or prohibited'],
         ['Confidentiality', 'Any non-disclosure requirements to be flowed down to appointed parties']],
        widths=[5.0, 11.6])
c.callout('If the Appointing Party has not yet issued a position, an interim instruction is enough to start: '
          'name the categories that are to be treated as restricted, and the detail can follow.',
          'An interim answer is sufficient')

# ── 3 ───────────────────────────────────────────────────────────────────────
c.h1('3  Items required before the kickoff')

c.h2('3.1  Originator code register')
c.para('The originator identifies the organisation that produced a container. It is the second field of every '
       'file, model, drawing and document name on the project, so no container can be numbered until the '
       'register exists.')
c.table(['Required', 'Detail'],
        [['The register', 'A three-character code for every appointed party, including sub-consultants'],
         ['Reserved codes', 'Codes reserved now for the contractor and specialist subcontractors, so that '
                            'Stage 3.1 does not reopen the numbering'],
         ['Allocation', 'Who allocates a code to a party joining mid-project, and how'],
         ['Confirmation', 'That three characters is the convention. The compliance check enforces exactly '
                          'three; a register of three-letter organisation codes therefore needs no change']],
        widths=[3.6, 13.0])
c.callout('**The originator is the organisation, not the discipline.** Where one organisation produces '
          'information for two disciplines it uses one originator code and two role codes; where two '
          'organisations work in the same discipline they use two originator codes. Coding originators by '
          'discipline duplicates the role field and fails the first time either case arises. `%s` has been '
          'used provisionally for information management pending the register.' % ORIGINATOR,
          'A point worth confirming')

c.h2('3.2  Standards hierarchy')
c.para('The Appointing Party standards package is derived from United States practice; the building is '
       'constructed in Uganda under Ugandan law. Three layers therefore apply and their precedence must be '
       'stated before any discipline begins calculation.')
c.table(['Required', 'Detail'],
        [['Confirmation of precedence', 'That statutory Ugandan requirements are the floor and cannot be '
                                        'waived; that an Appointing Party standard governs where it is '
                                        'stricter; and that a genuine conflict requires a written derogation'],
         ['Derogation authority', 'Who approves a derogation where an Appointing Party standard conflicts '
                                  'with Ugandan statute, and by what route'],
         ['Applicable statutory list', 'Confirmation of the statutory instruments that apply, so the '
                                       'reconciliation schedule is complete'],
         ['Exchange information requirements', 'The Appointing Party’s requirements document, if it has been '
                                               'issued and not yet passed on']],
        widths=[4.4, 12.2])
c.para('Section 4.1.3 of the BIM Execution Plan opens a reconciliation schedule with twelve rows already '
       'identified. The electrical row is the one that cannot be resolved by preference: Uganda operates at '
       '240 V single phase and 415 V three phase at 50 Hz, with BS 7671 protection practice, while the '
       'United States basis assumes 120/208 V at 60 Hz with different conductor sizing. Confirmation that '
       'the local standard governs for electrical design would close that row immediately.')

c.h2('3.3  Level and grid register')
c.table(['Required', 'Detail'],
        [['Levels', 'Level names and reduced levels, and which volumes each applies to'],
         ['Grids', 'The grid naming convention'],
         ['Confirmation', 'That the volumes are correct and complete: '
                          + ', '.join(v for _n, _c, v, _a in N.VOLUMES if _c != 'ZZ')]],
        widths=[3.6, 13.0])
c.para('Level codes appear in every container name, so a level renamed after issue invalidates every '
       'reference to it.')

c.h2('3.4  Software and versions')
c.table(['Required', 'Detail'],
        [['Authoring version', 'One Revit version for the whole project, for the duration of a stage. An '
                               'upgraded model cannot be reopened in the earlier version'],
         ['Coordination', 'The Navisworks version to be used'],
         ['Other platforms', 'Versions for review, specification and building management platforms'],
         ['Confirmation', 'That every appointed party can meet the agreed versions']],
        widths=[3.6, 13.0])

c.h2('3.5  Symbion standards and this role')
c.table(['Required', 'Detail'],
        [['House standards', 'Whether Symbion has existing BIM standards, templates or naming conventions '
                             'that should prevail over the drafts in the issued pack. Where a convention '
                             'already exists and its teams know it, that convention should be adopted'],
         ['Role and title', 'The role title and organisation to appear on the issued documents, and the '
                            'reporting line'],
         ['Authority', 'Confirmation of the authority to return information that does not meet the '
                       'requirements, and the escalation route where a party disputes it']],
        widths=[3.6, 13.0])

# ── 4 ───────────────────────────────────────────────────────────────────────
c.h1('4  Items for the kickoff workshop')
c.para('These do not block the kickoff; they are settled during it, and are listed so the right people attend '
       'and arrive prepared.')
c.table(['Item', 'Who must be present', 'Reference'],
        [['Clash matrix — which discipline models are tested against which', 'All discipline leads',
          'BEP 7.3.1'],
         ['Clash tolerances and clearances, including maintenance access',
          'MEP and structural leads, with manufacturer data for major plant', 'BEP 7.3.2'],
         ['Volume and workset convention — per-volume worksets, or one model per volume',
          'All discipline leads', 'BEP 4.5'],
         ['Model health thresholds — file size, warning counts', 'All discipline leads', 'BEP 10.2'],
         ['Task Information Delivery Plans — returns and dates', 'Task Team Managers', 'BEP 9'],
         ['Capability and capacity — confirmation of named individuals and time', 'Task Team Managers',
          'BEP 12.1'],
         ['Sheet number banding — the first digit of a sheet number carries meaning. Three schemes were in '
          'use across earlier drafts and one must be adopted', 'All discipline leads',
          'Document Control Standard 3'],
         ['FF&E catalogue scope — which categories are procured and specified through the catalogue, which '
          'sets what must be linked at handover', 'Interior designer, Appointing Party', 'BEP 15.2 item 18a']],
        widths=[7.4, 6.0, 3.2])
c.callout('**No sheet numbers can be allocated until the banding is settled.** A sheet renumbered after '
          'issue invalidates every drawing reference, every transmittal and every register row that names '
          'it. It is a five-minute decision at the kickoff and an expensive one afterwards.',
          'One that blocks drawing production')
c.callout('The clash tolerances are the item most often deferred and most expensive to defer. Until they are '
          'agreed, clash detection reports geometric collisions only, and nothing about access or '
          'maintainability is being checked at all. They are required before the first coordination cycle of '
          'Stage 2.2.', 'One to settle in the room')

# ── 5 ───────────────────────────────────────────────────────────────────────
c.h1('5  Summary and response')
c.table(['#', 'Item', 'From', 'Blocks', 'Received'],
        [['1', 'Site survey and coordinate system', 'Lead Appointed Party',
          'All modelling. Longest lead item', '[  ]'],
         ['2', 'Restricted information classification', 'Appointing Party',
          'Modelling of the affected areas', '[  ]'],
         ['3', 'Originator code register', 'Lead Appointed Party',
          'All container numbering', '[  ]'],
         ['4', 'Standards hierarchy and derogation authority', 'Lead / Appointing Party',
          'All discipline calculation', '[  ]'],
         ['5', 'Level and grid register', 'Lead Appointed Party', 'Project template, container numbering',
          '[  ]'],
         ['6', 'Software and versions', 'All parties', 'Project template', '[  ]'],
         ['7', 'Symbion house standards', 'Lead Appointed Party', 'Project template and the issued pack',
          '[  ]'],
         ['8', 'Role title and authority', 'Lead Appointed Party', 'Issue of the pack', '[  ]'],
         ['9', 'Exchange information requirements', 'Appointing Party',
          'Confirmation that the pack responds to them', '[  ]']],
        widths=[1.0, 6.0, 3.4, 4.4, 1.8], font=8)
c.para('An interim or partial answer is more useful than a complete one that arrives later. Items 1, 2 and 3 '
       'are the ones with consequences that compound: the survey because everything is positioned from it, '
       'the classification because it cannot be applied retrospectively, and the register because every '
       'container carries it.')

c.end_mark()

c.properties(
    title='KUT Mobilisation Information Request',
    subject='Kampala Uganda Temple — information required to complete mobilisation',
    category='Project correspondence',
    comments='Rev P01. Issued through the Common Data Environment. Uncontrolled when printed.',
    author='Symbion Consulting Group Studios')

c.save(OUT, generator='tools/build_symbion_request.py')
print('saved:', OUT)
