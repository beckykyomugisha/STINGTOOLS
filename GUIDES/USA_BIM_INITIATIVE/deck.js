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
pres.title = 'A BIM platform built in Kampala';

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

/* ------------------------------------------------ 1 TITLE */
let s = pres.addSlide();
darkBg(s);
motif(s, M, 1.6, ACC);
s.addText('TO THE COUNCIL OF THE UGANDA SOCIETY OF ARCHITECTS', { x: M, y: 2.05, w: CW, h: 0.32,
  fontSize: 12.5, bold: true, charSpacing: 2.5, color: ACC, fontFace: B, isTextBox: true, margin: 0 });
s.addText('A BIM platform, and the\nstandards to run it on', { x: M, y: 2.5, w: 11.2, h: 1.9,
  fontSize: 42, bold: true, color: PAPER, fontFace: H, lineSpacing: 50, isTextBox: true, margin: 0 });
s.addText('Built in Kampala, for the way we actually work', { x: M, y: 4.5, w: 10.6, h: 0.4,
  fontSize: 17, color: LIGHTTXT, fontFace: B, italic: true, isTextBox: true, margin: 0 });
s.addText('Davis Mayanja', { x: M, y: 5.55, w: 6, h: 0.3, fontSize: 14.5, bold: true, color: PAPER,
  fontFace: B, isTextBox: true, margin: 0 });
s.addText('September 2026', { x: M, y: 5.9, w: 7, h: 0.3, fontSize: 12.5, color: MUTED,
  fontFace: B, isTextBox: true, margin: 0 });
s.addNotes(
  'Register for the whole session: warm, unhurried, confident. You are a member showing the Council ' +
  'something you have built, not a supplier closing a sale. There is no moment where you need a yes, ' +
  'which is exactly why the room will listen.\n\n' +
  'SAY: "Mr President, Chairman, thank you for the time.\n\n' +
  'I have spent about two years building something and I thought it was time I showed the Council ' +
  'properly rather than kept describing it in passing.\n\n' +
  'What I want to show you is two things really. A working BIM platform, and the standards that go ' +
  'with it. And then I would like to put a view to you about where I think all of this is heading, ' +
  'and hear whether you see it the same way."\n\n' +
  'NEVER mention two years without earning or money being tight. It reads as need, and need makes ' +
  'people cautious rather than generous. Two years of investment is a credential.'
);

/* ------------------------------------------------ 2 WHAT THIS IS */
s = pres.addSlide();
kicker(s, 'WHAT YOU ARE LOOKING AT');
title(s, 'Two halves of the same thing');
card(s, M, 2.15, 5.95, 3.6, INK);
sq(s, M + 0.45, 2.55, ACC, 0.22);
s.addText('A platform', { x: M + 0.45, y: 2.95, w: 5.0, h: 0.5, fontSize: 26, bold: true,
  color: PAPER, fontFace: H, isTextBox: true, margin: 0 });
s.addText('The doing', { x: M + 0.45, y: 3.45, w: 5.0, h: 0.3, fontSize: 13, color: SALMON,
  fontFace: B, italic: true, isTextBox: true, margin: 0 });
s.addText('Tools inside the modelling software, and a shared record around it. Checking, ' +
  'classification, drawings, schedules, quantities, handover data, issues and correspondence, in ' +
  'one flow rather than five products.',
  { x: M + 0.45, y: 3.9, w: 5.05, h: 1.7, fontSize: 13.5, color: LIGHTTXT, fontFace: B,
    lineSpacing: 20, isTextBox: true, margin: 0 });
card(s, M + 6.4, 2.15, 5.95, 3.6, TINT);
sq(s, M + 6.85, 2.55, ACC, 0.22);
s.addText('And standards', { x: M + 6.85, y: 2.95, w: 5.0, h: 0.5, fontSize: 26, bold: true,
  color: INK, fontFace: H, isTextBox: true, margin: 0 });
s.addText('The agreeing', { x: M + 6.85, y: 3.45, w: 5.0, h: 0.3, fontSize: 13, color: ACC2,
  fontFace: B, italic: true, isTextBox: true, margin: 0 });
s.addText('Naming, classification, drawing conventions, what level of detail belongs at each stage, ' +
  'and what a handover has to contain. Written down, and checkable by the software rather than ' +
  'only by argument.',
  { x: M + 6.85, y: 3.9, w: 5.05, h: 1.7, fontSize: 13.5, color: INK2, fontFace: B,
    lineSpacing: 20, isTextBox: true, margin: 0 });
s.addText('Most tools give you the first half. The second half is usually left to each practice to ' +
  'invent for itself.', { x: M, y: 6.05, w: CW, h: 0.5, fontSize: 15.5, bold: true, color: ACC2,
    fontFace: H, italic: true, isTextBox: true, margin: 0 });
s.addNotes(
  'THE FIRST IMPRESSION. This is the frame everything else sits inside, so do not rush it.\n\n' +
  'SAY: "What I am going to show you is two halves of the same thing.\n\n' +
  'There is a platform: tools inside the modelling software, and a shared record around it. ' +
  'Checking, classification, drawings, schedules, quantities, handover data, issues and ' +
  'correspondence, all in one flow rather than five separate products.\n\n' +
  'And there are standards: naming, classification, drawing conventions, what level of detail ' +
  'belongs at what stage, what a handover has to contain. Written down, and checkable by the ' +
  'software rather than only by argument.\n\n' +
  'Most tools give you the first half. The second half usually gets left to each practice to invent ' +
  'for itself, which is a large part of why our drawings do not talk to each other."'
);

/* ------------------------------------------------ 3 SIX NEW THINGS */
s = pres.addSlide();
kicker(s, 'BEFORE ANYTHING ELSE');
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
  s.addText(r[0], { x: x + 0.7, y: y + 0.14, w: 5.0, h: 0.36, fontSize: 15, bold: true,
    color: INK, fontFace: H, isTextBox: true, margin: 0 });
  s.addText(r[1], { x: x + 0.7, y: y + 0.5, w: 5.05, h: 0.5, fontSize: 11.5, color: INK2,
    fontFace: B, lineSpacing: 15, isTextBox: true, margin: 0 });
});
s.addNotes(
  'THE CAPTURE SLIDE. Everyone here knows BIM, so do not explain it. Ninety seconds, fast. Each of ' +
  'these gets its own slide later.\n\n' +
  'SAY: "You all know what BIM is, so I will not explain it. Let me instead show you six things I ' +
  'think are new, and then take them properly one at a time.\n\n' +
  'You can talk to the model in plain English. You can hold a live meeting inside the model rather ' +
  'than over a screen share. One element keeps the same identity across four modelling tools, ' +
  'including a free one. You can raise an issue by long-pressing the element on your phone. A model ' +
  'gets a compliance score, an actual number. Drawings, schedules and quantities come out of the ' +
  'model. Handover data is assembled as you build. And Ugandan conditions are built in as defaults.\n\n' +
  'Any one of those is useful on its own. Together they change what a working day looks like."'
);

/* ------------------------------------------------ 4 LIFECYCLE */
s = pres.addSlide();
kicker(s, 'THE PLATFORM');
title(s, 'A medium-sized project, start to finish');
s.addText('Not five products with exports between them. One chain, where each stage already knows ' +
  'what the last one did.', { x: M, y: 2.02, w: 11.7, h: 0.45, fontSize: 15.5, italic: true,
    color: ACC2, fontFace: H, isTextBox: true, margin: 0 });
[
  ['Set up', 'Project created, the standard applied, the team invited. Consultants, contractor and client join free.'],
  ['Design', 'Authors model in Revit, ArchiCAD or the free route. Naming and classification applied as they go.'],
  ['Check and coordinate', 'The audit runs and scores the model. Clashes arrive as pins. The meeting happens inside the model.'],
  ['Document and tender', 'Sheets, title blocks, schedules and quantities out of the model. Transmittals and a drawing register.'],
  ['Build', 'Site records, photos and issues from a phone, offline, syncing when there is signal.'],
  ['Hand over and operate', 'The asset register was assembled along the way, so facilities management inherits data rather than a box of drawings.'],
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
  'This is the "complete platform" proof, and it is worth walking slowly because it is the slide ' +
  'that answers "what does this actually replace?"\n\n' +
  'SAY: "Let me take you through a medium-sized job, from first sketch to the building being run.\n\n' +
  'You set the project up. The standard gets applied to it. You invite the team, and the consultants, ' +
  'the contractor and the client all join free.\n\n' +
  'Your authors model in whatever they use, and naming and classification are applied as they go ' +
  'rather than at the end.\n\n' +
  'The audit runs and scores the model. Clashes come in as pins you can filter. And the coordination ' +
  'meeting happens inside the model rather than over a screen share.\n\n' +
  'Then documentation: sheets, title blocks, schedules and quantities straight out of the model, with ' +
  'transmittals and a drawing register kept for you.\n\n' +
  'On site, records and photos and issues from a phone, working offline and syncing when there is ' +
  'signal.\n\n' +
  'And at the end, handover. The asset register has been assembling itself the whole way through, so ' +
  'the facilities team inherits data rather than a box of drawings.\n\n' +
  'That last part is the one I would underline. Handover is usually a miserable exercise done in the ' +
  'final fortnight by whoever is least busy. Here it is a by-product of having done the rest ' +
  'properly."'
);

/* ------------------------------------------------ 5 AUTOMATION */
s = pres.addSlide();
kicker(s, 'THE PLATFORM');
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
  'Do not read the table. Point at it, then tell one story from your own work.\n\n' +
  'SAY: "This is the part I care most about.\n\n' +
  'Naming and classifying a model used to be element by element. Producing a drawing set meant ' +
  'setting up every sheet. A schedule of quantities was counted, typed, and out of date by the time ' +
  'it was issued. Finding what was wrong meant reading drawings and hoping. Handover information got ' +
  'assembled at the end, from memory.\n\n' +
  'Every one of those is now a pass that runs while you make tea."\n\n' +
  'THEN TELL ONE REAL STORY. A specific job, a specific evening, how long it took then and how long ' +
  'it takes now. One anecdote from your own projects will do more than the whole table. Architects ' +
  'believe other architects about late nights, and this is the moment the room stops evaluating and ' +
  'starts recognising.'
);

/* ------------------------------------------------ 6 MULTI-HOST */
s = pres.addSlide();
kicker(s, 'THE PLATFORM');
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
  'A full add-in inside Revit, which is the deepest integration. ArchiCAD works by saving to IFC, ' +
  'and changes come back the other way. Tekla arrives the same way, so the structural engineer is on ' +
  'the same project. And there is a free open-source route through Blender and Bonsai that costs ' +
  'nothing at all.\n\n' +
  'The line that matters is the last one. Every element keeps the same identity across all four. I ' +
  'have tested that with a single element resolving across all of them at once.\n\n' +
  'It is also why I would say this belongs to the profession rather than to one company. A tool tied ' +
  'to a single supplier should probably not be anybody\'s national standard."\n\n' +
  'BE EXACT IF ASKED: Revit is a native add-in, the others go through IFC, and there is no native ' +
  'ArchiCAD or Tekla plug-in. The precision is what makes the claim believable.'
);

/* ------------------------------------------------ MEETINGS */
s = pres.addSlide();
kicker(s, 'THE PART I AM PROUDEST OF');
title(s, 'Meet inside the model, not over it');
s.addText('A coordination meeting where nobody is screen-sharing. Everyone is in the model, and ' +
  'the presenter can bring the whole room to what they are looking at.',
  { x: M, y: 2.05, w: 11.7, h: 0.75, fontSize: 16, italic: true, color: ACC2, fontFace: H,
    lineSpacing: 22, isTextBox: true, margin: 0 });
[
  ['Camera and voice', 'A live meeting in the browser or on a phone. No third app to join, no separate link.'],
  ['Follow the presenter', 'The host isolates, colours or sections something and everyone else\'s view moves with them.'],
  ['It keeps its own minutes', 'Minutes, action items and attendance held against the meeting rather than in somebody\'s notebook.'],
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
  'This is the slide that separates you from every other BIM tool the room has seen, and from Zoom. ' +
  'Give it time.\n\n' +
  'SAY: "This is the part I am proudest of, and I have not seen it anywhere else.\n\n' +
  'We all know what a coordination meeting looks like. Somebody shares their screen, everybody else ' +
  'squints at it, and half the room is looking at a drawing they cannot navigate.\n\n' +
  'Here, nobody screen-shares. There is a live meeting with camera and voice, in the browser or on a ' +
  'phone, and everyone is inside the same model. When the presenter isolates something, or colours ' +
  'the model by clash status, or cuts a section, everyone who is following sees their own view move ' +
  'to the same place. They are not watching a video of the model. They are in it.\n\n' +
  'And the meeting keeps itself: minutes, action items, who attended. It can be recorded, so somebody ' +
  'who could not attend can watch it, or you can go back to it when a decision gets questioned six ' +
  'months later.\n\n' +
  'I should be straight with you, as I have been about everything else. All of that is built and ' +
  'running. The last test still on my list is a real two-person meeting with cameras on, and that is ' +
  'a test I cannot do alone."\n\n' +
  'THAT LAST LINE IS AN OPPORTUNITY. If somebody in the room offers to be the second person, accept ' +
  'immediately and fix a date before you leave. It is the cheapest possible way for a Council member ' +
  'to get involved, and it makes them a participant rather than an audience.'
);

/* ------------------------------------------------ VIEWER */
s = pres.addSlide();
kicker(s, 'AND WHILE YOU ARE IN THERE');
title(s, 'What you can do inside the model');
[
  ['Isolate and ghost', 'Pull out what matters and leave the rest faded for context'],
  ['Colour by anything', 'Clash status, issue status, discipline, level, or any parameter'],
  ['Explode', 'Pull an assembly apart to see how it goes together'],
  ['Section and measure', 'Cut through, and take a dimension off the model'],
  ['Mark up in three dimensions', 'Draw and dimension on the model rather than on a screenshot'],
  ['Pin an issue to an element', 'Long-press on a phone and the issue is anchored to that element, at that point'],
  ['See the clashes as pins', 'Filtered by status and type, so a meeting can work through them'],
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
  'You can isolate what matters and ghost the rest so you keep your bearings. Colour the model by ' +
  'anything: clash status, issue status, discipline, level, any parameter you like. Explode an ' +
  'assembly. Cut a section, take a measurement. Mark up in three dimensions rather than drawing on a ' +
  'screenshot. And you can pin an issue to an element, at the point you are looking at, so whoever ' +
  'picks it up later knows exactly what you meant.\n\n' +
  'Clashes come in as pins you can filter by status and type, which is what actually makes a ' +
  'coordination meeting move.\n\n' +
  'It is colour-blind safe, which matters because roughly one man in twelve needs that and almost ' +
  'nobody asks for it.\n\n' +
  'And all of it works on a phone, because that is where site is."'
);

/* ------------------------------------------------ CLIENT */
s = pres.addSlide();
kicker(s, 'AND FOR THE PERSON PAYING');
title(s, 'What a client can check from a phone');
s.addText('Without ringing you on a Saturday.', { x: M, y: 2.05, w: 11.6, h: 0.4, fontSize: 16,
  italic: true, color: ACC2, fontFace: H, isTextBox: true, margin: 0 });
[
  ['What am I being asked to decide?', 'Issues and approvals waiting on them, in one list rather than buried in email.'],
  ['What did I approve, and when?', 'A record of every issue and revision, with dates, so nobody relies on memory.'],
  ['What does it actually look like?', 'The model on their phone. Rotate it, walk it, tap an element and see what it is.'],
  ['What is happening on site?', 'Photographs from the last visit, located, dated, against the right part of the building.'],
  ['Where did we get to on that?', 'The minutes and actions from the meeting, and the recording if there was one.'],
  ['Is anything stuck?', 'Open issues by status, so a question that has been sitting for three weeks is visible.'],
].forEach(function (r, i) {
  const x = M + (i % 2) * 6.4;
  const y = 2.62 + Math.floor(i / 2) * 1.42;
  card(s, x, y, 5.95, 1.2, i % 2 ? TINT : TINT2);
  sq(s, x + 0.34, y + 0.28, ACC, 0.16);
  s.addText(r[0], { x: x + 0.72, y: y + 0.18, w: 5.0, h: 0.36, fontSize: 15, bold: true, color: INK,
    fontFace: H, isTextBox: true, margin: 0 });
  s.addText(r[1], { x: x + 0.72, y: y + 0.55, w: 5.05, h: 0.55, fontSize: 12, color: INK2,
    fontFace: B, lineSpacing: 16, isTextBox: true, margin: 0 });
});
card(s, M, 6.8, CW, 0.5, INK);
s.addText('And it costs them nothing to be there.', { x: M + 0.45, y: 6.8, w: CW - 0.9, h: 0.5,
  fontSize: 15, bold: true, color: SALMON, fontFace: H, valign: 'middle', isTextBox: true,
  margin: 0 });
s.addNotes(
  'Architects in the room will recognise every one of these as a phone call they have taken on a ' +
  'weekend. That recognition is the point.\n\n' +
  'SAY: "It is worth saying what this looks like from the other side, because a client with a phone ' +
  'is a large part of why a project runs smoothly or does not.\n\n' +
  'They can see what they are being asked to decide, in one list rather than buried in an email ' +
  'thread. They can see what they approved and when, so nobody has to rely on memory a year later. ' +
  'They can look at the model itself, rotate it, tap an element. They can see photographs from the ' +
  'last site visit, dated and located against the right part of the building. They can pick up where ' +
  'a meeting got to. And they can see whether anything is stuck.\n\n' +
  'And it costs them nothing to be there, which is the part that makes it actually happen. On a ' +
  'per-user platform the client is a licence somebody has to justify, so they never get added, and ' +
  'then they ring you on a Saturday instead."\n\n' +
  'That last sentence usually gets a laugh of recognition. Let it.'
);

/* ------------------------------------------------ 7 DEMO */
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
  'Narrate what you are doing rather than what the software is doing.\n\n' +
  'DO NOT demonstrate the engineering calculations or the conversation layer. Both are honest work ' +
  'in progress.\n\n' +
  'IF SOMETHING FAILS: say so, move on, use the recording. "That is one of the parts still being ' +
  'finished" is recoverable. Fiddling with a laptop in silence is not.'
);

/* ------------------------------------------------ 8 DIFFERENTIATION */
s = pres.addSlide();
kicker(s, 'WHY IT HAD TO BE BUILT HERE');
title(s, 'What global platforms will not do for us');
[
  ['Price per practice, not per person', 'Everyone outside your office joins free. Their business model cannot allow that.'],
  ['Ugandan conditions as defaults', 'Wind, seismic, soil bearing and rainfall by region. A market our size does not justify the work for them.'],
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
s.addText('None of this is them being worse. It is that a market our size does not justify the work ' +
  'for them. It does for us.', { x: M + 0.45, y: 6.5, w: CW - 0.9, h: 0.75, fontSize: 15.5,
    bold: true, color: SALMON, fontFace: H, valign: 'middle', isTextBox: true, margin: 0 });
s.addNotes(
  'THE DIFFERENTIATION SLIDE. Deliver it generously rather than competitively. You are not attacking ' +
  'anybody, you are explaining why a gap exists.\n\n' +
  'SAY: "People sometimes ask why build this at all when the international products exist. This is ' +
  'my honest answer.\n\n' +
  'It is priced per practice rather than per person, and everyone outside your office joins free. ' +
  'The large vendors cannot do that; their whole business model is per seat.\n\n' +
  'Ugandan conditions are in it as defaults. Wind, seismic zone, soil bearing, design rainfall by ' +
  'region. Nobody in California is going to build that.\n\n' +
  'It works with no connection, because that is what our sites are like. It bills in shillings, and ' +
  'mobile money is coming, because most practices here do not have a corporate card. There is a free ' +
  'route through open-source software, which would undercut their own funnel. And if something goes ' +
  'wrong you can reach somebody in this time zone who has worked on a project like yours.\n\n' +
  'None of that is them being worse than us. It is that a market our size does not justify the work ' +
  'for them. It does for us, because it is the only market we have."\n\n' +
  'DO NOT name competitors and do not compare prices unprompted. The structural argument is stronger ' +
  'than a price fight, and it survives any discount they might offer.'
);

/* ------------------------------------------------ 9 STANDARDS */
s = pres.addSlide();
kicker(s, 'THE OTHER HALF');
title(s, 'And the standard is already drafted');
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
s.addText('It is a draft, not a proposal. If the profession ever wanted a standard of its own, this ' +
  'is a starting point rather than a blank page.', { x: M, y: 6.35, w: CW, h: 0.45, fontSize: 14.5,
    bold: true, color: ACC2, fontFace: B, italic: true, isTextBox: true, margin: 0 });
s.addNotes(
  'This is the half most tools do not offer, and for a Council it is the more interesting half. Offer ' +
  'it lightly and do not push it.\n\n' +
  'SAY: "The other half is the standards, and this is the part most tools leave out.\n\n' +
  'Publishing a standard is the easy part. Getting anyone to comply with it is where most of them ' +
  'quietly die, because compliance is expensive and nobody can check it. A document on a website ' +
  'changes nothing about what actually arrives in an inbox.\n\n' +
  'So what is in here is a working draft: naming and classification applied automatically rather ' +
  'than typed, drawing types and title blocks so a set behaves the same way every time, what level ' +
  'of detail is expected at each stage, what a handover has to contain, and a check that runs ' +
  'against all of it and tells you what fails and where.\n\n' +
  'I want to be careful how I put this. It is a draft I built because I needed one, not a proposal I ' +
  'am putting to you. But if the profession ever wanted a standard of its own, it is a starting ' +
  'point rather than a blank page."\n\n' +
  'THEN LEAVE IT THERE. If the Council picks it up, it becomes their idea, which is the only way it ' +
  'would ever actually happen. If they do not, nothing is lost.'
);

/* ------------------------------------------------ 10 COPILOT */
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
  'The slide that will make the technical people sit forward. Be accurate; overselling this is what ' +
  'would cost you their respect.\n\n' +
  'SAY: "The last thing on the platform is not finished, and I am showing it anyway because I think ' +
  'it is where all of this is going.\n\n' +
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
  'DO NOT DEMONSTRATE THIS LIVE. The conversational turn is untested. Lead with the safety design ' +
  'rather than the clever part; that is the difference between a demo and an engineering decision.\n\n' +
  'CHECK THE OPERATION COUNT before quoting a number.'
);

/* ------------------------------------------------ 11 WHERE IT STANDS */
s = pres.addSlide();
kicker(s, 'BEING STRAIGHT WITH YOU');
title(s, 'Where it stands today');
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
  'SAY: "Let me be straight with you about how far along it is.\n\n' +
  'Working now, in daily use on live projects: the shared record, document control, issues, mobile ' +
  'offline, model checking, and the drawings, schedules, quantities and handover data.\n\n' +
  'Being finished: serving many practices from one system, payments, hosting sized for the region, ' +
  'an independent security review, and the conversation layer I just showed you.\n\n' +
  'And not yet validated: the engineering calculation engines. Complete and tested, but never through ' +
  'independent professional validation, so I only offer them as commissioned work with manual checks ' +
  'alongside. I would rather say that here than have an engineer discover it.\n\n' +
  'One line: ready for one practice today, not ready to serve the whole profession at once. That gap ' +
  'is the work that is left."'
);

/* ------------------------------------------------ ROLES */
s = pres.addSlide();
kicker(s, 'BEFORE THE PRICES MAKE SENSE');
title(s, 'Who you pay for, and who is free');
[
  ['Authors', 'The people who model', 'They need their own modelling software, which I do not sell. StingTools sits inside whatever they already use.', ACC],
  ['Coordinators', 'The people who run the job', 'Named seats on the platform. Checking, issues, documents, meetings, quantities. This is the part a practice pays for.', ACC],
  ['Everyone else', 'Client, contractor, QS, consultants', 'Unlimited, and free. They can view, comment, raise issues and join meetings without anybody buying them a licence.', SLATE],
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
s.addText('A four-person practice usually pays for two or three coordinators, and the rest of the ' +
  'project team costs nothing.', { x: M + 0.45, y: 6.0, w: CW - 0.9, h: 0.95, fontSize: 16.5,
    bold: true, color: PAPER, fontFace: H, valign: 'middle', isTextBox: true, margin: 0 });
s.addNotes(
  'Put this before the prices or the prices will not make sense. It is also the moment the room ' +
  'realises the economics are different rather than merely cheaper.\n\n' +
  'SAY: "Before I show you prices, it is worth explaining who actually gets counted, because it is ' +
  'not what people expect.\n\n' +
  'There are authors: the people who model. They need their own modelling software, which I do not ' +
  'sell and am not trying to replace. The tools sit inside whatever they already use.\n\n' +
  'There are coordinators: the people who actually run the job. Checking, issues, documents, ' +
  'meetings, quantities. Those are named seats, and that is the part a practice pays for.\n\n' +
  'And then there is everyone else. The client, the contractor, the quantity surveyor, the other ' +
  'consultants. Unlimited, and free. They can view, comment, raise issues and join meetings without ' +
  'anybody having to buy them a licence.\n\n' +
  'So a four-person practice usually pays for two or three coordinators, and the rest of the project ' +
  'team costs nothing at all. That is a different shape from paying per head, and it is the reason ' +
  'the whole team actually ends up on the platform rather than just the two people whose licences ' +
  'somebody could justify."'
);

/* ------------------------------------------------ 12 PRICE */
s = pres.addSlide();
kicker(s, 'WHAT IT COSTS TO USE');
title(s, 'Priced so that BIM can be normal');
s.addText('In my experience most practices here do not avoid BIM because they disagree with it. ' +
  'They avoid it because of what it costs to do properly.', { x: M, y: 2.05, w: 11.6, h: 0.85,
    fontSize: 17, bold: true, color: INK, fontFace: H, lineSpacing: 24, isTextBox: true, margin: 0 });
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
  'This is the adoption argument, and it is the one most likely to change what members actually do.\n\n' +
  'SAY: "A word about what it costs to use, because I think this is the real barrier.\n\n' +
  'In my experience most practices here do not avoid BIM because they disagree with it. They avoid ' +
  'it because of what it costs to do properly, and because it feels like something for big jobs.\n\n' +
  'So it is priced per practice rather than per person. Everyone outside your office joins free, ' +
  'which matters because the usual reason coordination software dies on a project is that nobody ' +
  'will pay for the other parties to be on it. It is billed in shillings. And there is a free route ' +
  'for anyone with no software budget at all.\n\n' +
  'Twenty-five dollars a month for the modelling tools on their own. Sixty for everything up to ' +
  'three people. A hundred and thirty for a practice of four to ten.\n\n' +
  'I am not claiming it does everything the international products do. I am saying it does what a ' +
  'Ugandan practice needs every week, at a price that lets BIM be normal rather than exceptional."\n\n' +
  'CHECK THE PRICES ARE CURRENT before saying them.'
);

/* ------------------------------------------------ 13 COST TO FINISH */
s = pres.addSlide();
kicker(s, 'WHAT IS LEFT');
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
  'State it as a fact, not a request. You are reporting what remains, not asking anyone to cover it.\n\n' +
  'SAY: "And so you have the whole picture, here is what finishing it would take.\n\n' +
  'Thirty-six thousand two hundred dollars to complete the platform. Twelve thousand for a year of ' +
  'hosting sized for regional use. Fifteen thousand for three training groups of about twenty-five ' +
  'people each. With contingency, about seventy-two and a half thousand dollars.\n\n' +
  'The three are separable. The training in particular stands on its own and could start first."\n\n' +
  'THEN STOP. Do not follow it with an ask. Somebody will very likely say "and how are you funding ' +
  'that?" — that question landing from them is far stronger than you raising it. When it comes: "I am ' +
  'looking at several routes. Some a professional body can open and an individual cannot, so I would ' +
  'value your thinking." Then stop again and let them offer, or not.'
);

/* ------------------------------------------------ 14 WHERE BIM IS GOING */
s = pres.addSlide();
kicker(s, 'A VIEW, FOR YOUR COMMENT');
title(s, 'Where I think this is all heading');
[
  ['From documents to requirements that check themselves',
   'The open standard for this arrived in 2024. A client issues machine-readable requirements and a model is checked against them, rather than a written specification nobody verifies.'],
  ['From vendor formats to open ones',
   'The open model format became an ISO standard in 2024 and now covers infrastructure and georeferencing. Increasingly it is what clients ask to be handed.'],
  ['From automation to assistance',
   'Research on AI working directly with models has moved very fast. The expectation is that practices run their own assistants well before 2030.'],
  ['From project information to asset information',
   'The value is moving past handover into operation and maintenance, and in time into city-scale data.'],
].forEach(function (r, i) {
  const y = 2.15 + i * 1.18;
  card(s, M, y, CW, 1.02, i % 2 ? TINT : TINT2);
  numTile(s, M + 0.35, y + 0.28, String(i + 1), i === 2 ? ACC : INK);
  s.addText(r[0], { x: M + 1.05, y: y + 0.15, w: 4.4, h: 0.75, fontSize: 14, bold: true, color: INK,
    fontFace: H, lineSpacing: 18, isTextBox: true, margin: 0 });
  s.addText(r[1], { x: M + 5.75, y: y + 0.17, w: 6.25, h: 0.75, fontSize: 11.5, color: INK2,
    fontFace: B, lineSpacing: 15, isTextBox: true, margin: 0 });
});
s.addText('That is my reading of it. I would genuinely like to know whether the Council sees it the ' +
  'same way.', { x: M, y: 6.95, w: CW, h: 0.4, fontSize: 13.5, color: MUTED, fontFace: B,
    italic: true, isTextBox: true, margin: 0 });
s.addNotes(
  'This makes you a colleague with a view rather than someone selling. Deliver it as an opinion open ' +
  'to correction.\n\n' +
  'SAY: "I want to put a view to you and hear whether you agree, because between you you will have ' +
  'seen more of this than I have.\n\n' +
  'Requirements are becoming machine-readable, and the open standard for that arrived in 2024. Open ' +
  'formats are winning, and the open model format became an ISO standard the same year. AI working ' +
  'directly with models has moved very fast, and the expectation is that practices run their own ' +
  'assistants well before 2030. And the value is moving past handover into operation and ' +
  'maintenance.\n\n' +
  'That is my reading. I would like to know whether the Council sees it the same way, because if I ' +
  'am wrong about the direction I would rather find out now than in three years."\n\n' +
  'THEN PAUSE AND LET THEM TALK. This is where the meeting turns into a discussion.'
);

/* ------------------------------------------------ OUTWARD */
s = pres.addSlide();
kicker(s, 'IF IT WORKS HERE');
title(s, 'How something like this actually spreads');
[
  ['Members use it', 'A handful of practices put it on real jobs because it fits their work and their budget.'],
  ['The Society recognises it', 'The naming and conventions become how members deliver, with the Council having a hand in shaping them.'],
  ['Clients start expecting it', 'Public bodies and larger clients ask for what our members can already produce.'],
  ['The region follows', 'Kenya, Rwanda and Tanzania have the same conditions, the same gap, and no local platform either.'],
].forEach(function (r, i) {
  const y = 2.15 + i * 1.1;
  card(s, M, y, CW, 0.96, i === 3 ? TINT2 : TINT);
  numTile(s, M + 0.35, y + 0.27, String(i + 1), i === 3 ? ACC : INK);
  s.addText(r[0], { x: M + 1.05, y: y + 0.15, w: 4.2, h: 0.42, fontSize: 16.5, bold: true,
    color: INK, fontFace: H, isTextBox: true, margin: 0 });
  s.addText(r[1], { x: M + 5.5, y: y + 0.16, w: 6.5, h: 0.68, fontSize: 12.5, color: INK2,
    fontFace: B, lineSpacing: 16, isTextBox: true, margin: 0 });
});
card(s, M, 6.6, CW, 0.72, INK);
s.addText('None of that happens by announcement. It happens one practice at a time.',
  { x: M + 0.45, y: 6.6, w: CW - 0.9, h: 0.72, fontSize: 15.5, bold: true, color: SALMON,
    fontFace: H, valign: 'middle', isTextBox: true, margin: 0 });
s.addNotes(
  'Ambition, delivered modestly. The last line is what stops it sounding grandiose, so do not drop it.\n\n' +
  'SAY: "One more thought, and then I will stop.\n\n' +
  'If something like this does work, I do not think it spreads by anybody announcing it.\n\n' +
  'It starts with a handful of members putting it on real jobs, because it fits their work and their ' +
  'budget. If that goes well, the naming and the conventions gradually become how members deliver, ' +
  'and at that point the Council would want a hand in shaping them, which is as it should be. Then ' +
  'clients start asking for what our members can already produce, rather than the other way round.\n\n' +
  'And beyond that, honestly, the region. Kenya, Rwanda and Tanzania have the same conditions we do, ' +
  'the same gap, and no local platform either. But that is a long way off and I would rather earn it ' +
  'here first.\n\n' +
  'None of that happens by announcement. It happens one practice at a time."\n\n' +
  'DO NOT OVERSELL THE REGIONAL PART. Naming it is enough; arguing for it makes you sound like ' +
  'somebody with a business plan rather than a member with a view.'
);

/* ------------------------------------------------ 15 HOW CLOSE */
s = pres.addSlide();
darkBg(s);
kicker(s, 'THE THING I MOST WANT TO LEAVE YOU WITH', ACC);
title(s, 'Adoption is closer than it looks', PAPER);
[
  ['The tools work', 'Not a prototype. In daily use on live projects.'],
  ['The price fits', 'A three-person practice can afford it, and there is a free route.'],
  ['It works with what you own', 'Revit, ArchiCAD, Tekla, or nothing at all.'],
  ['The standard is drafted', 'Not from scratch. From something that already runs.'],
].forEach(function (r, i) {
  const y = 2.3 + i * 0.95;
  numTile(s, M, y, String(i + 1), i === 3 ? ACC : '3A4048');
  s.addText(r[0], { x: M + 0.85, y: y - 0.02, w: 4.6, h: 0.45, fontSize: 18, bold: true,
    color: PAPER, fontFace: H, isTextBox: true, margin: 0 });
  s.addText(r[1], { x: M + 5.6, y: y - 0.01, w: 6.4, h: 0.5, fontSize: 13.5, color: LIGHTTXT,
    fontFace: B, isTextBox: true, margin: 0 });
});
card(s, M, 6.3, CW, 0.85, '2E333A');
s.addText('BIM adoption here has been treated as a five-year problem. I think it is closer to a ' +
  'decision.', { x: M + 0.45, y: 6.3, w: CW - 0.9, h: 0.85, fontSize: 16.5, bold: true,
    color: SALMON, fontFace: H, valign: 'middle', isTextBox: true, margin: 0 });
s.addNotes(
  'THE EMOTIONAL PAYOFF. Slow down. This is the sentence you want them repeating to people who were ' +
  'not in the room.\n\n' +
  'SAY: "If there is one thing I would like to leave you with, it is this.\n\n' +
  'We have talked about BIM adoption in this country as though it were a five-year problem. Skills, ' +
  'cost, software, standards, all of it a long way off.\n\n' +
  'But look at where the pieces actually are. The tools work, and not as a prototype: they are in ' +
  'daily use on live projects. The price fits, so a three-person practice can afford it, and there ' +
  'is a free route for anyone who cannot. It works with whatever people already own, or with nothing ' +
  'at all. And a standard is drafted, not from scratch, but from something that already runs.\n\n' +
  'I am not saying it is finished, and I have been honest with you about what is not. But I think ' +
  'adoption here is closer to a decision than it is to a five-year problem, and that is not ' +
  'something I could have said two years ago."\n\n' +
  'PAUSE HERE before the last slide. Let it sit.'
);

/* ------------------------------------------------ 16 STEER */
s = pres.addSlide();
kicker(s, 'WHAT I WOULD VALUE FROM YOU');
title(s, 'Four things I would like your thinking on');
[
  ['Is the direction right?', 'Have I read where this is going correctly, or am I missing something you can see from where you sit?'],
  ['What would members actually use?', 'I have built what I needed on my own projects. You know the membership far better than I do.'],
  ['Would training be useful?', 'If it would, what shape should it take, and who should shape the content?'],
  ['Who else should see this?', 'Inside the Society, or outside it.'],
].forEach(function (a, i) {
  const y = 2.25 + i * 1.12;
  card(s, M, y, CW, 0.95, i % 2 ? TINT : TINT2);
  numTile(s, M + 0.35, y + 0.25, String(i + 1), i === 0 ? ACC : INK);
  s.addText(a[0], { x: M + 1.05, y: y + 0.15, w: 4.3, h: 0.42, fontSize: 17, bold: true, color: INK,
    fontFace: H, isTextBox: true, margin: 0 });
  s.addText(a[1], { x: M + 5.6, y: y + 0.16, w: 6.4, h: 0.65, fontSize: 12.5, color: INK2,
    fontFace: B, lineSpacing: 16, isTextBox: true, margin: 0 });
});
s.addText('I am not asking the Council to decide anything today. I would rather have your thinking ' +
  'than a decision.', { x: M, y: 6.85, w: CW, h: 0.45, fontSize: 15, bold: true, color: ACC2,
    fontFace: H, italic: true, isTextBox: true, margin: 0 });
s.addNotes(
  'Questions invite people in; requests make them defensive. This is a stronger close than an ask.\n\n' +
  'SAY: "I will stop there, because what I came for is your thinking rather than a decision.\n\n' +
  'Have I read the direction correctly, or am I missing something you can see from where you sit?\n\n' +
  'What would members actually use? I have built what I needed on my own projects, and you know the ' +
  'membership far better than I do.\n\n' +
  'Would training be useful, and if so what shape should it take and who should shape the content?\n\n' +
  'And who else should see this, inside the Society or outside it?\n\n' +
  'I am not asking the Council to decide anything today. I would rather have your thinking than a ' +
  'decision. Thank you for the time."\n\n' +
  'THEN BE QUIET AND LET THEM TALK.\n\n' +
  'BEFORE THEY DISPERSE write down three things: anything they suggested you build or change, anyone ' +
  'they said you should talk to, and whether they want to see it again. All three come from them ' +
  'rather than from you, which is what makes them worth having.'
);

/* ------------------------------------------------ 17 CLOSE */
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
