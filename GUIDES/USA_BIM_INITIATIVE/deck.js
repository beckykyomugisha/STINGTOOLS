const pptxgen = require('pptxgenjs');

const INK = '22262B';
const INK2 = '343A42';
const PAPER = 'FFFFFF';
const TINT = 'F0EEEA';
const TINT2 = 'E4E0DA';
const ACC = 'C15F3C';
const ACC2 = '8F4229';
const SLATE = '4A6670';
const MUTED = '78808A';
const LIGHTTXT = 'E8E4DE';
const SALMON = 'E8A183';

const H = 'Cambria';
const B = 'Calibri';
const M = 0.65;
const W = 13.333;
const CW = W - M * 2;

const pres = new pptxgen();
pres.layout = 'LAYOUT_WIDE';
pres.author = 'Davis Mayanja';
pres.company = 'PlanScape';
pres.title = 'Progress report to the Council';

function sq(s, x, y, c, size) {
  s.addShape(pres.ShapeType.rect, { x: x, y: y, w: size || 0.16, h: size || 0.16, fill: { color: c } });
}
function motif(s, x, y, c) {
  sq(s, x, y, c, 0.15); sq(s, x + 0.22, y, c, 0.15); sq(s, x + 0.44, y, c, 0.15);
}
function darkBg(s) { s.background = { color: INK }; }
function kicker(s, text, color) {
  s.addText(text, { x: M, y: 0.5, w: CW, h: 0.3, fontSize: 12, bold: true, charSpacing: 2.5,
    color: color || ACC, fontFace: B, isTextBox: true, margin: 0 });
}
function title(s, text, color, size) {
  s.addText(text, { x: M, y: 0.88, w: CW, h: 1.0, fontSize: size || 38, bold: true,
    color: color || INK, fontFace: H, isTextBox: true, margin: 0, valign: 'top' });
}
function card(s, x, y, w, h, fill) {
  s.addShape(pres.ShapeType.rect, { x: x, y: y, w: w, h: h, fill: { color: fill || TINT },
    shadow: { type: 'outer', angle: 90, offset: 2, blur: 8, color: 'BFB9B0', opacity: 0.45 } });
}
function numTile(s, x, y, n, color, txt) {
  s.addShape(pres.ShapeType.rect, { x: x, y: y, w: 0.46, h: 0.46, fill: { color: color || ACC } });
  s.addText(n, { x: x, y: y, w: 0.46, h: 0.46, fontSize: 18, bold: true, color: txt || 'FFFFFF',
    fontFace: H, align: 'center', valign: 'middle', isTextBox: true, margin: 0 });
}

/* ---------------------------------------------------- 1 TITLE */
let s = pres.addSlide();
darkBg(s);
motif(s, M, 1.6, ACC);
s.addText('A PROGRESS REPORT TO THE COUNCIL', { x: M, y: 2.05, w: CW, h: 0.32, fontSize: 12.5,
  bold: true, charSpacing: 3, color: ACC, fontFace: B, isTextBox: true, margin: 0 });
s.addText('Where the tools are,\nand where I think they should go', { x: M, y: 2.5, w: 11.2, h: 1.9,
  fontSize: 42, bold: true, color: PAPER, fontFace: H, lineSpacing: 50, isTextBox: true, margin: 0 });
s.addText('PlanScape and StingTools, built in Kampala, two years in', { x: M, y: 4.5, w: 10.6,
  h: 0.4, fontSize: 17, color: LIGHTTXT, fontFace: B, italic: true, isTextBox: true, margin: 0 });
s.addText('Davis Mayanja', { x: M, y: 5.55, w: 6, h: 0.3, fontSize: 14.5, bold: true, color: PAPER,
  fontFace: B, isTextBox: true, margin: 0 });
s.addText('Uganda Society of Architects  ·  September 2026', { x: M, y: 5.9, w: 7, h: 0.3,
  fontSize: 12.5, color: MUTED, fontFace: B, isTextBox: true, margin: 0 });
s.addNotes(
  'Posture for the whole session: a member reporting on work, not a supplier pitching. You are not ' +
  'asking for anything. You are showing them something and asking what they make of it.\n\n' +
  'SAY: "Mr President, Chairman, thank you for the time. I have been building something for about ' +
  'two years and I thought it was time I showed the Council rather than kept describing it.\n\n' +
  'I will show you where it has got to, what is left, and what it would take to finish. Then I want ' +
  'to put a view to you about where I think this whole area is going, and hear whether you agree.\n\n' +
  'I am not asking the Council to decide anything today. I would rather have your thinking than a ' +
  'decision."\n\n' +
  'NEVER mention money troubles or two years without earning. In this room that reads as need, and ' +
  'need makes people cautious rather than generous. Two years of investment is a credential; two ' +
  'years of hardship is a different conversation and it does not belong here.'
);

/* ---------------------------------------------------- 2 WHAT IS NEW */
s = pres.addSlide();
kicker(s, 'TO START WITH');
title(s, 'Six things you have probably not seen before');
[
  ['Talk to the model in plain English', 'Ask which rooms are missing a fire rating, and the answer comes back from the model itself.'],
  ['One identity across four tools', 'Revit, ArchiCAD, Tekla and free open-source Blender on one project, with no converting by hand.'],
  ['A compliance score, as a number', 'How much of a model actually meets the standard, measured rather than argued about.'],
  ['Drawings and schedules produced for you', 'Sheets, title blocks, schedules and quantities generated from the model instead of drawn again.'],
  ['Handover data assembled as you build', 'Rather than compiled in a panic in the fortnight before practical completion.'],
  ['Ugandan conditions built in', 'Wind, seismic zone, soil bearing and design rainfall by region, as defaults.'],
].forEach(function (r, i) {
  const x = M + (i % 2) * 6.4;
  const y = 2.1 + Math.floor(i / 2) * 1.6;
  card(s, x, y, 5.95, 1.35, i % 2 ? TINT : TINT2);
  sq(s, x + 0.35, y + 0.3, ACC, 0.18);
  s.addText(r[0], { x: x + 0.78, y: y + 0.2, w: 4.9, h: 0.42, fontSize: 16.5, bold: true,
    color: INK, fontFace: H, isTextBox: true, margin: 0 });
  s.addText(r[1], { x: x + 0.78, y: y + 0.64, w: 4.95, h: 0.6, fontSize: 12.5, color: INK2,
    fontFace: B, lineSpacing: 17, isTextBox: true, margin: 0 });
});
s.addNotes(
  'THE CAPTURE SLIDE. Everyone here knows what BIM is, so do not explain BIM. Show them what they ' +
  'have not seen. Ninety seconds, fast, no dwelling. Each one gets its own slide later.\n\n' +
  'SAY: "You all know what BIM is, so I am not going to explain it. Let me instead show you six ' +
  'things I think are new, and then go through them properly.\n\n' +
  'You can talk to the model in plain English. One element keeps the same identity across four ' +
  'different modelling tools, including a free one. A model gets a compliance score, an actual ' +
  'number. Drawings, schedules and quantities come out of the model rather than being drawn again. ' +
  'Handover data is assembled as you build rather than at the end. And Ugandan conditions are built ' +
  'in as defaults, which no imported tool does.\n\n' +
  'Any one of those is useful on its own. Together they change what a working day looks like."'
);

/* ---------------------------------------------------- 3 TWO PARTS */
s = pres.addSlide();
kicker(s, 'ORIENTATION');
title(s, 'Two parts, designed as one system');
card(s, M, 2.2, 5.95, 3.6, INK);
sq(s, M + 0.45, 2.6, ACC, 0.22);
s.addText('StingTools', { x: M + 0.45, y: 3.0, w: 5.0, h: 0.5, fontSize: 26, bold: true,
  color: PAPER, fontFace: H, isTextBox: true, margin: 0 });
s.addText('Inside the modelling software', { x: M + 0.45, y: 3.5, w: 5.0, h: 0.3, fontSize: 13,
  color: SALMON, fontFace: B, italic: true, isTextBox: true, margin: 0 });
s.addText('Checking, tagging and classification. Drawings, schedules, quantities and handover data ' +
  'produced from the model. Engineering sizing where a project needs it.',
  { x: M + 0.45, y: 3.95, w: 5.05, h: 1.6, fontSize: 13.5, color: LIGHTTXT, fontFace: B,
    lineSpacing: 20, isTextBox: true, margin: 0 });
card(s, M + 6.4, 2.2, 5.95, 3.6, TINT);
sq(s, M + 6.85, 2.6, ACC, 0.22);
s.addText('PlanScape', { x: M + 6.85, y: 3.0, w: 5.0, h: 0.5, fontSize: 26, bold: true, color: INK,
  fontFace: H, isTextBox: true, margin: 0 });
s.addText('The shared record around it', { x: M + 6.85, y: 3.5, w: 5.0, h: 0.3, fontSize: 13,
  color: ACC2, fontFace: B, italic: true, isTextBox: true, margin: 0 });
s.addText('Documents, issues and correspondence in one place for a whole project team. Priced per ' +
  'practice, with everyone outside the office joining free.',
  { x: M + 6.85, y: 3.95, w: 5.05, h: 1.6, fontSize: 13.5, color: INK2, fontFace: B,
    lineSpacing: 20, isTextBox: true, margin: 0 });
s.addText('Started in 2021. About two years of full-time work in them.', { x: M, y: 6.1, w: CW,
  h: 0.4, fontSize: 14.5, color: MUTED, fontFace: B, italic: true, isTextBox: true, margin: 0 });
s.addNotes(
  'Sixty seconds. They know BIM. They just need to know which part is which so the rest makes sense.\n\n' +
  'SAY: "Two parts. StingTools sits inside the modelling software and does the checking, the tagging, ' +
  'and produces the drawings, schedules, quantities and handover data. PlanScape is the shared record ' +
  'around it, where the documents and issues live for the whole team.\n\n' +
  'They were designed as one system rather than bolted together. Started in 2021, with about two ' +
  'years of full-time work in them."'
);

/* ---------------------------------------------------- 4 AUTOMATION */
s = pres.addSlide();
kicker(s, 'WHAT CHANGES');
title(s, 'The same work, without the evening');
s.addText('The point is not that it does something new. It is that it does the same work without ' +
  'anybody staying late.', { x: M, y: 2.05, w: 11.6, h: 0.5, fontSize: 16, italic: true,
    color: ACC2, fontFace: H, isTextBox: true, margin: 0 });
[
  ['Naming and classifying a model', 'By hand, element by element', 'One pass, consistent and checkable'],
  ['Producing a drawing set', 'Set up each sheet, place each view', 'Sheets and title blocks to a fixed standard'],
  ['A schedule of quantities', 'Counted, typed, out of date on issue', 'Taken from the model, and it updates'],
  ['Finding what is wrong', 'Reading drawings and hoping', 'A report naming what fails, and where'],
  ['Handover information', 'Assembled at the end, from memory', 'Collected as the project is built'],
].forEach(function (r, i) {
  const y = 2.72 + i * 0.82;
  card(s, M, y, CW, 0.7, i % 2 ? TINT : TINT2);
  s.addText(r[0], { x: M + 0.35, y: y + 0.19, w: 3.5, h: 0.36, fontSize: 14, bold: true, color: INK,
    fontFace: H, isTextBox: true, margin: 0 });
  s.addText(r[1], { x: M + 4.1, y: y + 0.2, w: 3.5, h: 0.34, fontSize: 12, color: MUTED,
    fontFace: B, italic: true, isTextBox: true, margin: 0 });
  s.addText('to', { x: M + 7.72, y: y + 0.2, w: 0.4, h: 0.34, fontSize: 12, bold: true, color: ACC,
    fontFace: B, isTextBox: true, margin: 0 });
  s.addText(r[2], { x: M + 8.25, y: y + 0.2, w: 3.75, h: 0.34, fontSize: 12.5, bold: true,
    color: INK, fontFace: B, isTextBox: true, margin: 0 });
});
s.addNotes(
  'THE IMPRESS SLIDE. Do not read the table. Point at it, then tell one story from your own work.\n\n' +
  'SAY: "This is the part I care most about. The point of any of this is not that it does something ' +
  'nobody could do before. It is that it does the same work without somebody staying until midnight.\n\n' +
  'Naming and classifying a model used to be element by element. Producing a drawing set meant ' +
  'setting up every sheet. A schedule of quantities was counted, typed, and out of date by the time ' +
  'it was issued. Finding what was wrong meant reading drawings and hoping. Handover information got ' +
  'assembled at the end, from memory.\n\n' +
  'Every one of those is now a pass that runs while you make tea."\n\n' +
  'THEN TELL ONE REAL STORY. A specific job, a specific evening, how long it took then and how long ' +
  'it takes now. One concrete anecdote from your own projects will do more than the whole table. ' +
  'Architects believe other architects about late nights.'
);

/* ---------------------------------------------------- 5 MULTI-HOST */
s = pres.addSlide();
kicker(s, 'WHAT CHANGES');
title(s, 'It does not matter what you draw in');
[
  ['Revit', 'A full add-in, the deepest automation', ACC],
  ['ArchiCAD', 'Save to IFC and it arrives, with changes coming back', SLATE],
  ['Blender with Bonsai', 'Free and open source. No licence at all.', ACC],
  ['Tekla', 'Structural models arrive the same way', SLATE],
].forEach(function (h4, i) {
  const x = M + (i % 2) * 6.4;
  const y = 2.15 + Math.floor(i / 2) * 1.55;
  card(s, x, y, 5.95, 1.3, TINT);
  sq(s, x + 0.4, y + 0.32, h4[2], 0.2);
  s.addText(h4[0], { x: x + 0.85, y: y + 0.22, w: 4.8, h: 0.4, fontSize: 18, bold: true, color: INK,
    fontFace: H, isTextBox: true, margin: 0 });
  s.addText(h4[1], { x: x + 0.85, y: y + 0.66, w: 4.85, h: 0.5, fontSize: 13, color: INK2,
    fontFace: B, isTextBox: true, margin: 0 });
});
card(s, M, 5.35, CW, 1.35, INK);
s.addText('Every element keeps the same identity across all four.', { x: M + 0.45, y: 5.55,
  w: CW - 0.9, h: 0.45, fontSize: 19, bold: true, color: PAPER, fontFace: H, isTextBox: true,
  margin: 0 });
s.addText('A practice on ArchiCAD, an engineer on Tekla and a student on free software can work on ' +
  'one project without anyone converting anything by hand.', { x: M + 0.45, y: 6.02, w: CW - 0.9,
    h: 0.5, fontSize: 14, color: LIGHTTXT, fontFace: B, isTextBox: true, margin: 0 });
s.addNotes(
  'In a room of architects this answers the question everyone is already holding, and most of them ' +
  'are not on Revit.\n\n' +
  'SAY: "A fair question at this point is which software it needs. The answer matters less than you ' +
  'would expect.\n\n' +
  'There is a full add-in inside Revit, which is the deepest integration. ArchiCAD works by saving ' +
  'to IFC, and changes come back the other way. Tekla arrives the same way, so the structural ' +
  'engineer is on the same project. And there is a free, open-source route through Blender and ' +
  'Bonsai that costs nothing at all.\n\n' +
  'The line that matters is the last one. Every element keeps the same identity across all four. I ' +
  'have tested that with one element resolving across all of them at once.\n\n' +
  'It is also the reason I would argue any of this could ever belong to the profession rather than ' +
  'to one company. Something tied to a single supplier should not be a national anything."\n\n' +
  'BE EXACT IF ASKED: Revit is a native add-in, the others go through IFC, and there is no native ' +
  'ArchiCAD or Tekla plug-in. Say it plainly. The precision is what makes the claim believable.'
);

/* ---------------------------------------------------- 6 DEMO */
s = pres.addSlide();
darkBg(s);
motif(s, M, 2.55, ACC);
s.addText('Rather than describe it', { x: M, y: 3.0, w: 11.5, h: 0.6, fontSize: 20, color: MUTED,
  fontFace: B, italic: true, isTextBox: true, margin: 0 });
s.addText('Let me show you.', { x: M, y: 3.6, w: 11.5, h: 1.2, fontSize: 54, bold: true,
  color: PAPER, fontFace: H, isTextBox: true, margin: 0 });
s.addText('A real model  ·  checked and classified  ·  drawings and schedules out  ·  quantities out',
  { x: M, y: 5.0, w: 11.5, h: 0.5, fontSize: 15, color: SALMON, fontFace: B, isTextBox: true,
    margin: 0 });
s.addNotes(
  'Six to eight minutes. Rehearsed twice end to end on the same setup you will run on.\n\n' +
  'Show architectural work. A real model, the check running, drawings and a schedule coming out, a ' +
  'quantity pulled. Everyone in this room has done all of that by hand at two in the morning.\n\n' +
  'SAY as you start: "This is a real project, not a sample file."\n\n' +
  'Narrate what you are doing rather than what the software is doing. "This would have taken me an ' +
  'afternoon" is worth more than any feature name.\n\n' +
  'DO NOT demonstrate the engineering calculations or the conversation layer. Both are honest work ' +
  'in progress, and neither is what this audience needs to see.\n\n' +
  'IF SOMETHING FAILS: say so, move on, use the recording. "That is one of the parts still being ' +
  'finished" is recoverable. Fiddling with a laptop in silence is not.'
);

/* ---------------------------------------------------- 7 COPILOT */
s = pres.addSlide();
kicker(s, 'WHAT IS COMING');
title(s, 'And before long, you will simply ask it');
card(s, M, 2.1, CW, 1.5, INK);
s.addText('"Which rooms on level two are missing a fire rating?"', { x: M + 0.5, y: 2.35, w: 11.4,
  h: 0.5, fontSize: 22, bold: true, color: SALMON, fontFace: H, italic: true, isTextBox: true,
  margin: 0 });
s.addText('"Tag everything on this level."          "Give me quantities for the north block."',
  { x: M + 0.5, y: 2.92, w: 11.4, h: 0.4, fontSize: 15, color: LIGHTTXT, fontFace: B, italic: true,
    isTextBox: true, margin: 0 });
[
  ['Built and tested', 'The connection between an assistant and the model is in place and working inside Revit. Over forty operations: query, create, tag, size, export.'],
  ['Safe by design', 'Every operation checks the licence, runs inside a transaction that can be rolled back, and can be run as a trial first so nothing is written.'],
  ['Still to finish', 'The conversation layer on top is the part I am working on now. I am showing the direction, not claiming it is done.'],
].forEach(function (r, i) {
  const x = M + i * 4.0;
  card(s, x, 3.85, 3.7, 2.5, i === 2 ? TINT2 : TINT);
  s.addText(r[0], { x: x + 0.3, y: 4.1, w: 3.1, h: 0.4, fontSize: 15.5, bold: true, color: ACC2,
    fontFace: H, isTextBox: true, margin: 0 });
  s.addText(r[1], { x: x + 0.3, y: 4.58, w: 3.15, h: 1.65, fontSize: 12.5, color: INK2, fontFace: B,
    lineSpacing: 17, isTextBox: true, margin: 0 });
});
s.addText('This is where the industry is heading. I would rather we arrived early than caught up late.',
  { x: M, y: 6.55, w: CW, h: 0.4, fontSize: 14, color: MUTED, fontFace: B, italic: true,
    isTextBox: true, margin: 0 });
s.addNotes(
  'This is the slide that will make the ICT chair sit forward. Be accurate and do not oversell, ' +
  'because overselling it is exactly what would cost you their respect.\n\n' +
  'SAY: "The last thing I want to show you is not finished, and I am showing it anyway because I ' +
  'think it is where all of this is going.\n\n' +
  'The connection between an AI assistant and the model itself is built and tested. Over forty ' +
  'operations: it can query a model, create things, tag, size, export. And it is built carefully. ' +
  'Every operation checks the licence, runs inside a transaction that can be rolled back, and can be ' +
  'run as a trial first, so nothing happens to a model that cannot be undone.\n\n' +
  'What I am still finishing is the conversation layer on top. So I am not going to demonstrate it ' +
  'and tell you it works.\n\n' +
  'But the direction is clear enough. Instead of learning where a command lives, you ask for the ' +
  'outcome. Which rooms are missing a fire rating. Tag this level. Give me quantities for the north ' +
  'block. The research here has moved very fast in two years, and the expectation across the ' +
  'industry is that practices will be running their own assistants on their own projects well before ' +
  '2030. I would rather we arrived early than caught up late."\n\n' +
  'DO NOT DEMONSTRATE THIS LIVE. The conversational turn is untested, and a failure here would undo ' +
  'the credibility the honest slides earned you.\n\n' +
  'CHECK THE OPERATION COUNT before quoting a number.'
);

/* ---------------------------------------------------- 8 WHERE IT STANDS */
s = pres.addSlide();
kicker(s, 'HOW FAR ALONG');
title(s, 'Where it stands, plainly');
[
  ['Working now', ACC, [
    'The shared project record and document control',
    'Issues and correspondence',
    'Mobile, working offline on site',
    'Model checking and classification',
    'Drawings, schedules, quantities, handover data',
  ]],
  ['Being finished', SLATE, [
    'Serving many practices from one system',
    'Mobile money payments',
    'Hosting sized for regional use',
    'An independent security review',
    'The conversation layer',
  ]],
  ['Not yet validated', MUTED, [
    'The engineering calculation engines',
    'Complete and tested, but never through independent professional validation',
    'Offered only as commissioned work, cross-checked by hand',
  ]],
].forEach(function (c, i) {
  const x = M + i * 4.0;
  sq(s, x, 2.18, c[1], 0.2);
  s.addText(c[0], { x: x, y: 2.5, w: 3.7, h: 0.45, fontSize: 19, bold: true, color: INK,
    fontFace: H, isTextBox: true, margin: 0 });
  c[2].forEach(function (item, j) {
    s.addText(item, { x: x, y: 3.05 + j * 0.58, w: 3.6, h: 0.54, fontSize: 12.5, color: INK2,
      fontFace: B, lineSpacing: 17, isTextBox: true, margin: 0 });
  });
});
card(s, M, 6.05, CW, 0.9, INK);
s.addText('Ready for one practice to use today. Not yet ready to serve the whole profession at once.',
  { x: M + 0.45, y: 6.05, w: CW - 0.9, h: 0.9, fontSize: 18, bold: true, color: PAPER, fontFace: H,
    valign: 'middle', isTextBox: true, margin: 0 });
s.addNotes(
  'Volunteer all of this. Being the person who says what is not finished is worth more in this room ' +
  'than any feature.\n\n' +
  'SAY: "Let me be plain about how far along it is.\n\n' +
  'Working now, in daily use on live projects: the shared record, document control, issues, mobile ' +
  'offline, the model checking, and the drawings, schedules, quantities and handover data.\n\n' +
  'Being finished: serving many practices from one system, payments, hosting sized for the region, ' +
  'an independent security review, and the conversation layer I just showed you.\n\n' +
  'And not yet validated: the engineering calculation engines. They are complete and tested but they ' +
  'have never been through independent professional validation, so I only offer them as commissioned ' +
  'work with manual checks alongside. I would rather say that here than have an engineer discover it.\n\n' +
  'The summary is one line. Ready for one practice today. Not ready to serve the whole profession at ' +
  'once. That gap is the work that is left."'
);

/* ---------------------------------------------------- 9 COST */
s = pres.addSlide();
kicker(s, 'HOW MUCH IS LEFT');
title(s, 'What it would take to finish');
[
  ['Completing the platform', 'USD 36,200', 'Serving many practices, payments, security review, testing'],
  ['Regional hosting', 'USD 12,000', 'Twelve months, sized to grow with use'],
  ['Training, three groups', 'USD 15,000', 'About 25 people per group'],
].forEach(function (c, i) {
  const x = M + i * 4.0;
  card(s, x, 2.2, 3.7, 2.5);
  s.addText(c[1], { x: x + 0.35, y: 2.5, w: 3.0, h: 0.6, fontSize: 27, bold: true, color: ACC,
    fontFace: H, isTextBox: true, margin: 0 });
  s.addText(c[0], { x: x + 0.35, y: 3.15, w: 3.1, h: 0.4, fontSize: 15.5, bold: true, color: INK,
    fontFace: H, isTextBox: true, margin: 0 });
  s.addText(c[2], { x: x + 0.35, y: 3.62, w: 3.0, h: 0.95, fontSize: 12.5, color: INK2, fontFace: B,
    lineSpacing: 17, isTextBox: true, margin: 0 });
});
card(s, M, 5.0, CW, 1.55, INK);
s.addText('USD 72,680', { x: M + 0.45, y: 5.25, w: 3.4, h: 0.65, fontSize: 30, bold: true,
  color: PAPER, fontFace: H, isTextBox: true, margin: 0 });
s.addText('including 15% contingency', { x: M + 0.45, y: 5.92, w: 3.4, h: 0.35, fontSize: 12.5,
  color: MUTED, fontFace: B, italic: true, isTextBox: true, margin: 0 });
s.addText('The three are separable. The training stands on its own, and it is the part that could ' +
  'start first.', { x: M + 4.4, y: 5.42, w: 7.6, h: 0.8, fontSize: 15.5, color: PAPER, fontFace: B,
    lineSpacing: 22, isTextBox: true, margin: 0 });
s.addNotes(
  'State this as a fact, not as a request. You are reporting what remains, not asking anyone to ' +
  'cover it. That difference is everything and the room will feel it.\n\n' +
  'SAY: "You asked how far along and what is left, so here is the money side of it.\n\n' +
  'Thirty-six thousand two hundred dollars to complete the platform. Twelve thousand for a year of ' +
  'hosting sized for regional use. Fifteen thousand for three training groups of about twenty-five ' +
  'people each. With contingency, about seventy-two and a half thousand dollars, or two hundred and ' +
  'sixty-nine million shillings.\n\n' +
  'The three are separable. The training in particular stands on its own and could start first."\n\n' +
  'THEN STOP. Do not follow it with an ask. Somebody will almost certainly say "and how are you ' +
  'funding that?" — that question landing from them is far stronger than the same subject raised by ' +
  'you. When it comes, answer briefly and honestly: you are looking at several routes, some of which ' +
  'a professional body can open and an individual cannot, and you would value their thinking. Then ' +
  'let them offer, or not. Do not push.'
);

/* ---------------------------------------------------- 10 PRICE */
s = pres.addSlide();
kicker(s, 'WHAT IT COSTS TO USE');
title(s, 'The case for putting BIM in everyday work');
s.addText('Most practices here do not avoid BIM because they disagree with it. They avoid it because ' +
  'of what it costs to do properly.', { x: M, y: 2.05, w: 11.6, h: 0.85, fontSize: 17, bold: true,
    color: INK, fontFace: H, lineSpacing: 24, isTextBox: true, margin: 0 });
[
  ['Priced per practice', 'Not per person, so a practice pays once rather than once per member of staff.'],
  ['Everyone outside joins free', 'Client, contractor and quantity surveyor cost nothing to bring onto a project.'],
  ['Billed in shillings', 'Not in dollars, and not exposed to a rate nobody here controls.'],
  ['A free route exists', 'Through Blender and Bonsai, for anyone with no software budget at all.'],
].forEach(function (e, i) {
  const x = M + (i % 2) * 6.4;
  const y = 3.1 + Math.floor(i / 2) * 1.32;
  card(s, x, y, 5.95, 1.12);
  sq(s, x + 0.35, y + 0.27, ACC, 0.16);
  s.addText(e[0], { x: x + 0.72, y: y + 0.19, w: 4.9, h: 0.35, fontSize: 15.5, bold: true,
    color: INK, fontFace: H, isTextBox: true, margin: 0 });
  s.addText(e[1], { x: x + 0.72, y: y + 0.56, w: 4.95, h: 0.5, fontSize: 12.5, color: INK2,
    fontFace: B, isTextBox: true, margin: 0 });
});
card(s, M, 5.9, CW, 1.0, INK);
[['USD 25', 'Modelling tools alone, one seat'], ['USD 60', 'Everything, up to 3 people'],
 ['USD 130', 'Everything, 4 to 10 people']].forEach(function (q, i) {
  const x = M + 0.45 + i * 3.3;
  s.addText(q[0], { x: x, y: 6.05, w: 3.15, h: 0.35, fontSize: 17, bold: true, color: SALMON,
    fontFace: H, isTextBox: true, margin: 0 });
  s.addText(q[1], { x: x, y: 6.41, w: 3.15, h: 0.3, fontSize: 11.5, color: LIGHTTXT, fontFace: B,
    isTextBox: true, margin: 0 });
});
s.addText('a month,\nper practice', { x: M + 10.35, y: 6.1, w: 1.7, h: 0.6, fontSize: 11,
  italic: true, color: MUTED, fontFace: B, lineSpacing: 13, isTextBox: true, margin: 0 });
s.addNotes(
  'This is the "why it can be in daily work" argument, and it is the one most likely to change what ' +
  'members actually do.\n\n' +
  'SAY: "I want to put one thing to you about adoption.\n\n' +
  'In my experience most practices here do not avoid BIM because they disagree with it. They avoid ' +
  'it because of what it costs to do properly, and because it feels like something for big jobs.\n\n' +
  'So this is priced per practice rather than per person. Everyone outside your office joins free, ' +
  'which matters because the usual reason coordination software dies on a project is that nobody ' +
  'will pay for the other parties to be on it. It is billed in shillings. And there is a free route ' +
  'for anyone with no software budget at all.\n\n' +
  'Twenty-five dollars a month for the modelling tools on their own. Sixty for everything up to ' +
  'three people. A hundred and thirty for a practice of four to ten.\n\n' +
  'I am not claiming it does everything the international products do. I am saying it does the ' +
  'things a Ugandan practice needs every week, at a price that lets BIM be normal rather than ' +
  'exceptional."\n\n' +
  'CHECK THE PRICES ARE CURRENT before saying them. Do not volunteer a comparison against named ' +
  'products. If asked, the numbers are in the written guide.'
);

/* ---------------------------------------------------- 11 WHERE BIM IS GOING */
s = pres.addSlide();
kicker(s, 'A VIEW, FOR YOUR COMMENT');
title(s, 'Where I think this is all heading');
[
  ['From documents to requirements that check themselves',
   'The open standard for this arrived in 2024. Instead of a written specification nobody verifies, a client issues machine-readable requirements and a model is checked against them automatically.'],
  ['From vendor formats to open ones',
   'The open model format became an ISO standard in 2024 and now covers infrastructure and georeferencing, not only buildings. Increasingly it is what clients ask to be handed.'],
  ['From automation to assistance',
   'Research on AI working directly with models has moved very fast. The expectation is that practices run their own assistants on their own projects well before 2030.'],
  ['From project information to asset information',
   'The value is moving past handover into operation and maintenance, and in time into city-scale data.'],
].forEach(function (r, i) {
  const y = 2.15 + i * 1.18;
  card(s, M, y, CW, 1.02, i % 2 ? TINT : TINT2);
  numTile(s, M + 0.35, y + 0.28, String(i + 1), i === 2 ? ACC : INK);
  s.addText(r[0], { x: M + 1.05, y: y + 0.15, w: 4.4, h: 0.75, fontSize: 14, bold: true,
    color: INK, fontFace: H, lineSpacing: 18, isTextBox: true, margin: 0 });
  s.addText(r[1], { x: M + 5.75, y: y + 0.17, w: 6.25, h: 0.75, fontSize: 11.5, color: INK2,
    fontFace: B, lineSpacing: 15, isTextBox: true, margin: 0 });
});
s.addText('That is my reading of it. I would genuinely like to know whether the Council sees it the ' +
  'same way.', { x: M, y: 6.95, w: CW, h: 0.4, fontSize: 13.5, color: MUTED, fontFace: B,
    italic: true, isTextBox: true, margin: 0 });
s.addNotes(
  'This is the slide that makes you a colleague with a view rather than someone selling something. ' +
  'Deliver it as an opinion open to correction, because that is what it is.\n\n' +
  'SAY: "I want to put a view to you and hear whether you agree, because between you you will have ' +
  'seen more of this than I have.\n\n' +
  'Four things seem to me to be happening.\n\n' +
  'First, information requirements are becoming machine-readable. The open standard for that arrived ' +
  'in 2024. Instead of a written specification nobody really verifies, a client issues requirements ' +
  'a computer can check a model against. That changes how our work gets judged.\n\n' +
  'Second, open formats are winning. The open model format became an ISO standard in 2024 and now ' +
  'covers infrastructure and georeferencing, not just buildings. More clients will ask to be handed ' +
  'that rather than a proprietary file.\n\n' +
  'Third, and fastest, AI working directly with models. The research has moved very quickly in two ' +
  'years and the expectation is that practices will run their own assistants well before 2030.\n\n' +
  'Fourth, the value is moving past handover into operation and maintenance.\n\n' +
  'That is my reading. I would like to know whether the Council sees it the same way, because if I ' +
  'am wrong about the direction I would rather find out now than in three years."\n\n' +
  'THEN PAUSE AND LET THEM TALK. This is where the meeting turns into a discussion, which is what ' +
  'you want. Do not rush on.'
);

/* ---------------------------------------------------- 12 PROPOSALS */
s = pres.addSlide();
kicker(s, 'A SUGGESTION, NOT A PLAN');
title(s, 'Where I think we could aim the tools');
[
  ['Adopt the open requirements standard',
   'So that what a client asks for and whether a model meets it become the same thing. It would also make the checking internationally recognised rather than my own invention.'],
  ['Keep open formats at the centre',
   'It is what makes any of this shareable, and it is why the tools should never be tied to one vendor, mine included.'],
  ['Finish the assistant, carefully',
   'The real prize is that BIM stops requiring someone to remember where a command lives.'],
  ['Draft something the profession could adopt',
   'Naming, classification, what a handover contains. There is a working draft in the tools already. It would need the Council to shape it and put its name to it, if the Council wanted that at all.'],
].forEach(function (r, i) {
  const y = 2.15 + i * 1.18;
  card(s, M, y, CW, 1.02, TINT);
  sq(s, M + 0.38, y + 0.42, i === 3 ? ACC : SLATE, 0.18);
  s.addText(r[0], { x: M + 0.85, y: y + 0.15, w: 4.6, h: 0.75, fontSize: 14, bold: true,
    color: INK, fontFace: H, lineSpacing: 18, isTextBox: true, margin: 0 });
  s.addText(r[1], { x: M + 5.75, y: y + 0.15, w: 6.25, h: 0.78, fontSize: 11.5, color: INK2,
    fontFace: B, lineSpacing: 15, isTextBox: true, margin: 0 });
});
s.addText('These are suggestions. If the Council sees the priorities differently, I would rather ' +
  'build to that.', { x: M, y: 6.95, w: CW, h: 0.4, fontSize: 13.5, bold: true, color: ACC2,
    fontFace: B, italic: true, isTextBox: true, margin: 0 });
s.addNotes(
  'Strong proposals, held lightly. That is the whole register. People support what they helped ' +
  'shape, so leave room for them to shape it.\n\n' +
  'SAY: "If that reading is roughly right, here is where I think the tools should aim. These are ' +
  'suggestions rather than a plan I have already settled on.\n\n' +
  'Adopt the open requirements standard, so that what a client asks for and whether a model meets it ' +
  'become the same thing. That would also make the checking internationally recognised rather than ' +
  'something I invented on my own.\n\n' +
  'Keep open formats at the centre, because that is what makes any of it shareable, and it is why ' +
  'these tools should never be tied to one vendor, including me.\n\n' +
  'Finish the assistant carefully, because the real prize is that BIM stops requiring somebody to ' +
  'remember where a command lives.\n\n' +
  'And a fourth, which I put last because it is the Council\'s to decide and not mine. There is a ' +
  'working draft in the tools of naming, classification and what a handover should contain. If the ' +
  'profession ever wanted something of its own, that draft is a starting point. It would need the ' +
  'Council to shape it and put its name to it, if the Council wanted that at all.\n\n' +
  'These are suggestions. If you see the priorities differently, I would rather build to that."\n\n' +
  'DO NOT PUSH THE FOURTH ONE. Put it down and leave it. If they pick it up, it becomes their idea, ' +
  'which is far better than it being yours.'
);

/* ---------------------------------------------------- 13 STEER */
s = pres.addSlide();
darkBg(s);
kicker(s, 'WHAT I WOULD VALUE FROM YOU', ACC);
title(s, 'Four things I would like your thinking on', PAPER);
[
  ['Is the direction right?', 'Have I read where this is going correctly, or am I missing something you can see from where you sit?'],
  ['What would members actually use?', 'I have built what I needed on my own projects. You know the membership far better than I do.'],
  ['Would training be useful?', 'If it would, what shape should it take, and who should shape the content?'],
  ['Who else should see this?', 'Inside the Society, or outside it.'],
].forEach(function (a, i) {
  const y = 2.3 + i * 1.05;
  numTile(s, M, y, String(i + 1), i === 0 ? ACC : '3A4048');
  s.addText(a[0], { x: M + 0.85, y: y - 0.02, w: 4.6, h: 0.45, fontSize: 18, bold: true,
    color: PAPER, fontFace: H, isTextBox: true, margin: 0 });
  s.addText(a[1], { x: M + 5.6, y: y - 0.02, w: 6.4, h: 0.8, fontSize: 13.5, color: LIGHTTXT,
    fontFace: B, lineSpacing: 18, isTextBox: true, margin: 0 });
});
card(s, M, 6.65, CW, 0.7, '2E333A');
s.addText('I am not asking the Council to decide anything today.', { x: M + 0.45, y: 6.65,
  w: CW - 0.9, h: 0.7, fontSize: 16, bold: true, color: SALMON, fontFace: H, valign: 'middle',
  isTextBox: true, margin: 0 });
s.addNotes(
  'This replaces an ask, and it is a stronger close than one. Questions invite people in; requests ' +
  'make them defensive.\n\n' +
  'SAY: "I will stop there, because what I came for is your thinking rather than a decision.\n\n' +
  'Four things I would value.\n\n' +
  'Have I read the direction correctly, or am I missing something you can see from where you sit?\n\n' +
  'What would members actually use? I have built what I needed on my own projects, and you know the ' +
  'membership far better than I do.\n\n' +
  'Would training be useful, and if so what shape should it take and who should shape the content?\n\n' +
  'And who else should see this, inside the Society or outside it?\n\n' +
  'I am not asking the Council to decide anything today. Thank you for the time."\n\n' +
  'THEN BE QUIET AND LET THEM TALK. The whole meeting is designed to arrive here.\n\n' +
  'BEFORE THEY DISPERSE, write down three things: anything they suggested you build or change, ' +
  'anyone they said you should talk to, and whether they want to see it again. Those are the only ' +
  'outcomes that matter, and all three come from them rather than from you.'
);

/* ---------------------------------------------------- 14 CLOSE */
s = pres.addSlide();
darkBg(s);
motif(s, M, 2.6, ACC);
s.addText('Thank you', { x: M, y: 3.05, w: 11.5, h: 1.1, fontSize: 46, bold: true, color: PAPER,
  fontFace: H, isTextBox: true, margin: 0 });
s.addText('I would rather spend the rest of the time on your questions than on my slides.',
  { x: M, y: 4.2, w: 10.5, h: 0.5, fontSize: 17, color: LIGHTTXT, fontFace: B, italic: true,
    isTextBox: true, margin: 0 });
s.addText('Davis E. Mayanja', { x: M, y: 5.35, w: 6, h: 0.35, fontSize: 16, bold: true, color: PAPER,
  fontFace: B, isTextBox: true, margin: 0 });
s.addText('mayanjadavis@gmail.com   ·   +256 787 472 999', { x: M, y: 5.75, w: 9, h: 0.35,
  fontSize: 13, color: MUTED, fontFace: B, isTextBox: true, margin: 0 });
s.addNotes(
  'Leave this up through the discussion.\n\n' +
  'Answer the question asked, then stop. The commonest mistake in a room like this is answering a ' +
  'short question at length and talking yourself into a weaker position than you started in.\n\n' +
  '"I do not know, and I will come back to you" is a complete answer. Never invent a number, a date ' +
  'or a client.\n\n' +
  'If somebody offers something, take it graciously and write it down. Do not negotiate it in the ' +
  'room and do not talk them further into it.'
);

pres.writeFile({ fileName: process.argv[2] }).then(function () {
  console.log('written', process.argv[2]);
});
