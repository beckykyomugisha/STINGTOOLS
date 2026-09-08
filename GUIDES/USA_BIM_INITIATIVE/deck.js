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
pres.title = 'BIM for Ugandan Practice';

function sq(s, x, y, color, size) {
  s.addShape(pres.ShapeType.rect, { x: x, y: y, w: size || 0.16, h: size || 0.16, fill: { color: color } });
}
function motif(s, x, y, color) {
  sq(s, x, y, color, 0.15); sq(s, x + 0.22, y, color, 0.15); sq(s, x + 0.44, y, color, 0.15);
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
function numTile(s, x, y, n, color, txtcolor) {
  s.addShape(pres.ShapeType.rect, { x: x, y: y, w: 0.46, h: 0.46, fill: { color: color || ACC } });
  s.addText(n, { x: x, y: y, w: 0.46, h: 0.46, fontSize: 18, bold: true, color: txtcolor || 'FFFFFF',
    fontFace: H, align: 'center', valign: 'middle', isTextBox: true, margin: 0 });
}

/* ------------------------------------------------ 1 TITLE */
let s = pres.addSlide();
darkBg(s);
motif(s, M, 1.55, ACC);
s.addText('PRESENTATION TO THE COUNCIL', { x: M, y: 2.0, w: CW, h: 0.32, fontSize: 12.5,
  bold: true, charSpacing: 3, color: ACC, fontFace: B, isTextBox: true, margin: 0 });
s.addText('Building Information Modelling\nfor Ugandan practice', { x: M, y: 2.45, w: 10.6, h: 1.9,
  fontSize: 44, bold: true, color: PAPER, fontFace: H, lineSpacing: 50, isTextBox: true, margin: 0 });
s.addText('PlanScape and StingTools — a BIM platform built in Kampala', { x: M, y: 4.45, w: 10.6,
  h: 0.4, fontSize: 17, color: LIGHTTXT, fontFace: B, italic: true, isTextBox: true, margin: 0 });
s.addText('Davis Mayanja', { x: M, y: 5.5, w: 6, h: 0.3, fontSize: 14.5, bold: true, color: PAPER,
  fontFace: B, isTextBox: true, margin: 0 });
s.addText('Uganda Society of Architects  ·  September 2026', { x: M, y: 5.85, w: 7, h: 0.3,
  fontSize: 12.5, color: MUTED, fontFace: B, isTextBox: true, margin: 0 });
s.addNotes(
  'Hold until everyone is seated. Do not talk over people sitting down, and do not warm up. ' +
  'This room is senior and short on time.\n\n' +
  'SAY: "Mr President, Chairman, thank you for making the time. My name is Davis Mayanja. I am an ' +
  'architect-side BIM specialist, and for the last few years I have been building software here in ' +
  'Kampala for the way we actually work.\n\n' +
  'I have twenty minutes of material and I would much rather spend forty on your questions, so I ' +
  'will move quickly."'
);

/* ------------------------------------------------ 2 THE ASK */
s = pres.addSlide();
kicker(s, 'SO YOU KNOW WHERE THIS IS GOING');
title(s, 'What I am asking for, and what I am not');

card(s, M, 2.15, 5.95, 3.7, TINT);
s.addText('Asking for', { x: M + 0.4, y: 2.5, w: 5.15, h: 0.4, fontSize: 21, bold: true,
  color: ACC2, fontFace: H, isTextBox: true, margin: 0 });
const asks2 = [
  'That the Society endorses a BIM training programme for its members',
  'A technical counterpart in the ICT Cluster, so the evaluation is yours',
  'Your help identifying how the remaining work gets funded',
];
asks2.forEach(function (t, i) {
  sq(s, M + 0.4, 3.2 + i * 0.78, ACC, 0.13);
  s.addText(t, { x: M + 0.72, y: 3.06 + i * 0.78, w: 4.75, h: 0.7, fontSize: 14, color: INK2,
    fontFace: B, lineSpacing: 19, isTextBox: true, margin: 0 });
});

card(s, M + 6.4, 2.15, 5.95, 3.7, INK);
s.addText('Not asking for', { x: M + 6.8, y: 2.5, w: 5.15, h: 0.4, fontSize: 21, bold: true,
  color: SALMON, fontFace: H, isTextBox: true, margin: 0 });
const nots = [
  'Any money from the Society',
  'A decision on any agreement today',
  'Exclusivity, or anything binding on your members',
];
nots.forEach(function (t, i) {
  sq(s, M + 6.8, 3.2 + i * 0.78, ACC, 0.13);
  s.addText(t, { x: M + 7.12, y: 3.06 + i * 0.78, w: 4.75, h: 0.7, fontSize: 14, color: LIGHTTXT,
    fontFace: B, lineSpacing: 19, isTextBox: true, margin: 0 });
});
s.addText('None of what I am asking for costs the Society money.', { x: M, y: 6.15, w: CW, h: 0.5,
  fontSize: 16, bold: true, color: ACC2, fontFace: H, italic: true, isTextBox: true, margin: 0 });
s.addNotes(
  'Put this up front so nobody spends the next twenty minutes wondering what the catch is. A room ' +
  'that is waiting for an ask does not listen properly.\n\n' +
  'SAY: "Before I start, let me tell you where this is going, so you are not sitting there waiting ' +
  'for the ask.\n\n' +
  'I am asking three things. That the Society endorses a BIM training programme for its members. ' +
  'That you name someone in the ICT Cluster as a technical counterpart, so the judgement about ' +
  'whether this is any good is yours and not mine. And that you help me work out how the remaining ' +
  'development gets funded.\n\n' +
  'I am not asking the Society for money. I am not asking you to decide anything today. And I am not ' +
  'asking for exclusivity or for anything that binds your members.\n\n' +
  'None of it costs the Society anything. Now let me tell you why I think it is worth your time."\n\n' +
  'PAUSE after that. The room relaxes, and everything after this is heard differently.'
);

/* ------------------------------------------------ 3 STANDARD CHANGED */
s = pres.addSlide();
kicker(s, 'ONE  ·  WHAT HAS CHANGED');
title(s, 'The standard for project information has moved');

s.addText('ISO 19650', { x: M, y: 2.15, w: 5.4, h: 0.85, fontSize: 52, bold: true, color: ACC,
  fontFace: H, isTextBox: true, margin: 0 });
s.addText('is now the accepted international standard for managing information on a building ' +
  'project — how it is named, versioned, approved and handed over.',
  { x: M, y: 3.05, w: 5.6, h: 1.6, fontSize: 15, color: INK2, fontFace: B, lineSpacing: 22,
    isTextBox: true, margin: 0 });

s.addText('And governments have been requiring it', { x: M + 6.4, y: 2.1,
  w: 5.95, h: 0.4, fontSize: 16, bold: true, color: INK, fontFace: H, isTextBox: true, margin: 0 });
[
  ['2013', 'Dubai', 'Municipality Circular 196, widened 2015'],
  ['2015', 'Singapore', 'All new projects over 5,000 sq m'],
  ['2016', 'United Kingdom', 'All centrally funded public projects'],
  ['2020', 'Germany', 'New federal transport infrastructure'],
].forEach(function (c, i) {
  const y = 2.55 + i * 0.72;
  card(s, M + 6.4, y, 5.95, 0.62, TINT);
  s.addText(c[0], { x: M + 6.7, y: y + 0.13, w: 0.8, h: 0.36, fontSize: 15, bold: true,
    color: ACC, fontFace: H, isTextBox: true, margin: 0 });
  s.addText(c[1], { x: M + 7.55, y: y + 0.13, w: 2.0, h: 0.36, fontSize: 14, bold: true,
    color: INK, fontFace: B, isTextBox: true, margin: 0 });
  s.addText(c[2], { x: M + 9.5, y: y + 0.15, w: 2.7, h: 0.34, fontSize: 11, color: INK2,
    fontFace: B, isTextBox: true, margin: 0 });
});

card(s, M, 5.55, CW, 1.05, INK);
s.addText('This has only ever moved in one direction. No country that adopted it has gone back.',
  { x: M + 0.45, y: 5.55, w: CW - 0.9, h: 1.05, fontSize: 17, color: PAPER, fontFace: H,
    italic: true, valign: 'middle', isTextBox: true, margin: 0 });
s.addNotes(
  'SAY: "The way a set of building information is expected to be put together has changed, and it ' +
  'changed outside Uganda first.\n\n' +
  'ISO 19650 is now the accepted international standard for managing project information — how it ' +
  'is named, how it is versioned, who approved what and when, and what condition it is handed over ' +
  'in. It is not a modelling standard. It is an information standard, which is why it matters to ' +
  'architects and not only to software people.\n\n' +
  'And governments have started to require it. The United Kingdom, the Emirates, Singapore, ' +
  'Germany — BIM is mandated on public work in all of them.\n\n' +
  'I want to be careful here. I am not telling you Uganda has mandated anything. It has not. I am ' +
  'telling you the direction of travel, and it has only ever gone one way."\n\n' +
  'Do not claim a Ugandan mandate. Someone will check, and you lose the room.'
);

/* ------------------------------------------------ 4 REGION */
s = pres.addSlide();
kicker(s, 'ONE  ·  WHAT HAS CHANGED');
title(s, 'The region has not kept pace');

card(s, M, 2.15, 6.0, 2.5, TINT);
s.addText('Kenya', { x: M + 0.4, y: 2.45, w: 5.2, h: 0.4, fontSize: 20, bold: true, color: ACC2,
  fontFace: H, isTextBox: true, margin: 0 });
s.addText('Peer economy, larger construction sector. Published research finds BIM adoption still ' +
  'lagging, and names the consequence as poor coordination of information between the parties on a ' +
  'project.', { x: M + 0.4, y: 2.95, w: 5.2, h: 1.5, fontSize: 14, color: INK2, fontFace: B,
    lineSpacing: 21, isTextBox: true, margin: 0 });

card(s, M + 6.4, 2.15, 6.0, 2.5, TINT);
s.addText('Uganda', { x: M + 6.8, y: 2.45, w: 5.2, h: 0.4, fontSize: 20, bold: true, color: ACC2,
  fontFace: H, isTextBox: true, margin: 0 });
s.addText('No national standard for how architectural information is delivered. No locally built, ' +
  'locally supported tooling. Everything on the market is priced in dollars and assumes a ' +
  'connection.', { x: M + 6.8, y: 2.95, w: 5.2, h: 1.5, fontSize: 14, color: INK2, fontFace: B,
    lineSpacing: 21, isTextBox: true, margin: 0 });

s.addText('That is a gap. It is also an opening, because the body that moves first writes the ' +
  'standard instead of inheriting one.', { x: M, y: 5.1, w: 11.4, h: 1.2, fontSize: 21, bold: true,
    color: INK, fontFace: H, lineSpacing: 32, isTextBox: true, margin: 0 });
motif(s, M, 6.5, ACC);
s.addNotes(
  'SAY: "The region has not kept pace with that.\n\n' +
  'Kenya is the obvious comparison — bigger construction sector, same regional market. Published ' +
  'research on Kenyan adoption finds it still lagging, and it names the consequence: poor ' +
  'coordination of information between the parties on a project. That is a polite way of describing ' +
  'what we all recognise. Drawings that disagree. Schedules that do not match the model. Handover ' +
  'information assembled at the last minute.\n\n' +
  'Uganda is in the same position with two extra problems. We have no national standard for how ' +
  'architectural information is delivered. And there is nothing on the market built here.\n\n' +
  'That is a gap. But it is also an opening. The body that moves first on this writes the standard, ' +
  'instead of inheriting one written somewhere else for somebody else."'
);

/* ------------------------------------------------ 5 COST TO PRACTICE */
s = pres.addSlide();
kicker(s, 'ONE  ·  WHAT HAS CHANGED');
title(s, 'What that costs a practice in your membership');
[
  ['Not being asked twice', 'Clients and funders increasingly ask how information will be delivered. A practice with no answer stops appearing on shortlists, and never finds out why.'],
  ['Competing on unequal terms', 'International and regional firms bidding for work here already deliver this way. That is the comparison your members are measured against.'],
  ['Paying for it on site', 'Coordination done by hand is slower and less complete. What it misses does not disappear. It surfaces during construction, where it costs the most.'],
].forEach(function (r, i) {
  const x = M + i * 4.0;
  card(s, x, 2.2, 3.7, 3.55);
  numTile(s, x + 0.35, 2.55, String(i + 1), ACC);
  s.addText(r[0], { x: x + 0.35, y: 3.2, w: 3.0, h: 0.75, fontSize: 18, bold: true, color: INK,
    fontFace: H, lineSpacing: 23, isTextBox: true, margin: 0 });
  s.addText(r[1], { x: x + 0.35, y: 4.0, w: 3.0, h: 1.6, fontSize: 13, color: INK2, fontFace: B,
    lineSpacing: 19, isTextBox: true, margin: 0 });
});
s.addText('This is why I am in front of the Society rather than in front of individual practices.',
  { x: M, y: 6.1, w: CW, h: 0.5, fontSize: 15, color: MUTED, fontFace: B, italic: true,
    isTextBox: true, margin: 0 });
s.addNotes(
  'SAY: "What does that cost a practice in your membership? Three things.\n\n' +
  'First, not being asked twice. Clients and funders increasingly ask how project information will ' +
  'be delivered. A practice with no answer stops appearing on shortlists, and usually never finds ' +
  'out that is why.\n\n' +
  'Second, competing on unequal terms. The international and regional firms bidding for work here ' +
  'already work this way. That is the standard our members are being compared against, whether or ' +
  'not anyone told them.\n\n' +
  'Third, paying for it on site. Coordination done by hand is slower and less complete. What it ' +
  'misses does not go away — it turns up during construction, which is the most expensive place to ' +
  'find it, and the architect usually gets the blame.\n\n' +
  'That is why I am in front of the Society rather than in front of individual practices. A practice ' +
  'solves this one firm at a time. A professional body can solve it for the profession."'
);

/* ------------------------------------------------ 6 TWO PARTS */
s = pres.addSlide();
kicker(s, 'TWO  ·  WHAT I HAVE BUILT');
title(s, 'Two parts, designed as one system');

card(s, M, 2.15, 5.95, 3.9, INK);
sq(s, M + 0.45, 2.55, ACC, 0.22);
s.addText('StingTools', { x: M + 0.45, y: 2.95, w: 5.0, h: 0.5, fontSize: 26, bold: true,
  color: PAPER, fontFace: H, isTextBox: true, margin: 0 });
s.addText('Inside the design software', { x: M + 0.45, y: 3.45, w: 5.0, h: 0.3, fontSize: 13,
  color: SALMON, fontFace: B, italic: true, isTextBox: true, margin: 0 });
s.addText('Checks a model against data standards automatically. Keeps naming and classification ' +
  'consistent. Produces drawings, schedules and quantities from the model rather than by hand, and ' +
  'assembles handover information as the project is built.',
  { x: M + 0.45, y: 3.9, w: 5.05, h: 1.9, fontSize: 13.5, color: LIGHTTXT, fontFace: B,
    lineSpacing: 20, isTextBox: true, margin: 0 });

card(s, M + 6.4, 2.15, 5.95, 3.9, TINT);
sq(s, M + 6.85, 2.55, ACC, 0.22);
s.addText('PlanScape', { x: M + 6.85, y: 2.95, w: 5.0, h: 0.5, fontSize: 26, bold: true, color: INK,
  fontFace: H, isTextBox: true, margin: 0 });
s.addText('The platform around it', { x: M + 6.85, y: 3.45, w: 5.0, h: 0.3, fontSize: 13,
  color: ACC2, fontFace: B, italic: true, isTextBox: true, margin: 0 });
s.addText('One place for project documents, issues and correspondence, so a whole team works from ' +
  'the same record. Priced per practice rather than per person, and everyone outside your office ' +
  'joins free.', { x: M + 6.85, y: 3.9, w: 5.05, h: 1.9, fontSize: 13.5, color: INK2, fontFace: B,
    lineSpacing: 20, isTextBox: true, margin: 0 });

s.addText('Built in Kampala, from 2021, for the way we actually work here.', { x: M, y: 6.3, w: CW,
  h: 0.4, fontSize: 14.5, color: MUTED, fontFace: B, italic: true, isTextBox: true, margin: 0 });
s.addNotes(
  'Ninety seconds maximum. The demonstration does the real work.\n\n' +
  'SAY: "There are two parts, designed as one system.\n\n' +
  'StingTools sits inside the design software. It checks a model against data standards ' +
  'automatically, keeps naming and classification consistent, and produces drawings, schedules and ' +
  'quantities from the model instead of by hand. It also assembles handover information while the ' +
  'project is being built, rather than in a panic at the end.\n\n' +
  'PlanScape is the platform around it — one place for documents, issues and correspondence so the ' +
  'whole team works from the same record. It is priced per practice, not per person, and anyone ' +
  'outside your office joins free.\n\n' +
  'Both were started in 2021, here, for the conditions we actually work in."'
);

/* ------------------------------------------------ 7 MULTI-PLATFORM  (the new one) */
s = pres.addSlide();
kicker(s, 'TWO  ·  WHAT I HAVE BUILT');
title(s, 'It does not matter what your members draw in');

const hosts = [
  ['Revit', 'Full add-in inside Revit', ACC],
  ['ArchiCAD', 'Save to IFC and it arrives, with changes written back', SLATE],
  ['Blender + Bonsai', 'A free, open-source route. No licence at all.', ACC],
  ['Tekla', 'Structural models come in through IFC', SLATE],
];
hosts.forEach(function (hst, i) {
  const x = M + (i % 2) * 6.4;
  const y = 2.15 + Math.floor(i / 2) * 1.55;
  card(s, x, y, 5.95, 1.3, TINT);
  sq(s, x + 0.4, y + 0.32, hst[2], 0.2);
  s.addText(hst[0], { x: x + 0.85, y: y + 0.22, w: 4.8, h: 0.4, fontSize: 18, bold: true,
    color: INK, fontFace: H, isTextBox: true, margin: 0 });
  s.addText(hst[1], { x: x + 0.85, y: y + 0.66, w: 4.85, h: 0.5, fontSize: 13, color: INK2,
    fontFace: B, isTextBox: true, margin: 0 });
});

card(s, M, 5.35, CW, 1.35, INK);
s.addText('One project. One shared record. Every element keeps the same identity across all four.',
  { x: M + 0.45, y: 5.55, w: CW - 0.9, h: 0.45, fontSize: 19, bold: true, color: PAPER,
    fontFace: H, isTextBox: true, margin: 0 });
s.addText('A practice on ArchiCAD, a structural engineer on Tekla and a student on free software ' +
  'can work on the same project without anyone converting anything by hand.',
  { x: M + 0.45, y: 6.02, w: CW - 0.9, h: 0.5, fontSize: 14, color: LIGHTTXT, fontFace: B,
    isTextBox: true, margin: 0 });
s.addNotes(
  'THE SLIDE THAT WINS THE ICT CHAIR. In a room of architects, "which software does it need" is the ' +
  'first question on everyone\'s mind, and most of them are not on Revit.\n\n' +
  'SAY: "Now, the question everyone in this room is waiting to ask. Which software does it need.\n\n' +
  'The answer is that it does not matter much. There is a full add-in inside Revit, which is the ' +
  'deepest integration. ArchiCAD works by saving to IFC — the model arrives in the platform, and ' +
  'changes come back the other way. Tekla comes in the same way, so your structural engineer is on ' +
  'the same project. And there is a free, open-source route through Blender and Bonsai, which costs ' +
  'nothing at all — no licence, for anybody.\n\n' +
  'The important part is the last line. Every element keeps the same identity across all four. So a ' +
  'practice on ArchiCAD, a structural engineer on Tekla and a student on free software can all work ' +
  'on one project, and nobody is converting anything by hand or losing track of what is what.\n\n' +
  'I tested that with one element identity resolving across all four at once. It works."\n\n' +
  'IF ASKED how deep each one goes, be exact and do not inflate it: Revit is a native add-in and the ' +
  'others go through IFC. That is a deliberate design decision, not a gap — IFC is the open standard ' +
  'and it means no one is locked in. But there is no native ArchiCAD or Tekla plug-in, and you ' +
  'should say so if asked.\n\n' +
  'IF ASKED about the free route, it is worth dwelling on: a member with no software budget at all, ' +
  'or a student, can take part using Blender and Bonsai. That matters to a professional body.'
);

/* ------------------------------------------------ 8 WHERE IT STANDS */
s = pres.addSlide();
kicker(s, 'TWO  ·  WHAT I HAVE BUILT');
title(s, 'Where it stands today — plainly');
[
  ['In production', ACC, [
    'Document control and the shared project record',
    'Issues and correspondence',
    'The mobile application, working offline',
    'Model checking and audit',
    'Drawings, schedules, quantities, handover data',
  ]],
  ['Still in hand', SLATE, [
    'Serving many practices from one system',
    'Mobile money payments',
    'Hosting sized for regional use',
    'An independent security review',
  ]],
  ['Not yet validated', MUTED, [
    'The engineering calculation engines',
    'Complete and tested, but never taken through independent professional validation',
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
  'Volunteer every word of this before anyone asks. If the room discovers it instead of being told ' +
  'it, you lose them.\n\n' +
  'SAY: "Let me be plain about where this stands.\n\n' +
  'In production, in daily use on live projects: the document control, the issues, the shared ' +
  'record, the mobile app that works offline, the model checking, and the drawings, schedules, ' +
  'quantities and handover data.\n\n' +
  'Still in hand: serving many practices from one system, payments, hosting sized for regional use, ' +
  'and an independent security review.\n\n' +
  'And not yet validated: the engineering calculation engines. They are complete and tested but they ' +
  'have never been taken through independent professional validation, so I only offer them as ' +
  'commissioned work with manual cross-checks alongside. I would rather say that than have an ' +
  'engineer find it out.\n\n' +
  'The honest summary is one line. It is ready for one practice to use today. It is not yet ready to ' +
  'serve the whole profession at once. That difference is the work that remains."'
);

/* ------------------------------------------------ 9 DEMO */
s = pres.addSlide();
darkBg(s);
motif(s, M, 2.55, ACC);
s.addText('Rather than describe it', { x: M, y: 3.0, w: 11.5, h: 0.6, fontSize: 20, color: MUTED,
  fontFace: B, italic: true, isTextBox: true, margin: 0 });
s.addText('Let me show you.', { x: M, y: 3.6, w: 11.5, h: 1.2, fontSize: 54, bold: true,
  color: PAPER, fontFace: H, isTextBox: true, margin: 0 });
s.addText('A real model  ·  tagged and audited  ·  drawings and schedules out  ·  quantities out',
  { x: M, y: 5.0, w: 11.5, h: 0.5, fontSize: 15, color: SALMON, fontFace: B, isTextBox: true,
    margin: 0 });
s.addNotes(
  'Six minutes maximum. Rehearsed twice, end to end, on the same setup you will actually run on.\n\n' +
  'Show architectural work, not engineering. Take a real model, tag it, run the audit, show what it ' +
  'catches, produce a drawing and a schedule, pull a quantity. Every person in this room has done ' +
  'each of those by hand at two in the morning, which is exactly why it lands.\n\n' +
  'SAY as you start: "This is a real project, not a sample file."\n\n' +
  'Narrate what you are doing, not what the software is doing. "This would normally take me an ' +
  'afternoon" is worth more than any feature name.\n\n' +
  'Do not demonstrate the calculation engines. They are the one thing you have told the room is ' +
  'unvalidated.\n\n' +
  'IF SOMETHING FAILS: say so, move on, use the recording. Do not try to fix it in front of the ' +
  'room. "That is the part I told you is still being finished" is recoverable. Silence while you ' +
  'fiddle with a laptop is not.'
);

/* ------------------------------------------------ 10 CPD */
s = pres.addSlide();
kicker(s, 'THREE  ·  WHAT THE SOCIETY GAINS');
title(s, 'The first one is already your obligation');
s.addText('20', { x: M, y: 2.2, w: 2.6, h: 1.7, fontSize: 110, bold: true, color: ACC, fontFace: H,
  isTextBox: true, margin: 0 });
s.addText('CPD points every practising architect must earn each year to renew a practising licence',
  { x: M, y: 3.95, w: 4.6, h: 1.1, fontSize: 15, bold: true, color: INK, fontFace: B,
    lineSpacing: 21, isTextBox: true, margin: 0 });
s.addText('Architects Registration (Continuing Professional\nDevelopment) Bye Laws, gazetted 2019',
  { x: M, y: 5.15, w: 4.6, h: 0.7, fontSize: 12, color: MUTED, fontFace: B, italic: true,
    lineSpacing: 16, isTextBox: true, margin: 0 });
[
  ['It is permanent', 'The obligation does not go away, and it repeats every year for every member in practice.'],
  ['This content is hard to find here', 'Most CPD available locally is a supplier presentation. Structured, hands-on training in information standards is not on offer.'],
  ['The Society would own it', 'Your programme, your accreditation, your name, delivered by one of your own members rather than an imported trainer.'],
].forEach(function (c, i) {
  const y = 2.2 + i * 1.5;
  card(s, M + 5.3, y, 7.1, 1.28, i === 2 ? TINT2 : TINT);
  s.addText(c[0], { x: M + 5.65, y: y + 0.18, w: 6.4, h: 0.35, fontSize: 16, bold: true,
    color: ACC2, fontFace: H, isTextBox: true, margin: 0 });
  s.addText(c[1], { x: M + 5.65, y: y + 0.58, w: 6.4, h: 0.6, fontSize: 13, color: INK2,
    fontFace: B, lineSpacing: 18, isTextBox: true, margin: 0 });
});
s.addNotes(
  'This is your strongest argument. Slow down, and let the number sit on the screen.\n\n' +
  'SAY: "The first thing the Society gains is not a favour I am doing you. It is something you are ' +
  'already obliged to do.\n\n' +
  'Every practising architect in Uganda needs twenty CPD points a year to renew a practising ' +
  'licence. That is the 2019 bye-laws, not a suggestion. So the Society has a permanent obligation ' +
  'to put credible content in front of its members, every year, forever.\n\n' +
  'And this content is hard to source here. Most of what is available locally is a supplier ' +
  'presenting a product. Structured, hands-on training in information standards is not really on ' +
  'offer in Kampala.\n\n' +
  'It would be your programme. Your accreditation, your name, delivered by one of your own members ' +
  'rather than someone flown in."\n\n' +
  'THEN ASK, do not assert: "I do not know what accreditation would require for a course like this, ' +
  'or what points it would carry, or what your members currently pay for CPD. You do. That is one of ' +
  'the things I would like your guidance on."\n\n' +
  'Asking makes them co-owners of the idea. Claiming you already know makes you a supplier.'
);

/* ------------------------------------------------ 11 STANDARD */
s = pres.addSlide();
kicker(s, 'THREE  ·  WHAT THE SOCIETY GAINS');
title(s, 'A standard the Society could actually enforce');
s.addText('Publishing a standard is the easy part.\nGetting anyone to comply with it is not.',
  { x: M, y: 2.2, w: 5.6, h: 1.3, fontSize: 23, bold: true, color: INK, fontFace: H,
    lineSpacing: 34, isTextBox: true, margin: 0 });
s.addText('Standards die because compliance is expensive and nobody can check it. A document on a ' +
  'website changes nothing about what actually arrives in an inbox.',
  { x: M, y: 3.65, w: 5.6, h: 1.5, fontSize: 14, color: INK2, fontFace: B, lineSpacing: 21,
    isTextBox: true, margin: 0 });
card(s, M + 6.4, 2.15, 5.95, 3.55, INK);
s.addText('What already exists', { x: M + 6.85, y: 2.5, w: 5.1, h: 0.4, fontSize: 19, bold: true,
  color: SALMON, fontFace: H, isTextBox: true, margin: 0 });
[
  'Naming and classification schemes',
  'Drawing types, title blocks, view templates',
  'A check that runs against a model',
  'A report naming exactly what fails, and where',
].forEach(function (t, i) {
  sq(s, M + 6.85, 3.15 + i * 0.6, ACC, 0.13);
  s.addText(t, { x: M + 7.15, y: 3.03 + i * 0.6, w: 4.9, h: 0.5, fontSize: 13.5, color: LIGHTTXT,
    fontFace: B, isTextBox: true, margin: 0 });
});
s.addText('A standard that can be checked automatically is a standard that gets used.',
  { x: M, y: 5.95, w: 11.5, h: 0.7, fontSize: 21, bold: true, color: ACC2, fontFace: H,
    italic: true, isTextBox: true, margin: 0 });
s.addNotes(
  'Aim this at the Chair of the Board of Practice. Standards and documentation quality are his remit.\n\n' +
  'SAY: "The second thing is a standard.\n\n' +
  'Publishing a standard is the easy part. Getting anyone to comply with it is the hard part, and it ' +
  'is where most of them die, because compliance is expensive and nobody can check it. A document on ' +
  'a website changes nothing about what actually arrives in an inbox.\n\n' +
  'What I would put on the table is different. The naming and classification schemes, the drawing ' +
  'types, the title blocks and templates already exist — and so does a check that runs against a ' +
  'model and tells you exactly what fails and where.\n\n' +
  'A standard that can be checked automatically is a standard that gets used. If the Society wanted ' +
  'to write a national standard for architectural information, it would start from something that ' +
  'already works rather than a blank page."\n\n' +
  'IF ASKED whether you are proposing they adopt your naming scheme: "No. I am proposing it as a ' +
  'first draft for you to argue with. It should carry your name, not mine."'
);

/* ------------------------------------------------ 12 SMALL PRACTICE */
s = pres.addSlide();
kicker(s, 'THREE  ·  WHAT THE SOCIETY GAINS');
title(s, 'And it has to work for a four-person practice');
[
  ['Priced per practice', 'Not per person. A practice pays once, not once for every member of staff.'],
  ['Everyone outside joins free', 'Client, contractor, quantity surveyor and consultants all join a project at no licence cost to anyone.'],
  ['Billed in shillings', 'Not in dollars, and not exposed to a rate nobody here controls.'],
  ['A free route exists', 'Through Blender and Bonsai, a member or a student with no software budget can still take part.'],
].forEach(function (e, i) {
  const x = M + (i % 2) * 6.4;
  const y = 2.2 + Math.floor(i / 2) * 1.75;
  card(s, x, y, 5.95, 1.5);
  sq(s, x + 0.4, y + 0.35, ACC, 0.18);
  s.addText(e[0], { x: x + 0.85, y: y + 0.25, w: 4.8, h: 0.38, fontSize: 16.5, bold: true,
    color: INK, fontFace: H, isTextBox: true, margin: 0 });
  s.addText(e[1], { x: x + 0.85, y: y + 0.68, w: 4.85, h: 0.7, fontSize: 13, color: INK2,
    fontFace: B, lineSpacing: 18, isTextBox: true, margin: 0 });
});
card(s, M, 5.85, CW, 1.08, INK);
[['USD 25', 'Revit tools alone, one seat'], ['USD 60', 'Full platform, up to 3 people'],
 ['USD 130', 'Full platform, 4 to 10 people']].forEach(function (q, i) {
  const x = M + 0.45 + i * 3.35;
  s.addText(q[0], { x: x, y: 6.02, w: 3.2, h: 0.35, fontSize: 17, bold: true, color: SALMON,
    fontFace: H, isTextBox: true, margin: 0 });
  s.addText(q[1], { x: x, y: 6.38, w: 3.2, h: 0.3, fontSize: 11.5, color: LIGHTTXT, fontFace: B,
    isTextBox: true, margin: 0 });
});
s.addText('a month,\nper practice,\nnot per person', { x: M + 10.3, y: 5.96, w: 1.7, h: 0.9,
  fontSize: 11, italic: true, color: MUTED, fontFace: B, lineSpacing: 13, isTextBox: true,
  margin: 0 });
s.addNotes(
  'SAY: "The third thing is that none of this matters unless it works for the practices you actually ' +
  'represent, which are mostly small.\n\n' +
  'It is priced per practice, not per person. A four-person practice pays once, not four times.\n\n' +
  'Everyone outside your office joins free. The client, the contractor, the quantity surveyor, the ' +
  'other consultants — no licence cost to anybody. That matters more than it sounds, because the ' +
  'usual reason coordination software fails on a project here is that nobody will pay for the other ' +
  'parties to be on it.\n\n' +
  'It is billed in shillings. And there is a free route through Blender and Bonsai, so a member with ' +
  'no software budget, or a student, can still take part.\n\n' +
  'And to be concrete, because somebody is about to ask. Twenty-five dollars a month for the Revit tools on their own. Sixty for the full platform for up to three people. A hundred and thirty for a practice of four to ten. Per practice, not per person, and less again on annual billing.\n\nAnd I would like to offer Society members a discounted rate on top of that. I would rather put that on the table ' +
  'now than have you ask me for it later."\n\n' +
  'CHECK THE PRICES ARE STILL CURRENT before you say them out loud.\n\n' +
  'Offering a discount unprompted reads as goodwill; conceding one under pressure reads as a margin ' +
  'you were hiding. Do not name a discount percentage in the room, only the willingness to agree one.'
);

/* ------------------------------------------------ 13 COHORT */
s = pres.addSlide();
kicker(s, 'THREE  ·  WHAT THE SOCIETY GAINS');
title(s, 'What a cohort would actually cover');
[
  ['Day 1', 'Why information standards exist', 'ISO 19650 in plain terms. Naming, versions, approvals, handover. What a client is really asking for.'],
  ['Day 2', 'Modelling to a standard', 'Working in your own software — Revit, ArchiCAD or the free route — to a shared naming and classification scheme.'],
  ['Day 3', 'Checking and coordinating', 'Running the audit, reading what it reports, raising and closing issues across a team.'],
  ['Day 4', 'Getting the work out', 'Drawings, schedules and quantities from the model. Where the time actually goes.'],
  ['Day 5', 'Handover, and an exercise', 'Producing handover information, then a hands-on exercise members complete and take away.'],
].forEach(function (dd, i) {
  const y = 2.15 + i * 0.92;
  card(s, M, y, CW, 0.78, i % 2 ? TINT : TINT2);
  s.addText(dd[0], { x: M + 0.35, y: y + 0.2, w: 0.85, h: 0.4, fontSize: 15, bold: true,
    color: ACC2, fontFace: H, isTextBox: true, margin: 0 });
  s.addText(dd[1], { x: M + 1.35, y: y + 0.2, w: 3.5, h: 0.4, fontSize: 15, bold: true, color: INK,
    fontFace: H, isTextBox: true, margin: 0 });
  s.addText(dd[2], { x: M + 5.1, y: y + 0.2, w: 6.7, h: 0.45, fontSize: 12.5, color: INK2,
    fontFace: B, isTextBox: true, margin: 0 });
});
s.addText('About 25 members per cohort. Hands-on throughout, on their own machines.',
  { x: M, y: 6.85, w: CW, h: 0.4, fontSize: 13.5, color: MUTED, fontFace: B, italic: true,
    isTextBox: true, margin: 0 });
s.addNotes(
  'This slide exists because "endorse a training programme" is not a thing anyone can say yes to ' +
  'until they know what is in it. Do not read the table out. Point at it and summarise.\n\n' +
  'SAY: "So that you are not endorsing something abstract, this is roughly what a week would look ' +
  'like.\n\n' +
  'Day one is why information standards exist at all, in plain terms — what a client is actually ' +
  'asking for when they ask for BIM. Day two is modelling to a standard, and people work in ' +
  'whatever they already use. Day three is checking and coordinating. Day four is getting the work ' +
  'out — drawings, schedules, quantities. Day five is handover, and an exercise they complete and ' +
  'take away.\n\n' +
  'About twenty-five members at a time, hands-on throughout, on their own machines.\n\n' +
  'The shape is mine but the content should be yours. If the Board of Education wants it structured ' +
  'differently, I would rather build what you would actually accredit."\n\n' +
  'That last sentence matters. It hands them ownership, and it is the sentence most likely to turn ' +
  'this from a proposal into their programme.'
);

/* ------------------------------------------------ 14 LIMITS */
s = pres.addSlide();
kicker(s, 'WHAT I AM NOT GOING TO OVERSELL');
title(s, 'Three things you should hear from me');
[
  ['The deepest automation is in Revit', 'The other tools connect through IFC, which is the open standard and means nobody is locked in — but there is no native ArchiCAD or Tekla plug-in, and I am not going to imply there is.'],
  ['The calculation engines are unvalidated', 'Complete and tested, but never taken through independent professional validation. Offered only as commissioned work, with manual cross-checks. Your engineers would be the right people to validate them.'],
  ['It is not finished', 'Ready for one practice today. Serving the whole profession at once is the work that remains, and it is the work I am here about.'],
].forEach(function (l, i) {
  const y = 2.15 + i * 1.55;
  card(s, M, y, CW, 1.35, i === 0 ? TINT2 : TINT);
  numTile(s, M + 0.4, y + 0.42, String(i + 1), INK);
  s.addText(l[0], { x: M + 1.15, y: y + 0.25, w: 4.3, h: 0.85, fontSize: 17, bold: true, color: INK,
    fontFace: H, lineSpacing: 22, isTextBox: true, margin: 0 });
  s.addText(l[1], { x: M + 5.7, y: y + 0.22, w: 6.3, h: 1.0, fontSize: 12.5, color: INK2,
    fontFace: B, lineSpacing: 17, isTextBox: true, margin: 0 });
});
s.addText('You would have found all three in about four minutes. I would rather you heard them ' +
  'from me.', { x: M, y: 6.85, w: CW, h: 0.4, fontSize: 13.5, color: MUTED, fontFace: B,
    italic: true, isTextBox: true, margin: 0 });
s.addNotes(
  'This slide earns you more than any other. Do not rush it, and do not apologise through it.\n\n' +
  'SAY: "Three things I want you to hear from me rather than find out.\n\n' +
  'First, the deepest automation is inside Revit. The other tools connect through IFC, which is the ' +
  'open standard and is genuinely the right answer because it means nobody is locked in — but there ' +
  'is no native ArchiCAD plug-in and no native Tekla plug-in, and I am not going to imply otherwise.\n\n' +
  'Second, the engineering calculation engines have never been independently validated. They work ' +
  'and they are tested, but I only offer them as commissioned work with manual checks in parallel. ' +
  'If the Society put engineers on validating them, that sign-off would carry real weight.\n\n' +
  'Third, it is not finished, and I said that earlier.\n\n' +
  'You would have found all three of those in about four minutes. I would rather you heard them ' +
  'from me."'
);

/* ------------------------------------------------ 15 COST */
s = pres.addSlide();
kicker(s, 'WHAT REMAINS');
title(s, 'What it takes to finish, and what it costs');
[
  ['Platform completion', 'USD 36,200', 'Serving many practices, payments, security review, testing'],
  ['Regional hosting', 'USD 12,000', 'Twelve months, sized to grow with use'],
  ['Training programme', 'USD 15,000', 'Three cohorts of about 25 members each'],
].forEach(function (c, i) {
  const x = M + i * 4.0;
  card(s, x, 2.2, 3.7, 2.5);
  s.addText(c[1], { x: x + 0.35, y: 2.5, w: 3.0, h: 0.6, fontSize: 27, bold: true, color: ACC,
    fontFace: H, isTextBox: true, margin: 0 });
  s.addText(c[0], { x: x + 0.35, y: 3.15, w: 3.0, h: 0.4, fontSize: 16, bold: true, color: INK,
    fontFace: H, isTextBox: true, margin: 0 });
  s.addText(c[2], { x: x + 0.35, y: 3.6, w: 3.0, h: 0.95, fontSize: 12.5, color: INK2, fontFace: B,
    lineSpacing: 17, isTextBox: true, margin: 0 });
});
card(s, M, 5.0, CW, 1.55, INK);
s.addText('USD 72,680', { x: M + 0.45, y: 5.25, w: 3.4, h: 0.65, fontSize: 30, bold: true,
  color: PAPER, fontFace: H, isTextBox: true, margin: 0 });
s.addText('including 15% contingency', { x: M + 0.45, y: 5.92, w: 3.4, h: 0.35, fontSize: 12.5,
  color: MUTED, fontFace: B, italic: true, isTextBox: true, margin: 0 });
s.addText('These three are separable. Nobody has to find the whole figure — the training programme ' +
  'can be funded on its own, and it is the part that can start first.',
  { x: M + 4.4, y: 5.3, w: 7.6, h: 1.0, fontSize: 15.5, color: PAPER, fontFace: B, lineSpacing: 22,
    isTextBox: true, margin: 0 });
s.addNotes(
  'Do not linger on the total. The separability line is the important one. Say it twice if you need to.\n\n' +
  'SAY: "So what does finishing it take.\n\n' +
  'Thirty-six thousand two hundred dollars to complete the platform — the work to serve many ' +
  'practices safely, payments, an independent security review, and testing. Twelve thousand for ' +
  'twelve months of hosting sized to grow. Fifteen thousand for three training cohorts of about ' +
  'twenty-five people each.\n\n' +
  'With contingency that is seventy-two thousand six hundred and eighty dollars, about two hundred ' +
  'and sixty-nine million shillings.\n\n' +
  'But here is the part I want you to hear. Those three are separable. Nobody has to find the whole ' +
  'figure. The training programme is fifteen thousand dollars, it stands on its own, it is the part ' +
  'that can start first, and it is the part that would prove the rest."'
);

/* ------------------------------------------------ 16 FUNDING */
s = pres.addSlide();
kicker(s, 'THE ASK');
title(s, 'There are doors you can open that I cannot');
[
  ['Member course fees', 'Members already budget for CPD. It is required by law.'],
  ['Development partners', 'They fund professional bodies for capacity building. They will not fund me.'],
  ['Industry sponsorship', 'Sponsors pay for access to the profession. Only you can grant it.'],
  ['Innovation and ICT funding', 'A local software product with regional reach fits national priorities.'],
  ['University partnership', 'A joint application reaches research funding neither of us reaches alone.'],
  ['Advance subscriptions', 'Revenue rather than debt, but only once delivery is certain.'],
].forEach(function (r, i) {
  const x = M + (i % 3) * 4.0;
  const y = 2.2 + Math.floor(i / 3) * 1.85;
  card(s, x, y, 3.7, 1.6, TINT);
  s.addText(r[0], { x: x + 0.3, y: y + 0.25, w: 3.1, h: 0.55, fontSize: 15.5, bold: true,
    color: ACC2, fontFace: H, lineSpacing: 20, isTextBox: true, margin: 0 });
  s.addText(r[1], { x: x + 0.3, y: y + 0.82, w: 3.1, h: 0.65, fontSize: 12.5, color: INK2,
    fontFace: B, lineSpacing: 17, isTextBox: true, margin: 0 });
});
s.addText('I have brought a page on each of these. I would like your view on which are worth ' +
  'pursuing, and who I should be speaking to.', { x: M, y: 6.15, w: CW, h: 0.6, fontSize: 15,
    bold: true, color: INK, fontFace: B, isTextBox: true, margin: 0 });
s.addNotes(
  'Hand out the funding routes page as you start this slide. Physically giving them something ' +
  'changes the register of the conversation.\n\n' +
  'SAY: "Which brings me to what I am actually asking for.\n\n' +
  'There are routes to funding this that a professional body can open and I cannot. A development ' +
  'partner will fund the Society for capacity building in the construction sector — they will not ' +
  'fund me. A sponsor will pay to be associated with your CPD programme — they will not pay to be ' +
  'associated with mine. And members already budget for CPD, because the law requires them to.\n\n' +
  'There are also routes through innovation funding, through a university partnership, and through ' +
  'advance subscriptions, though I would not take money for something before I could guarantee ' +
  'delivery.\n\n' +
  'I have written a page on each so you are not starting from a blank sheet. What I would like is ' +
  'your view on which are worth pursuing, and who I should be talking to."\n\n' +
  'THEN STOP TALKING. Let the silence do the work. What you want from this meeting is a name and an ' +
  'introduction, not a resolution to consider it.'
);

/* ------------------------------------------------ 17 WHAT HAPPENS NEXT */
s = pres.addSlide();
kicker(s, 'IF YOU SAY YES');
title(s, 'What would happen next');
[
  ['Within two weeks', 'I bring back a cohort outline shaped by whoever you nominate from the Board of Education, not by me alone.'],
  ['Then', 'You confirm dates and help fill the first cohort. I handle delivery, materials and the exercise.'],
  ['The cohort runs', 'Twenty-five members, one week, hands-on. It either works or it does not, and everyone can see which.'],
  ['After that', 'We review it together, then decide whether there is a second and a third, and whether the funding conversation is worth having.'],
].forEach(function (n, i) {
  const y = 2.25 + i * 1.05;
  numTile(s, M, y, String(i + 1), i === 3 ? ACC : INK);
  s.addText(n[0], { x: M + 0.85, y: y - 0.02, w: 2.9, h: 0.45, fontSize: 17, bold: true,
    color: INK, fontFace: H, isTextBox: true, margin: 0 });
  s.addText(n[1], { x: M + 4.0, y: y - 0.04, w: 8.0, h: 0.85, fontSize: 13.5, color: INK2,
    fontFace: B, lineSpacing: 19, isTextBox: true, margin: 0 });
});
card(s, M, 6.3, CW, 0.8, TINT2);
s.addText('Nothing here commits the Society beyond the first cohort.',
  { x: M + 0.45, y: 6.3, w: CW - 0.9, h: 0.8, fontSize: 16, bold: true, color: ACC2, fontFace: H,
    valign: 'middle', isTextBox: true, margin: 0 });
s.addNotes(
  'Committees are wary of open-ended commitments. This slide exists to show them the smallest ' +
  'possible first step and a clear exit after it.\n\n' +
  'SAY: "If you did say yes, this is what would happen, so you know what you are agreeing to.\n\n' +
  'Within a couple of weeks I would come back with a cohort outline shaped by whoever you nominate ' +
  'from the Board of Education, rather than written by me alone. You would confirm dates and help ' +
  'fill the first cohort; I would handle delivery, the materials and the exercise. The cohort runs ' +
  'for a week. And then we look at it together and decide whether there is a second and a third.\n\n' +
  'Nothing there commits the Society beyond that first cohort. If it is no good, you have lost a ' +
  'week of a venue and I have lost rather more, which seems like the right way round."\n\n' +
  'That last line usually gets a small laugh, and it makes the point that you are carrying the risk.'
);

/* ------------------------------------------------ 18 HORIZON */
s = pres.addSlide();
kicker(s, 'AND IF IT WORKS');
title(s, 'Where this could go');

s.addText('The Society would own the standard. Software is one way to comply with it, not the ' +
  'only way.', { x: M, y: 2.05, w: 11.6, h: 0.5, fontSize: 17, bold: true, color: ACC2,
    fontFace: H, italic: true, isTextBox: true, margin: 0 });

[
  ['A standard you publish', 'Naming, classification, level of detail by stage, handover. Written by the Society, complied with using any software. The draft already exists.', ACC],
  ['A register you operate', 'Every member project carries an identifier. A client can check that a drawing came from a registered practice and met the standard.', SLATE],
  ['Buildings that hand over with their data', 'Public buildings handed over with an asset register instead of a box of drawings, so they can actually be maintained.', SLATE],
].forEach(function (r, i) {
  const y = 2.72 + i * 1.2;
  card(s, M, y, CW, 1.05, i === 0 ? TINT : TINT2);
  sq(s, M + 0.4, y + 0.42, r[2], 0.2);
  s.addText(r[0], { x: M + 0.85, y: y + 0.22, w: 4.3, h: 0.65, fontSize: 17, bold: true,
    color: INK, fontFace: H, lineSpacing: 21, isTextBox: true, margin: 0 });
  s.addText(r[1], { x: M + 5.4, y: y + 0.25, w: 6.6, h: 0.7, fontSize: 12.5, color: INK2,
    fontFace: B, lineSpacing: 17, isTextBox: true, margin: 0 });
});

card(s, M, 6.35, CW, 0.75, INK);
s.addText('None of this is what I am asking you to decide today.',
  { x: M + 0.45, y: 6.35, w: CW - 0.9, h: 0.75, fontSize: 16, bold: true, color: SALMON,
    fontFace: H, valign: 'middle', isTextBox: true, margin: 0 });

s.addNotes(
  'Forty-five seconds. This exists so the President and the ICT chair have something to be ambitious ' +
  'about, and so nobody later says you were not straight about where this was heading. Then get ' +
  'straight back to the small ask on the next slide.\n\n' +
  'SAY: "One last thing, and then I will stop.\n\n' +
  'If the training works, I think there is something larger here, and I would rather you heard it ' +
  'from me now than found out later.\n\n' +
  'Uganda has no national standard for how architectural information is delivered. The Society could ' +
  'write one. Naming, classification, what level of detail is expected at each stage, what a handover ' +
  'has to contain. Written by you, complied with using whatever software a practice already owns.\n\n' +
  'Beyond that, a register: every member project carrying an identifier, so a client can check that a ' +
  'drawing came from a registered practice and met the standard. And further out, public buildings ' +
  'that hand over with an asset register instead of a box of drawings, so somebody can actually ' +
  'maintain them.\n\n' +
  'The important word in all of that is yours. The Society would own the standard. My software would ' +
  'be one way to comply with it, and not the only way, because a standard tied to one company is not ' +
  'a national standard.\n\n' +
  'None of that is what I am asking you to decide today."\n\n' +
  'IF THEY GET EXCITED, do not chase it. "I would love to talk about that properly once we have run a ' +
  'cohort and you have seen whether the thing works." Trading the small yes for a large maybe is the ' +
  'commonest way to lose a meeting like this.\n\n' +
  'IF ASKED whether you are proposing they adopt your naming scheme: "No. I am offering it as a first ' +
  'draft for you to argue with. It should carry your name, not mine."'
);

/* ------------------------------------------------ 17 THE ASK */
s = pres.addSlide();
darkBg(s);
kicker(s, 'IN SUMMARY', ACC);
title(s, 'Three things I am asking for', PAPER);
[
  ['Endorse the training programme', 'As a Society initiative for members, and let us look at whether it can be accredited for CPD.'],
  ['Name a technical counterpart', 'Someone in the ICT Cluster, so the judgement about this is yours rather than mine.'],
  ['Help me find the funding route', 'Not your money. Your judgement on which door to knock on, and ideally an introduction.'],
].forEach(function (a, i) {
  const y = 2.3 + i * 1.32;
  numTile(s, M, y, String(i + 1), ACC);
  s.addText(a[0], { x: M + 0.85, y: y - 0.06, w: 4.6, h: 0.5, fontSize: 20, bold: true, color: PAPER,
    fontFace: H, isTextBox: true, margin: 0 });
  s.addText(a[1], { x: M + 5.6, y: y - 0.04, w: 6.4, h: 0.9, fontSize: 14, color: LIGHTTXT,
    fontFace: B, lineSpacing: 20, isTextBox: true, margin: 0 });
});
card(s, M, 6.25, CW, 0.8, '2E333A');
s.addText('None of these costs the Society money.', { x: M + 0.45, y: 6.25, w: CW - 0.9, h: 0.8,
  fontSize: 17, bold: true, color: SALMON, fontFace: H, valign: 'middle', isTextBox: true,
  margin: 0 });
s.addNotes(
  'Have this word-perfect. It is the close, and it is the only part of the meeting they will repeat ' +
  'to anyone who was not there.\n\n' +
  'SAY: "So, three things.\n\n' +
  'One. Endorse the training programme as a Society initiative for your members, and let us find out ' +
  'together whether it can be accredited for CPD.\n\n' +
  'Two. Name someone in the ICT Cluster as a technical counterpart, so the judgement about whether ' +
  'this is any good is yours and not mine.\n\n' +
  'Three. Help me find the route to fund the rest. Not your money — your judgement about which door ' +
  'to knock on, and if you are willing, an introduction.\n\n' +
  'None of those costs the Society anything. Thank you. I would rather spend the remaining time on ' +
  'your questions than on my slides."\n\n' +
  'BEFORE THEY DISPERSE, get four things written down: who takes this forward, when they next meet, ' +
  'what they need from you before then, and whether you may approach a funder using the Society name.'
);

/* ------------------------------------------------ 18 CLOSE */
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
  'Leave this up during the discussion. It keeps your contact details on screen for forty minutes.\n\n' +
  'During questions: answer the question asked, then stop. The commonest mistake in a room like this ' +
  'is answering a short question at length and talking yourself into a weaker position.\n\n' +
  '"I do not know, and I will come back to you by Friday" is a complete and respectable answer. ' +
  'Never invent a number, a date, or a customer.'
);

pres.writeFile({ fileName: process.argv[2] }).then(function () {
  console.log('written', process.argv[2]);
});
