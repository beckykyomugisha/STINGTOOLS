const d = require('docx');
const fs = require('fs');
const {
  Document, Packer, Paragraph, TextRun, HeadingLevel, AlignmentType,
  Table, TableRow, TableCell, WidthType, BorderStyle, ShadingType,
  PageBreak, Header, Footer, PageNumber, convertInchesToTwip,
} = d;

const INK = '22262B';
const ACC2 = '8F4229';
const ACCENT = 'C15F3C';
const GREY = '5A6068';
const TINT = 'F0EEEA';
const TINT2 = 'E4E0DA';
const HF = 'Cambria';
const BF = 'Calibri';
const FULL = 9000;

const NB = {
  top: { style: BorderStyle.NONE }, bottom: { style: BorderStyle.NONE },
  left: { style: BorderStyle.NONE }, right: { style: BorderStyle.NONE },
  insideHorizontal: { style: BorderStyle.NONE }, insideVertical: { style: BorderStyle.NONE },
};

const kids = [];
const A = function () { for (const x of arguments) kids.push(x); };

function h1(t) {
  return new Paragraph({ heading: HeadingLevel.HEADING_1, pageBreakBefore: true,
    spacing: { before: 0, after: 200 },
    children: [new TextRun({ text: t, font: HF, size: 34, bold: true, color: INK })] });
}
function h2(t) {
  return new Paragraph({ heading: HeadingLevel.HEADING_2, spacing: { before: 320, after: 140 },
    children: [new TextRun({ text: t, font: HF, size: 26, bold: true, color: ACC2 })] });
}
function h3(t) {
  return new Paragraph({ heading: HeadingLevel.HEADING_3, spacing: { before: 240, after: 100 },
    children: [new TextRun({ text: t, font: HF, size: 23, bold: true, color: INK })] });
}
function p(t, o) {
  o = o || {};
  return new Paragraph({ spacing: { after: o.after === undefined ? 140 : o.after, line: 288 },
    children: [new TextRun({ text: t, font: BF, size: o.size || 21, italic: o.italic,
      bold: o.bold, color: o.color || '2A2E34' })] });
}
function bullet(t) {
  return new Paragraph({ bullet: { level: 0 }, spacing: { after: 90, line: 276 },
    children: [new TextRun({ text: t, font: BF, size: 21, color: '2A2E34' })] });
}
function numbered(t) {
  return new Paragraph({ numbering: { reference: 'nums', level: 0 },
    spacing: { after: 90, line: 276 },
    children: [new TextRun({ text: t, font: BF, size: 21, color: '2A2E34' })] });
}
function box(paras, fill) {
  return new Table({ width: { size: FULL, type: WidthType.DXA }, columnWidths: [FULL], borders: NB,
    rows: [new TableRow({ children: [new TableCell({
      width: { size: FULL, type: WidthType.DXA },
      shading: { type: ShadingType.CLEAR, fill: fill || TINT, color: 'auto' },
      margins: { top: 200, bottom: 200, left: 220, right: 220 },
      children: paras })] })] });
}
function note(title, body, fill) {
  return box([
    new Paragraph({ spacing: { after: 120 },
      children: [new TextRun({ text: title, font: HF, size: 23, bold: true, color: ACC2 })] }),
    new Paragraph({ spacing: { after: 0, line: 288 },
      children: [new TextRun({ text: body, font: BF, size: 21, color: '1E2228' })] }),
  ], fill || TINT2);
}
function say(label, lines) {
  const inner = [new Paragraph({ spacing: { after: 120 },
    children: [new TextRun({ text: label, font: HF, size: 20, bold: true, color: ACC2 })] })];
  lines.forEach(function (l, i) {
    inner.push(new Paragraph({ spacing: { after: i === lines.length - 1 ? 0 : 120, line: 288 },
      children: [new TextRun({ text: l, font: BF, size: 21, italic: true, color: '1E2228' })] }));
  });
  return box(inner, TINT);
}
function gap(n) { return new Paragraph({ spacing: { after: n || 120 }, children: [] }); }
function tbl(headers, rows, widths) {
  const total = widths.reduce(function (a, b) { return a + b; }, 0);
  const cw = widths.map(function (w) { return Math.round(FULL * w / total); });
  const head = new TableRow({ tableHeader: true, children: headers.map(function (hh, i) {
    return new TableCell({ width: { size: cw[i], type: WidthType.DXA },
      shading: { type: ShadingType.CLEAR, fill: INK, color: 'auto' },
      margins: { top: 120, bottom: 120, left: 140, right: 140 },
      children: [new Paragraph({ children: [new TextRun({ text: hh, font: BF, size: 19,
        bold: true, color: 'FFFFFF' })] })] }); }) });
  const body = rows.map(function (r, ri) {
    return new TableRow({ children: r.map(function (c, i) {
      return new TableCell({ width: { size: cw[i], type: WidthType.DXA },
        shading: ri % 2 ? { type: ShadingType.CLEAR, fill: 'F7F6F4', color: 'auto' } : undefined,
        margins: { top: 110, bottom: 110, left: 140, right: 140 },
        children: String(c).split('\n').map(function (line) {
          return new Paragraph({ spacing: { line: 264 },
            children: [new TextRun({ text: line, font: BF, size: 19,
              bold: i === 0, color: '2A2E34' })] }); }) }); }) });
  });
  return new Table({ width: { size: FULL, type: WidthType.DXA }, columnWidths: cw,
    borders: {
      top: { style: BorderStyle.SINGLE, size: 2, color: 'D8D4CE' },
      bottom: { style: BorderStyle.SINGLE, size: 2, color: 'D8D4CE' },
      left: { style: BorderStyle.NONE }, right: { style: BorderStyle.NONE },
      insideHorizontal: { style: BorderStyle.SINGLE, size: 2, color: 'E4E1DC' },
      insideVertical: { style: BorderStyle.NONE } },
    rows: [head].concat(body) });
}

/* ================================================= COVER */
A(
  new Paragraph({ spacing: { before: 1600, after: 100 }, children: [new TextRun({
    text: 'UGANDA SOCIETY OF ARCHITECTS', font: BF, size: 20, bold: true,
    color: ACCENT, characterSpacing: 60 })] }),
  new Paragraph({ spacing: { after: 160 }, children: [new TextRun({
    text: 'Presentation guide', font: HF, size: 60, bold: true, color: INK })] }),
  new Paragraph({ spacing: { after: 400 }, children: [new TextRun({
    text: 'Everything to know, and everything to say', font: HF, size: 28, italic: true,
    color: GREY })] }),
  box([
    new Paragraph({ spacing: { after: 100 }, children: [new TextRun({
      text: 'PlanScape and StingTools, a BIM platform built in Kampala', font: BF, size: 22,
      bold: true, color: INK })] }),
    new Paragraph({ spacing: { after: 0 }, children: [new TextRun({
      text: 'Prepared for Davis Mayanja  ·  September 2026', font: BF, size: 20, color: GREY })] }),
  ], TINT),
  gap(400),
  p('One place to look before, during and after the meeting with the Council. It carries the argument, the words to say, the questions you will be asked, the funding routes to hand over, and the things that should not be improvised on the day.', { size: 22 })
);

/* ================================================= HOW TO USE */
A(
  h1('How to use this'),
  p('Read it through once. Then use it in three passes.'),
  tbl(['When', 'What to read'], [
    ['A week before', 'Part 1 and Part 3. Start the demonstration rehearsals, and ask for the Chair of the Board of Education to be added to the meeting.'],
    ['The night before', 'Part 2 out loud, twice. Part 8 out loud once. Part 9 last thing before bed.'],
    ['On the day', 'Part 10. Take Part 7 printed, one copy per person.'],
  ], [1, 3]),
  gap(200),
  note('The one rule',
    'Never invent a number, a date, or a customer. "I do not know, and I will come back to you by Friday" is a complete and respectable answer in a room of professionals. A fabricated one is the only mistake here you cannot recover from.')
);

/* ================================================= PART 1 */
A(
  h1('Part 1 · The meeting'),
  h2('What you are asking for'),
  p('Three things, and none of them costs the Society money. Say all three in the first two minutes so nobody spends the next twenty wondering what the catch is. A room that is waiting for an ask does not listen properly.'),
  numbered('That the Society endorses a BIM training programme for its members, and looks at whether it can be accredited for CPD.'),
  numbered('That someone in the ICT Cluster is named as a technical counterpart, so the judgement about the platform is theirs rather than yours.'),
  numbered('Their help identifying how the remaining development gets funded, choosing from the routes in Part 7.'),
  gap(160),
  h3('What you are not asking for'),
  bullet('Any money from the Society.'),
  bullet('A decision on any agreement on the day.'),
  bullet('Exclusivity, or anything that binds their members.'),
  gap(200),
  note('What a good outcome actually looks like',
    'A committee saying "we will consider it" is the normal outcome of a first meeting and is not a failure, provided you leave with something checkable. Before the room disperses, get four things written down: who takes this forward by name, when they next meet and whether this is on that agenda, what they need from you before then, and whether you may approach a funder using the Society name.'),
  gap(200),
  h2('Who is in the room'),
  p('The Chair of the Board of Practice, the Chairman of the ICT Cluster, and about three further members, convened by the President. The published 23rd Council gives you the names.'),
  tbl(['Office', 'Holder', 'What they want to know'], [
    ['President', 'Arch. Amunsimiire Kenneth', 'Convening the meeting.'],
    ['Chair, Board of Practice', 'Arch. Abdu Wahab Nyanzi', 'Does this expose the Society? Practice standards, documentation quality, professional conduct.'],
    ['Chairman, ICT Cluster', 'Not published. Ask for the name.', 'Is it real, and is it sound? This person delivers the technical verdict, and the demonstration is aimed at them.'],
    ['Chair, Board of Education', 'Arch. Daniel Sekamwa', 'Not currently invited. Training and CPD are his remit, and CPD is your strongest argument. Ask for him.'],
    ['Chair, Board of Research and Development', 'Arch. Clare Ruhweza', 'Not invited. A locally built platform sits squarely in his remit. Worth a follow-up.'],
  ], [1.1, 1.2, 2.4]),
  gap(200),
  note('Do this before the meeting',
    'Ask for Arch. Daniel Sekamwa, Chair of the Board of Education, to be added. The meeting is largely about a training programme, CPD is his remit, and your strongest argument is currently aimed at an empty chair. Asking costs nothing and shows you understand how the Society is organised, which is a credibility deposit before you have said a word.'),
  gap(180),
  h2('The shape of the argument'),
  p('Four movements, in this order. The order matters as much as the content, because leading with the Society\'s problem rather than yours is what makes the rest of it land.'),
  tbl(['#', 'Movement', 'Why it goes here'], [
    ['1', 'A change is coming to your members\' market', 'Makes the problem theirs. A professional body exists to protect its members\' standing, so this is squarely its business.'],
    ['2', 'Here is what exists, including the demonstration', 'Makes it real. Nothing in a slide deck moves a technical audience as much as watching it work.'],
    ['3', 'Here is what the Society gains', 'Makes it worth their while. CPD, a standard they author, and terms that work for a small practice.'],
    ['4', 'What is left, what it costs, and the ask', 'Only after the first three. Money before value is what makes people defensive.'],
  ], [0.4, 1.7, 2.5])
);

/* ================================================= PART 2 SCRIPT */
A(
  h1('Part 2 · What to say'),
  p('The full script, slide by slide. It is also in the speaker notes of the slide deck, so it can be read from the presenter view on the day. Memorise the shape rather than the words, except where Part 9 says the wording is fixed.'),
  p('Twenty minutes of content, forty of discussion. If you are running over, cut from the two market slides. Never cut the honesty slide, the multi-platform slide, or the ask.', { italic: true, color: GREY }),

  h2('Slides 1 and 2 · Opening, and where this is going'),
  p('Hold until everyone is seated. Do not talk over people sitting down, and do not warm up.'),
  say('SAY', [
    '"Mr President, Chairman, thank you for making the time. My name is Davis Mayanja. I am an architect-side BIM specialist, and for the last few years I have been building software here in Kampala for the way we actually work.',
    'I have twenty minutes of material and I would much rather spend forty on your questions, so I will move quickly.',
    'Before I start, let me tell you where this is going, so you are not sitting there waiting for the ask.',
    'I am asking three things. That the Society endorses a BIM training programme for its members. That you name someone in the ICT Cluster as a technical counterpart, so the judgement about whether this is any good is yours and not mine. And that you help me work out how the remaining development gets funded.',
    'I am not asking the Society for money. I am not asking you to decide anything today. And I am not asking for exclusivity or for anything that binds your members.',
    'None of it costs the Society anything. Now let me tell you why I think it is worth your time."',
  ]),
  p('Pause there. The room relaxes, and everything after it is heard differently.', { italic: true, color: GREY }),

  h2('Slides 3 to 5 · What has changed'),
  say('SAY', [
    '"The way a set of building information is expected to be put together has changed, and it changed outside Uganda first.',
    'ISO 19650 is now the accepted international standard for managing project information: how it is named, how it is versioned, who approved what and when, and what condition it is handed over in. It is not a modelling standard. It is an information standard, which is why it matters to architects and not only to software people.',
    'And governments have started to require it. The United Kingdom, the Emirates, Singapore, Germany. BIM is mandated on public work in all of them. I want to be careful here: I am not telling you Uganda has mandated anything. It has not. I am telling you the direction of travel, and it has only ever gone one way.',
    'The region has not kept pace. Kenya is the obvious comparison, with a bigger construction sector and the same regional market. Published research on Kenyan adoption finds it still lagging, and names the consequence as poor coordination of information between the parties on a project. That is a polite way of describing what we all recognise. Drawings that disagree. Schedules that do not match the model. Handover information assembled at the last minute.',
    'Uganda is in the same position with two extra problems. We have no national standard for how architectural information is delivered. And there is nothing on the market built here.',
    'That is a gap. It is also an opening, because the body that moves first writes the standard instead of inheriting one.',
    'What does that cost a practice in your membership? Three things. Not being asked twice, because clients increasingly ask how information will be delivered and a practice with no answer stops appearing on shortlists without ever finding out why. Competing on unequal terms, because the international and regional firms bidding for work here already deliver this way. And paying for it on site, because coordination done by hand is slower and less complete, and what it misses turns up during construction where it costs the most and where the architect usually gets the blame.',
    'That is why I am in front of the Society rather than in front of individual practices. A practice solves this one firm at a time. A professional body can solve it for the profession."',
  ]),
  note('Do not overstate this section',
    'If you claim a Ugandan BIM mandate exists, someone will check and you lose the room. Say "the direction of travel" every time.'),

  h2('Slide 6 · What you have built'),
  p('Ninety seconds. The demonstration does the real work.'),
  say('SAY', [
    '"There are two parts, designed as one system.',
    'StingTools sits inside the design software. It checks a model against data standards automatically, keeps naming and classification consistent, and produces drawings, schedules and quantities from the model instead of by hand. It also assembles handover information while the project is being built, rather than in a panic at the end.',
    'PlanScape is the platform around it. One place for documents, issues and correspondence, so the whole team works from the same record. It is priced per practice, not per person, and anyone outside your office joins free.',
    'Both were started in 2021, here, for the conditions we actually work in."',
  ]),

  h2('Slide 7 · Whatever your members draw in'),
  p('The slide most likely to win the ICT Cluster chair. In a room of architects, "which software does it need" is the question everyone is already holding, and most of them are not on Revit. Answer it before it is asked.'),
  say('SAY', [
    '"Now, the question everyone in this room is waiting to ask. Which software does it need.',
    'The answer is that it does not matter much. There is a full add-in inside Revit, which is the deepest integration. ArchiCAD works by saving to IFC: the model arrives in the platform, and changes come back the other way. Tekla comes in the same way, so your structural engineer is on the same project. And there is a free, open-source route through Blender and Bonsai, which costs nothing at all, no licence, for anybody.',
    'The important part is the last line. Every element keeps the same identity across all four. So a practice on ArchiCAD, a structural engineer on Tekla and a student on free software can all work on one project, and nobody is converting anything by hand or losing track of what is what.',
    'I have tested that with a single element identity resolving across all four at once. It works."',
  ]),
  note('If asked how deep each one goes, be exact',
    'Revit is a native add-in. The others go through IFC. That is a deliberate design decision rather than a gap, because IFC is the open standard and it means nobody is locked in. But there is no native ArchiCAD plug-in and no native Tekla plug-in, and you should say so plainly if asked. Being precise here is what makes the rest of the slide believable.'),
  p('The free route is worth dwelling on with this audience. A member with no software budget, or a student, can take part using Blender and Bonsai at zero licence cost. For a professional body worried about access, that lands harder than any feature.', { italic: true, color: GREY }),

  h2('Slide 8 · Where it stands'),
  p('The slide that decides whether the room trusts you. Volunteer every word before anyone asks.'),
  say('SAY', [
    '"Let me be plain about where this stands.',
    'In production, in daily use on live projects: the document control, the issues, the shared record, the mobile app that works offline, the model checking, and the drawings, schedules, quantities and handover data.',
    'Still in hand: serving many practices from one system, payments, hosting sized for regional use, and an independent security review.',
    'And not yet validated: the engineering calculation engines. They are complete and tested but they have never been taken through independent professional validation, so I only offer them as commissioned work with manual cross-checks alongside. I would rather say that than have an engineer find it out.',
    'The honest summary is one line. It is ready for one practice to use today. It is not yet ready to serve the whole profession at once. That difference is the work that remains."',
  ]),

  h2('Slide 9 · The demonstration'),
  p('Six minutes. Part 3 covers it in full. It is the highest-risk part of the meeting and the highest-reward.'),

  h2('Slides 10 to 13 · What the Society gains'),
  say('SAY', [
    '"The first thing the Society gains is not a favour I am doing you. It is something you are already obliged to do.',
    'Every practising architect in Uganda needs twenty CPD points a year to renew a practising licence. That is the 2019 bye-laws, not a suggestion. So the Society has a permanent obligation to put credible content in front of its members, every year, forever. And this content is hard to source here. Most of what is available locally is a supplier presenting a product. Structured, hands-on training in information standards is not really on offer in Kampala.',
    'It would be your programme. Your accreditation, your name, delivered by one of your own members rather than someone flown in.',
    'The second thing is a standard. Publishing a standard is the easy part; getting anyone to comply is where most of them die, because compliance is expensive and nobody can check it. A document on a website changes nothing about what actually arrives in an inbox. What I would put on the table is different. The naming and classification schemes, the drawing types and templates already exist, and so does a check that runs against a model and says exactly what fails and where. A standard that can be checked automatically is a standard that gets used.',
    'The third thing is that none of this matters unless it works for the practices you actually represent, which are mostly small. It is priced per practice, not per person. Everyone outside your office joins free: client, contractor, quantity surveyor, other consultants, no licence cost to anybody. That matters more than it sounds, because the usual reason coordination software fails on a project here is that nobody will pay for the other parties to be on it. It is billed in shillings. And there is a free route through Blender and Bonsai for anyone with no software budget.',
    'I would also like to offer Society members a discounted rate. I would rather put that on the table now than have you ask me for it later.',
    'And so that you are not endorsing something abstract, this is roughly what a training week looks like. Day one is why information standards exist at all. Day two is modelling to a standard, in whatever software people already use. Day three is checking and coordinating. Day four is getting the work out: drawings, schedules, quantities. Day five is handover, and an exercise they complete and take away. About twenty-five members at a time, hands-on throughout.',
    'The shape is mine, but the content should be yours. If the Board of Education wants it structured differently, I would rather build what you would actually accredit."',
  ]),
  note('On CPD, ask rather than assert',
    'Say: "I do not know what accreditation would require for a course like this, or what points it would carry, or what your members currently pay for CPD. You do. That is one of the things I would like your guidance on." Asking makes them co-owners of the idea. Claiming you already know makes you a supplier.'),

  h2('Slide 14 · The limits'),
  p('This slide earns you more than any other. Do not rush it, and do not apologise through it.'),
  say('SAY', [
    '"Three things I want you to hear from me rather than find out.',
    'First, the deepest automation is inside Revit. The other tools connect through IFC, which is the open standard and is genuinely the right answer because it means nobody is locked in. But there is no native ArchiCAD plug-in and no native Tekla plug-in, and I am not going to imply otherwise.',
    'Second, the engineering calculation engines have never been independently validated. They work and they are tested, but I only offer them as commissioned work with manual checks in parallel. If the Society put engineers on validating them, that sign-off would carry real weight.',
    'Third, it is not finished, and I said that earlier.',
    'You would have found all three of those in about four minutes. I would rather you heard them from me."',
  ]),

  h2('Slides 15 to 17 · The cost and the ask'),
  say('SAY', [
    '"So what does finishing it take. Thirty-six thousand two hundred dollars to complete the platform: serving many practices safely, payments, an independent security review, and testing. Twelve thousand for twelve months of hosting sized to grow. Fifteen thousand for three training cohorts of about twenty-five people each. With contingency that is seventy-two thousand six hundred and eighty dollars, about two hundred and sixty-nine million shillings.',
    'But here is the part I want you to hear. Those three are separable. Nobody has to find the whole figure. The training programme is fifteen thousand dollars, it stands on its own, it is the part that can start first, and it is the part that would prove the rest.',
    'Which brings me to what I am actually asking for. There are routes to funding this that a professional body can open and I cannot. A development partner will fund the Society for capacity building in the construction sector; they will not fund me. A sponsor will pay to be associated with your CPD programme; they will not pay to be associated with mine. And members already budget for CPD, because the law requires them to.',
    'I have written a page on each of these so you are not starting from a blank sheet. What I would like is your view on which are worth pursuing, and who I should be talking to.',
    'So, three things. One: endorse the training programme as a Society initiative for your members, and let us find out together whether it can be accredited for CPD. Two: name someone in the ICT Cluster as a technical counterpart. Three: help me find the route to fund the rest. Not your money, your judgement about which door to knock on, and if you are willing, an introduction.',
    'None of those costs the Society anything. Thank you. I would rather spend the remaining time on your questions than on my slides."',
  ]),
  p('Hand the funding routes page over as you reach the funding slide. Physically giving them something changes the register of the conversation. Then stop talking and let the silence work.', { italic: true, color: GREY })
);

/* ================================================= PART 3 DEMO */
A(
  h1('Part 3 · The demonstration'),
  note('The rule',
    'Nothing is demonstrated live that has not been proven twice, end to end, on the same setup you will actually run on. A demonstration that half works in front of the ICT chair is worse than a recording that works.'),
  gap(180),
  h2('What to show'),
  p('These are architects. Show architectural work, not engineering. Every person in the room has done each of these by hand at two in the morning, which is exactly why it lands.'),
  bullet('A real model. Say so: "this is a real project, not a sample file."'),
  bullet('Tagging and the check. Run it, and show what it catches.'),
  bullet('A drawing and a schedule coming out consistent.'),
  bullet('A quantity taken from the model.'),
  bullet('If the platform half is solid: the shared record, an issue raised, the mobile app working offline.'),
  gap(120),
  p('Do not demonstrate the calculation engines. They are the one part you have told the room is unvalidated, and showing them invites the question you least want.', { italic: true, color: GREY }),
  gap(120),
  note('Worth showing if the setup allows it',
    'A model from one tool and a model from another sitting in the same project, with the same element identity across both. It takes thirty seconds and it proves the multi-platform claim rather than asserting it. If it is not reliable on the day, leave it out and say the claim is tested rather than showing it half working.'),
  h2('The rehearsal checklist, finished a week before'),
  tbl(['#', 'Step'], [
    ['1', 'Write the demonstration path down as numbered steps, not in your head.'],
    ['2', 'Confirm which setup it runs against. Verify it rather than assuming.'],
    ['3', 'First run: the full path, timed. Write down every failure.'],
    ['4', 'Fix or cut whatever failed. Cutting is a legitimate and often correct outcome.'],
    ['5', 'Second run: the full path, clean, with no interventions.'],
    ['6', 'Record the successful run. That is your fallback whatever you decide.'],
    ['7', 'Test the projector, the resolution and the cable. Assume there is no internet.'],
    ['8', 'Choose one of the three options below, and stop changing your mind.'],
  ], [0.3, 3.7]),
  gap(180),
  h2('Choose one and commit'),
  tbl(['Option', 'Choose it when'], [
    ['Everything live', 'Both runs were clean end to end on the real setup.'],
    ['Modelling live, platform recorded', 'The modelling side is solid and the platform side is not. This is the most likely outcome and it is still strong, because the modelling half is what an architect can judge on sight.'],
    ['Recorded only', 'Neither run was clean. There is no shame in this. Show the recording and say plainly that a live version is a few weeks away.'],
  ], [1.2, 2.8]),
  gap(180),
  note('If something fails in the room',
    'Say so, move on, and use the recording. Do not try to fix it in front of everyone. "That is the part I told you is still being finished" is a recoverable sentence, and it is consistent with what you said earlier. Silence while you fiddle with a laptop is not recoverable, and it is the thing they will remember.',
    TINT)
);

/* ================================================= PART 4 VALUE */
A(
  h1('Part 4 · Where this matters to the Society'),
  p('Not to individual architects, but to the institution. A professional body says yes to things that help it discharge its own mandate.'),

  h2('1 · Statutory CPD'),
  p('Every practising architect in Uganda needs 20 CPD points a year to renew a practising licence, under the Architects Registration (Continuing Professional Development) Bye Laws gazetted in 2019. This is not optional and it does not go away.'),
  p('The Society therefore has a permanent, recurring obligation to put credible content in front of its members, and information standards is exactly the content that is hardest to source locally. Most CPD available to Ugandan architects is a supplier presentation.'),
  p('It is also a funding route rather than only a benefit. Members pay for CPD because the law requires them to, so course fees can make delivery self-sustaining.', { bold: true }),

  h2('2 · Members getting locked out of their own market'),
  p('This is the reason it is worth doing now, and it is the argument that makes the problem theirs. A professional body exists to protect its members\' standing and livelihood, so a change in what clients expect that its members are not ready for is precisely its business.'),
  p('The Society can either find out about it when members start losing tenders, or get ahead of it. Frame it as a warning you are bringing them rather than an opportunity you are selling.'),

  h2('3 · A standard the Society authors'),
  p('The Board of Practice owns practice standards. Publishing a standard is easy and getting compliance is not, because compliance is expensive and unverifiable.'),
  p('What is different here is a working implementation rather than a document. Naming and classification schemes, drawing types, title blocks and templates, and a check that runs against a model and reports what fails. A standard that can be checked automatically is a standard that gets used.'),

  h2('4 · It works with whatever members already use'),
  p('This is the point most likely to matter in a room of architects, because a good proportion of them are not on Revit.'),
  tbl(['Tool', 'How it connects', 'What that means for a member'], [
    ['Revit', 'A full add-in inside Revit', 'The deepest automation. Tagging, checking, drawings, schedules, quantities, handover.'],
    ['ArchiCAD', 'Save to IFC, and changes come back', 'An ArchiCAD practice works normally and still takes part in the shared project.'],
    ['Blender with Bonsai', 'A free, open-source extension', 'No licence cost at all. A member with no software budget, or a student, can take part.'],
    ['Tekla', 'Through IFC', 'The structural engineer is on the same project as the architect.'],
  ], [0.9, 1.4, 2.5]),
  gap(140),
  p('The part that matters is that every element keeps the same identity across all four, so one project can carry work from all of them without anyone converting anything by hand.', { bold: true }),
  p('Be exact about depth if asked. Revit is a native add-in and the others go through IFC. That is a deliberate design decision, because IFC is the open standard and it means nobody is locked in. There is no native ArchiCAD or Tekla plug-in.', { italic: true, color: GREY }),

  h2('5 · Terms that work for a small practice'),
  bullet('Priced per practice rather than per person.'),
  bullet('Everyone outside the office joins free, so client, contractor and quantity surveyor cost nothing to include.'),
  bullet('Billed in shillings.'),
  bullet('Works offline on site.'),
  bullet('A free route exists for anyone with no software budget.'),
  p('Offer a discounted member rate before they ask for one. Offering it unprompted reads as goodwill; conceding it under pressure reads as a margin you were hiding. Do not name a percentage in the room, only the willingness to agree one.'),

  h2('6 · Documentation quality and professional risk'),
  p('Board of Practice language: competence, evidence, and fewer disputes. An automatic check catches inconsistency before issue. Revision control means the record of what was issued, when and to whom exists without anyone maintaining it by hand. A coordination report is evidence that a conflict was raised and when.'),
  p('It also helps members defend their fees, because demonstrable deliverables justify fees in a market where architects are constantly asked to discount.'),

  h2('7 · The graduate pipeline'),
  p('Board of Education again. Student and graduate access means graduates arrive employable and practices carry less training cost. The Council has Graduate, Student and Technician representatives, so these are represented constituencies rather than an abstraction. The free Blender route makes student access cost nothing at all.'),
  p('Be transparent that this is mutual. It also builds the platform\'s user base, and saying so costs nothing while pre-empting the observation.', { italic: true, color: GREY }),

  h2('8 · A local product, locally hosted'),
  bullet('Built in Kampala, for Ugandan conditions.'),
  bullet('Can be hosted in Uganda, so data need not leave the country.'),
  bullet('Open, exportable formats throughout, so nothing is trapped.'),
  bullet('Subscription spend stays in the domestic economy rather than being exported.'),

  h2('9 · Uganda-specific content'),
  p('Regional defaults by Ugandan region, covering wind speed, seismic zone, soil bearing capacity, design rainfall and live loads, alongside local code defaults, shilling budgets and offline working. This is the Society\'s own operating context encoded in software, and it is the clearest answer to "why not just use what everyone else uses".')
);

/* ================================================= PART 5 TRAINING */
A(
  h1('Part 5 · The training programme'),
  p('The primary ask is that the Society endorses this, so it needs to be concrete. Nobody endorses a training programme they cannot picture.'),
  h2('A week, in outline'),
  tbl(['Day', 'Subject', 'What members do'], [
    ['Day 1', 'Why information standards exist', 'ISO 19650 in plain terms: naming, versions, approvals, handover. What a client is actually asking for when they ask for BIM.'],
    ['Day 2', 'Modelling to a standard', 'Working in their own software, whether Revit, ArchiCAD or the free route, to a shared naming and classification scheme.'],
    ['Day 3', 'Checking and coordinating', 'Running the check, reading what it reports, raising and closing issues across a team.'],
    ['Day 4', 'Getting the work out', 'Drawings, schedules and quantities from the model. Where the time actually goes on a real job.'],
    ['Day 5', 'Handover, and an exercise', 'Producing handover information, then a hands-on exercise each member completes and takes away.'],
  ], [0.5, 1.5, 3.0]),
  gap(180),
  h2('Shape of delivery'),
  tbl(['Item', 'Proposed'], [
    ['Size', 'About 25 members per cohort.'],
    ['Format', 'Hands-on throughout, on members\' own machines and in their own software.'],
    ['Cohorts', 'Three, at USD 5,000 each. The first proves the format; the second and third scale it.'],
    ['Venue', 'Provided or arranged by the Society. Projector, screen and connectivity are budgeted.'],
    ['Materials', 'Printed workbook, sample project files, and a written standard members keep.'],
    ['Who delivers', 'A member of the Society, not an imported trainer.'],
  ], [1, 3]),
  gap(200),
  note('Hand the content over',
    'Say plainly that the shape is yours but the content should be theirs: "If the Board of Education wants it structured differently, I would rather build what you would actually accredit." That single sentence is the one most likely to turn this from your proposal into their programme, which is the whole point of the meeting.'),
  gap(180),
  h2('What you need to ask them'),
  bullet('What accreditation would require for a course like this, and what points it would carry.'),
  bullet('What members currently pay for CPD in Kampala.'),
  bullet('Whether the Society would host, promote, or co-brand it.'),
  bullet('Who from the Board of Education should shape the content.'),
  p('You do not know any of these, and they do. Asking is the move that makes them co-owners.', { italic: true, color: GREY })
);

/* ================================================= PART 6 LIMITS */
A(
  h1('Part 6 · What to disclose yourself'),
  p('Say all of these before anyone asks. A room of architects will find them in about four minutes, and disclosed they read as honesty while discovered they read as a problem.'),
  tbl(['Limit', 'How to put it'], [
    ['The deepest automation is inside Revit', 'The other tools connect through IFC, which is the open standard and means nobody is locked in. But there is no native ArchiCAD plug-in and no native Tekla plug-in. Say that plainly. Being precise here is what makes the rest of the multi-platform claim believable.'],
    ['The calculation engines are unvalidated', 'Complete and tested, never taken through independent professional validation. Offered only as commissioned work with manual cross-checks alongside. Then invite the Society\'s engineers to validate them, which turns a weakness into a role for them.'],
    ['It is not finished', 'Ready for one practice today, not yet ready to serve the whole profession at once. Use the wording in Part 9.'],
    ['There is no Ugandan mandate', 'Say "the direction of travel". Never imply one exists, because someone will check.'],
  ], [1.3, 2.7])
);

/* ================================================= PART 7 FUNDING */
A(
  h1('Part 7 · Funding routes'),
  p('Print this and hand it over. Asking for help finding funding invites a sympathetic nod and nothing else. Six named routes, four of which only the Society can open, invites a decision.'),
  h2('What is being funded'),
  tbl(['Item', 'Cost'], [
    ['Platform completion: serving many practices, payments, security review, testing', 'USD 36,200'],
    ['Hosting sized for regional use, twelve months', 'USD 12,000'],
    ['Training programme, three cohorts of about 25', 'USD 15,000'],
    ['Subtotal', 'USD 63,200'],
    ['Contingency, 15 per cent', 'USD 9,480'],
    ['Total', 'USD 72,680, about UGX 269 million'],
  ], [3.2, 1.0]),
  gap(160),
  p('These are separable, and saying so makes the ask far easier. Nobody has to find the whole figure. The training programme can be funded on its own and start first, so a funder who can only reach USD 15,000 is still a funder.', { bold: true }),
  h2('The routes'),
  tbl(['Route', 'Why it works', 'What the Society unlocks'], [
    ['1. Member course fees', 'CPD is required by law, so members already budget for it. This is not asking them to fund a platform. It is selling them something they are obliged to buy.', 'Accreditation, the member list, and its name on the programme. None of which costs it money.'],
    ['2. Development partners', 'The major development banks and agencies fund construction-sector capacity building and digital skills.', 'A professional body is a fundable grantee and a young private company is not. This is the clearest thing the Society has that you do not.'],
    ['3. Industry sponsorship', 'Cement, steel, roofing and glazing manufacturers, banks and insurers routinely sponsor professional CPD for access to the profession.', 'Convening power and existing corporate relationships. Sponsors pay for access to members, and only the Society can grant it.'],
    ['4. Government partnership', 'If public procurement moves toward digital delivery requirements, capacity building becomes a public need and the Society is the obvious delivery partner for the profession.', 'Standing to be that partner, which an individual does not have. Present it as something to prepare for, never as funding that exists.'],
    ['5. Innovation and ICT funding', 'A locally built software product with regional export potential fits national digital-economy priorities.', 'Institutional backing, which materially strengthens an application from a small developer. Check first whether these fund product completion or only research.'],
    ['6. University partnership', 'A joint application with Makerere or Kyambogo reaches research funding neither party reaches alone, with curriculum integration as a deliverable.', 'The Boards of Education and of Research and Development are the natural sponsors, and neither chair is currently in the meeting.'],
  ], [0.9, 2.1, 1.9]),
  gap(180),
  say('HOW TO PUT IT IN THE ROOM', [
    '"I am not asking the Society for money.',
    'What I am asking is this. There are routes to funding this that a professional body can open and I cannot. A development partner will fund the Society for capacity building in the construction sector; they will not fund me. A sponsor will pay to be associated with your CPD programme; they will not pay to be associated with mine. And members already have to buy CPD every year.',
    'I have listed the ones I can see. I would like your view on which are worth pursuing, and who I should be talking to."',
    'Then hand over the page and stop talking.',
  ]),
  gap(160),
  p('A resolution to explore funding is worth very little. A name and an introduction is worth a great deal. Leave with a person to contact rather than a good feeling.', { bold: true })
);

/* ================================================= PART 8 Q&A */
A(
  h1('Part 8 · The questions you will be asked'),
  p('Rehearse these out loud. A prepared answer delivered hesitantly reads worse than an honest "I do not know" delivered calmly.'),

  h3('Q1 · "Which software does it need?"'),
  p('The first question in a room of architects, and most of them are not on Revit. Answer it on the slide before it is asked.'),
  say('ANSWER', [
    '"It does not matter much. There is a full add-in inside Revit, which is the deepest integration. ArchiCAD works by saving to IFC, and changes come back the other way. Tekla comes in the same way. And there is a free, open-source route through Blender and Bonsai that costs nothing at all.',
    'Every element keeps the same identity across all four, so a practice on ArchiCAD, an engineer on Tekla and a student on free software can work on one project without anyone converting anything by hand."',
  ]),
  p('If pressed on depth: Revit is a native add-in, the others go through IFC, and there is no native ArchiCAD or Tekla plug-in. Say it plainly. The precision is what makes the rest credible.', { italic: true, color: GREY }),

  h3('Q2 · "Is the platform finished?"'),
  say('ANSWER', [
    '"The coordination core is in production and in daily use on live projects: documents, issues, the shared record, the mobile app. The Revit add-in is in production use for tagging, checking, drawings, schedules and quantities.',
    'The work still in hand is a different kind of thing. Turning a platform that works for one practice into one that can serve the whole profession at once: many practices on one system, payments, regional hosting, an independent security review, and the training programme.',
    'Ready for one practice today. Not yet ready to serve everyone at once. That is the honest position, and it is why the completion figure is about thirty-six thousand dollars and not half a million."',
  ]),

  h3('Q3 · "Who else uses it?"'),
  p('The weakest area on paper. Handle it with what is true, then turn it around.'),
  say('ANSWER', [
    '"It is in daily use on the project I am currently information manager on: a six-building institutional campus in central Kampala, more than eight design disciplines, working to ISO 19650 under an international client\'s standards.',
    'What I do not have is twenty Ugandan practices using it. That is precisely what the Society\'s endorsement and a first training cohort would build."',
  ]),

  h3('Q4 · "What happens if you are unavailable?"'),
  say('ANSWER', [
    '"Five things, and they would be written into any agreement. Everything you hold opens without my platform, because it is all in open formats. Source code and configuration can go into escrow with your solicitors, released on defined events. The platform can be self-hosted on your own infrastructure. Standards files and configuration are handed over as deliverables. And the route to moving onto a mainstream commercial product is priced, so leaving is a budget decision rather than a rescue."',
  ]),

  h3('Q5 · "Does this commit the Society to anything?"'),
  say('ANSWER', [
    '"No. I am not asking for money, I am not asking for a decision today, and I am not asking for exclusivity or for anything binding on your members.',
    'What I am asking for is an endorsement of a training programme, a technical counterpart, and your help thinking about funding. If the Society later wanted a formal arrangement, that should go through your own governance in the normal way, and I would expect it to."',
  ]),

  h3('Q6 · "Why would a member not just buy the international product?"'),
  say('ANSWER', [
    '"For authoring, they should. Those tools are unmatched and nothing I do replaces them. The difference is everything around authoring: coordination, document control, field work, quantities, handover data.',
    'The international products charge per user. This charges per practice, with everyone outside the office joining free, so consultants, contractors and clients cost nothing to include. For a ten-person practice here that is the difference between viable and not. And it is priced in shillings, hostable in Uganda, and supported from Kampala."',
  ]),

  h3('Q7 · "What would the Society get out of it commercially?"'),
  say('ANSWER', [
    '"That is worth agreeing properly rather than in a meeting. What I would put forward is a discounted rate for members, a share in what the training programme earns, and the Society\'s name on the programme itself.',
    'I have views on the shape of that, but I would rather hear yours first and then write it down properly."',
  ]),
  p('Do not name a percentage in the room. Agree that it is worth agreeing.', { italic: true, color: GREY }),

  h3('Q8 · "Where do your numbers come from?"'),
  say('ANSWER', [
    '"The completion figure is built from an itemised scope of work with a named team and a four-month schedule, and it carries its own contingency. I am happy to walk anyone through the line items.',
    'The uptake projections are illustrative planning estimates and I would describe them as nothing more than that. Before this goes near a funder they need a proper bottom-up basis, and that is work I would want to do with the Society\'s own membership data rather than guess at."',
  ]),

  h3('Q9 · "What do you actually want from us today?"'),
  p('Have this word-perfect. It is the close, and the only part they will repeat to anyone who was not there.'),
  say('ANSWER', [
    '"Three things, none of which cost the Society money.',
    'One: endorse the training programme as a Society initiative for members, and let us look at whether it can be accredited for CPD.',
    'Two: name someone in the ICT Cluster as technical counterpart, so the judgement is yours and not mine.',
    'Three: help me find the route to fund the rest. Not your money, your judgement on which door to knock on, and ideally an introduction."',
  ]),

  h2('Questions you should ask them'),
  p('Ending on your questions makes it a conversation between colleagues rather than a pitch, and every one of these closes something you genuinely need.'),
  numbered('What would accreditation require for a course like this, and what points would it carry?'),
  numbered('What do members currently pay for CPD?'),
  numbered('Who from the Board of Education should shape the content?'),
  numbered('Who would you want as technical counterpart in the ICT Cluster?'),
  numbered('Does the Society have existing relationships with any development partner, and who holds them?'),
  numbered('Would the Society share its registration details, founding year and membership numbers?'),
  numbered('Has the Society tried anything on BIM adoption before, and what stalled it?'),
  numbered('What would make this an easy yes for you, and what would make it an easy no?')
);

/* ================================================= PART 9 WORDING */
A(
  h1('Part 9 · Wording to keep consistent'),
  p('Consistency across every document and conversation is worth more than any individual phrasing. These are the lines to keep the same wherever they come up.'),

  h2('Where the platform stands'),
  say('FIXED WORDING', [
    'The coordination core is in production and in daily use on live projects: the shared project record, document control, issues, dashboards and the mobile application. StingTools is in production use for tagging, model checking, drawing and schedule production, quantities and handover data.',
    'The work still in hand is of a different kind. Turning a platform that works for one practice into one that can serve many practices across several countries: many organisations on one system, mobile money payments, hosting sized for the region, and an independent security review.',
    'Ready for one practice today. Not yet ready to serve the whole profession at once.',
  ]),
  bullet('Never say the platform is complete or finished, or imply the remaining work is optional polish.'),
  bullet('Never say it does not work yet either. It does. Both overstatements are avoidable, and the second is the one people reach for when they are trying to be modest.'),

  h2('Which tools it works with'),
  say('FIXED WORDING', [
    'A full add-in inside Revit. ArchiCAD and Tekla connect through IFC, the open standard, with changes written back. A free, open-source route through Blender and Bonsai. Every element keeps the same identity across all four.',
    'The deepest automation is in Revit. There is no native ArchiCAD plug-in and no native Tekla plug-in, and that is a deliberate design decision rather than a gap, because IFC means nobody is locked in.',
  ]),

  h2('Origin'),
  say('FIXED WORDING', [
    'PlanScape and StingTools were built starting in 2021, worked out from first principles. They were not used on the Tilenga oil development project, but seeing that project\'s coordination and design problems up close, on a programme of that size, is what accelerated the research behind them from 2023 onward.',
  ]),
  p('Say "not used on Tilenga" plainly. The proximity is the honest and interesting part of the story, and any ambiguity about it becomes a liability as the platform\'s profile grows.', { italic: true, color: GREY }),

  h2('The Society\'s role'),
  p('Only after the Society has actually agreed to it:'),
  say('AFTER AGREEMENT', ['The Society\'s national BIM initiative, developed by one of its members.']),
  p('Until then, and this is the position for the presentation:'),
  say('UNTIL THEN', ['A BIM platform built in Kampala by a member of the Society, proposed for adoption as the Society\'s national BIM initiative.']),
  p('Do not use the first form in any material before the Society has endorsed it. Using it prematurely is the fastest way to lose a professional body\'s trust.', { italic: true, color: GREY }),

  h2('Ownership'),
  say('FIXED WORDING', [
    'PlanScape and StingTools are owned by their developer. Nothing proposed transfers ownership. The Society would receive a training programme for its members, a discounted rate for members, and a share in what the programme earns.',
  ])
);

/* ================================================= PART 10 CHECKLIST */
A(
  h1('Part 10 · Checklist'),
  h2('Now, before the date is agreed'),
  bullet('Propose two or three dates.'),
  bullet('Ask for Arch. Daniel Sekamwa, Chair of the Board of Education, to be added.'),
  bullet('Ask for the name of the ICT Cluster chairman.'),
  bullet('Start the demonstration rehearsals. They take longer than anyone expects.'),

  h2('A week before'),
  bullet('Rehearsal checklist finished, and the live or recorded decision made and committed to.'),
  bullet('Recording of the successful run made and tested.'),
  bullet('Funding routes page laid out on your letterhead, six copies printed.'),
  bullet('One-page training outline printed, six copies.'),

  h2('Three days before'),
  bullet('Read Part 2 out loud, twice, standing up.'),
  bullet('Rehearse Q1, Q2, Q3 and Q9 until they are fluent.'),
  bullet('Check every number you plan to say out loud against the written source.'),

  h2('The day before'),
  bullet('Confirm attendance, venue and start time.'),
  bullet('Test projector, resolution and cable at the venue if possible.'),
  bullet('Load everything offline. Assume there is no internet.'),
  bullet('Charge the laptop, and pack the charger and an adapter.'),
  bullet('Re-read Part 9 last thing.'),

  h2('On the day'),
  bullet('State the three asks in the first two minutes.'),
  bullet('Volunteer the honesty slide before anyone asks.'),
  bullet('Answer the software question on the slide, not in the Q&A.'),
  bullet('Hand over the funding routes page, then stop talking.'),
  bullet('Ask your eight questions.'),
  bullet('Write down who said what, immediately afterwards, before it fades.'),

  h2('Within 24 hours'),
  bullet('Thank-you email with the three asks restated in writing.'),
  bullet('Send anything you promised, by the date you promised it.'),
  bullet('Record every answer you were given, especially on accreditation and governance.'),
  bullet('If you were given a name or an introduction, act on it this week.')
);

/* ================================================= PART 11 OPEN */
A(
  h1('Part 11 · Still open'),
  p('Things that are not settled, and should not be improvised in the room. If one of these comes up and you do not have the answer, say so.'),
  tbl(['Question', 'Who answers it'], [
    ['Who chairs the ICT Cluster. This person delivers the technical verdict and half the presentation is aimed at them.', 'Ask when confirming the date'],
    ['What CPD accreditation requires, what points a course carries, and what members currently pay.', 'The Society, in the meeting'],
    ['Whether the Society would host, promote or co-brand the training programme.', 'The Society, in the meeting'],
    ['What the Society receives commercially, if a formal arrangement is ever wanted.', 'Both, later and in writing'],
    ['Which funding route is worth pursuing first, and who to approach.', 'The Society, in the meeting'],
    ['What can be demonstrated reliably on the day.', 'Only the rehearsals answer this'],
  ], [3.0, 1.2]),
  gap(220),
  note('A closing note',
    'The strongest thing in this whole package is not the platform. It is that you are willing to say what is not finished, what is not validated, and what you do not know. A room of senior professionals has heard a great many confident pitches, and very few of them included the weaknesses. That is what they will remember, and it is what makes people want to help.'),
);

const doc = new Document({
  creator: 'Davis Mayanja',
  title: 'Presentation guide',
  numbering: { config: [{ reference: 'nums', levels: [{ level: 0, format: 'decimal', text: '%1.',
    alignment: AlignmentType.START,
    style: { paragraph: { indent: { left: 460, hanging: 300 } } } }] }] },
  styles: { default: { document: { run: { font: BF, size: 21, color: '2A2E34' } } } },
  sections: [{
    properties: { page: { margin: { top: 1100, bottom: 1100, left: 1100, right: 1100 } } },
    headers: { default: new Header({ children: [new Paragraph({ alignment: AlignmentType.RIGHT,
      spacing: { after: 200 }, children: [new TextRun({ text: 'Presentation guide',
        font: BF, size: 16, color: '9AA0A6' })] })] }) },
    footers: { default: new Footer({ children: [new Paragraph({ alignment: AlignmentType.RIGHT,
      children: [new TextRun({ text: 'Page ', font: BF, size: 16, color: '9AA0A6' }),
        new TextRun({ children: [PageNumber.CURRENT], font: BF, size: 16, color: '9AA0A6' })] })] }) },
    children: kids,
  }],
});

Packer.toBuffer(doc).then(function (buf) {
  fs.writeFileSync(process.argv[2], buf);
  console.log('written', process.argv[2]);
});
