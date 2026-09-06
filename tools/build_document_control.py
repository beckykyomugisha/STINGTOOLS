# -*- coding: utf-8 -*-
"""Build the KUT Document Control Standard as a formatted corporate .docx.

WHY THIS DOCUMENT EXISTS
The register schedules it (Z-022) and nothing produced it. A June version exists
outside this repository, written before the numbering was settled: it assumes
four-character originators and alphabetic volume codes, so following it produces
container names the compliance check rejects.

It also has no rule for the things that only come up once information is moving:
what happens to a number when a drawing is cancelled, how a superseded revision
is marked, who may authorise A1, and whether a retired number is ever reused.
Those gaps do not bite at mobilisation. They bite at the first cancelled drawing,
by which time the answer has been improvised differently by three people.

WHAT IT IS NOT
It is not a second naming convention. Every code table here renders from
tools/kut_naming.py, the same source BEP 4.2 renders from, because the pack has
already been through the failure where the plan and the standard each held their
own copy and both were internally consistent. This document adds PROCEDURE --
issue, revision, notice, retirement, authority -- and states the codes only so a
drafter does not need two documents open.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from corporate_docx import CorporateDoc  # noqa: E402
import kut_naming as N  # noqa: E402

OUT = 'KUT_Document_Control_Standard.docx'
ORIGINATOR = N.EXAMPLE_ORIGINATOR

c = CorporateDoc()

c.title_page(
    title='Document Control Standard',
    eyebrow='Kampala Uganda Temple',
    strapline='Numbering, revision, issue and retirement of project information',
    control_rows=[
        ('Document reference', 'KUT-%s-ZZ-ZZ-RP-Z-0003' % ORIGINATOR),
        ('Revision', 'P01'),
        ('Status / suitability', 'A1 — Authorised for use'),
        ('Prepared by', 'Symbion Consulting Group Studios'),
        ('Role', 'Information Manager'),
        ('Date of issue', '[FILL]'),
    ],
    note='Supersedes the earlier Document Control Standard and Numbering Convention issued before the '
         'numbering was settled. Issued through the Common Data Environment. Uncontrolled when printed.')

c.footer('KUT Document Control Standard   |   Rev P01')

# ── document control ────────────────────────────────────────────────────────
c.h1('Document control')

c.h2('Revision history')
c.table(['Rev', 'Date', 'Prepared', 'Checked', 'Summary of change'],
        [['P01', '[FILL]', '[FILL]', '[FILL]',
          'First issue. Supersedes the earlier standard and numbering convention'],
         ['', '', '', '', ''],
         ['', '', '', '', '']],
        widths=[1.4, 2.4, 2.6, 2.6, 7.6])

c.h2('Status of this document')
c.para('This standard sets out how project information is numbered, revised, issued, superseded and '
       'retired. It applies to every container produced by every appointed party, for the full duration '
       'of the appointment.')
c.para('The BIM Execution Plan is the contractual statement of the information requirements, and Section 4.2 '
       'of that plan states the container naming convention. This standard states the same convention and '
       'adds the procedure that surrounds it. Where the two differ, the BIM Execution Plan takes precedence '
       'and this standard is corrected at the next revision.')
c.callout('Two earlier documents are superseded by this one: the Document Control Standard and the Drawing '
          'and Document Numbering Convention issued before the numbering was settled. Both assume a '
          'four-character originator code and alphabetic volume codes. Container names built to either will '
          'be rejected by the compliance check. Withdraw any local copies.', 'Superseded documents')

c.h2('Contents')
c.table(['Section', 'Title'],
        [['1', 'Scope and principles'],
         ['2', 'Container naming'],
         ['3', 'Sheet numbering'],
         ['4', 'Revision'],
         ['5', 'Suitability and authorisation'],
         ['6', 'Issue and transmittal'],
         ['7', 'Superseding, cancelling and replacing'],
         ['8', 'Number retirement and reuse'],
         ['9', 'The register'],
         ['10', 'Non-conformity']],
        widths=[2.6, 14.0], font=9)

# ── 1 ───────────────────────────────────────────────────────────────────────
c.h1('1  Scope and principles')
c.para('Every piece of information produced for this project is a container: a model, a drawing, a sheet, a '
       'schedule, a report, a specification, a calculation or a notice. Every container has a name, a '
       'revision and a suitability, and appears in the register.')
c.numlist([
    '**A container is identified by its name, not by its file name on a local machine.** The name is the '
    'same in the register, on the sheet, in the transmittal and in the model.',
    '**Nothing is issued outside the Common Data Environment.** Information sent by email, messaging '
    'application or portable media has not been issued and carries no status.',
    '**A number is allocated once and is never reused.** A cancelled drawing keeps its number for the life '
    'of the project; the number is retired, not recycled.',
    '**Revision and suitability are different things.** The revision says which version this is. The '
    'suitability says what it may be used for. Both change, and they change for different reasons.',
])

# ── 2 ───────────────────────────────────────────────────────────────────────
c.h1('2  Container naming')
c.para('Every container name has seven fields, separated by hyphens, in this order.')
c.mono(N.example_name())
c.table(['Field', 'Length', 'Permitted values'],
        N.container_fields('%d' % N.ORIGINATOR_LENGTH),
        widths=[2.6, 3.6, 10.4])
c.callout('The originator code register has not yet been issued by the Lead Appointed Party. `%s` is used '
          'provisionally for information management. **No container is to be numbered until the register '
          'is issued** — renumbering after Deliverable A affects every issued document, every transmittal '
          'and every reference in a report.' % ORIGINATOR, 'Open item')

c.h2('2.1  Volumes')
c.table(['Numbering value', 'Volume code', 'Volume', 'Gross internal area'],
        [[num, code, name, ('%s m²' % format(area, ',')) if area else '—']
         for num, code, name, area in N.VOLUMES],
        widths=[3.2, 3.0, 6.4, 4.0])
c.para('The numbering value is the field that appears in a container name. The volume code is the location '
       'token carried on an ELEMENT, which is a different string with a different grammar; the two are '
       'related here so they are not confused.')

c.h2('2.2  Levels')
c.table(['Code', 'Level'], [[c_, m] for c_, m in N.LEVELS], widths=[3.0, 13.6])
c.para('A container covering more than one level takes ZZ. A container to which level does not apply — a '
       'report, a specification, a schedule — takes XX.')

c.h2('2.3  Types')
c.table(['Code', 'Type', 'Code', 'Type'],
        [[N.TYPES[i][0], N.TYPES[i][1],
          N.TYPES[i + (len(N.TYPES) + 1) // 2][0] if i + (len(N.TYPES) + 1) // 2 < len(N.TYPES) else '',
          N.TYPES[i + (len(N.TYPES) + 1) // 2][1] if i + (len(N.TYPES) + 1) // 2 < len(N.TYPES) else '']
         for i in range((len(N.TYPES) + 1) // 2)],
        widths=[2.0, 6.3, 2.0, 6.3])

c.h2('2.4  Roles')
c.table(['Code', 'Role'], [[c_, m] for c_, m in N.ROLES], widths=[3.0, 13.6])
c.para('Interiors is carried under A, and lighting under E. Cost information is issued under %s with the '
       'quantity surveyor as originator.' % N.CONTAINER_ONLY_ROLES[0][0])
c.table(['Code', 'Role'], [[c_, m] for c_, m in N.CONTAINER_ONLY_ROLES], widths=[3.0, 13.6])
c.callout('The role field of a container name and the discipline code carried on an element are separate. '
          'Container roles include %s for federated and multi-discipline containers; element discipline '
          'codes are limited to the eight above and are validated on every element. A container produced by '
          'one organisation for two disciplines takes one originator code and two role codes.'
          % N.CONTAINER_ONLY_ROLES[0][0], 'Roles and disciplines are not the same field')

# ── 3 ───────────────────────────────────────────────────────────────────────
c.h1('3  Sheet numbering')
c.para('The Number field of a SHEET is banded: its first digit says what kind of drawing it is, and the '
       'remaining three are sequential within that band. Every other container type takes a plain sequence '
       'from 0001.')
c.table(['Band', 'Content'], [[b, m] for b, m in N.SHEET_BANDS], widths=[2.6, 14.0])
c.para('Banding is used because a drawing set grows in the middle. A section added after issue takes the '
       'next free number in the 3xxx band; under a plain sequence it would either take a number far from '
       'its neighbours or force everything after it to be renumbered, and a renumbered sheet invalidates '
       'every reference to it.')
if not N.SHEET_BANDS_CONFIRMED:
    c.callout('**This scheme is proposed, not confirmed.** Three numbering schemes were in use across the '
              'earlier documents. This one is carried forward for decision at the mobilisation kickoff '
              '(BIM Execution Plan, Section 15.2). Until it is confirmed, no sheet numbers are to be '
              'allocated — a sheet renumbered after issue invalidates every drawing reference, every '
              'transmittal and every register row that names it.', 'Decision required at kickoff')

# ── 4 ───────────────────────────────────────────────────────────────────────
c.h1('4  Revision')
c.table(['Series', 'Meaning', 'When'],
        [['P01, P02, P03 …', 'Preliminary', 'Every revision before the information becomes contractual'],
         ['C01, C02, C03 …', 'Contractual', 'From the first issue at A1 onward']],
        widths=[3.6, 4.0, 9.0])
c.table(['Rule', 'Requirement'],
        [['Increment', 'Every issue increments the revision. A container is never reissued at the same '
                       'revision with different content'],
         ['Reset', 'The revision does not reset when the series changes. The first contractual revision '
                   'following P04 is C01, and P04 remains in the register'],
         ['Content', 'A revision records what changed and why, in the revision panel and in the register'],
         ['Model revisions', 'A model carries the revision of the data drop it was published in'],
         ['Withdrawn', 'A revision issued in error is withdrawn by a notice and the next revision issued. '
                       'It is not deleted and the number is not reused']],
        widths=[3.6, 13.0])

# ── 5 ───────────────────────────────────────────────────────────────────────
c.h1('5  Suitability and authorisation')
c.table(['Code', 'Meaning', 'CDE state'],
        [[a, b, d_] for a, b, d_ in N.SUITABILITY], widths=[3.0, 9.6, 4.0])

c.h2('5.1  Who may authorise')
c.para('Authorisation is the act of accepting information for a stated use. It is recorded in the register '
       'against the container and the revision, with the name of the person who gave it.')
c.table(['Suitability', 'Authorised by', 'Recorded'],
        [['S0', 'Nobody. Work in progress is not authorised and is not visible outside the originating '
                'task team', '—'],
         ['S1 to S4', 'The Task Team Manager of the originating party', 'Register and transmittal'],
         ['A1 to An', 'The Appointing Party, or the Lead Appointed Party where the Appointing Party has '
                      'delegated it in writing', 'Register, transmittal and the authorisation record'],
         ['B1 to Bn', 'As A1, with the comments recorded and a date by which they are to be closed',
          'Register, transmittal and the comment schedule']],
        widths=[2.6, 9.4, 4.6])
c.callout('**A1 is the only status that makes information contractual, and the Information Manager cannot '
          'confer it.** The Information Manager verifies that a container is fit to be authorised — named '
          'correctly, complete, checked, and accompanied by its transmittal — and presents it. The decision '
          'belongs to the Appointing Party. A container marked A1 without a recorded authoriser is a '
          'non-conformity under Section 10.', 'Authority')

# ── 6 ───────────────────────────────────────────────────────────────────────
c.h1('6  Issue and transmittal')
c.para('Every issue of information is accompanied by a transmittal. The transmittal is itself a container, '
       'of type TR, and is numbered and registered like any other.')
c.table(['The transmittal records', 'Detail'],
        [['What', 'Every container issued, by name and revision'],
         ['To whom', 'Every recipient, by organisation and person'],
         ['For what', 'The suitability of each container and the purpose of the issue'],
         ['When', 'The date of issue, and the date any response is required'],
         ['By whom', 'The person issuing, and the person who authorised the issue'],
         ['Where', 'The Common Data Environment location the information was published to']],
        widths=[4.4, 12.2])
c.para('A recipient who receives information without a transmittal has not been issued it. The correct '
       'response is to ask for the transmittal, not to use the information.')

# ── 7 ───────────────────────────────────────────────────────────────────────
c.h1('7  Superseding, cancelling and replacing')
c.para('Three things can happen to a container after it has been issued, and they are not the same. Each is '
       'recorded by its own notice, issued as a container of type TR.')
c.table(['Event', 'What it means', 'What happens to the number', 'Notice'],
        [['Superseded', 'A later revision of the same container has been issued. This is the normal case '
                        'and happens at every reissue',
          'Kept. The container continues at the next revision',
          'None. The register records the superseded revision as archived'],
         ['Cancelled', 'The container is no longer required and no later revision will be issued. The scope '
                       'it covered has gone',
          'Retired. Never reused — see Section 8',
          'Cancellation notice, issued to everyone who received the container'],
         ['Replaced', 'The container is no longer required, and a DIFFERENT container now covers its scope. '
                      'The scope moved rather than disappeared',
          'Retired, and cross-referenced to the replacing container in both directions',
          'Replacement notice, naming both containers']],
        widths=[2.6, 5.4, 4.4, 4.2], font=8)
c.callout('The distinction that matters is between superseded and cancelled. A superseded drawing is still '
          'the current information for its scope, at a later revision. A cancelled drawing means there is '
          'no current information for that scope, and somebody needs to know that. Marking a cancellation '
          'as a supersession leaves a recipient waiting for a revision that will never arrive.',
          'Superseded is not cancelled')

c.h2('7.1  Marking')
c.table(['Where', 'Requirement'],
        [['On the sheet', 'A cancelled or replaced sheet is stamped across its face and reissued once at '
                          'the next revision, so the stamped version is what a recipient holds'],
         ['In the register', 'Status set, with the date, the reason, and the replacing container where '
                             'there is one'],
         ['In the CDE', 'Moved to the archived state. Never deleted'],
         ['In the model', 'Any view or sheet corresponding to a cancelled container is removed from the '
                          'issue set, not from the model']],
        widths=[3.4, 13.2])

# ── 8 ───────────────────────────────────────────────────────────────────────
c.h1('8  Number retirement and reuse')
c.para('A retired number is never issued to a different container. This is not a filing preference: '
       'drawing numbers are quoted in requests for information, instructions, valuations, correspondence '
       'and site records, none of which are reissued when a number changes hands. A reused number makes '
       'every one of those references ambiguous, and the ambiguity is only discovered when somebody builds '
       'the wrong thing.')
c.table(['Rule', 'Requirement'],
        [['Retirement', 'A cancelled or replaced number is marked retired in the register on the day the '
                        'notice is issued'],
         ['Reuse', 'Prohibited, for the life of the project and the handover record'],
         ['Gaps', 'A gap in a sequence is expected and is not an error. The register explains every gap'],
         ['Sheet bands', 'A retired sheet number leaves a gap in its band. The next sheet in that band '
                         'takes the next free number, not the retired one'],
         ['Audit', 'The Information Manager checks for reused numbers at every gate. A reused number is a '
                   'gate failure, not an observation']],
        widths=[3.4, 13.2])

# ── 9 ───────────────────────────────────────────────────────────────────────
c.h1('9  The register')
c.para('The register is the record of every container on the project: its name, its title, its originator, '
       'its current revision, its suitability, its status, its authoriser and the transmittal it was last '
       'issued on. It is maintained by the Information Manager and reissued with the monthly status report.')
c.table(['Requirement', 'Detail'],
        [['Completeness', 'Every container that has ever been issued appears, including cancelled, '
                          'replaced and superseded ones'],
         ['Currency', 'Updated at every issue, not in batches'],
         ['Authority', 'The register is the record. Where a local copy of a document disagrees with the '
                       'register, the register is correct and the local copy is out of date'],
         ['Retention', 'Retained for the life of the project and handed over as part of the record '
                       'information']],
        widths=[3.4, 13.2])

# ── 10 ──────────────────────────────────────────────────────────────────────
c.h1('10  Non-conformity')
c.para('Information that does not meet this standard is returned unaccepted, and the originating party '
       'remains responsible for the programme consequences. The following are non-conformities.')
c.table(['#', 'Non-conformity'],
        [['1', 'A container name that does not match Section 2'],
         ['2', 'A sheet number outside the bands in Section 3, once those bands are confirmed'],
         ['3', 'An issue at the same revision as a previous issue with different content'],
         ['4', 'Information issued without a transmittal'],
         ['5', 'A container marked A1 with no recorded authoriser'],
         ['6', 'A cancelled container recorded as superseded, or the reverse'],
         ['7', 'A reused number'],
         ['8', 'Information issued outside the Common Data Environment']],
        widths=[1.2, 15.4])
c.para('The first four are detected automatically at the point of sharing. The remainder are detected at '
       'the gate audit, which is later and more expensive to correct.')

c.end_mark()

c.properties(
    title='KUT Document Control Standard',
    subject='Kampala Uganda Temple — numbering, revision, issue and retirement of project information',
    category='Project procedure',
    comments='Rev P01. Issued through the Common Data Environment. Uncontrolled when printed.',
    author='Symbion Consulting Group Studios')

c.save(OUT, generator='tools/build_document_control.py')
print('saved:', OUT)
