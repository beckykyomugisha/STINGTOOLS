const d = require('docx');
const fs = require('fs');
const {
  Document, Packer, Paragraph, TextRun, HeadingLevel, AlignmentType,
  Table, TableRow, TableCell, WidthType, BorderStyle, ShadingType,
  Header, Footer, PageNumber,
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
            children: [new TextRun({ text: line, font: BF, size: 19, bold: i === 0,
              color: '2A2E34' })] }); }) }); }) });
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

/* ============================================ COVER */
A(
  new Paragraph({ spacing: { before: 1600, after: 100 }, children: [new TextRun({
    text: 'UGANDA SOCIETY OF ARCHITECTS', font: BF, size: 20, bold: true, color: ACCENT,
    characterSpacing: 60 })] }),
  new Paragraph({ spacing: { after: 160 }, children: [new TextRun({
    text: 'Presentation guide', font: HF, size: 60, bold: true, color: INK })] }),
  new Paragraph({ spacing: { after: 400 }, children: [new TextRun({
    text: 'A progress report to the Council', font: HF, size: 28, italic: true, color: GREY })] }),
  box([
    new Paragraph({ spacing: { after: 100 }, children: [new TextRun({
      text: 'PlanScape and StingTools, built in Kampala, two years in', font: BF, size: 22,
      bold: true, color: INK })] }),
    new Paragraph({ spacing: { after: 0 }, children: [new TextRun({
      text: 'Prepared for Davis Mayanja  ·  September 2026', font: BF, size: 20, color: GREY })] }),
  ], TINT),
  gap(400),
  p('You are not pitching. You are a member of the Society showing the Council what you have built, how far it has got, what it would take to finish, and where you think the whole area is going. Then you are asking what they make of it.', { size: 22 }),
  gap(140),
  p('That posture is the single most important thing in this document. Everything else follows from it.', { size: 22, bold: true })
);

/* ============================================ HOW TO USE */
A(
  h1('How to use this'),
  tbl(['When', 'What to read'], [
    ['A week before', 'Part 1, then Part 5. Start the demonstration rehearsals.'],
    ['The night before', 'Part 2 out loud, twice. Part 10 out loud once. Part 11 last thing.'],
    ['On the day', 'Part 12. Then put it away and talk to people.'],
  ], [1, 3]),
  gap(200),
  note('The one rule',
    'Never invent a number, a date, or a client. "I do not know, and I will come back to you" is a complete and respectable answer in a room of professionals. A fabricated one is the only mistake here you cannot recover from.')
);

/* ============================================ PART 1 THE ROOM */
A(
  h1('Part 1 · Reading the room'),
  p('This part is about posture rather than content, and it matters more than the slides do.'),

  h2('You are a colleague, not a supplier'),
  p('You are a member of this Society presenting to its Council. That is a much stronger position than an outsider pitching a professional body, and the whole presentation is built to sit in it. A supplier asks for things. A colleague shows what they have been working on and asks what people make of it.'),
  p('The practical difference is that you never have to close. There is no moment where you need a yes, which means there is no moment where the room has to defend itself against you. That relaxed quality is worth more than any argument on any slide.'),

  h2('Three things not to say'),
  tbl(['Do not say', 'Why'], [
    ['Anything about two years without earning, or about money being tight', 'In this room that reads as need, and need makes people cautious rather than generous. Two years of investment is a credential. Two years of hardship is a different conversation and it belongs somewhere else, with someone else.'],
    ['"I need" or "I am asking for"', 'The presentation deliberately has no ask. Introducing one halfway through undoes the posture the rest of it is built on.'],
    ['Anything you have not verified', 'These are senior professionals with long memories. One overstatement checked and found wanting costs more than the whole presentation earns.'],
  ], [1.3, 2.7]),

  h2('Strong proposals, held lightly'),
  p('You said you do not want to look as though you have already decided everything, and that instinct is right. But there is a failure mode on the other side too: arriving with no view at all produces a pleasant conversation and no follow-up.'),
  p('The balance is to bring clear proposals and hold them loosely. Say what you think, then say plainly that you would rather hear their view and build to that. People support what they helped shape, so leave them something to shape.'),
  say('THE PHRASE THAT DOES THIS WORK', [
    '"That is my reading of it. If the Council sees the priorities differently, I would rather build to that."',
  ]),

  h2('The money question, and how to let them ask it'),
  p('You are showing what remains and what it costs, so the money is on the table. The important thing is that you put it there as a fact and not as a request.'),
  p('State the figure, say the three parts are separable, and stop. In a room like this somebody will almost always then ask how you are funding it. That question landing from them is far stronger than the same subject raised by you, because it turns a request into an offer of help.'),
  say('WHEN THEY ASK', [
    '"I am looking at several routes. Some of them a professional body can open and an individual cannot, so I would value your thinking on which are worth pursuing."',
  ]),
  p('Then stop again, and let them offer or not. Do not push, and do not fill the silence.', { italic: true, color: GREY }),

  h2('They already know BIM'),
  p('Do not explain BIM, do not define ISO 19650, and do not make the case for why coordination matters. Everyone in that room has heard it. Explaining it to them reads as either condescension or padding, and both cost you.'),
  p('Open instead with what they have not seen. The second slide exists entirely for this: six things that are new, delivered fast, before anyone has decided what kind of presentation this is.'),

  h2('Remember what they are'),
  p('They are designers. They will read the craft of the presentation itself as evidence about the craft of the software, whether or not they mean to. Keep it clean, keep it typographic, and do not fill it up. Restraint reads as confidence in this room specifically.'),
  gap(140),
  note('Answer, then stop',
    'The commonest mistake in a meeting like this is answering a short question at length. You start from a strong position, keep talking, add a caveat, add another, and end up somewhere weaker than where you began. Answer the question that was asked, then be quiet. If they want more they will ask.'),

  h2('What actually counts as a good outcome'),
  p('Not a decision. You are not asking for one, so getting one would be a surprise rather than a success. What you want is three things, and all three come from them rather than from you:'),
  numbered('Something they suggest you build, change, or think about differently.'),
  numbered('Somebody they say you should talk to, inside the Society or outside it.'),
  numbered('An interest in seeing it again.'),
  p('Write all three down before you leave the building. A meeting that produces any of them has done its job.', { bold: true })
);

/* ============================================ PART 2 SCRIPT */
A(
  h1('Part 2 · What to say'),
  p('Slide by slide. It is also in the speaker notes of the deck, so it can be read from presenter view on the day. Learn the shape rather than the words.'),
  p('Around twenty-five minutes of material, plus the demonstration. The discussion afterwards is the point, so do not fill the hour.', { italic: true, color: GREY }),

  h2('1 · Opening'),
  say('SAY', [
    '"Mr President, Chairman, thank you for the time. I have spent about two years building something and I thought it was time I showed the Council properly rather than kept describing it in passing.',
    'What I want to show you is two things really. A working BIM platform, and the standards that go with it. Then I would like to put a view to you about where I think all of this is heading, and hear whether you see it the same way."',
  ]),

  h2('2 · Two halves of the same thing'),
  p('The frame everything else sits inside. Do not rush it.'),
  say('SAY', [
    '"There is a platform: tools inside the modelling software, and a shared record around it. Checking, classification, drawings, schedules, quantities, handover data, issues and correspondence, all in one flow rather than five separate products.',
    'And there are standards: naming, classification, drawing conventions, what level of detail belongs at what stage, what a handover has to contain. Written down, and checkable by the software rather than only by argument.',
    'Most tools give you the first half. The second half usually gets left to each practice to invent for itself, which is a large part of why our drawings do not talk to each other."',
  ]),

  h2('3 · Eight things you may not have seen'),
  p('Ninety seconds, fast. This decides what kind of attention you get for the rest of the session.'),
  say('SAY', [
    '"You all know what BIM is, so I will not explain it. Let me show you eight things I think are new, and then take them one at a time.',
    'You can talk to the model in plain English. You can hold a live meeting inside the model rather than over a screen share. One element keeps the same identity across four modelling tools, including a free one. You can raise an issue by long-pressing the element on your phone. A model gets a compliance score. Drawings, schedules and quantities come out of the model. Handover data is assembled as you build. And Ugandan conditions are built in as defaults.',
    'Any one of those is useful on its own. Together they change what a working day looks like."',
  ]),

  h2('4 · One flow, from model to handover'),
  say('SAY', [
    '"What I was trying to build was one chain rather than five products with exports between them.',
    'You model in whatever you use. Classification and naming get applied to a standard. The check runs and gives you a score and a report of what fails. Issues sit in one record the team shares. Drawings, schedules and quantities come out of the model. And the handover data has been accumulating the whole way along.',
    'Each step already knows what the last one did. That is most of why handover information is usually such a miserable exercise, and why it is not one here."',
  ]),

  h2('5 · The same work, without the evening'),
  say('SAY', [
    '"This is the part I care most about. Naming and classifying a model used to be element by element. Producing a drawing set meant setting up every sheet. A schedule of quantities was counted, typed, and out of date by the time it was issued. Finding what was wrong meant reading drawings and hoping. Handover information got assembled at the end, from memory.',
    'Every one of those is now a pass that runs while you make tea."',
  ]),
  note('Then tell one real story',
    'A specific job, a specific evening, how long it took then and how long it takes now. One anecdote from your own projects will do more than the whole table. Architects believe other architects about late nights, and this is where the room stops evaluating and starts recognising.'),

  h2('6 · It does not matter what you draw in'),
  say('SAY', [
    '"A full add-in inside Revit, which is the deepest integration. ArchiCAD by saving to IFC, with changes coming back. Tekla the same way. And a free open-source route through Blender and Bonsai.',
    'Every element keeps the same identity across all four. I have tested that with a single element resolving across all of them at once.',
    'It is also why I would say this belongs to the profession rather than to one company. A tool tied to a single supplier should probably not be anybody\'s national standard."',
  ]),

  h2('7 · Meet inside the model, not over it'),
  p('The slide that separates this from every other tool the room has seen, and from Zoom. Give it time.'),
  say('SAY', [
    '"This is the part I am proudest of, and I have not seen it anywhere else.',
    'We all know what a coordination meeting looks like. Somebody shares their screen, everybody else squints at it, and half the room is looking at a drawing they cannot navigate.',
    'Here nobody screen-shares. There is a live meeting with camera and voice, in the browser or on a phone, and everyone is inside the same model. When the presenter isolates something, or colours the model by clash status, or cuts a section, everyone who is following sees their own view move to the same place. They are not watching a video of the model. They are in it.',
    'And the meeting keeps itself: minutes, action items, who attended. It can be recorded, so somebody who could not attend can watch it, or you can go back to it when a decision gets questioned six months later.',
    'I should be straight with you, as I have been about everything else. All of that is built and running. The last test on my list is a real two-person meeting with cameras on, and that is a test I cannot do alone."',
  ]),
  note('That last line is an opportunity',
    'If somebody offers to be the second person, accept immediately and fix a date before you leave the room. It is the cheapest possible way for a Council member to become involved, and it turns them from an audience into a participant.'),

  h2('8 · What you can do inside the model'),
  p('Do not read all eight. Pick three and show them in the demonstration instead.'),
  say('SAY', [
    '"And while you are in there, it is a proper coordination viewer rather than a picture.',
    'Isolate what matters and ghost the rest so you keep your bearings. Colour the model by clash status, issue status, discipline, level, any parameter you like. Explode an assembly. Cut a section, take a measurement. Mark up in three dimensions rather than drawing on a screenshot. Pin an issue to an element, at the point you are looking at, so whoever picks it up later knows exactly what you meant.',
    'Clashes come in as pins you can filter by status and type, which is what actually makes a coordination meeting move.',
    'It is colour-blind safe, which matters because roughly one man in twelve needs that and almost nobody asks. And all of it works on a phone, because that is where site is."',
  ]),

  h2('9 · The demonstration'),
  p('Six to eight minutes. Part 5 covers it fully.'),

  h2('10 · What global platforms will not do for us'),
  p('Deliver generously rather than competitively. Part 3 has the full argument.'),
  say('SAY', [
    '"People sometimes ask why build this at all when the international products exist. This is my honest answer.',
    'It is priced per practice rather than per person, and everyone outside your office joins free. The large vendors cannot do that; their whole business model is per seat.',
    'Ugandan conditions are in it as defaults. Wind, seismic zone, soil bearing, design rainfall by region. Nobody in California is going to build that.',
    'It works with no connection, because that is what our sites are like. It bills in shillings, and mobile money is coming, because most practices here do not have a corporate card. There is a free route through open-source software, which would undercut their own funnel. And if something goes wrong you can reach somebody in this time zone who has worked on a project like yours.',
    'None of that is them being worse than us. It is that a market our size does not justify the work for them. It does for us, because it is the only market we have."',
  ]),

  h2('11 · And the standard is already drafted'),
  say('SAY', [
    '"Publishing a standard is the easy part. Getting anyone to comply with it is where most of them quietly die, because compliance is expensive and nobody can check it.',
    'So what is in here is a working draft: naming and classification applied automatically, drawing types and title blocks so a set behaves the same way every time, what level of detail is expected at each stage, what a handover has to contain, and a check that runs against all of it.',
    'I want to be careful how I put this. It is a draft I built because I needed one, not a proposal I am putting to you. But if the profession ever wanted a standard of its own, it is a starting point rather than a blank page."',
  ]),
  note('Then leave it there',
    'Do not push it. If the Council picks it up it becomes their idea, which is the only way it would ever actually happen.'),

  h2('12 · And before long, you will simply ask it'),
  p('Accuracy matters more here than anywhere. Part 7 covers what is true and what is not.'),
  say('SAY', [
    '"The connection between an AI assistant and the model is built and tested. Over forty operations: query, create, tag, size, export. Every one checks the licence, runs inside a transaction that can be rolled back, and can be run as a trial first, so nothing happens to a model that cannot be undone.',
    'What I am still finishing is the conversation layer on top, so I am not going to demonstrate it and tell you it works.',
    'But the direction seems clear. Instead of learning where a command lives, you ask for the outcome. That matters for adoption more than it sounds, because most of what stops people using tools like this is not disagreement. It is that learning where everything lives takes time nobody has."',
  ]),

  h2('13 · Where it stands today'),
  say('SAY', [
    '"Working now, in daily use on live projects: the shared record, document control, issues, mobile offline, model checking, drawings, schedules, quantities and handover data.',
    'Being finished: serving many practices from one system, payments, hosting sized for the region, an independent security review, and the conversation layer.',
    'Not yet validated: the engineering calculation engines. Complete and tested, but never through independent professional validation, so I only offer them as commissioned work with manual checks alongside.',
    'One line: ready for one practice today, not ready to serve the whole profession at once."',
  ]),

  h2('14 · Priced so that BIM can be normal'),
  say('SAY', [
    '"In my experience most practices here do not avoid BIM because they disagree with it. They avoid it because of what it costs to do properly, and because it feels like something for big jobs.',
    'So it is priced per practice rather than per person. Everyone outside your office joins free. It is billed in shillings. And there is a free route for anyone with no software budget at all.',
    'Twenty-five dollars a month for the modelling tools alone. Sixty for everything up to three people. A hundred and thirty for a practice of four to ten.',
    'I am not claiming it does everything the international products do. I am saying it does what a Ugandan practice needs every week, at a price that lets BIM be normal rather than exceptional."',
  ]),

  h2('15 · What it would take to finish'),
  say('SAY', [
    '"Thirty-six thousand two hundred dollars to complete the platform. Twelve thousand for a year of hosting sized for regional use. Fifteen thousand for three training groups of about twenty-five people each. With contingency, about seventy-two and a half thousand dollars.',
    'The three are separable. The training in particular stands on its own and could start first."',
  ]),
  p('Then stop. See Part 1 on letting them raise the funding question.', { bold: true }),

  h2('16 · Where I think this is all heading'),
  say('SAY', [
    '"Requirements are becoming machine-readable, and the open standard for that arrived in 2024. Open formats are winning, and the open model format became an ISO standard the same year. AI working directly with models has moved very fast, and the expectation is that practices run their own assistants well before 2030. And the value is moving past handover into operation and maintenance.',
    'That is my reading. I would like to know whether the Council sees it the same way, because if I am wrong about the direction I would rather find out now than in three years."',
  ]),

  h2('17 · How something like this actually spreads'),
  p('Ambition delivered modestly. The last line is what stops it sounding grandiose, so do not drop it.'),
  say('SAY', [
    '"If something like this does work, I do not think it spreads by anybody announcing it.',
    'It starts with a handful of members putting it on real jobs, because it fits their work and their budget. If that goes well, the naming and conventions gradually become how members deliver, and at that point the Council would want a hand in shaping them, which is as it should be. Then clients start asking for what our members can already produce, rather than the other way round.',
    'And beyond that, honestly, the region. Kenya, Rwanda and Tanzania have the same conditions we do, the same gap, and no local platform either. But that is a long way off and I would rather earn it here first.',
    'None of that happens by announcement. It happens one practice at a time."',
  ]),

  h2('18 · Adoption is closer than it looks'),
  p('The emotional close, and the sentence you want repeated to people who were not in the room. Slow down.'),
  say('SAY', [
    '"We have talked about BIM adoption in this country as though it were a five-year problem. Skills, cost, software, standards, all of it a long way off.',
    'But look at where the pieces actually are. The tools work, and not as a prototype: they are in daily use on live projects. The price fits, so a three-person practice can afford it, and there is a free route for anyone who cannot. It works with whatever people already own, or with nothing at all. And a standard is drafted, not from scratch, but from something that already runs.',
    'I am not saying it is finished, and I have been honest about what is not. But I think adoption here is closer to a decision than it is to a five-year problem, and that is not something I could have said two years ago."',
  ]),

  h2('19 · What you would value from them'),
  say('SAY', [
    '"I will stop there, because what I came for is your thinking rather than a decision.',
    'Have I read the direction correctly, or am I missing something you can see from where you sit? What would members actually use? Would training be useful, and who should shape the content? And who else should see this?',
    'I am not asking the Council to decide anything today. Thank you for the time."',
  ]),
  p('Then be quiet and let them talk. The whole session is designed to arrive here.', { bold: true })
);

/* ============================================ PART 3 DIFFERENTIATION */
A(
  h1('Part 3 · What only a local platform can do'),
  p('The commercial heart of the case. The argument is not that the international products are bad, because they are very good. It is that a market of our size does not justify certain work for them, and does for you.'),

  h2('The seven things they will not build'),
  tbl(['What', 'Why they will not'], [
    ['Price per practice rather than per person', 'Their entire revenue model is per seat. Matching this would mean undoing how they earn, so they cannot, however much they might want to.'],
    ['Everyone outside the office joining free', 'Same reason, and it is the most useful single thing for a project here, because the usual reason coordination software dies is that nobody will pay for the other parties.'],
    ['Ugandan conditions as defaults', 'Wind, seismic, soil bearing, design rainfall by region. Encoding one small market\'s conditions is economically irrational for a global vendor.'],
    ['Genuine neutrality about authoring', 'A vendor cannot be neutral about its own modelling tool. Only a third party can be, which is why the multi-tool identity matters more than it first appears.'],
    ['A free open-source route', 'Letting people take part at zero licence cost would undercut their own funnel.'],
    ['Billing in shillings, and mobile money', 'Too small a market to justify local payment integration, and most practices here have no corporate card.'],
    ['Working properly with no connection', 'Their products assume connectivity, because their main markets have it.'],
  ], [1.5, 2.5]),
  gap(180),
  note('Say it generously',
    'The line that makes this land is the one that gives them credit: "None of this is them being worse than us. It is that a market our size does not justify the work for them. It does for us, because it is the only market we have." Delivered that way it reads as clear-eyed rather than defensive, and nobody has to defend a supplier they may also use.'),
  gap(160),
  h2('Two more, worth knowing but not worth a slide'),
  bullet('Hosting in Uganda, so data need not leave the country.'),
  bullet('Support in the same time zone from somebody who has worked on a project like theirs. It matters more than any feature when something breaks on a Friday afternoon.'),
  gap(160),
  h2('And the one they have not got at all'),
  p('A live coordination meeting held inside the model, where the presenter can move everyone\'s view. The big platforms have viewers, and separately everyone has video calls. Putting the two together, with camera and voice on one side and follow-the-presenter on the other, is genuinely uncommon and it is the thing most likely to be remembered from the session.'),
  p('Be honest about its state: built and running, with a real two-person camera test still outstanding.', { italic: true, color: GREY }),
  gap(160),
  h2('What not to do with this argument'),
  bullet('Do not name competitors on a slide.'),
  bullet('Do not volunteer a price comparison. The structural argument is stronger and survives any discount they offer.'),
  bullet('Do not claim feature parity. You have not built twenty years of software, and saying so plainly is what makes the rest credible.'),
  gap(160),
  note('If they push you to compare on price',
    'Answer precisely if asked, never volunteer it. At published list prices the main authoring collection is about USD 3,675 per seat per year, cloud collaboration around USD 1,284 per collaborator, and cloud document access about USD 500 per user with no free tier. A ten-person practice reaches roughly USD 12,840 a year before adding a single contractor. The same ten people here are about USD 1,560, with unlimited free external members. Say it once and stop: "the difference is not really the price, it is that they charge for every person on the project and we do not." Then note these are list prices rather than reseller quotes, and that nothing here replaces the authoring software.')
);

/* ============================================ PART 4 HOW IT IS USED */
A(
  h1('Part 4 · How it is actually used'),
  p('Three things worth being fluent on, because they are what turn a list of features into something a practice can picture itself doing.'),

  h2('A medium-sized project, start to finish'),
  tbl(['Stage', 'What happens'], [
    ['Set up', 'Project created, the standard applied, the team invited. Consultants, contractor and client all join free.'],
    ['Design', 'Authors model in Revit, ArchiCAD or the free route. Naming and classification applied as they go rather than at the end.'],
    ['Check and coordinate', 'The audit runs and scores the model. Clashes arrive as filterable pins. The coordination meeting happens inside the model.'],
    ['Document and tender', 'Sheets, title blocks, schedules and quantities out of the model. Transmittals and a drawing register kept automatically.'],
    ['Build', 'Site records, photographs and issues from a phone, working offline and syncing when there is signal.'],
    ['Hand over and operate', 'The asset register assembled along the way, so facilities management inherits data rather than a box of drawings.'],
  ], [1.1, 2.9]),
  gap(160),
  p('The line to underline is the last one. Handover is normally a miserable exercise done in the final fortnight by whoever is least busy. Here it is a by-product of having done the rest properly.', { bold: true }),

  h2('Authors, coordinators, and everyone else'),
  p('Explain this before showing prices, or the prices will not make sense. It is also the moment a room realises the economics are a different shape rather than merely cheaper.'),
  tbl(['Who', 'What they do', 'What they cost'], [
    ['Authors', 'Model the building', 'They need their own modelling software, which you do not sell and are not replacing. The tools sit inside whatever they already use.'],
    ['Coordinators', 'Run the job: checking, issues, documents, meetings, quantities', 'Named seats. This is the part a practice pays for.'],
    ['Everyone else', 'Client, contractor, quantity surveyor, other consultants', 'Unlimited and free. View, comment, raise issues, join meetings, with nobody buying them a licence.'],
  ], [0.8, 1.7, 2.3]),
  gap(160),
  say('THE LINE THAT LANDS IT', [
    '"A four-person practice usually pays for two or three coordinators, and the rest of the project team costs nothing at all.',
    'That is a different shape from paying per head, and it is the reason the whole team actually ends up on the platform rather than just the two people whose licences somebody could justify."',
  ]),

  h2('What a client checks from a phone'),
  p('Every one of these is a phone call an architect in that room has taken on a weekend. That recognition is what makes the slide work.'),
  bullet('What am I being asked to decide? Issues and approvals in one list rather than buried in email.'),
  bullet('What did I approve, and when? A dated record, so nobody relies on memory a year later.'),
  bullet('What does it actually look like? The model on the phone, rotate it, tap an element.'),
  bullet('What is happening on site? Photographs from the last visit, dated and located.'),
  bullet('Where did we get to on that? Minutes, actions, and the recording if there was one.'),
  bullet('Is anything stuck? Open issues by status, so a three-week-old question is visible.'),
  gap(160),
  note('The sentence that usually gets a laugh',
    '"And it costs them nothing to be there, which is the part that makes it actually happen. On a per-user platform the client is a licence somebody has to justify, so they never get added, and then they ring you on a Saturday instead." Let the laugh land. It is recognition, and recognition is what you are there for.')
);

/* ============================================ PART 3 DEMO */
A(
  h1('Part 5 · The demonstration'),
  note('The rule',
    'Nothing is demonstrated live that has not been proven twice, end to end, on the same setup you will actually run on. A demonstration that half works in front of this room is worse than a recording that works.'),
  gap(180),
  h2('What to show'),
  p('These are architects. Show architectural work. Every person in the room has done all of this by hand at two in the morning, which is exactly why it lands.'),
  bullet('A real model. Say so: "this is a real project, not a sample file."'),
  bullet('The check running, and what it catches.'),
  bullet('A drawing and a schedule coming out consistent.'),
  bullet('A quantity taken from the model.'),
  bullet('If the shared record is solid: an issue raised, and the mobile app working offline.'),
  gap(140),
  h2('What not to show'),
  tbl(['Do not demonstrate', 'Why'], [
    ['The engineering calculations', 'You have just told the room they are not independently validated. Demonstrating them invites exactly the question you least want.'],
    ['The conversation layer', 'It is the part you said you are still finishing. Showing it and having it stumble would undo the credibility the honest slides earned.'],
  ], [1.2, 2.8]),
  gap(180),
  h2('The rehearsal checklist'),
  tbl(['#', 'Step'], [
    ['1', 'Write the path down as numbered steps, not in your head.'],
    ['2', 'Confirm which setup it runs against. Verify rather than assume.'],
    ['3', 'First run: full path, timed. Write down every failure.'],
    ['4', 'Fix or cut what failed. Cutting is a legitimate and often correct outcome.'],
    ['5', 'Second run: full path, clean, no interventions.'],
    ['6', 'Record the successful run. That is the fallback whatever you decide.'],
    ['7', 'Test the projector, the resolution and the cable. Assume there is no internet.'],
    ['8', 'Choose live or recorded, and stop changing your mind.'],
  ], [0.3, 3.7]),
  gap(180),
  note('If something fails in the room',
    'Say so, move on, use the recording. Do not try to fix it in front of everyone. "That is one of the parts still being finished" is a recoverable sentence and it is consistent with what you said earlier. Fiddling with a laptop in silence is not recoverable, and it is what they will remember.')
);

/* ============================================ PART 4 WHERE BIM IS GOING */
A(
  h1('Part 6 · Where BIM is going'),
  p('The research behind the view you are putting to the Council. Know it well enough to discuss it, because someone in that room will want to.'),

  h2('1 · Requirements are becoming machine-readable'),
  p('This is the most useful development for what you have built, and the least widely known in the room.'),
  p('The buildingSMART Information Delivery Specification, IDS, reached version 1.0 in June 2024. It is an open standard for writing information requirements in a form software can check automatically. Instead of a written specification that somebody reads and hopes to have satisfied, a client issues requirements and a model is verified against them.'),
  p('That is precisely the pattern your checking already follows. Adopting IDS would make it an implementation of an international standard rather than a private convention, which is a much stronger position for a tool that hopes to be adopted broadly.', { bold: true }),

  h2('2 · Open formats are winning'),
  p('IFC became an ISO standard, ISO 16739-1, in 2024. Version 4.3 extends it well past buildings into infrastructure and adds proper georeferencing. Clients and public bodies increasingly ask to be handed IFC rather than a proprietary file, because it is the only thing they can be confident of opening in twenty years.'),
  p('Your multi-tool work already sits on this, which is what lets you talk about the tools belonging to the profession rather than to a company.'),

  h2('3 · From automation to assistance'),
  p('The fastest-moving of the four. Research on large language models working directly with building models has expanded very quickly, across information retrieval, compliance checking, and construction information interaction. Studies report that people working with an assistant experience markedly lower mental and time pressure than working manually.'),
  p('The industry expectation for the period to 2030 is augmentation rather than replacement, with firms building and running their own assistants on their own projects.'),
  p('This is where your work is genuinely ahead rather than merely current, and it is worth knowing that when you present it.', { bold: true }),

  h2('4 · From project information to asset information'),
  p('The value is moving past the handover moment into operation and maintenance, and in time into city-scale and sensor data. For a country where public asset registers are widely acknowledged to be poor, this is the layer with the most public value and the one most likely to interest government.'),

  h2('Sources'),
  p('Worth having read before the day, in case someone asks where the view comes from.', { italic: true, color: GREY }),
  bullet('buildingSMART International, on IFC and the Information Delivery Specification.'),
  bullet('ISO 16739-1:2024, the IFC standard.'),
  bullet('AEC Magazine, on the agentic future of BIM.'),
  bullet('Recent peer-reviewed work on large language models for BIM-based compliance checking.')
);

/* ============================================ PART 5 ASSISTANT */
A(
  h1('Part 7 · The assistant, and how to talk about it'),
  p('This is the most exciting thing you have and the easiest to overstate. Both facts deserve attention.'),

  h2('What is actually true'),
  tbl(['Claim', 'Status'], [
    ['A connection between an AI assistant and the model exists and works inside Revit', 'Built and tested. This is real and you can say it plainly.'],
    ['Over forty operations: query, create, tag, size, export', 'In the registry. Verify the exact count before quoting a number.'],
    ['Every operation checks the licence, runs in a transaction that can be rolled back, supports a trial run', 'Built into the design. This is the part a technical person will most respect.'],
    ['You can hold a conversation with your model today', 'Not yet. The conversation layer is unfinished. Do not claim it and do not demonstrate it.'],
  ], [1.6, 2.4]),
  gap(180),
  note('Why the safety design is the impressive part',
    'Anyone can connect a language model to something. The reason a technical person will take this seriously is that you thought about what happens when it goes wrong: a licence check on every operation, everything inside a transaction that can be rolled back, and a trial mode that writes nothing. Lead with that rather than with the clever part. It is the difference between a demo and an engineering decision.'),
  gap(180),
  h2('How to frame it'),
  say('THE FRAME', [
    '"Today you have to know where a command lives. Soon you will just say what you want.',
    'That is not a small change for adoption. Most of what stops people using tools like this is not disagreement, it is that learning where everything lives takes time nobody has."',
  ]),
  gap(140),
  p('That connects the assistant back to your adoption argument, which is what makes it more than a novelty.'),
  h2('The line to hold'),
  p('If someone asks whether they can see it working: "Not today. The connection underneath is tested but the conversation layer on top is what I am building now, and I would rather show you something finished than something that half works in front of you."'),
  p('Saying that costs you nothing and buys you a great deal. It is also the same standard you applied to the calculation engines, so it reads as consistency rather than excuse.', { italic: true, color: GREY })
);

/* ============================================ PART 6 PROPOSALS */
A(
  h1('Part 8 · What you are proposing'),
  p('Four proposals, offered as suggestions. The first three are about the tools. The fourth is about the profession, and it belongs to the Council rather than to you.'),

  h2('1 · Adopt the open requirements standard'),
  p('Build support for IDS, so that what a client asks for and whether a model satisfies it become the same artefact. It converts your checking from a private convention into an implementation of an international standard, which is what makes it credible to anyone outside your own projects.'),
  p('This is the strongest single recommendation in the whole document. It is concrete, it is near-term, and it aligns you with where the standards bodies are already going.', { bold: true }),

  h2('2 · Keep open formats at the centre'),
  p('IFC is what makes the work shareable, and it is the reason the tools can honestly be described as belonging to the profession rather than to a supplier. Say out loud that this includes not being locked to you.'),

  h2('3 · Finish the assistant, carefully'),
  p('The prize is not novelty. It is that BIM stops requiring someone to remember where a command lives, which is the real barrier to everyday use in a small practice.'),

  h2('4 · A draft the profession could adopt, if it wanted one'),
  p('There is already a working draft inside the tools of naming, classification, drawing conventions and what a handover should contain. If the profession ever wanted a standard of its own, that is a starting point rather than a blank page.'),
  gap(140),
  note('How to offer this one',
    'Put it down and leave it. It is the Council\'s to pick up or ignore, and saying so is what makes it possible for them to pick it up. If you push it, it stays your idea and they have to decide whether to back you. If you offer it and step back, it can become theirs, which is the only way it ever actually happens.'),
  gap(180),
  p('If the Council does take an interest, the useful next step is small: someone from the Council reads the draft and tells you what is wrong with it. That is a fortnight of somebody\'s evenings, not a programme.')
);

/* ============================================ PART 7 QUESTIONS */
A(
  h1('Part 9 · The questions you will be asked'),
  p('Rehearse these out loud. A prepared answer delivered hesitantly reads worse than an honest "I do not know" delivered calmly.'),

  h3('"Which software does it need?"'),
  say('ANSWER', [
    '"It matters less than you would expect. A full add-in inside Revit, which is the deepest integration. ArchiCAD by saving to IFC, with changes coming back. Tekla the same way. And a free open-source route through Blender and Bonsai.',
    'Every element keeps the same identity across all four, so a practice on ArchiCAD, an engineer on Tekla and a student on free software can work on one project without anyone converting anything by hand."',
  ]),
  p('If pressed: Revit is native, the others go through IFC, and there is no native ArchiCAD or Tekla plug-in.', { italic: true, color: GREY }),

  h3('"Is it finished?"'),
  say('ANSWER', [
    '"Ready for one practice to use today, and not yet ready to serve the whole profession at once. The shared record, the checking, drawings, schedules, quantities and handover data are all in daily use on live projects. What is left is serving many practices from one system, payments, regional hosting, a security review, and the conversation layer."',
  ]),

  h3('"Who else uses it?"'),
  say('ANSWER', [
    '"It is in daily use on the project I am currently information manager on: a six-building institutional campus in central Kampala, more than eight design disciplines, working to ISO 19650 under an international client\'s standards.',
    'What I do not have is twenty Ugandan practices using it, and I am not going to dress up a pipeline for you."',
  ]),

  h3('"How are you funding it?"'),
  p('Let them ask this. Do not raise it.'),
  say('ANSWER', [
    '"I am looking at several routes. Some of them a professional body can open and an individual cannot, so I would value your thinking on which are worth pursuing."',
  ]),
  p('Then stop. If they offer something, take it graciously, write it down, and do not negotiate it in the room.', { italic: true, color: GREY }),

  h3('"What happens if you are unavailable?"'),
  say('ANSWER', [
    '"Everything anyone holds is in open formats, so it opens without my software. Source code and configuration can go into escrow. The platform can be self-hosted. And the route onto a mainstream commercial product is priced, so leaving would be a budget decision rather than a rescue.',
    'I would rather build something people can walk away from than something they are stuck with."',
  ]),

  h3('"Where does the data live, and who can see it?"'),
  say('ANSWER', [
    '"Each practice has its own space and nobody sees another practice\'s work. Within a project you control who is invited and what they can do. It can be hosted in Uganda, and for a client who insists, on their own infrastructure.',
    'Everything is in open, exportable formats, so if you stop using it you leave with your information rather than losing it."',
  ]),
  p('If pressed on the security review: it is one of the things still to be done and it is in the costed work. Say so rather than implying it has happened.', { italic: true, color: GREY }),

  h3('"What does it cost a practice?"'),
  say('ANSWER', [
    '"Twenty-five dollars a month for the modelling tools alone, one seat. Sixty for everything, up to three people. A hundred and thirty for a practice of four to ten. Per practice, not per person, and less again on annual billing. And everyone outside your office joins free."',
  ]),
  gap(140),
  note('If they push you to compare against the big products',
    'Answer precisely if asked, but never volunteer it. At published list prices the main authoring collection is about USD 3,675 per seat per year, cloud collaboration around USD 1,284 per collaborator, and cloud document access about USD 500 per user with no free tier, so a ten-person practice reaches roughly USD 12,840 a year before adding a single contractor. The same ten people here are about USD 1,560 with unlimited free external members. Say it once and stop: "the difference is not really the price, it is that they charge for every person on the project and we do not." Then note that these are list prices rather than reseller quotes, and that nothing here replaces the authoring software.'),

  h3('"Are you asking us for something?"'),
  say('ANSWER', [
    '"No. I wanted to show the Council what I have been building, be honest about how far it has got, and hear what you make of the direction. If something useful comes out of that, good. But I am not asking for a decision."',
  ]),
  p('This answer is the whole presentation in four sentences. Have it ready, because someone will probably test it.', { bold: true }),

  h2('Questions to ask them'),
  numbered('Have I read the direction correctly, or am I missing something?'),
  numbered('What would members actually use, out of everything I have shown you?'),
  numbered('Would training be useful, and what shape should it take?'),
  numbered('Who else should see this?'),
  numbered('Is there anything here the Society would want to have a hand in shaping?')
);

/* ============================================ PART 8 WORDING */
A(
  h1('Part 10 · Wording to keep consistent'),
  p('Say these the same way every time, in the room and afterwards.'),

  h2('How far along it is'),
  say('FIXED WORDING', [
    'The shared record, document control, issues, the offline mobile app, model checking, drawings, schedules, quantities and handover data are in production and in daily use on live projects.',
    'Still being finished: serving many practices from one system, payments, hosting sized for the region, an independent security review, and the conversation layer.',
    'Ready for one practice to use today. Not yet ready to serve the whole profession at once.',
  ]),
  bullet('Never say it is complete or finished.'),
  bullet('Never say it does not work yet either. It does. Both overstatements are avoidable, and the second is the one people reach for when they are trying to be modest.'),

  h2('The assistant'),
  say('FIXED WORDING', [
    'The connection between an assistant and the model is built and tested inside Revit, with over forty operations, each checking the licence, running inside a transaction that can be rolled back, and supporting a trial run that writes nothing.',
    'The conversation layer on top is unfinished. I am showing the direction, not claiming it is done.',
  ]),

  h2('Which tools it works with'),
  say('FIXED WORDING', [
    'A full add-in inside Revit. ArchiCAD and Tekla through IFC, with changes written back. A free open-source route through Blender and Bonsai. Every element keeps the same identity across all four.',
    'The deepest automation is in Revit. There is no native ArchiCAD or Tekla plug-in, and that is a deliberate design decision rather than a gap, because IFC means nobody is locked in.',
  ]),

  h2('The calculation engines'),
  say('FIXED WORDING', [
    'Complete and exercised in testing, but never taken through independent professional validation. Offered only as separately commissioned work, with manual cross-checks alongside, and the responsible engineer retaining professional responsibility.',
  ]),

  h2('Origin'),
  say('FIXED WORDING', [
    'Built starting in 2021, worked out from first principles. They were not used on the Tilenga oil development project, but seeing that project\'s coordination and design problems up close, on a programme of that size, is what accelerated the work from 2023 onward.',
  ]),
  p('Say "not used on Tilenga" plainly. The proximity is the honest and interesting part of the story, and ambiguity about it becomes a liability as the profile grows.', { italic: true, color: GREY })
);

/* ============================================ PART 9 CHECKLIST */
A(
  h1('Part 11 · Checklist'),
  h2('A week before'),
  bullet('Start the demonstration rehearsals. They take longer than anyone expects.'),
  bullet('Verify the operation count for the assistant, and check the prices are current.'),
  bullet('Pick the one real story you will tell on the automation slide.'),

  h2('Three days before'),
  bullet('Read Part 2 out loud, twice, standing up.'),
  bullet('Rehearse the four questions in Part 7 that you are most likely to get.'),
  bullet('Decide live or recorded for the demonstration, and stop changing your mind.'),

  h2('The day before'),
  bullet('Confirm the room, the time and what the projector takes.'),
  bullet('Load everything offline. Assume there is no internet.'),
  bullet('Charge the laptop, and pack the charger and an adapter.'),
  bullet('Read Part 1 and Part 8 last thing.'),

  h2('On the day'),
  bullet('Say in the first minute that you are not asking for a decision.'),
  bullet('Do not explain BIM.'),
  bullet('Volunteer what is unfinished before anyone asks.'),
  bullet('State the cost, then stop talking.'),
  bullet('Ask your five questions and let them talk.'),
  bullet('Write down what they suggested, who they named, and whether they want to see it again.'),

  h2('Within two days'),
  bullet('A short thank-you, with anything you promised attached.'),
  bullet('Act on any name they gave you this week, not next month.'),
  bullet('Write down what you would do differently, while it is fresh.')
);

/* ============================================ PART 10 CLOSE */
A(
  h1('Part 12 · Afterwards'),
  h2('If they engage'),
  p('The most likely good outcome is that one or two people in the room become interested and want to talk more. That is worth more than a resolution, because it is a person rather than a minute. Follow it up within the week while the presentation is still vivid.'),

  h2('If they are polite but flat'),
  p('It happens, and it is not a verdict on the work. Committees are busy and often need a second exposure before anything moves. Send the thank-you, do exactly what you said you would, and let the next piece of progress create the next reason to meet.'),
  p('Do not follow a flat reception with a longer written case. It reads as pressure, and a body that has just been non-committal will not be moved by more pages.', { italic: true, color: GREY }),

  h2('If someone offers help'),
  p('Take it graciously, write down exactly what was offered and by whom, and do not negotiate it in the room. Then confirm it in writing within two days, in the plainest possible terms, so that both of you remember the same thing.'),

  h2('A closing note'),
  note('What actually makes this land',
    'The strongest thing in this presentation is not the software. It is that you are willing to stand in front of your own Council and say what is unfinished, what is unvalidated, and what you are not sure about. Very few people do that, and in a room of senior professionals it is the thing that gets remembered and repeated. Everything else is detail.')
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
      spacing: { after: 200 }, children: [new TextRun({ text: 'Presentation guide', font: BF,
        size: 16, color: '9AA0A6' })] })] }) },
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
