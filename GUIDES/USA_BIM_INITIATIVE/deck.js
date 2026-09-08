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

const ONE = 'ONE  ·  WHAT HAS CHANGED';
const TWO = 'TWO  ·  WHAT NOW EXISTS';
const THREE = 'THREE  ·  WHY IT IS NOW WITHIN REACH';
const FOUR = 'FOUR  ·  WHAT IT COULD MEAN FOR THE SOCIETY';

const pres = new pptxgen();
pres.layout = 'LAYOUT_WIDE';
pres.author = 'Davis Mayanja';
pres.company = 'PlanScape';
pres.title = 'BIM for Ugandan practice';

function sq(s, x, y, c, size) {
  s.addShape(pres.ShapeType.rect, { x: x, y: y, w: size || 0.16, h: size || 0.16, fill: { color: c } });
}
function motif(s, x, y, c) {
  sq(s, x, y, c, 0.15); sq(s, x + 0.22, y, c, 0.15); sq(s, x + 0.44, y, c, 0.15);
}
function darkBg(s) { s.background = { color: INK }; }
function kicker(s, t, c) {
  s.addText(t, { x: M, y: 0.5, w: CW, h: 0.3, fontSize: 12, bold: true, charSpacing: 2.5,
    color: c || ACC, fontFace: B, isTextBox: true, margin: 0 });
}
function title(s, t, c, size) {
  s.addText(t, { x: M, y: 0.88, w: CW, h: 1.0, fontSize: size || 38, bold: true, color: c || INK,
    fontFace: H, isTextBox: true, margin: 0, valign: 'top' });
}
function card(s, x, y, w, h, fill) {
  s.addShape(pres.ShapeType.rect, { x: x, y: y, w: w, h: h, fill: { color: fill || TINT },
    shadow: { type: 'outer', angle: 90, offset: 2, blur: 8, color: 'BFB9B0', opacity: 0.45 } });
}
function numTile(s, x, y, n, c, txt) {
  s.addShape(pres.ShapeType.rect, { x: x, y: y, w: 0.46, h: 0.46, fill: { color: c || ACC } });
  s.addText(n, { x: x, y: y, w: 0.46, h: 0.46, fontSize: 18, bold: true, color: txt || 'FFFFFF',
    fontFace: H, align: 'center', valign: 'middle', isTextBox: true, margin: 0 });
}

/* ============================================== 1 TITLE */
let s = pres.addSlide();
darkBg(s);
motif(s, M, 1.6, ACC);
s.addText('PRESENTED TO THE COUNCIL', { x: M, y: 2.05, w: CW, h: 0.32, fontSize: 12.5, bold: true,
  charSpacing: 3, color: ACC, fontFace: B, isTextBox: true, margin: 0 });
s.addText('Building Information Modelling\nfor Ugandan practice', { x: M, y: 2.5, w: 11.2, h: 1.9,
  fontSize: 42, bold: true, color: PAPER, fontFace: H, lineSpacing: 50, isTextBox: true, margin: 0 });
s.addText('PlanScape and StingTools, a BIM platform built in Kampala', { x: M, y: 4.5, w: 10.6,
  h: 0.4, fontSize: 17, color: LIGHTTXT, fontFace: B, italic: true, isTextBox: true, margin: 0 });
s.addText('Davis Mayanja', { x: M, y: 5.55, w: 6, h: 0.3, fontSize: 14.5, bold: true, color: PAPER,
  fontFace: B, isTextBox: true, margin: 0 });
s.addText('Uganda Society of Architects  ·  September 2026', { x: M, y: 5.9, w: 7, h: 0.3,
  fontSize: 12.5, color: MUTED, fontFace: B, isTextBox: true, margin: 0 });
s.addNotes(
  'Register for the whole session: warm, unhurried, confident. You are a member showing the Council ' +
  'something, not a supplier closing a sale. Nothing here needs a yes, which is exactly why the room ' +
  'will listen.\n\n' +
  'SAY: "Mr President, Chairman, thank you for the time.\n\n' +
  'I have spent about two years building something, and I thought it was time I showed the Council ' +
  'properly rather than kept describing it in passing.\n\n' +
  'What I want to put to you is fairly simple. Adopting BIM in this country has always been ' +
  'expensive and difficult. I think that has changed, and I would like to show you why, and then ' +
  'hear whether you see it the same way."\n\n' +
  'NEVER mention two years without earning, or money being tight. It reads as need, and need makes ' +
  'people cautious rather than generous. Two years of investment is a credential.'
);

/* ============================================== 2 WHERE THIS IS GOING */
s = pres.addSlide();
kicker(s, 'SO YOU ARE NOT WAITING FOR IT');
title(s, 'Where this is going');
card(s, M, 2.15, 5.95, 3.7, TINT);
s.addText('What I am proposing', { x: M + 0.4, y: 2.5, w: 5.15, h: 0.4, fontSize: 21, bold: true,
  color: ACC2, fontFace: H, isTextBox: true, margin: 0 });
[
  'That the Society might endorse a BIM training programme for its members',
  'That someone in the ICT Cluster looks at this properly, so the judgement is yours',
  'That we talk about how the remaining work could be funded',
].forEach(function (t, i) {
  sq(s, M + 0.4, 3.2 + i * 0.78, ACC, 0.13);
  s.addText(t, { x: M + 0.72, y: 3.06 + i * 0.78, w: 4.75, h: 0.7, fontSize: 14, color: INK2,
    fontFace: B, lineSpacing: 19, isTextBox: true, margin: 0 });
});
card(s, M + 6.4, 2.15, 5.95, 3.7, INK);
s.addText('What I am not proposing', { x: M + 6.8, y: 2.5, w: 5.15, h: 0.4, fontSize: 21,
  bold: true, color: SALMON, fontFace: H, isTextBox: true, margin: 0 });
[
  'Any money from the Society',
  'A decision on anything today',
  'Exclusivity, or anything binding on your members',
].forEach(function (t, i) {
  sq(s, M + 6.8, 3.2 + i * 0.78, ACC, 0.13);
  s.addText(t, { x: M + 7.12, y: 3.06 + i * 0.78, w: 4.75, h: 0.7, fontSize: 14, color: LIGHTTXT,
    fontFace: B, lineSpacing: 19, isTextBox: true, margin: 0 });
});
s.addText('These are suggestions. Whether any of them is worth doing is entirely the Council’s call.',
  { x: M, y: 6.15, w: CW, h: 0.5, fontSize: 16, bold: true, color: ACC2, fontFace: H, italic: true,
    isTextBox: true, margin: 0 });
s.addNotes(
  'Put this up front so nobody spends the next twenty minutes wondering what the catch is. A room ' +
  'that is waiting for an ask does not listen properly.\n\n' +
  'SAY: "Before I start, let me say where this is going, so you are not sitting there waiting for ' +
  'the ask.\n\n' +
  'There are three things I would put to you. That the Society might endorse a BIM training ' +
  'programme for its members. That somebody in the ICT Cluster looks at this properly, so the ' +
  'judgement about whether it is any good is yours rather than mine. And that at some point we talk ' +
  'about how the remaining work could be funded.\n\n' +
  'I am not asking the Society for money. I am not asking you to decide anything today. And I am ' +
  'not asking for exclusivity or anything that binds your members.\n\n' +
  'These are suggestions. Whether any of them is worth doing is entirely your call, and I would ' +
  'genuinely rather hear your thinking than get a decision."\n\n' +
  'PAUSE after that. The room relaxes, and everything after it is heard differently.'
);

/* ============================================== 3 THE STANDARD MOVED */
s = pres.addSlide();
kicker(s, ONE);
title(s, 'The standard for project information has moved');
s.addText('ISO 19650', { x: M, y: 2.15, w: 5.4, h: 0.85, fontSize: 52, bold: true, color: ACC,
  fontFace: H, isTextBox: true, margin: 0 });
s.addText('is now the accepted international standard for managing information on a building ' +
  'project — how it is named, versioned, approved and handed over.',
  { x: M, y: 3.05, w: 5.6, h: 1.6, fontSize: 15, color: INK2, fontFace: B, lineSpacing: 22,
    isTextBox: true, margin: 0 });
s.addText('And governments have been requiring it', { x: M + 6.4, y: 2.1, w: 5.95, h: 0.4,
  fontSize: 16, bold: true, color: INK, fontFace: H, isTextBox: true, margin: 0 });
[
  ['2013', 'Dubai', 'Municipality Circular 196, widened 2015'],
  ['2015', 'Singapore', 'All new projects over 5,000 sq m'],
  ['2016', 'United Kingdom', 'All centrally funded public projects'],
  ['2020', 'Germany', 'New federal transport infrastructure'],
].forEach(function (c, i) {
  const y = 2.55 + i * 0.72;
  card(s, M + 6.4, y, 5.95, 0.62, TINT);
  s.addText(c[0], { x: M + 6.7, y: y + 0.13, w: 0.8, h: 0.36, fontSize: 15, bold: true, color: ACC,
    fontFace: H, isTextBox: true, margin: 0 });
  s.addText(c[1], { x: M + 7.55, y: y + 0.13, w: 2.0, h: 0.36, fontSize: 14, bold: true,
    color: INK, fontFace: B, isTextBox: true, margin: 0 });
  s.addText(c[2], { x: M + 9.5, y: y + 0.15, w: 2.7, h: 0.34, fontSize: 11, color: INK2,
    fontFace: B, isTextBox: true, margin: 0 });
});
card(s, M, 5.55, CW, 1.05, INK);
s.addText('Thirteen years, four continents, and it has only ever moved in one direction.',
  { x: M + 0.45, y: 5.55, w: CW - 0.9, h: 1.05, fontSize: 17, color: PAPER, fontFace: H,
    italic: true, valign: 'middle', isTextBox: true, margin: 0 });
s.addNotes(
  'SAY: "The way a set of building information is expected to be put together has changed, and it ' +
  'changed outside Uganda first.\n\n' +
  'ISO 19650 is now the accepted international standard for managing project information. It is not ' +
  'a modelling standard. It is an information standard, which is why it matters to architects and ' +
  'not only to software people.\n\n' +
  'And governments have been requiring it, one after another. Dubai in 2013. Singapore in 2015, for ' +
  'every new project over five thousand square metres. The United Kingdom in 2016, on all centrally ' +
  'funded public work. Germany in 2020, for federal transport infrastructure.\n\n' +
  'I want to be careful here: I am not telling you Uganda has mandated anything. It has not. I am ' +
  'telling you the direction of travel, and over thirteen years it has only gone one way."\n\n' +
  'Do not claim a Ugandan mandate. Someone will check, and you lose the room.'
);

/* ============================================== 4 REGION BEHIND */
s = pres.addSlide();
kicker(s, ONE);
title(s, 'And the region has not kept pace');
card(s, M, 2.12, 6.0, 2.25, TINT);
s.addText('Kenya', { x: M + 0.4, y: 2.36, w: 5.2, h: 0.4, fontSize: 20, bold: true, color: ACC2,
  fontFace: H, isTextBox: true, margin: 0 });
s.addText('Peer economy, larger construction sector. Published research finds adoption still ' +
  'lagging, and names the consequence as poor coordination of information between the parties on ' +
  'a project.', { x: M + 0.4, y: 2.85, w: 5.2, h: 1.35, fontSize: 13.5, color: INK2, fontFace: B,
    lineSpacing: 20, isTextBox: true, margin: 0 });
card(s, M + 6.4, 2.12, 6.0, 2.25, TINT);
s.addText('Uganda', { x: M + 6.8, y: 2.36, w: 5.2, h: 0.4, fontSize: 20, bold: true, color: ACC2,
  fontFace: H, isTextBox: true, margin: 0 });
s.addText('No national standard for how architectural information is delivered. No locally built, ' +
  'locally supported tooling. Everything on the market priced in dollars and assuming a ' +
  'connection.', { x: M + 6.8, y: 2.85, w: 5.2, h: 1.35, fontSize: 13.5, color: INK2, fontFace: B,
    lineSpacing: 20, isTextBox: true, margin: 0 });
s.addText('WHAT THAT COSTS A PRACTICE IN YOUR MEMBERSHIP', { x: M, y: 4.55, w: CW, h: 0.32,
  fontSize: 12, bold: true, charSpacing: 2, color: MUTED, fontFace: B, isTextBox: true, margin: 0 });
[
  ['Not being asked twice', 'Clients increasingly ask how information will be delivered. A practice with no answer stops appearing on shortlists, and never finds out why.'],
  ['Competing on unequal terms', 'The international and regional firms bidding here already work this way. That is the comparison our members are measured against.'],
  ['Paying for it on site', 'Coordination by hand is slower and less complete, and what it misses turns up during construction where it costs the most.'],
].forEach(function (r, i) {
  const x = M + i * 4.0;
  card(s, x, 4.95, 3.7, 1.9, TINT2);
  numTile(s, x + 0.32, 5.18, String(i + 1), ACC);
  s.addText(r[0], { x: x + 0.9, y: 5.22, w: 2.7, h: 0.4, fontSize: 14.5, bold: true, color: INK,
    fontFace: H, isTextBox: true, margin: 0 });
  s.addText(r[1], { x: x + 0.32, y: 5.76, w: 3.1, h: 0.98, fontSize: 11.5, color: INK2,
    fontFace: B, lineSpacing: 15, isTextBox: true, margin: 0 });
});
s.addNotes(
  'SAY: "The region has not kept pace with that.\n\n' +
  'Kenya is the obvious comparison, with a bigger construction sector and the same regional market. ' +
  'Published research on Kenyan adoption finds it still lagging, and names the consequence as poor ' +
  'coordination of information between the parties on a project. That is a polite way of describing ' +
  'what we all recognise. Drawings that disagree. Schedules that do not match the model. Handover ' +
  'assembled at the last minute.\n\n' +
  'Uganda is in the same position with two extra problems. No national standard for how ' +
  'architectural information is delivered, and nothing on the market built here.\n\n' +
  'And what does that cost a practice in your membership? Three things. Not being asked twice, ' +
  'because a practice with no answer stops appearing on shortlists and never learns why. Competing ' +
  'on unequal terms against firms that already work this way. And paying for it on site, where ' +
  'coordination errors cost the most and the architect usually gets the blame."'
);

/* ============================================== 5 THE BARRIER */
s = pres.addSlide();
darkBg(s);
kicker(s, ONE, ACC);
title(s, 'But the barrier was never the idea', PAPER);
s.addText('Nobody in this room needs persuading that BIM is a better way to work. We have known ' +
  'that for fifteen years.', { x: M, y: 2.1, w: 11.6, h: 0.85, fontSize: 19, color: LIGHTTXT,
    fontFace: H, italic: true, lineSpacing: 27, isTextBox: true, margin: 0 });
[
  ['It was expensive', 'Per-seat licensing at Ugandan fee levels, in dollars, before anyone had drawn a line.'],
  ['It was difficult', 'Standards to invent yourself, tools that assumed a connection, and a learning curve nobody had time for.'],
  ['It was for big jobs', 'Which meant a four-person practice quietly decided it was not for them.'],
].forEach(function (r, i) {
  const x = M + i * 4.0;
  card(s, x, 3.35, 3.7, 2.35, '2E333A');
  sq(s, x + 0.32, 3.62, ACC, 0.18);
  s.addText(r[0], { x: x + 0.32, y: 3.95, w: 3.1, h: 0.42, fontSize: 18, bold: true, color: PAPER,
    fontFace: H, isTextBox: true, margin: 0 });
  s.addText(r[1], { x: x + 0.32, y: 4.45, w: 3.15, h: 1.1, fontSize: 12.5, color: LIGHTTXT,
    fontFace: B, lineSpacing: 17, isTextBox: true, margin: 0 });
});
s.addText('Everything after this slide is about those three things, and whether they are still true.',
  { x: M, y: 6.1, w: CW, h: 0.5, fontSize: 16.5, bold: true, color: SALMON, fontFace: H,
    isTextBox: true, margin: 0 });
s.addNotes(
  'THE HINGE OF THE WHOLE PRESENTATION. Slow down here. It reframes everything that follows, and it ' +
  'flatters the room rather than lecturing it.\n\n' +
  'SAY: "Before I show you anything, I want to be clear about what I think the actual problem has ' +
  'been.\n\n' +
  'It was never the idea. Nobody in this room needs persuading that BIM is a better way to work. We ' +
  'have known that for fifteen years.\n\n' +
  'The problem was that it was expensive: per-seat licensing at our fee levels, in dollars, before ' +
  'anyone had drawn a line. It was difficult: standards you had to invent for yourself, tools that ' +
  'assumed a connection, a learning curve nobody had time for. And it felt like something for big ' +
  'jobs, which meant a four-person practice quietly decided it was not for them.\n\n' +
  'Everything after this slide is about those three things, and whether they are still true. I think ' +
  'they are not, and that is really the only thing I came to say."'
);

/* ============================================== 6 TWO PARTS */
s = pres.addSlide();
kicker(s, TWO);
title(s, 'Two parts, and the standards to run them on');
card(s, M, 2.2, 5.95, 3.6, INK);
sq(s, M + 0.45, 2.6, ACC, 0.22);
s.addText('The platform', { x: M + 0.45, y: 3.0, w: 5.0, h: 0.5, fontSize: 26, bold: true,
  color: PAPER, fontFace: H, isTextBox: true, margin: 0 });
s.addText('The doing', { x: M + 0.45, y: 3.5, w: 5.0, h: 0.3, fontSize: 13, color: SALMON,
  fontFace: B, italic: true, isTextBox: true, margin: 0 });
s.addText('StingTools inside the modelling software, and PlanScape as the shared record around it. ' +
  'Checking, classification, drawings, schedules, quantities, handover data, issues and ' +
  'correspondence, in one flow rather than five products.',
  { x: M + 0.45, y: 3.95, w: 5.05, h: 1.65, fontSize: 13.5, color: LIGHTTXT, fontFace: B,
    lineSpacing: 20, isTextBox: true, margin: 0 });
card(s, M + 6.4, 2.2, 5.95, 3.6, TINT);
sq(s, M + 6.85, 2.6, ACC, 0.22);
s.addText('And the standards', { x: M + 6.85, y: 3.0, w: 5.0, h: 0.5, fontSize: 26, bold: true,
  color: INK, fontFace: H, isTextBox: true, margin: 0 });
s.addText('The agreeing', { x: M + 6.85, y: 3.5, w: 5.0, h: 0.3, fontSize: 13, color: ACC2,
  fontFace: B, italic: true, isTextBox: true, margin: 0 });
s.addText('Naming, classification, drawing conventions, what level of detail belongs at each stage, ' +
  'and what a handover has to contain. Written down, and checkable by the software rather than only ' +
  'by argument.', { x: M + 6.85, y: 3.95, w: 5.05, h: 1.65, fontSize: 13.5, color: INK2,
    fontFace: B, lineSpacing: 20, isTextBox: true, margin: 0 });
s.addText('Most tools give you the first half. The second half is usually left to each practice to ' +
  'invent for itself, which is a fair part of why our drawings do not talk to each other.',
  { x: M, y: 6.05, w: CW, h: 0.55, fontSize: 15, bold: true, color: ACC2, fontFace: H,
    italic: true, isTextBox: true, margin: 0 });
s.addNotes(
  'SAY: "What I have built is two halves of the same thing.\n\n' +
  'There is a platform. StingTools sits inside the modelling software and does the checking, the ' +
  'classification, and produces the drawings, schedules, quantities and handover data. PlanScape is ' +
  'the shared record around it, where the documents and issues live for the whole team. One flow ' +
  'rather than five separate products.\n\n' +
  'And there are standards: naming, classification, drawing conventions, what level of detail ' +
  'belongs at what stage, what a handover has to contain. Written down, and checkable by the ' +
  'software rather than only by argument.\n\n' +
  'Most tools give you the first half. The second half usually gets left to each practice to invent ' +
  'for itself, which is a fair part of why our drawings do not talk to each other."'
);

/* ============================================== 7 EIGHT THINGS */
s = pres.addSlide();
kicker(s, TWO);
title(s, 'Eight things you may not have seen before');
[
  ['Talk to the model in plain English', 'Ask which rooms are missing a fire rating, and the answer comes back from the model itself.'],
  ['Meet inside the model, not over it', 'A live meeting with camera and voice, where everyone is in the same model and follows the presenter.'],
  ['One identity across four tools', 'Revit, ArchiCAD, Tekla and free open-source Blender on one project, with no converting by hand.'],
  ['Raise an issue by pointing at it', 'Long-press an element on a phone and the issue is pinned to that element, at that spot.'],
  ['A compliance score, as a number', 'How much of a model actually meets the standard, measured rather than argued about.'],
  ['Drawings and schedules produced for you', 'Sheets, title blocks, schedules and quantities generated from the model instead of drawn again.'],
  ['Handover data assembled as you build', 'Rather than compiled in a panic in the fortnight before practical completion.'],
  ['Ugandan conditions built in', 'Wind, seismic zone, soil bearing and design rainfall by region, as defaults.'],
].forEach(function (r, i) {
  const x = M + (i % 2) * 6.4;
  const y = 2.08 + Math.floor(i / 2) * 1.24;
  card(s, x, y, 5.95, 1.05, i % 2 ? TINT : TINT2);
  sq(s, x + 0.32, y + 0.24, ACC, 0.16);
  s.addText(r[0], { x: x + 0.7, y: y + 0.14, w: 5.0, h: 0.36, fontSize: 15, bold: true, color: INK,
    fontFace: H, isTextBox: true, margin: 0 });
  s.addText(r[1], { x: x + 0.7, y: y + 0.5, w: 5.05, h: 0.5, fontSize: 11.5, color: INK2,
    fontFace: B, lineSpacing: 15, isTextBox: true, margin: 0 });
});
s.addNotes(
  'THE CAPTURE SLIDE. Everyone here knows BIM, so do not explain it. Ninety seconds, fast. Each of ' +
  'these gets its own slide later.\n\n' +
  'SAY: "You all know what BIM is, so I will not explain it. Let me instead show you eight things I ' +
  'think are new, and then take them one at a time.\n\n' +
  'You can talk to the model in plain English. You can hold a live meeting inside the model rather ' +
  'than over a screen share. One element keeps the same identity across four modelling tools, ' +
  'including a free one. You can raise an issue by long-pressing the element on your phone. A model ' +
  'gets a compliance score. Drawings, schedules and quantities come out of the model. Handover data ' +
  'is assembled as you build. And Ugandan conditions are in it as defaults.\n\n' +
  'Any one of those is useful on its own. Together they change what a working day looks like."'
);

/* ============================================== 8 LIFECYCLE */
s = pres.addSlide();
kicker(s, TWO);
title(s, 'A medium-sized project, start to finish');
s.addText('One chain, where each stage already knows what the last one did.', { x: M, y: 2.02,
  w: 11.7, h: 0.45, fontSize: 15.5, italic: true, color: ACC2, fontFace: H, isTextBox: true,
  margin: 0 });
[
  ['Set up', 'Project created, the standard applied, the team invited. Consultants, contractor and client join free.'],
  ['Design', 'Authors model in Revit, ArchiCAD or the free route. Naming and classification applied as they go.'],
  ['Check and coordinate', 'The audit runs and scores the model. Clashes arrive as pins. The meeting happens inside the model.'],
  ['Document and tender', 'Sheets, title blocks, schedules and quantities out of the model. Transmittals and a drawing register.'],
  ['Build', 'Site records, photographs and issues from a phone, offline, syncing when there is signal.'],
  ['Hand over and operate', 'The asset register was assembled along the way, so facilities management inherits data, not a box of drawings.'],
].forEach(function (r, i) {
  const x = M + (i % 3) * 4.0;
  const y = 2.62 + Math.floor(i / 3) * 2.05;
  card(s, x, y, 3.7, 1.8, i < 3 ? TINT : TINT2);
  numTile(s, x + 0.3, y + 0.28, String(i + 1), i === 5 ? ACC : INK);
  s.addText(r[0], { x: x + 0.9, y: y + 0.3, w: 2.6, h: 0.42, fontSize: 16.5, bold: true,
    color: INK, fontFace: H, isTextBox: true, margin: 0 });
  s.addText(r[1], { x: x + 0.3, y: y + 0.88, w: 3.15, h: 0.85, fontSize: 12, color: INK2,
    fontFace: B, lineSpacing: 16, isTextBox: true, margin: 0 });
});
s.addText('The handover data is not a job at the end. It accumulates the whole way through.',
  { x: M, y: 6.85, w: CW, h: 0.4, fontSize: 13.5, color: MUTED, fontFace: B, italic: true,
    isTextBox: true, margin: 0 });
s.addNotes(
  'This is the "complete platform" proof. Walk it slowly — it answers "what does this actually ' +
  'replace?"\n\n' +
  'SAY: "Let me take you through a medium-sized job, from first sketch to the building being run.\n\n' +
  'You set the project up. The standard gets applied. You invite the team, and the consultants, the ' +
  'contractor and the client all join free.\n\n' +
  'Your authors model in whatever they use, and naming and classification are applied as they go ' +
  'rather than at the end. The audit runs and scores the model. Clashes come in as pins. The ' +
  'coordination meeting happens inside the model.\n\n' +
  'Then documentation: sheets, title blocks, schedules and quantities straight out of the model, ' +
  'with transmittals and a drawing register kept for you. On site, records and photographs and ' +
  'issues from a phone, working offline.\n\n' +
  'And at the end, handover. The asset register has been assembling itself the whole way through, so ' +
  'the facilities team inherits data rather than a box of drawings.\n\n' +
  'That last part is the one I would underline. Handover is usually a miserable exercise done in the ' +
  'final fortnight by whoever is least busy. Here it is a by-product of having done the rest ' +
  'properly."'
);

/* ============================================== 9 AUTOMATION */
s = pres.addSlide();
kicker(s, TWO);
title(s, 'The same work, without the evening');
s.addText('The point is not that it does something nobody could do before. It is that it does the ' +
  'same work without anybody staying late.', { x: M, y: 2.05, w: 11.6, h: 0.5, fontSize: 16,
    italic: true, color: ACC2, fontFace: H, isTextBox: true, margin: 0 });
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
  'THE SLIDE THAT LANDS EMOTIONALLY — this is the "difficult" half of the barrier. Do not read ' +
  'the table. Point at it, then tell one story from your own work.\n\n' +
  'SAY: "Naming and classifying a model used to be element by element. Producing a drawing set meant ' +
  'setting up every sheet. A schedule of quantities was counted, typed, and out of date by the time ' +
  'it was issued. Finding what was wrong meant reading drawings and hoping. Handover information got ' +
  'assembled at the end, from memory.\n\n' +
  'Every one of those is now a pass that runs while you make tea."\n\n' +
  'THEN TELL ONE REAL STORY. A specific job, a specific evening, how long it took then and how long ' +
  'it takes now. One anecdote from your own projects will do more than the whole table. Architects ' +
  'believe other architects about late nights, and this is where the room stops evaluating and ' +
  'starts recognising.'
);

/* ============================================== 10 MULTI-HOST */
s = pres.addSlide();
kicker(s, TWO);
title(s, 'It does not matter what your members draw in');
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
  'A full add-in inside Revit, which is the deepest integration. ArchiCAD works by saving to IFC, ' +
  'and changes come back the other way. Tekla arrives the same way. And there is a free ' +
  'open-source route through Blender and Bonsai that costs nothing at all.\n\n' +
  'The line that matters is the last one. Every element keeps the same identity across all four. I ' +
  'tested that with a single element resolving across all of them at once.\n\n' +
  'It is also why I would say this belongs to the profession rather than to one company. A tool tied ' +
  'to a single supplier should probably not be anybody’s national standard."\n\n' +
  'BE EXACT IF ASKED: Revit is a native add-in, the others go through IFC, and there is no native ' +
  'ArchiCAD or Tekla plug-in. The precision is what makes the claim believable.'
);

/* ============================================== 11 MEETINGS */
s = pres.addSlide();
kicker(s, TWO);
title(s, 'Meet inside the model, not over it');
s.addText('A coordination meeting where nobody is screen-sharing. Everyone is in the model, and the ' +
  'presenter can bring the whole room to what they are looking at.', { x: M, y: 2.05, w: 11.7,
    h: 0.75, fontSize: 16, italic: true, color: ACC2, fontFace: H, lineSpacing: 22,
    isTextBox: true, margin: 0 });
[
  ['Camera and voice', 'A live meeting in the browser or on a phone. No third app to join, no separate link.'],
  ['Follow the presenter', 'The host isolates, colours or sections something and everyone else’s view moves with them.'],
  ['It keeps its own minutes', 'Minutes, action items and attendance held against the meeting rather than in somebody’s notebook.'],
  ['Recorded, and playable after', 'For whoever could not attend, or for the record when a decision is questioned later.'],
].forEach(function (r, i) {
  const x = M + (i % 2) * 6.4;
  const y = 3.0 + Math.floor(i / 2) * 1.55;
  card(s, x, y, 5.95, 1.3, TINT);
  sq(s, x + 0.38, y + 0.32, ACC, 0.18);
  s.addText(r[0], { x: x + 0.8, y: y + 0.22, w: 4.9, h: 0.38, fontSize: 16, bold: true, color: INK,
    fontFace: H, isTextBox: true, margin: 0 });
  s.addText(r[1], { x: x + 0.8, y: y + 0.63, w: 4.95, h: 0.55, fontSize: 12.5, color: INK2,
    fontFace: B, lineSpacing: 16, isTextBox: true, margin: 0 });
});
s.addText('The last test on this one is a real two-person meeting with cameras. Everything under it ' +
  'is built and running.', { x: M, y: 6.25, w: CW, h: 0.4, fontSize: 13, color: MUTED, fontFace: B,
    italic: true, isTextBox: true, margin: 0 });
s.addNotes(
  'The slide that separates this from every other BIM tool the room has seen, and from Zoom. Give ' +
  'it time.\n\n' +
  'SAY: "This is the part I am proudest of, and I have not seen it anywhere else.\n\n' +
  'We all know what a coordination meeting looks like. Somebody shares their screen, everybody else ' +
  'squints at it, and half the room is looking at a drawing they cannot navigate.\n\n' +
  'Here nobody screen-shares. There is a live meeting with camera and voice, in the browser or on a ' +
  'phone, and everyone is inside the same model. When the presenter isolates something, or colours ' +
  'the model by clash status, or cuts a section, everyone following sees their own view move to the ' +
  'same place. They are not watching a video of the model. They are in it.\n\n' +
  'And the meeting keeps itself: minutes, action items, who attended. It can be recorded, so ' +
  'somebody who could not attend can watch it, or you can go back to it when a decision gets ' +
  'questioned six months later.\n\n' +
  'I should be straight with you, as I have been about everything else. All of that is built and ' +
  'running. The last test on my list is a real two-person meeting with cameras on, and that is a ' +
  'test I cannot do alone."\n\n' +
  'THAT LAST LINE IS AN OPPORTUNITY. If somebody offers to be the second person, accept immediately ' +
  'and fix a date before you leave. It turns a Council member from an audience into a participant.'
);

/* ============================================== 12 VIEWER */
s = pres.addSlide();
kicker(s, TWO);
title(s, 'And what you can do while you are in there');
[
  ['Isolate and ghost', 'Pull out what matters, leave the rest faded for context'],
  ['Colour by anything', 'Clash status, issue status, discipline, level, any parameter'],
  ['Explode', 'Pull an assembly apart to see how it goes together'],
  ['Section and measure', 'Cut through, and take a dimension off the model'],
  ['Mark up in three dimensions', 'On the model rather than on a screenshot'],
  ['Pin an issue to an element', 'Long-press on a phone and it anchors at that point'],
  ['Clashes as filterable pins', 'By status and type, so a meeting can work through them'],
  ['Colour-blind safe', 'Because roughly one man in twelve needs it and nobody asks'],
].forEach(function (r, i) {
  const x = M + (i % 2) * 6.4;
  const y = 2.15 + Math.floor(i / 2) * 1.22;
  card(s, x, y, 5.95, 1.02, i % 2 ? TINT : TINT2);
  sq(s, x + 0.32, y + 0.24, SLATE, 0.15);
  s.addText(r[0], { x: x + 0.68, y: y + 0.14, w: 5.0, h: 0.35, fontSize: 15, bold: true, color: INK,
    fontFace: H, isTextBox: true, margin: 0 });
  s.addText(r[1], { x: x + 0.68, y: y + 0.5, w: 5.05, h: 0.4, fontSize: 11.5, color: INK2,
    fontFace: B, isTextBox: true, margin: 0 });
});
s.addText('All of it on a phone as well as a laptop, because that is where site is.',
  { x: M, y: 7.0, w: CW, h: 0.4, fontSize: 13.5, color: MUTED, fontFace: B, italic: true,
    isTextBox: true, margin: 0 });
s.addNotes(
  'Do not read all eight. Pick three and show them in the demonstration instead.\n\n' +
  'SAY: "And while you are in there, it is a proper coordination viewer rather than a picture.\n\n' +
  'Isolate what matters and ghost the rest so you keep your bearings. Colour the model by clash ' +
  'status, issue status, discipline, level, any parameter you like. Explode an assembly. Cut a ' +
  'section, take a measurement. Mark up in three dimensions rather than drawing on a screenshot. ' +
  'And pin an issue to an element, at the point you are looking at, so whoever picks it up later ' +
  'knows exactly what you meant.\n\n' +
  'Clashes come in as pins you can filter by status and type, which is what actually makes a ' +
  'coordination meeting move.\n\n' +
  'It is colour-blind safe, which matters because roughly one man in twelve needs that and almost ' +
  'nobody asks. And all of it works on a phone, because that is where site is."'
);

/* ============================================== 13 CLIENT */
s = pres.addSlide();
kicker(s, TWO);
title(s, 'What a client can check from a phone');
s.addText('Without ringing you on a Saturday.', { x: M, y: 2.05, w: 11.6, h: 0.4, fontSize: 16,
  italic: true, color: ACC2, fontFace: H, isTextBox: true, margin: 0 });
[
  ['What am I being asked to decide?', 'Issues and approvals waiting on them, in one list rather than buried in email.'],
  ['What did I approve, and when?', 'A record of every issue and revision, with dates, so nobody relies on memory.'],
  ['What does it actually look like?', 'The model on their phone. Rotate it, walk it, tap an element and see what it is.'],
  ['What is happening on site?', 'Photographs from the last visit, located, dated, against the right part of the building.'],
  ['Where did we get to on that?', 'The minutes and actions from the meeting, and the recording if there was one.'],
  ['Is anything stuck?', 'Open issues by status, so a question sitting for three weeks is visible.'],
].forEach(function (r, i) {
  const x = M + (i % 2) * 6.4;
  const y = 2.62 + Math.floor(i / 2) * 1.4;
  card(s, x, y, 5.95, 1.18, i % 2 ? TINT : TINT2);
  sq(s, x + 0.34, y + 0.27, ACC, 0.16);
  s.addText(r[0], { x: x + 0.72, y: y + 0.17, w: 5.0, h: 0.36, fontSize: 15, bold: true, color: INK,
    fontFace: H, isTextBox: true, margin: 0 });
  s.addText(r[1], { x: x + 0.72, y: y + 0.54, w: 5.05, h: 0.55, fontSize: 12, color: INK2,
    fontFace: B, lineSpacing: 16, isTextBox: true, margin: 0 });
});
card(s, M, 6.75, CW, 0.55, INK);
s.addText('And it costs them nothing to be there.', { x: M + 0.45, y: 6.75, w: CW - 0.9, h: 0.55,
  fontSize: 15, bold: true, color: SALMON, fontFace: H, valign: 'middle', isTextBox: true,
  margin: 0 });
s.addNotes(
  'Every one of these is a phone call an architect in that room has taken on a weekend. That ' +
  'recognition is the point.\n\n' +
  'SAY: "It is worth saying what this looks like from the other side, because a client with a phone ' +
  'is a large part of why a project runs smoothly or does not.\n\n' +
  'They can see what they are being asked to decide, in one list rather than buried in an email ' +
  'thread. What they approved and when, so nobody relies on memory a year later. They can look at ' +
  'the model itself. They can see photographs from the last site visit, dated and located. They can ' +
  'pick up where a meeting got to. And they can see whether anything is stuck.\n\n' +
  'And it costs them nothing to be there, which is the part that makes it actually happen. On a ' +
  'per-user platform the client is a licence somebody has to justify, so they never get added — ' +
  'and then they ring you on a Saturday instead."\n\n' +
  'That last sentence usually gets a laugh of recognition. Let it.'
);

/* ============================================== 14 DEMO */
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
  'in progress.\n\n' +
  'IF SOMETHING FAILS: say so, move on, use the recording. "That is one of the parts still being ' +
  'finished" is recoverable. Fiddling with a laptop in silence is not.'
);

/* ============================================== 15 COPILOT */
s = pres.addSlide();
kicker(s, TWO);
title(s, 'And before long, you will simply ask it');
card(s, M, 2.1, CW, 1.5, INK);
s.addText('"Which rooms on level two are missing a fire rating?"', { x: M + 0.5, y: 2.35, w: 11.4,
  h: 0.5, fontSize: 22, bold: true, color: SALMON, fontFace: H, italic: true, isTextBox: true,
  margin: 0 });
s.addText('"Tag everything on this level."          "Give me quantities for the north block."',
  { x: M + 0.5, y: 2.92, w: 11.4, h: 0.4, fontSize: 15, color: LIGHTTXT, fontFace: B, italic: true,
    isTextBox: true, margin: 0 });
[
  ['Built and tested', 'The connection between an assistant and the model works inside Revit. Over forty operations: query, create, tag, size, export.'],
  ['Safe by design', 'Every operation checks the licence, runs inside a transaction that can be rolled back, and can be run as a trial that writes nothing.'],
  ['Still to finish', 'The conversation layer on top is what I am working on now. I am showing the direction rather than claiming it is done.'],
].forEach(function (r, i) {
  const x = M + i * 4.0;
  card(s, x, 3.85, 3.7, 2.5, i === 2 ? TINT2 : TINT);
  s.addText(r[0], { x: x + 0.3, y: 4.1, w: 3.1, h: 0.4, fontSize: 15.5, bold: true, color: ACC2,
    fontFace: H, isTextBox: true, margin: 0 });
  s.addText(r[1], { x: x + 0.3, y: 4.58, w: 3.15, h: 1.65, fontSize: 12.5, color: INK2, fontFace: B,
    lineSpacing: 17, isTextBox: true, margin: 0 });
});
s.addText('Most of what stops people using tools like this is not disagreement. It is that learning ' +
  'where everything lives takes time nobody has.', { x: M, y: 6.55, w: CW, h: 0.4, fontSize: 14,
    color: MUTED, fontFace: B, italic: true, isTextBox: true, margin: 0 });
s.addNotes(
  'The slide that will make the technical people sit forward. Be accurate — overselling this is ' +
  'exactly what would cost you their respect.\n\n' +
  'SAY: "One last thing on the platform, and it is not finished. I am showing it anyway because I ' +
  'think it is where all of this is going.\n\n' +
  'The connection between an AI assistant and the model itself is built and tested. Over forty ' +
  'operations: query a model, create things, tag, size, export. And it is built carefully. Every ' +
  'operation checks the licence, runs inside a transaction that can be rolled back, and can be run ' +
  'as a trial first, so nothing happens to a model that cannot be undone.\n\n' +
  'What I am still finishing is the conversation layer on top, so I am not going to demonstrate it ' +
  'and tell you it works.\n\n' +
  'But the direction seems clear. Instead of learning where a command lives, you ask for the ' +
  'outcome. And that matters for adoption more than it sounds, because most of what stops people ' +
  'using tools like this is not disagreement. It is that learning where everything lives takes time ' +
  'nobody has."\n\n' +
  'DO NOT DEMONSTRATE THIS LIVE. Lead with the safety design rather than the clever part — that ' +
  'is the difference between a demo and an engineering decision.\n\n' +
  'CHECK THE OPERATION COUNT before quoting a number.'
);

/* ============================================== 16 WHERE IT STANDS */
s = pres.addSlide();
kicker(s, TWO);
title(s, 'Where it stands today, plainly');
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
  'Volunteer all of it. Being the person who says what is unfinished is worth more in this room than ' +
  'any feature.\n\n' +
  'SAY: "Let me be plain about how far along it is.\n\n' +
  'Working now, in daily use on live projects: the shared record, document control, issues, mobile ' +
  'offline, model checking, and the drawings, schedules, quantities and handover data.\n\n' +
  'Being finished: serving many practices from one system, payments, hosting sized for the region, ' +
  'an independent security review, and the conversation layer.\n\n' +
  'And not yet validated: the engineering calculation engines. Complete and tested, but never ' +
  'through independent professional validation, so I only offer them as commissioned work with ' +
  'manual checks alongside. I would rather say that here than have an engineer discover it.\n\n' +
  'One line: ready for one practice today, not ready to serve the whole profession at once. That ' +
  'gap is the work that is left."'
);

/* ============================================== 17 ROLES */
s = pres.addSlide();
kicker(s, THREE);
title(s, 'Who you pay for, and who is free');
[
  ['Authors', 'The people who model', 'They need their own modelling software, which I do not sell. The tools sit inside whatever they already use.', ACC],
  ['Coordinators', 'The people who run the job', 'Named seats. Checking, issues, documents, meetings, quantities. This is the part a practice pays for.', ACC],
  ['Everyone else', 'Client, contractor, QS, consultants', 'Unlimited, and free. View, comment, raise issues and join meetings, with nobody buying them a licence.', SLATE],
].forEach(function (r, i) {
  const x = M + i * 4.0;
  card(s, x, 2.2, 3.7, 3.5, i === 2 ? TINT2 : TINT);
  sq(s, x + 0.3, 2.5, r[3], 0.2);
  s.addText(r[0], { x: x + 0.3, y: 2.85, w: 3.1, h: 0.45, fontSize: 21, bold: true, color: INK,
    fontFace: H, isTextBox: true, margin: 0 });
  s.addText(r[1], { x: x + 0.3, y: 3.32, w: 3.1, h: 0.4, fontSize: 12.5, color: ACC2, fontFace: B,
    italic: true, isTextBox: true, margin: 0 });
  s.addText(r[2], { x: x + 0.3, y: 3.85, w: 3.15, h: 1.7, fontSize: 12.5, color: INK2, fontFace: B,
    lineSpacing: 17, isTextBox: true, margin: 0 });
});
card(s, M, 6.0, CW, 0.95, INK);
s.addText('A four-person practice usually pays for two or three coordinators. The rest of the ' +
  'project team costs nothing.', { x: M + 0.45, y: 6.0, w: CW - 0.9, h: 0.95, fontSize: 16.5,
    bold: true, color: PAPER, fontFace: H, valign: 'middle', isTextBox: true, margin: 0 });
s.addNotes(
  'Put this before the prices or the prices will not make sense. It is also where the room realises ' +
  'the economics are a different shape rather than merely cheaper.\n\n' +
  'SAY: "Now the affordability half, and before I show prices it is worth explaining who actually ' +
  'gets counted, because it is not what people expect.\n\n' +
  'There are authors: the people who model. They need their own modelling software, which I do not ' +
  'sell and am not trying to replace.\n\n' +
  'There are coordinators: the people who run the job. Checking, issues, documents, meetings, ' +
  'quantities. Those are named seats, and that is the part a practice pays for.\n\n' +
  'And then everyone else. Client, contractor, quantity surveyor, other consultants. Unlimited, and ' +
  'free.\n\n' +
  'So a four-person practice usually pays for two or three coordinators, and the rest of the ' +
  'project team costs nothing at all. That is a different shape from paying per head, and it is the ' +
  'reason the whole team actually ends up on the platform rather than just the two people whose ' +
  'licences somebody could justify."'
);

/* ============================================== 18 PRICE */
s = pres.addSlide();
kicker(s, THREE);
title(s, 'And it has to work for a four-person practice');
[
  ['Priced per practice', 'Not per person, so a practice pays once rather than once per member of staff.'],
  ['Everyone outside joins free', 'Client, contractor and quantity surveyor cost nothing to bring onto a project.'],
  ['Billed in shillings', 'Not in dollars, and not exposed to a rate nobody here controls.'],
  ['A free route exists', 'Through Blender and Bonsai, for anyone with no software budget at all.'],
].forEach(function (e, i) {
  const x = M + (i % 2) * 6.4;
  const y = 2.2 + Math.floor(i / 2) * 1.42;
  card(s, x, y, 5.95, 1.2);
  sq(s, x + 0.35, y + 0.3, ACC, 0.16);
  s.addText(e[0], { x: x + 0.72, y: y + 0.2, w: 4.9, h: 0.35, fontSize: 15.5, bold: true,
    color: INK, fontFace: H, isTextBox: true, margin: 0 });
  s.addText(e[1], { x: x + 0.72, y: y + 0.58, w: 4.95, h: 0.5, fontSize: 12.5, color: INK2,
    fontFace: B, isTextBox: true, margin: 0 });
});
card(s, M, 5.2, CW, 1.05, INK);
[['USD 25', 'Modelling tools alone, one seat'], ['USD 60', 'Everything, up to 3 people'],
 ['USD 130', 'Everything, 4 to 10 people']].forEach(function (q, i) {
  const x = M + 0.45 + i * 3.3;
  s.addText(q[0], { x: x, y: 5.38, w: 3.15, h: 0.35, fontSize: 17, bold: true, color: SALMON,
    fontFace: H, isTextBox: true, margin: 0 });
  s.addText(q[1], { x: x, y: 5.74, w: 3.15, h: 0.3, fontSize: 11.5, color: LIGHTTXT, fontFace: B,
    isTextBox: true, margin: 0 });
});
s.addText('a month,\nper practice', { x: M + 10.35, y: 5.42, w: 1.7, h: 0.6, fontSize: 11,
  italic: true, color: MUTED, fontFace: B, lineSpacing: 13, isTextBox: true, margin: 0 });
s.addText('If the Society ever wanted a member rate, I would rather offer one than be asked for it.',
  { x: M, y: 6.5, w: CW, h: 0.45, fontSize: 15.5, bold: true, color: ACC2, fontFace: H,
    italic: true, isTextBox: true, margin: 0 });
s.addNotes(
  'This is the "cheap" half of the thesis, and the argument most likely to change what members ' +
  'actually do.\n\n' +
  'SAY: "And it has to work for a four-person practice, or none of the rest matters.\n\n' +
  'It is priced per practice rather than per person. Everyone outside your office joins free, which ' +
  'matters because the usual reason coordination software dies on a project is that nobody will pay ' +
  'for the other parties to be on it. It is billed in shillings. And there is a free route for ' +
  'anyone with no software budget at all.\n\n' +
  'Twenty-five dollars a month for the modelling tools on their own. Sixty for everything up to ' +
  'three people. A hundred and thirty for a practice of four to ten.\n\n' +
  'I am not claiming it does everything the international products do. I am saying it does what a ' +
  'Ugandan practice needs every week, at a price that lets BIM be normal rather than exceptional.\n\n' +
  'And if the Society ever wanted a member rate, I would rather offer one than be asked for it."\n\n' +
  'CHECK THE PRICES ARE CURRENT before saying them. Do not name a discount percentage in the room, ' +
  'only the willingness to agree one.'
);

/* ============================================== 19 DIFFERENTIATION */
s = pres.addSlide();
kicker(s, THREE);
title(s, 'What global platforms will not do for us');
[
  ['Price per practice, not per person', 'Everyone outside your office joins free. Their business model cannot allow it.'],
  ['Ugandan conditions as defaults', 'Wind, seismic, soil bearing and rainfall by region. A market our size does not justify the work.'],
  ['Work with no connection', 'Site records queue on the phone and sync later, because that is what our sites are like.'],
  ['Bill in shillings', 'And mobile money, because most practices here do not have a corporate card.'],
  ['Offer a free route', 'Blender and Bonsai, at no licence cost. It would undercut their own funnel.'],
  ['Answer the phone in this time zone', 'From someone who has worked on a project like yours.'],
].forEach(function (r, i) {
  const x = M + (i % 2) * 6.4;
  const y = 2.15 + Math.floor(i / 2) * 1.45;
  card(s, x, y, 5.95, 1.22, TINT);
  sq(s, x + 0.35, y + 0.26, ACC, 0.16);
  s.addText(r[0], { x: x + 0.72, y: y + 0.18, w: 4.9, h: 0.36, fontSize: 15.5, bold: true,
    color: INK, fontFace: H, isTextBox: true, margin: 0 });
  s.addText(r[1], { x: x + 0.72, y: y + 0.56, w: 4.95, h: 0.55, fontSize: 12, color: INK2,
    fontFace: B, lineSpacing: 16, isTextBox: true, margin: 0 });
});
card(s, M, 6.5, CW, 0.75, INK);
s.addText('None of this is them being worse. A market our size does not justify the work for them. ' +
  'It does for us.', { x: M + 0.45, y: 6.5, w: CW - 0.9, h: 0.75, fontSize: 15.5, bold: true,
    color: SALMON, fontFace: H, valign: 'middle', isTextBox: true, margin: 0 });
s.addNotes(
  'Deliver it generously rather than competitively. You are not attacking anybody, you are ' +
  'explaining why a gap exists.\n\n' +
  'SAY: "People sometimes ask why build this at all when the international products exist. This is ' +
  'my honest answer.\n\n' +
  'It is priced per practice rather than per person, and everyone outside your office joins free. ' +
  'The large vendors cannot do that; their whole business model is per seat.\n\n' +
  'Ugandan conditions are in it as defaults. Wind, seismic zone, soil bearing, design rainfall by ' +
  'region. Nobody in California is going to build that.\n\n' +
  'It works with no connection, because that is what our sites are like. It bills in shillings, and ' +
  'mobile money is coming, because most practices here do not have a corporate card. There is a ' +
  'free route through open-source software, which would undercut their own funnel. And if something ' +
  'goes wrong you can reach somebody in this time zone who has worked on a project like yours.\n\n' +
  'None of that is them being worse than us. A market our size does not justify the work for them. ' +
  'It does for us, because it is the only market we have."\n\n' +
  'DO NOT name competitors and do not compare prices unprompted. The structural argument is ' +
  'stronger than a price fight and survives any discount they might offer.'
);

/* ============================================== 20 CPD */
s = pres.addSlide();
kicker(s, FOUR);
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
  ['It would be the Society’s', 'Your programme, your accreditation, your name, delivered by one of your own members rather than an imported trainer.'],
].forEach(function (c, i) {
  const y = 2.2 + i * 1.5;
  card(s, M + 5.3, y, 7.1, 1.28, i === 2 ? TINT2 : TINT);
  s.addText(c[0], { x: M + 5.65, y: y + 0.18, w: 6.4, h: 0.35, fontSize: 16, bold: true,
    color: ACC2, fontFace: H, isTextBox: true, margin: 0 });
  s.addText(c[1], { x: M + 5.65, y: y + 0.58, w: 6.4, h: 0.6, fontSize: 13, color: INK2,
    fontFace: B, lineSpacing: 18, isTextBox: true, margin: 0 });
});
s.addNotes(
  'The strongest institutional argument. Slow down, and let the number sit on the screen.\n\n' +
  'SAY: "If any of this is useful to the Society, the first place is not a favour I would be doing ' +
  'you. It is something you are already obliged to do.\n\n' +
  'Every practising architect in Uganda needs twenty CPD points a year to renew a practising ' +
  'licence. That is the 2019 bye-laws, not a suggestion. So the Society has a permanent obligation ' +
  'to put credible content in front of its members, every year, forever.\n\n' +
  'And this content is hard to source here. Most of what is available locally is a supplier ' +
  'presenting a product. Structured, hands-on training in information standards is not really on ' +
  'offer in Kampala.\n\n' +
  'It would be your programme. Your accreditation, your name, delivered by one of your own members ' +
  'rather than someone flown in."\n\n' +
  'THEN ASK, do not assert: "I do not know what accreditation would require for a course like this, ' +
  'or what points it would carry, or what your members currently pay. You do. That is one of the ' +
  'things I would like your guidance on." Asking makes them co-owners; claiming you know makes you ' +
  'a supplier.'
);

/* ============================================== 21 STANDARD */
s = pres.addSlide();
kicker(s, FOUR);
title(s, 'A standard the Society could actually enforce');
s.addText('Publishing a standard is the easy part. Getting anyone to comply with it is where most ' +
  'of them quietly die.', { x: M, y: 2.05, w: 11.6, h: 0.5, fontSize: 16, italic: true,
    color: ACC2, fontFace: H, isTextBox: true, margin: 0 });
[
  ['Naming and classification', 'Applied automatically rather than typed'],
  ['Drawing types and title blocks', 'So a set looks and behaves the same way every time'],
  ['Level of detail by stage', 'What is expected at concept, at tender, at handover'],
  ['What a handover must contain', 'Written down, and collected as you go'],
  ['A check that runs against it', 'Naming what fails, and where, as a number'],
  ['Ugandan defaults throughout', 'Because a standard should fit where it is used'],
].forEach(function (r, i) {
  const x = M + (i % 2) * 6.4;
  const y = 2.7 + Math.floor(i / 2) * 1.15;
  card(s, x, y, 5.95, 0.98, i % 2 ? TINT : TINT2);
  sq(s, x + 0.32, y + 0.4, SLATE, 0.15);
  s.addText(r[0], { x: x + 0.68, y: y + 0.14, w: 5.0, h: 0.35, fontSize: 15, bold: true, color: INK,
    fontFace: H, isTextBox: true, margin: 0 });
  s.addText(r[1], { x: x + 0.68, y: y + 0.5, w: 5.0, h: 0.35, fontSize: 12, color: INK2,
    fontFace: B, isTextBox: true, margin: 0 });
});
s.addText('It is a draft I built because I needed one, not a proposal. But if the profession ever ' +
  'wanted a standard of its own, it is a starting point rather than a blank page.', { x: M, y: 6.35,
    w: CW, h: 0.5, fontSize: 14.5, bold: true, color: ACC2, fontFace: B, italic: true,
    isTextBox: true, margin: 0 });
s.addNotes(
  'This is the half most tools do not offer, and for a Council it is the more interesting half. ' +
  'Offer it lightly and do not push it.\n\n' +
  'SAY: "The second place it might be useful is the standards.\n\n' +
  'Publishing a standard is the easy part. Getting anyone to comply with it is where most of them ' +
  'quietly die, because compliance is expensive and nobody can check it. A document on a website ' +
  'changes nothing about what actually arrives in an inbox.\n\n' +
  'So what is in here is a working draft: naming and classification applied automatically rather ' +
  'than typed, drawing types and title blocks so a set behaves the same way every time, what level ' +
  'of detail is expected at each stage, what a handover has to contain, and a check that runs ' +
  'against all of it and tells you what fails and where.\n\n' +
  'I want to be careful how I put this. It is a draft I built because I needed one, not a proposal ' +
  'I am putting to you. But if the profession ever wanted a standard of its own, it is a starting ' +
  'point rather than a blank page."\n\n' +
  'THEN LEAVE IT THERE. If the Council picks it up it becomes their idea, which is the only way it ' +
  'would ever actually happen. If they do not, nothing is lost.'
);

/* ============================================== 22 COHORT */
s = pres.addSlide();
kicker(s, FOUR);
title(s, 'What a training week could cover');
[
  ['Day 1', 'Why information standards exist', 'ISO 19650 in plain terms. Naming, versions, approvals, handover. What a client is really asking for.'],
  ['Day 2', 'Modelling to a standard', 'Working in their own software — Revit, ArchiCAD or the free route — to a shared naming scheme.'],
  ['Day 3', 'Checking and coordinating', 'Running the check, reading what it reports, raising and closing issues across a team.'],
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
s.addText('About 25 members at a time, hands-on throughout. The shape is mine; the content should ' +
  'be yours.', { x: M, y: 6.85, w: CW, h: 0.4, fontSize: 13.5, color: MUTED, fontFace: B,
    italic: true, isTextBox: true, margin: 0 });
s.addNotes(
  'This exists because nobody can endorse a training programme they cannot picture. Do not read the ' +
  'table — point at it and summarise.\n\n' +
  'SAY: "And so that you are not looking at something abstract, this is roughly what a week could ' +
  'look like.\n\n' +
  'Day one is why information standards exist at all, in plain terms. Day two is modelling to a ' +
  'standard, in whatever people already use. Day three is checking and coordinating. Day four is ' +
  'getting the work out — drawings, schedules, quantities. Day five is handover, and an exercise ' +
  'they complete and take away.\n\n' +
  'About twenty-five members at a time, hands-on throughout.\n\n' +
  'The shape is mine but the content should be yours. If the Board of Education wanted it structured ' +
  'differently, I would rather build what you would actually accredit."\n\n' +
  'That last sentence is the one most likely to turn this from your proposal into their programme.'
);

/* ============================================== 23 LIMITS */
s = pres.addSlide();
kicker(s, 'WHAT I AM NOT GOING TO OVERSELL');
title(s, 'Three things you should hear from me');
[
  ['The deepest automation is in Revit', 'The other tools connect through IFC, which is the open standard and means nobody is locked in — but there is no native ArchiCAD or Tekla plug-in, and I am not going to imply there is.'],
  ['The calculation engines are unvalidated', 'Complete and tested, but never taken through independent professional validation. Offered only as commissioned work, cross-checked by hand. Your engineers would be the right people to validate them.'],
  ['It is not finished', 'Ready for one practice today. Serving the whole profession at once is the work that remains.'],
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
  'open standard and is genuinely the right answer because it means nobody is locked in — but ' +
  'there is no native ArchiCAD plug-in and no native Tekla plug-in, and I am not going to imply ' +
  'otherwise.\n\n' +
  'Second, the engineering calculation engines have never been independently validated. They work ' +
  'and they are tested, but I only offer them as commissioned work with manual checks in parallel. ' +
  'If the Society put engineers on validating them, that sign-off would carry real weight.\n\n' +
  'Third, it is not finished, and I said that earlier.\n\n' +
  'You would have found all three of those in about four minutes. I would rather you heard them ' +
  'from me."'
);

/* ============================================== 24 COST */
s = pres.addSlide();
kicker(s, 'WHAT REMAINS');
title(s, 'What it would take to finish');
[
  ['Completing the platform', 'USD 36,200', 'Serving many practices, payments, security review, testing'],
  ['Regional hosting', 'USD 12,000', 'Twelve months, sized to grow with use'],
  ['Training, three groups', 'USD 15,000', 'About 25 members per group'],
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
  'State it as a fact, not a request. You are reporting what remains, not asking anyone to cover ' +
  'it. That difference is everything and the room will feel it.\n\n' +
  'SAY: "So you have the whole picture, here is what finishing it would take.\n\n' +
  'Thirty-six thousand two hundred dollars to complete the platform. Twelve thousand for a year of ' +
  'hosting sized for regional use. Fifteen thousand for three training groups of about twenty-five ' +
  'people each. With contingency, about seventy-two and a half thousand dollars.\n\n' +
  'The three are separable. The training in particular stands on its own and could start first."\n\n' +
  'THEN STOP. Do not follow it with an ask. Somebody will very likely say "and how are you funding ' +
  'that?" — that question landing from them is far stronger than you raising it. When it comes: ' +
  '"I am looking at several routes. Some a professional body can open and an individual cannot, so ' +
  'I would value your thinking." Then stop again and let them offer, or not. Do not push.'
);

/* ============================================== 25 THE PAYOFF */
s = pres.addSlide();
darkBg(s);
kicker(s, 'THE THING I CAME TO SAY', ACC);
title(s, 'It is no longer expensive, and no longer difficult', PAPER, 36);
[
  ['It is cheap', 'From twenty-five dollars a month, billed in shillings, and free if you have no budget at all.'],
  ['It is easy', 'The standards work happens as you model, not as a second job at the end of one.'],
  ['It works with what you own', 'Revit, ArchiCAD, Tekla — or nothing at all.'],
  ['And the standard is drafted', 'Not from scratch. From something that already runs on real projects.'],
].forEach(function (r, i) {
  const y = 2.3 + i * 0.98;
  numTile(s, M, y, String(i + 1), i === 3 ? ACC : '3A4048');
  s.addText(r[0], { x: M + 0.85, y: y - 0.02, w: 4.6, h: 0.45, fontSize: 18, bold: true,
    color: PAPER, fontFace: H, isTextBox: true, margin: 0 });
  s.addText(r[1], { x: M + 5.6, y: y - 0.01, w: 6.4, h: 0.55, fontSize: 13.5, color: LIGHTTXT,
    fontFace: B, isTextBox: true, margin: 0 });
});
card(s, M, 6.4, CW, 0.8, '2E333A');
s.addText('None of that was true two years ago. That is really the whole of what I came to say.',
  { x: M + 0.45, y: 6.4, w: CW - 0.9, h: 0.8, fontSize: 16.5, bold: true, color: SALMON,
    fontFace: H, valign: 'middle', isTextBox: true, margin: 0 });
s.addNotes(
  'THE PAYOFF. This is the sentence you want repeated to people who were not in the room. Slow ' +
  'right down, and do not add anything after it.\n\n' +
  'SAY: "If there is one thing I would like to leave you with, it is this.\n\n' +
  'We have talked about BIM adoption in this country as though it were a five-year problem. Skills, ' +
  'cost, software, standards, all of it a long way off. And for a long time that was fair.\n\n' +
  'But look at where the pieces actually are now. It is cheap — from twenty-five dollars a ' +
  'month, billed in shillings, and free if you have no budget at all. It is easy — the ' +
  'standards work happens as you model rather than as a second job at the end of one. It works with ' +
  'whatever people already own, or with nothing. And a standard is drafted, not from scratch, but ' +
  'from something that already runs on real projects.\n\n' +
  'I am not saying it is finished, and I have been honest with you about what is not. But adopting ' +
  'BIM in Uganda is no longer expensive and no longer difficult, and none of that was true two ' +
  'years ago.\n\n' +
  'That is really the whole of what I came to say."\n\n' +
  'PAUSE. Let it sit before the next slide.'
);

/* ============================================== 26 HORIZON */
s = pres.addSlide();
kicker(s, 'AND IF IT WORKS');
title(s, 'Where something like this could go');
s.addText('The Society would own the standard. Software is one way to comply with it, not the only ' +
  'way.', { x: M, y: 2.05, w: 11.6, h: 0.45, fontSize: 16.5, bold: true, color: ACC2, fontFace: H,
    italic: true, isTextBox: true, margin: 0 });
[
  ['Members use it', 'A handful of practices put it on real jobs because it fits their work and their budget.'],
  ['The Society recognises it', 'The naming and conventions become how members deliver, with the Council shaping them.'],
  ['Clients start expecting it', 'Public bodies and larger clients ask for what our members can already produce.'],
  ['The region follows', 'Kenya, Rwanda and Tanzania have the same conditions, the same gap, and no local platform either.'],
].forEach(function (r, i) {
  const y = 2.68 + i * 1.05;
  card(s, M, y, CW, 0.92, i === 3 ? TINT2 : TINT);
  numTile(s, M + 0.35, y + 0.23, String(i + 1), i === 3 ? ACC : INK);
  s.addText(r[0], { x: M + 1.05, y: y + 0.13, w: 4.2, h: 0.42, fontSize: 16, bold: true,
    color: INK, fontFace: H, isTextBox: true, margin: 0 });
  s.addText(r[1], { x: M + 5.5, y: y + 0.14, w: 6.5, h: 0.62, fontSize: 12.5, color: INK2,
    fontFace: B, lineSpacing: 16, isTextBox: true, margin: 0 });
});
card(s, M, 6.95, CW, 0.55, INK);
s.addText('None of that happens by announcement. It happens one practice at a time.',
  { x: M + 0.45, y: 6.95, w: CW - 0.9, h: 0.55, fontSize: 15, bold: true, color: SALMON,
    fontFace: H, valign: 'middle', isTextBox: true, margin: 0 });
s.addNotes(
  'Ambition delivered modestly. Forty-five seconds. The last line is what stops it sounding ' +
  'grandiose, so do not drop it.\n\n' +
  'SAY: "One more thought and then I will stop.\n\n' +
  'If something like this does work, I do not think it spreads by anybody announcing it.\n\n' +
  'It starts with a handful of members putting it on real jobs, because it fits their work and ' +
  'their budget. If that goes well, the naming and conventions gradually become how members deliver, ' +
  'and at that point the Council would want a hand in shaping them, which is as it should be. Then ' +
  'clients start asking for what our members can already produce, rather than the other way round.\n\n' +
  'And beyond that, honestly, the region. Kenya, Rwanda and Tanzania have the same conditions we do, ' +
  'the same gap, and no local platform either. But that is a long way off and I would rather earn it ' +
  'here first.\n\n' +
  'The important word in all of that is yours. The Society would own the standard. My software would ' +
  'be one way to comply with it, and not the only way, because a standard tied to one company is not ' +
  'a national standard.\n\n' +
  'None of that happens by announcement. It happens one practice at a time."\n\n' +
  'IF THEY GET EXCITED, do not chase it. "I would love to talk about that properly once we have run ' +
  'a cohort and you have seen whether the thing works."'
);

/* ============================================== 27 THE PROPOSAL */
s = pres.addSlide();
darkBg(s);
kicker(s, 'TO PUT IT PLAINLY', ACC);
title(s, 'Three things I would propose', PAPER);
[
  ['That the Society endorses a training programme', 'For members, and that we look at whether it could be accredited for CPD.'],
  ['That someone here looks at this properly', 'Somebody from the ICT Cluster, so the judgement about whether it is any good is yours.'],
  ['That we talk about the funding', 'Not your money. Your view on which door is worth knocking on.'],
].forEach(function (a, i) {
  const y = 2.35 + i * 1.3;
  numTile(s, M, y, String(i + 1), ACC);
  s.addText(a[0], { x: M + 0.85, y: y - 0.06, w: 5.1, h: 0.85, fontSize: 18, bold: true,
    color: PAPER, fontFace: H, lineSpacing: 23, isTextBox: true, margin: 0 });
  s.addText(a[1], { x: M + 6.1, y: y - 0.02, w: 5.9, h: 0.9, fontSize: 13.5, color: LIGHTTXT,
    fontFace: B, lineSpacing: 19, isTextBox: true, margin: 0 });
});
card(s, M, 6.3, CW, 0.85, '2E333A');
s.addText('All three are suggestions, and the last word is entirely yours — including deciding ' +
  'none of them.', { x: M + 0.45, y: 6.3, w: CW - 0.9, h: 0.85, fontSize: 16, bold: true,
    color: SALMON, fontFace: H, valign: 'middle', isTextBox: true, margin: 0 });
s.addNotes(
  'The close. Have it word-perfect, because it is the only part of the meeting they will repeat to ' +
  'anyone who was not there.\n\n' +
  'SAY: "So, to put it plainly, three things I would propose.\n\n' +
  'That the Society considers endorsing a training programme for members, and that we look together ' +
  'at whether it could be accredited for CPD.\n\n' +
  'That somebody here — from the ICT Cluster, ideally — looks at this properly, so the ' +
  'judgement about whether it is any good is yours rather than mine.\n\n' +
  'And that at some point we talk about how the rest gets funded. Not your money. Your view on which ' +
  'door is worth knocking on.\n\n' +
  'All three are suggestions. The last word is entirely yours, including deciding none of them. I ' +
  'would genuinely rather have your thinking than a decision today.\n\n' +
  'Thank you for the time."\n\n' +
  'THEN BE QUIET AND LET THEM TALK. The whole session is designed to arrive here.\n\n' +
  'BEFORE THEY DISPERSE, write down three things: anything they suggested you build or change, ' +
  'anyone they said you should talk to, and whether they want to see it again. All three come from ' +
  'them, which is what makes them worth having.'
);

/* ============================================== 28 CLOSE */
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
