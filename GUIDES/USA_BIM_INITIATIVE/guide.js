const d = require('docx');
const fs = require('fs');
const {
  Document, Packer, Paragraph, TextRun, HeadingLevel, AlignmentType,
  Table, TableRow, TableCell, WidthType, BorderStyle, ShadingType,
  PageBreak, Header, Footer, PageNumber, TableOfContents, convertInchesToTwip,
} = d;

const INK = '22262B';
const ACC = 'C15F3C';
const ACC2 = '8F4229';
const GREY = '5A6068';
const TINT = 'F0EEEA';
const TINT2 = 'E4E0DA';
const HF = 'Cambria';
const BF = 'Calibri';

const FULL = 9000;
const NB = { top: { style: BorderStyle.NONE }, bottom: { style: BorderStyle.NONE },
  left: { style: BorderStyle.NONE }, right: { style: BorderStyle.NONE },
  insideHorizontal: { style: BorderStyle.NONE }, insideVertical: { style: BorderStyle.NONE } };

const kids = [];
const A = function () { for (const x of arguments) kids.push(x); };

function h1(t) {
  return new Paragraph({
    heading: HeadingLevel.HEADING_1, spacing: { before: 420, after: 200 },
    children: [new TextRun({ text: t, font: HF, size: 34, bold: true, color: INK })],
  });
}
function h2(t) {
  return new Paragraph({
    heading: HeadingLevel.HEADING_2, spacing: { before: 320, after: 140 },
    children: [new TextRun({ text: t, font: HF, size: 26, bold: true, color: ACC2 })],
  });
}
function h3(t) {
  return new Paragraph({
    heading: HeadingLevel.HEADING_3, spacing: { before: 240, after: 100 },
    children: [new TextRun({ text: t, font: HF, size: 23, bold: true, color: INK })],
  });
}
function p(t, o) {
  o = o || {};
  return new Paragraph({
    spacing: { after: o.after === undefined ? 140 : o.after, line: 288 },
    indent: o.indent ? { left: convertInchesToTwip(o.indent) } : undefined,
    alignment: o.align,
    children: [new TextRun({
      text: t, font: BF, size: o.size || 21, italic: o.italic,
      bold: o.bold, color: o.color || '2A2E34',
    })],
  });
}
function rich(runs, o) {
  o = o || {};
  return new Paragraph({
    spacing: { after: o.after === undefined ? 140 : o.after, line: 288 },
    indent: o.indent ? { left: convertInchesToTwip(o.indent) } : undefined,
    children: runs.map(function (r) {
      return new TextRun({
        text: r[0], font: BF, size: o.size || 21, bold: r[1] === 'b',
        italic: r[1] === 'i', color: r[2] || '2A2E34',
      });
    }),
  });
}
function bullet(t, lvl) {
  return new Paragraph({
    bullet: { level: lvl || 0 }, spacing: { after: 90, line: 276 },
    children: [new TextRun({ text: t, font: BF, size: 21, color: '2A2E34' })],
  });
}
function numbered(t) {
  return new Paragraph({
    numbering: { reference: 'nums', level: 0 }, spacing: { after: 90, line: 276 },
    children: [new TextRun({ text: t, font: BF, size: 21, color: '2A2E34' })],
  });
}
function box(paras, fill) {
  return new Table({
    width: { size: FULL, type: WidthType.DXA },
    columnWidths: [FULL],
    borders: NB,
    rows: [new TableRow({
      children: [new TableCell({
        width: { size: FULL, type: WidthType.DXA },
        shading: { type: ShadingType.CLEAR, fill: fill || TINT, color: 'auto' },
        margins: { top: 200, bottom: 200, left: 220, right: 220 },
        children: paras,
      })],
    })],
  });
}
function say(label, lines) {
  const inner = [new Paragraph({
    spacing: { after: 120 },
    children: [new TextRun({ text: label, font: HF, size: 20, bold: true, color: ACC2 })],
  })];
  lines.forEach(function (l, i) {
    inner.push(new Paragraph({
      spacing: { after: i === lines.length - 1 ? 0 : 120, line: 288 },
      children: [new TextRun({ text: l, font: BF, size: 21, italic: true, color: '1E2228' })],
    }));
  });
  return box(inner, TINT);
}
function gap(n) {
  return new Paragraph({ spacing: { after: n || 120 }, children: [] });
}
function tbl(headers, rows, widths) {
  const total = widths.reduce(function (a, b) { return a + b; }, 0);
  const cw = widths.map(function (w) { return Math.round(FULL * w / total); });
  const head = new TableRow({
    tableHeader: true,
    children: headers.map(function (hh, i) {
      return new TableCell({
        width: { size: cw[i], type: WidthType.DXA },
        shading: { type: ShadingType.CLEAR, fill: INK, color: 'auto' },
        margins: { top: 120, bottom: 120, left: 140, right: 140 },
        children: [new Paragraph({ children: [new TextRun({ text: hh, font: BF, size: 19, bold: true, color: 'FFFFFF' })] })],
      });
    }),
  });
  const body = rows.map(function (r, ri) {
    return new TableRow({
      children: r.map(function (c, i) {
        return new TableCell({
          width: { size: cw[i], type: WidthType.DXA },
          shading: ri % 2 ? { type: ShadingType.CLEAR, fill: 'F7F6F4', color: 'auto' } : undefined,
          margins: { top: 110, bottom: 110, left: 140, right: 140 },
          children: String(c).split('\n').map(function (line, li) {
            return new Paragraph({
              spacing: { after: li === 0 ? 0 : 0, line: 264 },
              children: [new TextRun({ text: line, font: BF, size: 19, bold: i === 0 && li === 0, color: '2A2E34' })],
            });
          }),
        });
      }),
    });
  });
  return new Table({
    width: { size: FULL, type: WidthType.DXA }, columnWidths: cw,
    borders: {
      top: { style: BorderStyle.SINGLE, size: 2, color: 'D8D4CE' },
      bottom: { style: BorderStyle.SINGLE, size: 2, color: 'D8D4CE' },
      left: { style: BorderStyle.NONE }, right: { style: BorderStyle.NONE },
      insideHorizontal: { style: BorderStyle.SINGLE, size: 2, color: 'E4E1DC' },
      insideVertical: { style: BorderStyle.NONE },
    },
    rows: [head].concat(body),
  });
}

/* ============================================== COVER */
A(
  new Paragraph({ spacing: { before: 1600, after: 100 },
    children: [new TextRun({ text: 'UGANDA SOCIETY OF ARCHITECTS', font: BF, size: 20, bold: true, color: ACC, characterSpacing: 60 })] }),
  new Paragraph({ spacing: { after: 160 },
    children: [new TextRun({ text: 'Presentation guide', font: HF, size: 60, bold: true, color: INK })] }),
  new Paragraph({ spacing: { after: 400 },
    children: [new TextRun({ text: 'Everything to know, and everything to say', font: HF, size: 28, italic: true, color: GREY })] }),
  box([
    new Paragraph({ spacing: { after: 100 }, children: [new TextRun({ text: 'PlanScape and StingTools — a BIM platform built in Kampala', font: BF, size: 22, bold: true, color: INK })] }),
    new Paragraph({ spacing: { after: 0 }, children: [new TextRun({ text: 'Prepared for Davis Mayanja  ·  8 September 2026  ·  Meeting date not yet fixed', font: BF, size: 20, color: GREY })] }),
  ], TINT),
  gap(400),
  p('This document is the single place to look before, during and after the meeting with the Council. It carries the argument, the words to say, the questions you will be asked, the funding routes to hand over, and the things that must not be improvised.', { size: 22 }),
  new Paragraph({ children: [new PageBreak()] })
);

/* ============================================== HOW TO USE */
A(
  h1('How to use this'),
  p('Read it once, all the way through. Then use it in three passes:'),
  tbl(['When', 'What to read'], [
    ['A week before', 'Part 1 (the meeting), Part 3 (the demonstration) — start the rehearsals, and send Ken the request to add the Board of Education chair.'],
    ['The night before', 'Part 2 (the script) out loud, twice. Part 7 (hard questions) out loud once. Part 8 (fixed wording) last thing.'],
    ['On the day', 'Part 9 (checklist). Take Part 6 in printed for the room.'],
  ], [1, 3]),
  gap(200),
  box([
    new Paragraph({ spacing: { after: 120 }, children: [new TextRun({ text: 'The one rule', font: HF, size: 23, bold: true, color: ACC2 })] }),
    new Paragraph({ spacing: { after: 0, line: 288 }, children: [new TextRun({ text: 'Never invent a number, a date, or a customer. "I do not know, and I will come back to you by Friday" is a complete and respectable answer in a room of professionals. A fabricated one is the only mistake here you cannot recover from.', font: BF, size: 21, color: '1E2228' })] }),
  ], TINT2),
  new Paragraph({ children: [new PageBreak()] })
);

/* ============================================== PART 1 */
A(
  h1('Part 1 · The meeting'),
  h2('What you are asking for'),
  p('Three things, and none of them costs the Society money. This is deliberate: the hardest version of this ask — the Society borrowing — has been set aside, and saying so out loud is worth more than anything else in the first five minutes.'),
  numbered('Endorse the training programme as a Society initiative for members, and explore whether it can be accredited for CPD.'),
  numbered('Name a technical counterpart in the ICT Cluster, so the evaluation of the platform is theirs rather than yours.'),
  numbered('Help identify a funding route — with the routes page in hand, so they are choosing between options rather than inventing one.'),
  gap(160),
  h3('What you are explicitly not asking for'),
  bullet('Not asking the Society to borrow. The bank route is set aside. Say so in the opening.'),
  bullet('Not asking the Society for money out of its own funds.'),
  bullet('Not asking for a decision on any agreement, or on branding, today.'),
  gap(160),
  box([
    new Paragraph({ spacing: { after: 120 }, children: [new TextRun({ text: 'What a good outcome actually looks like', font: HF, size: 23, bold: true, color: ACC2 })] }),
    new Paragraph({ spacing: { after: 100, line: 288 }, children: [new TextRun({ text: 'A committee saying "we will consider it" is the normal outcome of a first meeting and is not a failure — but only if you leave with something checkable. Before the room disperses, get four things written down:', font: BF, size: 21, color: '1E2228' })] }),
    new Paragraph({ spacing: { after: 60 }, bullet: { level: 0 }, children: [new TextRun({ text: 'Who takes this forward, by name', font: BF, size: 21, color: '1E2228' })] }),
    new Paragraph({ spacing: { after: 60 }, bullet: { level: 0 }, children: [new TextRun({ text: 'When they next meet, and whether this is on that agenda', font: BF, size: 21, color: '1E2228' })] }),
    new Paragraph({ spacing: { after: 60 }, bullet: { level: 0 }, children: [new TextRun({ text: 'What they need from you before then, and by when', font: BF, size: 21, color: '1E2228' })] }),
    new Paragraph({ spacing: { after: 0 }, bullet: { level: 0 }, children: [new TextRun({ text: 'Whether you may approach a funder using the Society’s name, and who signs that off', font: BF, size: 21, color: '1E2228' })] }),
  ], TINT2),
  gap(200),
  h2('Who is in the room'),
  p('Confirmed by Ken: the Chair of the Board of Practice, the Chairman of the ICT Cluster, and about three further members. The published 23rd Council (2025–2027) gives you the names.'),
  tbl(['Office', 'Holder', 'What they want to know'], [
    ['President', 'Arch. Amunsimiire Kenneth', 'Convening the meeting. Your existing relationship with him is the reason the conflict declaration comes first.'],
    ['Chair, Board of Practice', 'Arch. Abdu Wahab Nyanzi', 'Does this expose the Society? Practice standards, documentation quality, professional conduct.'],
    ['Chairman, ICT Cluster', 'Not published — ask Ken', 'Is it real, and is it sound? This person delivers the technical verdict. The demo is aimed at them.'],
    ['Chair, Board of Education', 'Arch. Daniel Sekamwa', 'NOT INVITED. CPD and training are his remit, and CPD is your strongest argument. Ask for him.'],
    ['Chair, Board of R & D', 'Arch. Clare Ruhweza', 'Not invited. A locally built platform is squarely research and development. Worth a follow-up.'],
  ], [1.1, 1.2, 2.4]),
  gap(200),
  box([
    new Paragraph({ spacing: { after: 120 }, children: [new TextRun({ text: 'Do this before the meeting', font: HF, size: 23, bold: true, color: ACC2 })] }),
    new Paragraph({ spacing: { after: 0, line: 288 }, children: [new TextRun({ text: 'Ask Ken to add Arch. Daniel Sekamwa, Chair of the Board of Education. The meeting is largely about a training programme, CPD is his remit, and your strongest argument is currently aimed at an empty chair. Asking costs nothing and shows you understand how the Society is organised — which is a credibility deposit before you have said a word.', font: BF, size: 21, color: '1E2228' })] }),
  ], TINT2),
  gap(180),
  h2('The shape of the argument'),
  p('Three arguments, in this order. The order matters more than the content: leading with the Society’s problem rather than yours is what makes the rest of it land.'),
  tbl(['#', 'Argument', 'Why it goes here'], [
    ['1', 'A change is coming to your members’ market', 'Makes the problem theirs, not yours. A professional body exists to protect its members’ standing — this is squarely its business.'],
    ['2', 'Here is what exists — the demonstration', 'Makes it real. Nothing in a deck moves the ICT chair as much as watching it work.'],
    ['3', 'Here is what the Society gains', 'Makes it worth their while. CPD, a standard they author, member economics.'],
    ['4', 'What is left, what it costs, and the ask', 'Only after the first three. Money before value is what makes people defensive.'],
  ], [0.4, 1.6, 2.6]),
  new Paragraph({ children: [new PageBreak()] })
);

/* ============================================== PART 2 SCRIPT */
A(
  h1('Part 2 · What to say'),
  p('The full script, slide by slide. It is also in the speaker notes of the PowerPoint file, so you can read it from the presenter view. Do not memorise it word for word — memorise the shape, and keep the exact wording only where this document says the wording is fixed.'),
  p('Timing: twenty minutes of content, forty of discussion. If you are running over, cut from slides 5, 6 and 12 — never from the declaration, the honesty slide, or the ask.', { italic: true, color: GREY }),

  h2('Slides 1–2 · Opening and the declaration'),
  p('Hold until everyone is seated. Do not talk over people sitting down, and do not warm up — this room is senior and short on time.'),
  say('SAY', [
    '"Mr President, Chairman, thank you for making the time. My name is Davis Mayanja. I am an architect-side BIM specialist, and for the last few years I have been building software here in Kampala for the way we actually work.',
    'I have twenty minutes of material and I would much rather spend forty on your questions, so I will move quickly.',
    'Two things before anything else.',
    'First, a declaration. The President and I have worked together professionally. That is in the written proposal and I am saying it out loud here, because I would rather you heard it from me. My view is that this decision belongs with the Council on its merits, and it should go through whatever process your constitution requires. If it succeeds because of a relationship rather than because it is any good, that does not help me and it certainly does not help you.',
    'Second — and I want to be very clear, because one of the documents you have says otherwise. I sent a proposal built around a bank loan with the Society as the borrower. I have set that aside. I am not asking you to borrow. I am not asking you for money out of your own funds. And I am not asking you to sign anything today."',
  ]),
  p('Then pause. Let it land. The room will physically relax, and everything after this is heard differently.', { italic: true, color: GREY }),

  h2('Slide 3 · What you will cover'),
  p('Twenty seconds. It is a signpost, not content.'),
  say('SAY', [
    '"Three things. What has changed in how buildings get documented, and what that means for your members. What I have built — and I will show you rather than describe it. And where this is actually useful to the Society as an institution, which is the part I most want your view on. I will keep to twenty minutes."',
  ]),

  h2('Slides 4–6 · What has changed'),
  say('SAY', [
    '"The way a set of building information is expected to be put together has changed, and it changed outside Uganda first.',
    'ISO 19650 is now the accepted international standard for managing project information — how it is named, how it is versioned, who approved what and when, and what condition it is handed over in. It is not a modelling standard. It is an information standard, which is why it matters to architects and not only to software people.',
    'And governments have started to require it. The United Kingdom, the Emirates, Singapore, Germany — BIM is mandated on public work in all of them. I want to be careful here: I am not telling you Uganda has mandated anything. It has not. What I am telling you is the direction of travel, and it has only ever gone one way.',
    'The region has not kept pace. Kenya is the obvious comparison — bigger construction sector, same regional market. Published research on Kenyan adoption finds it still lagging, and names the consequence: poor coordination of information between the parties on a project. That is a polite way of describing exactly what we all recognise. Drawings that disagree. Schedules that do not match the model. Handover information assembled at the last minute.',
    'Uganda is in the same position with two additional problems. We have no national standard for how architectural information is delivered. And there is nothing on the market built here — everything is priced in dollars and assumes you have a connection.',
    'That is a gap. But I would put it to you that it is also an opening. The body that moves first gets to write the standard, rather than inherit one written somewhere else for somebody else.',
    'What does that cost a practice in your membership? Three things. First, not being asked twice — clients and funders increasingly ask how information will be delivered, and a practice with no answer stops appearing on shortlists and never finds out why. Second, competing on unequal terms — the international and regional firms bidding here already work this way. Third, paying for it on site — coordination done by hand is slower and less complete, and what it misses turns up during construction, where it is most expensive and where the architect usually gets the blame.',
    'That is why I am standing in front of the Society rather than in front of individual practices. A practice can only solve this one firm at a time. A professional body can solve it for the profession."',
  ]),
  box([
    new Paragraph({ spacing: { after: 0, line: 288 }, children: [new TextRun({ text: 'Do not overstate this section. If you claim a Ugandan BIM mandate exists, someone will check and you lose the room. Say "proposed" or "the direction of travel" every time.', font: BF, size: 21, bold: true, color: '1E2228' })] }),
  ], TINT2),

  h2('Slide 7 · What you have built'),
  p('Ninety seconds maximum. The demo does the real work.'),
  say('SAY', [
    '"There are two parts, designed as one system.',
    'StingTools sits inside the design software. It checks a model against data standards automatically, keeps naming and classification consistent, and produces drawings, schedules and quantities from the model instead of by hand. It also assembles handover information while the project is being built, rather than in a panic at the end.',
    'PlanScape is the platform around it — one place for documents, issues and correspondence so the whole team works from the same record. It is priced per organisation, not per person, and anyone outside your office joins free. I will come back to why that matters for a small practice.',
    'Both were started in 2021, here, for the conditions we actually work in."',
  ]),

  h2('Slide 8 · Where it stands — the honesty slide'),
  p('This is the slide that decides whether the room trusts you. Volunteer every word of it before anyone asks.'),
  say('SAY', [
    '"Let me be plain about where this stands, because you will have read one document from me that says the platform is complete and another that asks for money to finish it. Both were true about different things and I have corrected the wording — but you deserve the answer directly.',
    'In production, in daily use on live projects: the document control, the issues, the shared record, the mobile app that works offline, the model checking, and the drawings, schedules, quantities and handover data.',
    'Still in hand: serving many organisations from one system, payments, hosting sized for regional use, and an independent security review.',
    'And not yet validated: the engineering calculation engines. They are complete and tested but they have never been taken through independent professional validation, so I only offer them as commissioned work with manual cross-checks alongside. I would rather say that than have an engineer find it out.',
    'The honest summary is one line. It is production-ready for one deployment. It is not yet productised for a regional market. That difference is what the remaining work is."',
  ]),

  h2('Slide 9 · The demonstration'),
  p('Six minutes. See Part 3 in full — it is the highest-risk part of the meeting and the highest-reward.'),
  say('SAY as you start', [
    '"This is a real project, not a sample file."',
    'Then narrate what you are doing, not what the software is doing. "This would normally take me an afternoon" is worth more than any feature name.',
  ]),

  h2('Slides 10–12 · What the Society gains'),
  say('SAY', [
    '"The first thing the Society gains is not a favour I am doing you. It is something you are already obliged to do.',
    'Every practising architect in Uganda needs twenty CPD points a year to renew a practising licence. That is the 2019 bye-laws, not a suggestion. So the Society has a permanent, annual obligation to put credible content in front of its members — forever. And this particular content is hard to source here. Most of what is available locally is a supplier presenting a product. Structured, hands-on training in information standards is not really on offer in Kampala.',
    'It would be your programme. Your accreditation, your name, delivered by one of your own members rather than someone flown in.',
    'The second thing is a standard. Publishing a standard is the easy part; getting anyone to comply with it is where most of them die, because compliance is expensive and nobody can check it. A document on a website changes nothing about what actually arrives in an inbox. What I would put on the table is different — the naming and classification schemes, the drawing types, the templates already exist, and so does an audit that checks a model against them and says exactly what fails and where. A standard that software can check is a standard that gets used. If the Society wanted to author a national standard for architectural information, it would start from a working implementation rather than a blank page.',
    'The third thing is that none of this matters unless it works for the practices you actually represent, which are mostly small. It is priced per organisation, not per person. Everyone outside your office joins free — client, contractor, quantity surveyor, other consultants, no licence cost to anybody. That matters more than it sounds, because the usual reason coordination software fails on a project here is that nobody will pay for the other parties to be on it. It is billed in shillings. And it works with no connection.',
    'If the Society wanted a negotiated rate for members, I would much rather offer that now than be asked for it later."',
  ]),
  box([
    new Paragraph({ spacing: { after: 0, line: 288 }, children: [new TextRun({ text: 'On CPD, ask — do not assert. "I do not know what ARB accreditation would require for a course like this, or what points it would carry, or what your members currently pay for CPD. You do. That is one of the things I would like your guidance on." Asking makes them co-owners of the idea; claiming you already know makes you a vendor.', font: BF, size: 21, bold: true, color: '1E2228' })] }),
  ], TINT2),

  h2('Slide 13 · The limits'),
  p('This slide earns you more than any other. Do not rush it, and do not apologise through it.'),
  say('SAY', [
    '"Three things I want you to hear from me rather than find out.',
    'First, StingTools runs inside Revit. A lot of you work in ArchiCAD. PlanScape itself does not care what you model in — it works through IFC — and there is an ArchiCAD bridge, but it is early and I am not going to stand here and pretend it is mature.',
    'Second, the engineering calculation engines have never been independently validated. They work and they are tested, but I only offer them as commissioned work with manual checks in parallel. If the Society wanted to put engineers on validating them, that sign-off would carry real weight.',
    'Third, it is not finished, and I said that earlier.',
    'You would have found all three of those in about four minutes. I would rather you heard them from me."',
  ]),

  h2('Slides 14–16 · The cost and the ask'),
  say('SAY', [
    '"So what does finishing it actually take. Thirty-six thousand two hundred dollars to complete the platform — serving many organisations safely, payments, an independent security review, testing. Twelve thousand for twelve months of hosting sized to grow. Fifteen thousand for three training cohorts of about twenty-five people each. With contingency that is seventy-two thousand six hundred and eighty dollars, about two hundred and sixty-nine million shillings.',
    'But here is the part I want you to hear. Those three are separable. Nobody has to find the whole figure. The training programme is fifteen thousand dollars, it stands on its own, it is the part that can start first — and frankly it is the part that would prove the rest.',
    'Which brings me to what I am actually asking for. There are routes to funding this that a professional body can open and I cannot. A development partner will fund the Society for construction-sector capacity building — they will not fund me. A sponsor will pay to be associated with your CPD programme — they will not pay to be associated with mine. And members already budget for CPD, because the law requires them to.',
    'I have written a page on each of these so you are not starting from a blank sheet. What I would like is your view on which are worth pursuing, and who I should be talking to.',
    'So, three things. One: endorse the training programme as a Society initiative for your members, and let us find out together whether it can be accredited for CPD. Two: name someone in the ICT Cluster as a technical counterpart, so the assessment of whether this is any good is yours and not mine. Three: help me find the route to fund the rest — not your money, your judgement about which door to knock on, and if you are willing, an introduction.',
    'None of those costs the Society anything. Thank you — I would rather spend the remaining time on your questions than on my slides."',
  ]),
  p('Hand the funding routes page over as you reach slide 15. Physically giving them something changes the register of the conversation. Then stop talking and let the silence work.', { italic: true, color: GREY }),
  new Paragraph({ children: [new PageBreak()] })
);

/* ============================================== PART 3 DEMO */
A(
  h1('Part 3 · The demonstration'),
  box([
    new Paragraph({ spacing: { after: 0, line: 288 }, children: [new TextRun({ text: 'The rule: nothing is demonstrated live that has not been proven twice, end to end, on the build the demo will actually run on. Not the repository, not a local development run — the deployed thing. A demo that half-works in front of the ICT chair is worse than a recording that works.', font: BF, size: 21, bold: true, color: '1E2228' })] }),
  ], TINT2),
  gap(180),
  h2('What to show'),
  p('These are architects. Show architectural work, not engineering. Every person in the room has done each of these by hand at two in the morning, and that is precisely why it lands.'),
  bullet('A real model — say so: "this is a real project, not a sample file"'),
  bullet('Tagging and the audit — run it, and show what it catches'),
  bullet('A drawing and a schedule coming out consistent'),
  bullet('A quantity extracted from the model'),
  bullet('If the server half is solid: the shared record, an issue raised, the mobile app offline'),
  gap(120),
  p('Do not demonstrate the MEP calculation engines. They are the one part you have told the room is unvalidated, and showing them invites exactly the question you least want.', { italic: true, color: GREY }),
  h2('The rehearsal gate — complete by one week before'),
  tbl(['#', 'Step'], [
    ['1', 'Write the demo path down as numbered steps. Not in your head.'],
    ['2', 'Confirm which deployment it runs against. Verify it — do not assume.'],
    ['3', 'Rehearsal one: full path, timed, on the deployed build. Log every failure.'],
    ['4', 'Fix or cut whatever failed. Cutting is a legitimate and often correct outcome.'],
    ['5', 'Rehearsal two: full path, clean, no interventions.'],
    ['6', 'Record the successful run. This is your fallback whatever you decide.'],
    ['7', 'Test the projector, the resolution and the cable. Assume there is no internet.'],
    ['8', 'Commit to one of the three options below and stop changing your mind.'],
  ], [0.3, 3.7]),
  gap(180),
  h2('Choose one and commit'),
  tbl(['Option', 'Choose it when'], [
    ['Full live — Revit and PlanScape', 'Both rehearsals ran clean end to end, on the deployed build.'],
    ['Hybrid — Revit live, PlanScape recorded', 'The local path is solid and the server path is not. This is the most likely outcome and it is still strong: the local half is the part an architect can judge on sight.'],
    ['Recorded only', 'Neither rehearsal was clean. There is no shame in this. Ship the recording and say plainly that a live build is a few weeks away.'],
  ], [1.2, 2.8]),
  gap(180),
  box([
    new Paragraph({ spacing: { after: 120 }, children: [new TextRun({ text: 'If something fails in the room', font: HF, size: 23, bold: true, color: ACC2 })] }),
    new Paragraph({ spacing: { after: 0, line: 288 }, children: [new TextRun({ text: 'Say so, move on, and use the recording. Do not debug in front of the room. "That is the half I told you is still being finished" is a recoverable sentence, and it is even consistent with what you said on the honesty slide. Silence while you fiddle with a laptop is not recoverable — it is the thing they will remember.', font: BF, size: 21, color: '1E2228' })] }),
  ], TINT),
  new Paragraph({ children: [new PageBreak()] })
);

/* ============================================== PART 4 VALUE */
A(
  h1('Part 4 · Where this matters to the Society'),
  p('Not to individual architects — to the institution. A professional body says yes to things that help it discharge its own mandate. Ordered by strength of fit.'),
  h2('1 · Statutory CPD — the strongest fit by a distance'),
  rich([['Every practising architect in Uganda needs ', ''], ['20 CPD points a year to renew a practising licence', 'b'], [', under the Architects Registration (Continuing Professional Development) Bye Laws, gazetted 2019. This is not optional and it does not go away.', '']]),
  p('So the Society has a permanent, recurring obligation to put credible content in front of its members — and BIM and information standards is exactly the content that is hardest to source locally. Most CPD available to Ugandan architects is a supplier presentation.'),
  bullet('A structured, hands-on programme rather than a lecture, repeatable in cohorts'),
  bullet('Delivered by a member, so the Society is not importing a trainer'),
  bullet('The Society owns the programme, the accreditation and the name'),
  gap(100),
  p('It is also a funding route, not only a benefit. Members pay for CPD because the law requires them to. Cohort fees can make delivery self-sustaining without anyone borrowing anything.', { bold: true }),
  p('Ask, do not assume: what ARB accreditation requires, how points are awarded for a multi-day course, and what members currently pay. They know; you do not.', { italic: true, color: GREY }),

  h2('2 · Members getting locked out of their own market'),
  p('This is the "why now", and it is the argument that makes the problem theirs. A professional body exists to protect its members’ standing and livelihood — a change in what clients expect, that its members are not ready for, is precisely its business.'),
  p('The Society can either find out about it when members start losing tenders, or get ahead of it. Frame it as a warning you are bringing them, not a sales opportunity.'),

  h2('3 · A national standard the Society authors'),
  p('The Board of Practice owns practice standards. Publishing a standard is easy; getting compliance is not — standards die because compliance is expensive and unverifiable.'),
  p('What is different here: a working implementation rather than a document. Naming and tagging schemes, drawing types, title blocks, templates — and an audit that checks a model against them and reports what fails. A standard software can check is a standard that gets used.'),
  p('Value to the Society: national authority, a visible contribution, and relevance it did not have to manufacture.'),

  h2('4 · Small-practice economics'),
  p('Most members are small practices, and per-seat licensing at Ugandan fee levels is punishing.'),
  bullet('Priced per organisation, not per person'),
  bullet('Unlimited free external members — a three-person practice can bring the client, contractor and QS onto a project at no licence cost to anyone'),
  bullet('Billed in shillings'),
  bullet('Offline-capable for sites without connectivity'),
  p('A Society-negotiated member rate is the most conventional professional-body benefit there is. Offer it before they ask for it.'),

  h2('5 · Documentation quality and professional risk'),
  p('Board of Practice language: competence, evidence, fewer disputes. Automated audit catches inconsistency before issue; revision control means the record of what was issued, when and to whom exists without anyone maintaining it by hand; a clash report is evidence that a conflict was raised and when.'),
  p('It also supports fee defence — demonstrable deliverables justify fees in a market where architects are constantly asked to discount.'),

  h2('6 · The graduate pipeline'),
  p('Board of Education again. Student and graduate access means graduates arrive employable and practices carry less training cost. The Council has Graduate, Student and Technician representatives, so these are represented constituencies rather than an abstraction.'),
  p('Be transparent that it is mutual — it also builds the platform’s user base. Saying so costs nothing and pre-empts the observation.', { italic: true, color: GREY }),

  h2('7 · A local product, locally hosted — the ICT Cluster’s agenda'),
  bullet('Built in Kampala, for Ugandan conditions'),
  bullet('Can be hosted in Uganda; data need not leave the country'),
  bullet('Open, exportable formats throughout — IFC 4, BCF 2.1, COBie, RVT, DWG, Excel, PDF'),
  bullet('A priced exit path, so adoption is reversible'),
  bullet('Subscription spend stays in the domestic economy rather than being exported'),

  h2('8 · Uganda-specific content no imported tool carries'),
  p('Regional defaults by Ugandan region — wind speed, seismic zone, soil bearing capacity, design rainfall, live loads — plus local code defaults, shilling budgets, and offline-first working. This is the Society’s own operating context encoded in software, and it is the clearest answer to "why not just use what everyone else uses".'),

  h2('9 · The Society’s own operations'),
  p('Document control for the secretariat, committee papers, awards and competition submissions. Small, but "we use it ourselves" is a credibility asset when the Society advocates it to anyone else. Do not oversell this one — it is a footnote, not an argument.'),
  new Paragraph({ children: [new PageBreak()] })
);

/* ============================================== PART 5 LIMITS */
A(
  h1('Part 5 · The limits you must disclose'),
  p('Say all of these yourself, first. A room of architects will find them in about four minutes, and disclosed they are honesty while discovered they are a credibility problem.'),
  tbl(['Limit', 'How to put it'], [
    ['StingTools runs inside Revit, and many Ugandan architects use ArchiCAD', 'The single most likely objection in this specific room. PlanScape is authoring-agnostic through IFC and there is an ArchiCAD bridge, but it is early. Say so plainly, and say what the roadmap is. If this lands as a gotcha rather than a disclosure, it costs you the ICT chair.'],
    ['The MEP calculation engines are not independently validated', 'Complete and tested, never carried through independent professional validation. Offered only as separately commissioned services, with manual cross-checks in parallel. Then invite the Society’s engineers to be the validators — it turns a weakness into a role for them.'],
    ['Production-ready for one deployment, not productised for many', 'Use the fixed wording in Part 8. Volunteer it on the honesty slide.'],
    ['There is no Ugandan BIM mandate', 'Say "the direction of travel", never "it is being mandated here". Overstating this is the fastest way to lose the room.'],
  ], [1.3, 2.7]),
  new Paragraph({ children: [new PageBreak()] })
);

/* ============================================== PART 6 FUNDING */
A(
  h1('Part 6 · Funding routes'),
  p('This is the page to print and hand over. "Help me find funding" invites a sympathetic nod and nothing else. Eight named routes, four of which only the Society can open, invites a decision.'),
  h2('What is being funded'),
  tbl(['Item', 'Cost'], [
    ['Platform completion — multi-organisation support, payments, security review, testing', 'USD 36,200'],
    ['Hosting sized for regional use, twelve months', 'USD 12,000'],
    ['Training programme, three cohorts of about 25', 'USD 15,000'],
    ['Subtotal', 'USD 63,200'],
    ['Contingency, 15 per cent', 'USD 9,480'],
    ['Total', 'USD 72,680 (about UGX 269 million)'],
  ], [3.2, 1.0]),
  gap(160),
  p('These are separable, and saying so makes the ask far easier. Nobody has to find USD 72,680 — the training programme can be funded on its own and start first. A funder who can only reach USD 15,000 is still a funder.', { bold: true }),
  h2('The routes'),
  tbl(['Route', 'Why it works', 'What the Society unlocks'], [
    ['1. CPD cohort fees', 'CPD is statutory. Members already budget for it — this is not asking them to fund a platform, it is selling them something the law requires them to buy.', 'Accreditation, the member list, and its name on the programme. None of which costs it money.'],
    ['2. Development partners', 'World Bank, AfDB, GIZ, FCDO, EU, JICA, UN-Habitat all fund construction-sector capacity building and digital skills.', 'A professional body is a fundable grantee; a young private company is not. This is the clearest thing the Society has that you do not.'],
    ['3. Industry sponsorship', 'Cement, steel, roofing and glazing manufacturers, banks and insurers routinely sponsor professional CPD for access to the profession.', 'Its convening power and its existing corporate relationships. Sponsors pay for access to members — only the Society can grant it.'],
    ['4. Government partnership', 'If public procurement moves toward digital delivery, capacity building becomes a public need and the Society is the obvious delivery partner for the profession.', 'Standing to be that partner, which an individual does not have. Present it as an opportunity, never as funding that exists.'],
    ['5. Innovation and ICT funding', 'A locally built software product with regional export potential fits national digital-economy priorities. Worth checking NITA-U, the Ministry of ICT, UNCST, UIRI.', 'Institutional backing, which materially strengthens an application from a small developer. Check first whether these fund product completion or only research.'],
    ['6. University partnership', 'A co-application with Makerere or Kyambogo reaches research funding neither party reaches alone, with curriculum integration as a deliverable.', 'The Board of Education and Board of R & D are the natural sponsors — neither chair is currently in the meeting.'],
    ['7. Prepaid subscriptions', 'Practices commit to a discounted multi-year subscription paid up front. That is revenue, not debt — no interest, no guarantee, no lender.', 'The aggregation. Individually small; assembled through the Society they are a funding round. Only offer this with a clear scope and date.'],
    ['8. Society reserves or a levy', 'Listed for completeness. Do not lead with it — asking a professional body to spend reserves on a member’s product is the hardest version of this ask.', 'If the Society offers, accept gratefully and insist it goes through full governance.'],
  ], [0.9, 2.0, 2.0]),
  gap(180),
  say('HOW TO PUT IT IN THE ROOM', [
    '"I am not asking the Society for money, and I am not asking it to borrow — I looked at a bank facility and set it aside.',
    'What I am asking is this. There are routes to funding this that a professional body can open and I cannot. A donor will fund the Society for construction-sector capacity building; they will not fund me. A sponsor will pay to be associated with your CPD programme; they will not pay to be associated with mine. And members already have to buy CPD every year.',
    'I have listed the ones I can see. I would like your view on which are worth pursuing, and who I should be talking to."',
    'Then hand over the page and stop talking.',
  ]),
  new Paragraph({ children: [new PageBreak()] })
);

/* ============================================== PART 7 Q&A */
A(
  h1('Part 7 · The questions you will be asked'),
  p('Rehearse these out loud. A prepared answer delivered hesitantly reads worse than an honest "I do not know" delivered calmly. Volunteer Q1, Q2 and Q3 yourself in the first five minutes.'),

  h3('Q1 · "Is the platform finished, or is it still being built?"'),
  p('The one that decides the meeting. Ken holds two documents that used to disagree on this.'),
  say('ANSWER', [
    '"The coordination core is in production and in daily use on live projects — documents, issues, the shared record, the mobile app. The Revit add-in is in production use for tagging, model audit, drawings, schedules and quantities.',
    'The work still in hand is a different kind of thing: turning a platform that works for one deployment into a product that can be sold to many organisations across several countries. Multi-tenancy, payments, regional hosting, an independent security review, and the training programme.',
    'Production-ready for one deployment; not yet productised for a regional market. That is the honest position, and it is why the completion budget is about thirty-six thousand dollars and not half a million."',
  ]),
  p('If pressed on the earlier document saying "complete": the wording has been corrected, so answer in the past tense. "You are right that an earlier version read that way. I have corrected it, because the two documents have to say the same thing." Concede it — it is a fair hit and conceding costs nothing.', { italic: true, color: GREY }),

  h3('Q2 · "So what are you actually asking us to fund?"'),
  say('ANSWER', [
    '"Nothing, today. I sent you a proposal built around a bank facility with the Society as borrower. I have set that aside — I would rather not ask a professional body to take on debt for a platform it has not evaluated yet.',
    'What I am asking for today costs the Society nothing."',
  ]),
  p('Then stop talking. Do not fill the silence by re-opening the financing case. If they offer to help with funding, take it as an action rather than a decision: "I would welcome that. Could I come back to you with a specific, costed proposition once I have looked at the options properly?"', { italic: true, color: GREY }),

  h3('Q3 · "You and the President work together. Is that not a conflict?"'),
  say('ANSWER', [
    '"Yes, and I have declared it in the proposal and again at the start of this meeting. That is exactly why I am presenting to this Council rather than asking him to carry it, and why I think he should sit outside the approval decision. I would rather this succeeded on its merits or not at all."',
  ]),

  h3('Q4 · "What happens if you are unavailable?"'),
  say('ANSWER', [
    '"Five things, and they would be written into any agreement. Everything you hold opens without my platform — IFC, BCF, COBie, RVT, DWG, Excel, PDF. Source code and configuration go into escrow with your solicitors, released on defined events. The platform can be self-hosted on your own infrastructure. Standards files and workflow configuration are handed over as deliverables under version control. And the exit path to the mainstream commercial stack is priced, so migrating away is a budget decision rather than a rescue."',
  ]),

  h3('Q5 · "Could this put the Society, or one of us, on the hook for anything?"'),
  say('ANSWER', [
    '"No. In the proposal I sent, the Society would have been the borrower — and if it had no suitable security, a bank’s usual alternative is a personal guarantee from an office bearer. I put that in writing because I was not willing to have it discovered later, and it is one of the reasons I have set that route aside.',
    'What I am asking for today carries no financial obligation of any kind."',
  ]),
  p('Say it once, say it is set aside, move on. Do not reintroduce the risk in order to look candid.', { italic: true, color: GREY }),

  h3('Q6 · "Who actually uses this? Show us a customer."'),
  p('The weakest area on paper. Handle it with what is true, and then invert it.'),
  say('ANSWER', [
    '"It is in daily use on the project I am currently information manager on — a six-building institutional campus in central Kampala, more than eight design disciplines, working to ISO 19650 under a United States client’s standards.',
    'What I do not have is twenty Ugandan practices using it. That is precisely what the Society’s endorsement and a first training cohort would build."',
  ]),

  h3('Q7 · "Why would a member not just buy the international product?"'),
  say('ANSWER', [
    '"For authoring, they should — those tools are unmatched and nothing I do replaces them. The difference is everything around authoring: coordination, document control, field work, quantities, handover data.',
    'The international products charge per user. This charges per organisation, with unlimited free external members — so consultants, contractors and clients join a project at no licence cost. For a ten-person practice here that is the difference between viable and not. And it is priced in shillings, hostable in Uganda, and supported from Kampala."',
  ]),

  h3('Q8 · "What would the Society get out of it commercially?"'),
  p('Be careful here. The revenue-share terms in the draft MOU were consideration for the Society acting as borrower — and there is no longer a loan, so those economics no longer have a basis. Do not defend a number that is now orphaned.'),
  say('ANSWER', [
    '"Honestly, that needs restating. The share in the draft you have was consideration for the Society carrying a loan, and I have taken the loan off the table — so that arrangement no longer makes sense as drafted.',
    'What I would rather do is agree what the Society is giving and what it should get, and then write that down properly. I have views, but I would rather hear yours first."',
  ]),

  h3('Q9 · "Where do your revenue projections come from?"'),
  say('ANSWER', [
    '"They are illustrative planning estimates and the document says so. They are built from published tier pricing and an uptake assumption, not from a bottom-up market study, and they exclude training revenue, so they are conservative in that respect.',
    'Before this goes anywhere near a funder they need a proper bottom-up basis — how many practices and engineering firms operate in Uganda, what share twenty of them represents, and what conversion rate is defensible. That is work I would want to do with the Society’s own membership data rather than guess at."',
  ]),

  h3('Q10 · "What do you actually want from us today?"'),
  p('Have this word-perfect. It is the close, and it is the only part of the meeting they will repeat to anyone who was not there.'),
  say('ANSWER', [
    '"Three things, none of which cost the Society money.',
    'One: endorse the training programme as a Society initiative for members, and let us look at whether it can be accredited for CPD.',
    'Two: name someone in the ICT Cluster as technical counterpart, so the evaluation is yours and not mine.',
    'Three: help me find the route to fund the rest — not your money, your judgement on which door to knock on, and ideally an introduction."',
  ]),

  h2('Questions you should ask them'),
  p('Ending on your questions makes it a conversation between colleagues rather than a pitch — and every one of these closes something you genuinely need.'),
  numbered('How is the Society registered, and what does the constitution say about arrangements with a member’s business?'),
  numbered('What approval would something like this need, and what is the calendar for it?'),
  numbered('Would the Society share its registration details, founding year and membership numbers?'),
  numbered('What would CPD accreditation require for a course like this, and what do members currently pay?'),
  numbered('Who would you want as technical counterpart in the ICT Cluster?'),
  numbered('Has the Society tried anything on BIM adoption before, and what stalled it?'),
  numbered('What would make this an easy yes for you — and what would make it an easy no?'),
  new Paragraph({ children: [new PageBreak()] })
);

/* ============================================== PART 8 WORDING */
A(
  h1('Part 8 · Wording that must not be improvised'),
  p('Everything said publicly about this comes from here. Not from memory, not reworded to suit an audience. Consistency across every document and conversation is worth more than any individual phrasing.'),

  h2('Completion status'),
  p('Already applied to the initiative proposal and the draft MOU. Use it verbatim in the room.'),
  say('FIXED WORDING', [
    'The coordination core is in production and in daily use on live projects: the common data environment, document control, issues and RFIs, dashboards and the mobile application. StingTools is in production use for tagging, model audit, drawing and schedule production, quantities and handover data.',
    'The work still in hand is of a different kind: turning a platform that works for one deployment into a product that can be sold to many organisations across several countries — support for multiple organisations and countries on one system, mobile money payments, hosting sized for the region, and an independent security review.',
    'Production-ready for one deployment; not yet productised for a regional market.',
  ]),
  bullet('Never say "the platform is complete" or "finished", or anything implying the remaining work is optional polish.'),
  bullet('Never say "it does not work yet" either. It does. Both overstatements are avoidable, and the second is the one people reach for when they are being modest.'),

  h2('Origin'),
  say('FIXED WORDING', [
    'PlanScape and StingTools were built starting in 2021, worked out from first principles. They were not used on the Tilenga oil development project — but seeing that project’s coordination and design problems up close, on a programme of that size, is what accelerated the design and coordination research behind them from 2023 onward.',
  ]),
  p('Say "not used on Tilenga" plainly. The proximity is the honest and interesting part of the story; any ambiguity about it is a liability that grows with the platform’s profile.', { italic: true, color: GREY }),

  h2('The Society’s role'),
  p('Only after the Society has actually agreed to it:'),
  say('AFTER AGREEMENT', ['The Society’s national BIM platform, developed by one of its members and commercialised in partnership with the Society.']),
  p('Until then — and this is the position for the presentation itself:'),
  say('UNTIL THEN', ['A BIM platform built in Kampala by a member of the Society, proposed for adoption as the Society’s national BIM initiative.']),
  p('Do not use the first form in any material before the Society has endorsed it. Using it prematurely is the fastest way to lose a professional body’s trust.', { italic: true, color: GREY }),

  h2('Ownership'),
  p('The substance does not change — the platform belongs to its developer and nothing proposed transfers it. The legal wording is being settled separately, so in the room use this:'),
  say('FIXED WORDING', [
    'PlanScape and StingTools are owned by their developer. Nothing proposed transfers ownership. The Society would receive the right to describe the platform as its national BIM initiative, a training programme for its members, and an agreed share in what the platform earns.',
  ]),

  h2('The conflict of interest'),
  say('FIXED WORDING', [
    'Davis Mayanja and the President have an existing professional relationship. It is declared in the proposal and stated at the start of any presentation. The President’s role here is to introduce the initiative, not to approve it; the decision should rest with the Society’s proper governance process, on the merits.',
  ]),

  h2('Financing'),
  say('FIXED WORDING', [
    'I am not asking the Society to borrow. I explored a bank facility and have set it aside for now while I look at other routes. If financing comes back onto the table it will come back as a specific, costed proposition, through the Society’s own governance process, not as part of this conversation.',
  ]),
  new Paragraph({ children: [new PageBreak()] })
);

/* ============================================== PART 9 CHECKLIST */
A(
  h1('Part 9 · Checklist'),
  h2('Now — before the date is even agreed'),
  bullet('Reply to Ken: thank him for the seat list, propose two or three dates'),
  bullet('Ask Ken to add Arch. Daniel Sekamwa, Chair of the Board of Education'),
  bullet('Ask Ken for the name of the ICT Cluster chairman'),
  bullet('Start the demo rehearsals — they take longer than anyone expects'),

  h2('A week before'),
  bullet('Demo rehearsal gate complete; live / hybrid / recorded decided and committed to'),
  bullet('Recording of the successful demo run made and tested'),
  bullet('Initiative proposal re-dated — the September dates in the current draft have passed'),
  bullet('Funding routes page (Part 6) laid out on your letterhead, six copies printed'),

  h2('Three days before'),
  bullet('Read Part 2 out loud, twice, standing up'),
  bullet('Rehearse Q1, Q2, Q3, Q5 and Q10 out loud until they are fluent'),
  bullet('Print the leave-behind proposal × 6'),
  bullet('Prepare the one-page summary of what a training cohort covers'),

  h2('The day before'),
  bullet('Confirm attendance, venue and start time'),
  bullet('Test projector, resolution and cable at the venue if possible'),
  bullet('Load everything offline — assume there is no internet'),
  bullet('Charge the laptop; pack the charger and an HDMI adapter'),
  bullet('Do not pack the bank loan application. It is set aside — carrying it into the room creates exactly the confusion you are avoiding'),
  bullet('Re-read Part 8 last thing'),

  h2('On the day'),
  bullet('Declare the conflict in the first two minutes'),
  bullet('Say plainly that the loan route is set aside, and why'),
  bullet('Volunteer the honesty slide before anyone asks'),
  bullet('Hand over the funding routes page at slide 15, then stop talking'),
  bullet('Ask your seven questions'),
  bullet('Write down who said what, immediately afterwards, before it fades'),

  h2('Within 24 hours'),
  bullet('Thank-you email with the three asks restated in writing'),
  bullet('Send anything you promised in the room, by the date you promised it'),
  bullet('Record every answer you were given — especially the governance and CPD answers'),
  bullet('If you were given a name or an introduction, act on it this week, not next month'),
  new Paragraph({ children: [new PageBreak()] })
);

/* ============================================== PART 10 OPEN */
A(
  h1('Part 10 · Still open'),
  p('Things that are not settled, and that should not be improvised in the room. If one of these comes up and you do not have the answer, say so.'),
  tbl(['Question', 'Status', 'Who answers it'], [
    ['What office does Ken hold', 'CLOSED — he is the President, confirmed against the published 23rd Council (2025–2027)', '—'],
    ['Who chairs the ICT Cluster', 'Open — not in the published Council list. This person delivers the technical verdict.', 'Ask Ken with the date'],
    ['What CPD accreditation requires, and what points a course carries', 'Open. Also: what members currently pay for CPD in Kampala.', 'The Society, in the meeting'],
    ['What the constitution requires to approve an arrangement with a member’s business', 'Open', 'The Society, in the meeting'],
    ['What the funding route actually is', 'Open — the bank route is set aside and nothing has replaced it yet. The initiative proposal still needs re-pointing once this is decided.', 'Davis, with the Society’s help'],
    ['What the Society gets in return', 'Open — the revenue share in the draft MOU was consideration for carrying a loan. With no loan it has no basis and must be restated.', 'Joint'],
    ['Who the counterparty is on any agreement', 'Open — pending the decision on the company. Assign the platform IP out in writing before any dissolution.', 'Davis'],
    ['What is demo-safe on the deployed build', 'Open — resolved only by the rehearsal gate in Part 3.', 'Davis'],
  ], [1.5, 2.2, 1.0]),
  gap(220),
  box([
    new Paragraph({ spacing: { after: 120 }, children: [new TextRun({ text: 'A closing note', font: HF, size: 23, bold: true, color: ACC2 })] }),
    new Paragraph({ spacing: { after: 0, line: 288 }, children: [new TextRun({ text: 'The strongest thing in this whole package is not the platform — it is that you are willing to say what is not finished, what is not validated, and what you do not know. A room of senior professionals has heard a great many confident pitches. Very few of them included the weaknesses. That is the thing they will remember, and it is the thing most likely to make them want to help.', font: BF, size: 21, color: '1E2228' })] }),
  ], TINT2)
);

const doc = new Document({
  creator: 'Davis Mayanja',
  title: 'USA BIM Initiative — Presentation Guide',
  numbering: {
    config: [{
      reference: 'nums',
      levels: [{ level: 0, format: 'decimal', text: '%1.', alignment: AlignmentType.START,
        style: { paragraph: { indent: { left: 460, hanging: 300 } } } }],
    }],
  },
  styles: {
    default: { document: { run: { font: BF, size: 21, color: '2A2E34' } } },
  },
  sections: [{
    properties: { page: { margin: { top: 1100, bottom: 1100, left: 1100, right: 1100 } } },
    headers: {
      default: new Header({ children: [new Paragraph({
        alignment: AlignmentType.RIGHT, spacing: { after: 200 },
        children: [new TextRun({ text: 'USA BIM Initiative · Presentation Guide', font: BF, size: 16, color: '9AA0A6' })],
      })] }),
    },
    footers: {
      default: new Footer({ children: [new Paragraph({
        alignment: AlignmentType.RIGHT,
        children: [new TextRun({ text: 'Page ', font: BF, size: 16, color: '9AA0A6' }),
          new TextRun({ children: [PageNumber.CURRENT], font: BF, size: 16, color: '9AA0A6' })],
      })] }),
    },
    children: kids,
  }],
});

Packer.toBuffer(doc).then(function (buf) {
  fs.writeFileSync(process.argv[2], buf);
  console.log('written', process.argv[2]);
});
