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

const H = 'Cambria';
const B = 'Calibri';

const M = 0.65;
const W = 13.333;
const HT = 7.5;
const CW = W - M * 2;

const pres = new pptxgen();
pres.layout = 'LAYOUT_WIDE';
pres.author = 'Davis Mayanja';
pres.company = 'PlanScape';
pres.title = 'BIM for Ugandan Practice';

function sq(s, x, y, color, size) {
  s.addShape(pres.ShapeType.rect, {
    x: x, y: y, w: size || 0.16, h: size || 0.16, fill: { color: color },
  });
}

function motif(s, x, y, color) {
  sq(s, x, y, color, 0.15);
  sq(s, x + 0.22, y, color, 0.15);
  sq(s, x + 0.44, y, color, 0.15);
}

function darkBg(s) {
  s.background = { color: INK };
}

function kicker(s, text, color) {
  s.addText(text, {
    x: M, y: 0.5, w: CW, h: 0.3, fontSize: 12, bold: true, charSpacing: 2.5,
    color: color || ACC, fontFace: B, isTextBox: true, margin: 0,
  });
}

function title(s, text, color, y, size) {
  s.addText(text, {
    x: M, y: y === undefined ? 0.88 : y, w: CW, h: 1.0,
    fontSize: size || 38, bold: true, color: color || INK, fontFace: H,
    isTextBox: true, margin: 0, valign: 'top',
  });
}

function card(s, x, y, w, h, fill) {
  s.addShape(pres.ShapeType.rect, {
    x: x, y: y, w: w, h: h, fill: { color: fill || TINT },
    shadow: { type: 'outer', angle: 90, offset: 2, blur: 8, color: 'BFB9B0', opacity: 0.45 },
  });
}

function numTile(s, x, y, n, color, txtcolor) {
  s.addShape(pres.ShapeType.rect, {
    x: x, y: y, w: 0.46, h: 0.46, fill: { color: color || ACC },
  });
  s.addText(n, {
    x: x, y: y, w: 0.46, h: 0.46, fontSize: 18, bold: true,
    color: txtcolor || 'FFFFFF', fontFace: H, align: 'center', valign: 'middle',
    isTextBox: true, margin: 0,
  });
}

/* ---------------------------------------------------------- 1 TITLE */
let s = pres.addSlide();
darkBg(s);
motif(s, M, 1.55, ACC);
s.addText('PRESENTATION TO THE COUNCIL', {
  x: M, y: 2.0, w: CW, h: 0.32, fontSize: 12.5, bold: true, charSpacing: 3,
  color: ACC, fontFace: B, isTextBox: true, margin: 0,
});
s.addText('Building Information Modelling\nfor Ugandan practice', {
  x: M, y: 2.45, w: 10.6, h: 1.9, fontSize: 44, bold: true, color: PAPER,
  fontFace: H, lineSpacing: 50, isTextBox: true, margin: 0,
});
s.addText('PlanScape and StingTools — a BIM platform built in Kampala', {
  x: M, y: 4.45, w: 10.6, h: 0.4, fontSize: 17, color: LIGHTTXT,
  fontFace: B, italic: true, isTextBox: true, margin: 0,
});
s.addText('Davis Mayanja', {
  x: M, y: 5.5, w: 6, h: 0.3, fontSize: 14.5, bold: true, color: PAPER,
  fontFace: B, isTextBox: true, margin: 0,
});
s.addText('Uganda Society of Architects  ·  September 2026', {
  x: M, y: 5.85, w: 7, h: 0.3, fontSize: 12.5, color: MUTED,
  fontFace: B, isTextBox: true, margin: 0,
});
s.addNotes(
  'Hold here until everyone is settled. Do not start talking over people sitting down.\n\n' +
  'SAY: "Mr President, Chairman, thank you for making the time. My name is Davis Mayanja. ' +
  'I am an architect-side BIM specialist, and for the last few years I have been building ' +
  'software in Kampala for the way we actually work here.\n\n' +
  'I have twenty minutes of material and I would much rather spend forty on your questions, ' +
  'so I will move quickly."\n\n' +
  'Then go straight to the next slide. Do not warm up. This room is senior and short on time.'
);

/* ---------------------------------------------------------- 2 DECLARATION */
s = pres.addSlide();
kicker(s, 'BEFORE I BEGIN');
title(s, 'Two things to put on the table first');

card(s, M, 2.15, 5.95, 3.5);
s.addText('A declaration', {
  x: M + 0.4, y: 2.5, w: 5.15, h: 0.4, fontSize: 21, bold: true, color: ACC2,
  fontFace: H, isTextBox: true, margin: 0,
});
s.addText(
  'The President and I have an existing professional relationship. ' +
  'It is declared in the proposal you have, and I am declaring it here.\n\n' +
  'This decision should rest with the Council, on the merits, and through whatever ' +
  'process the constitution requires — not with any one office bearer.',
  {
    x: M + 0.4, y: 3.05, w: 5.15, h: 2.3, fontSize: 14, color: INK2,
    fontFace: B, lineSpacing: 21, isTextBox: true, margin: 0,
  }
);

card(s, M + 6.4, 2.15, 5.95, 3.5, INK);
s.addText('And what I am not asking for', {
  x: M + 6.8, y: 2.5, w: 5.15, h: 0.4, fontSize: 21, bold: true, color: 'E8A183',
  fontFace: H, isTextBox: true, margin: 0,
});
const nots = [
  'I am not asking the Society to borrow money.',
  'I am not asking the Society for money from its own funds.',
  'I am not asking for a decision on any agreement today.',
];
nots.forEach(function (t, i) {
  sq(s, M + 6.8, 3.2 + i * 0.72, ACC, 0.13);
  s.addText(t, {
    x: M + 7.1, y: 3.08 + i * 0.72, w: 4.9, h: 0.6, fontSize: 14.5, color: LIGHTTXT,
    fontFace: B, lineSpacing: 20, isTextBox: true, margin: 0,
  });
});

s.addText(
  'I explored a bank facility with the Society as borrower. I have set that aside — ' +
  'I would rather not ask a professional body to carry debt for something it has not yet evaluated.',
  {
    x: M, y: 5.95, w: CW, h: 0.6, fontSize: 14, color: MUTED, fontFace: B,
    italic: true, isTextBox: true, margin: 0,
  }
);
s.addNotes(
  'This slide is the most important two minutes of the meeting. Deliver it slowly and do not rush past it.\n\n' +
  'SAY: "Two things before anything else.\n\n' +
  'First, a declaration. The President and I have worked together professionally. ' +
  'That is in the written proposal and I am saying it out loud here, because I would rather you heard ' +
  'it from me. My view is that this decision belongs with the Council on its merits, and it should go ' +
  'through whatever process your constitution requires. If it succeeds because of a relationship rather ' +
  'than because it is any good, that does not help me and it certainly does not help you.\n\n' +
  'Second — and I want to be very clear because one of the documents you have says otherwise. ' +
  'I sent a proposal built around a bank loan with the Society as the borrower. I have set that aside. ' +
  'I am not asking you to borrow. I am not asking you for money out of your own funds. And I am not ' +
  'asking you to sign anything today."\n\n' +
  'PAUSE HERE. Let it land. The room will relax, and everything after this is heard differently.'
);

/* ---------------------------------------------------------- 3 AGENDA */
s = pres.addSlide();
kicker(s, 'WHAT I WOULD LIKE TO COVER');
title(s, 'Three things, then your questions');

const agenda = [
  ['What has changed', 'How buildings are documented has moved on. What that means for the people in your membership.'],
  ['What I have built', 'A demonstration, and an honest account of what is finished and what is not.'],
  ['What the Society gains', 'Where this is useful to you as an institution — and the one thing I am here to ask.'],
];
agenda.forEach(function (a, i) {
  const y = 2.3 + i * 1.35;
  numTile(s, M, y, String(i + 1), i === 2 ? ACC : INK);
  s.addText(a[0], {
    x: M + 0.85, y: y - 0.06, w: 4.0, h: 0.45, fontSize: 21, bold: true,
    color: INK, fontFace: H, isTextBox: true, margin: 0,
  });
  s.addText(a[1], {
    x: M + 5.0, y: y - 0.04, w: 7.0, h: 0.9, fontSize: 14.5, color: INK2,
    fontFace: B, lineSpacing: 21, isTextBox: true, margin: 0,
  });
});
s.addText('I will keep to twenty minutes.', {
  x: M, y: 6.45, w: CW, h: 0.4, fontSize: 14, color: MUTED, fontFace: B,
  italic: true, isTextBox: true, margin: 0,
});
s.addNotes(
  'Move through this quickly — twenty seconds. It is a signpost, not content.\n\n' +
  'SAY: "Three things. What has changed in how buildings get documented, and what that means for ' +
  'your members. What I have built — and I will show you rather than describe it. And where this is ' +
  'actually useful to the Society as an institution, which is the part I care most about your view on.\n\n' +
  'I will keep to twenty minutes."'
);

/* ---------------------------------------------------------- 4 STANDARD CHANGED */
s = pres.addSlide();
kicker(s, 'ONE  ·  WHAT HAS CHANGED');
title(s, 'The standard for project information has moved');

s.addText('ISO 19650', {
  x: M, y: 2.15, w: 5.4, h: 0.85, fontSize: 52, bold: true, color: ACC,
  fontFace: H, isTextBox: true, margin: 0,
});
s.addText(
  'is now the accepted international standard for managing information on a building project — ' +
  'how it is named, versioned, approved and handed over.',
  {
    x: M, y: 3.05, w: 5.6, h: 1.6, fontSize: 15, color: INK2, fontFace: B,
    lineSpacing: 22, isTextBox: true, margin: 0,
  }
);

s.addText('And governments have started requiring it', {
  x: M + 6.4, y: 2.15, w: 5.95, h: 0.4, fontSize: 17, bold: true, color: INK,
  fontFace: H, isTextBox: true, margin: 0,
});
const countries = ['United Kingdom', 'United Arab Emirates', 'Singapore', 'Germany'];
countries.forEach(function (c, i) {
  const x = M + 6.4 + (i % 2) * 3.05;
  const y = 2.75 + Math.floor(i / 2) * 1.15;
  card(s, x, y, 2.85, 0.92, TINT);
  s.addText(c, {
    x: x + 0.25, y: y + 0.16, w: 2.4, h: 0.6, fontSize: 14.5, bold: true,
    color: INK, fontFace: B, valign: 'middle', isTextBox: true, margin: 0,
  });
});
s.addText('BIM is mandated on public projects in each of these.', {
  x: M + 6.4, y: 5.1, w: 5.95, h: 0.4, fontSize: 13, color: MUTED, fontFace: B,
  italic: true, isTextBox: true, margin: 0,
});

card(s, M, 5.55, CW, 1.05, INK);
s.addText(
  'This has only ever moved in one direction. No country that has adopted it has gone back.',
  {
    x: M + 0.45, y: 5.55, w: CW - 0.9, h: 1.05, fontSize: 17, color: PAPER,
    fontFace: H, italic: true, valign: 'middle', isTextBox: true, margin: 0,
  }
);
s.addNotes(
  'SAY: "The way a set of building information is expected to be put together has changed, and it ' +
  'changed outside Uganda first.\n\n' +
  'ISO 19650 is now the accepted international standard for managing project information — how it is ' +
  'named, how it is versioned, who approved what and when, and what condition it is handed over in. ' +
  'It is not a modelling standard. It is an information standard, which is why it matters to architects ' +
  'rather than only to software people.\n\n' +
  'And governments have started to require it. The United Kingdom, the Emirates, Singapore, Germany — ' +
  'BIM is mandated on public work in all of them.\n\n' +
  'I want to be careful here: I am not telling you Uganda has mandated anything. It has not. What I am ' +
  'telling you is the direction of travel, and it has only ever gone one way."\n\n' +
  'DO NOT overstate this. If you claim a Ugandan mandate exists, someone will check, and you lose the room.'
);

/* ---------------------------------------------------------- 5 REGION BEHIND */
s = pres.addSlide();
kicker(s, 'ONE  ·  WHAT HAS CHANGED');
title(s, 'The region has not kept pace');

card(s, M, 2.15, 6.0, 2.5, TINT);
s.addText('Kenya', {
  x: M + 0.4, y: 2.45, w: 5.2, h: 0.4, fontSize: 20, bold: true, color: ACC2,
  fontFace: H, isTextBox: true, margin: 0,
});
s.addText(
  'Peer economy, larger construction sector. Published research finds BIM adoption ' +
  'still lagging — and names the consequence as poor coordination of information ' +
  'between the parties on a project.',
  {
    x: M + 0.4, y: 2.95, w: 5.2, h: 1.5, fontSize: 14, color: INK2, fontFace: B,
    lineSpacing: 21, isTextBox: true, margin: 0,
  }
);

card(s, M + 6.4, 2.15, 6.0, 2.5, TINT);
s.addText('Uganda', {
  x: M + 6.8, y: 2.45, w: 5.2, h: 0.4, fontSize: 20, bold: true, color: ACC2,
  fontFace: H, isTextBox: true, margin: 0,
});
s.addText(
  'No national information standard for architectural delivery. No locally built, ' +
  'locally supported tooling. Every option on the market is priced in dollars ' +
  'and assumes a connection.',
  {
    x: M + 6.8, y: 2.95, w: 5.2, h: 1.5, fontSize: 14, color: INK2, fontFace: B,
    lineSpacing: 21, isTextBox: true, margin: 0,
  }
);

s.addText(
  'That is a gap. It is also an opening — because the body that moves first gets to ' +
  'write the standard rather than inherit somebody else’s.',
  {
    x: M, y: 5.1, w: 11.4, h: 1.2, fontSize: 21, bold: true, color: INK,
    fontFace: H, lineSpacing: 32, isTextBox: true, margin: 0,
  }
);
motif(s, M, 6.5, ACC);
s.addNotes(
  'SAY: "The region has not kept pace with that.\n\n' +
  'Kenya is the obvious comparison — bigger construction sector, same regional market. Published ' +
  'research on Kenyan adoption finds it still lagging, and it names the consequence: poor coordination ' +
  'of information between the parties on a project. That is a polite way of describing exactly what we ' +
  'all recognise. Drawings that disagree. Schedules that do not match the model. Handover information ' +
  'assembled at the last minute.\n\n' +
  'Uganda is in the same position, with two additional problems. We have no national standard for how ' +
  'architectural information is delivered. And there is nothing on the market built here — everything ' +
  'is priced in dollars and assumes you have a connection.\n\n' +
  'That is a gap. But I would put it to you that it is also an opening. The body that moves first on ' +
  'this gets to write the standard, rather than inherit one written somewhere else for somebody else."'
);

/* ---------------------------------------------------------- 6 COST TO PRACTICE */
s = pres.addSlide();
kicker(s, 'ONE  ·  WHAT HAS CHANGED');
title(s, 'What that costs a practice in your membership');

const risks = [
  ['Not being asked twice', 'Clients and funders increasingly ask how information will be delivered. A practice with no answer stops appearing on shortlists — and never finds out why.'],
  ['Competing on unequal terms', 'International and regional firms bidding for work here already deliver this way. That is the comparison your members are being measured against.'],
  ['Paying for it on site', 'Coordination done by hand is slower and less complete. What it misses does not disappear — it surfaces during construction, where it is most expensive.'],
];
risks.forEach(function (r, i) {
  const x = M + i * 4.0;
  card(s, x, 2.2, 3.7, 3.55);
  numTile(s, x + 0.35, 2.55, String(i + 1), ACC);
  s.addText(r[0], {
    x: x + 0.35, y: 3.2, w: 3.0, h: 0.75, fontSize: 18, bold: true, color: INK,
    fontFace: H, lineSpacing: 23, isTextBox: true, margin: 0,
  });
  s.addText(r[1], {
    x: x + 0.35, y: 4.0, w: 3.0, h: 1.6, fontSize: 13, color: INK2, fontFace: B,
    lineSpacing: 19, isTextBox: true, margin: 0,
  });
});
s.addText(
  'This is why I am in front of the Society rather than in front of individual practices.',
  {
    x: M, y: 6.1, w: CW, h: 0.5, fontSize: 15, color: MUTED, fontFace: B,
    italic: true, isTextBox: true, margin: 0,
  }
);
s.addNotes(
  'SAY: "What does that actually cost a practice in your membership? Three things.\n\n' +
  'First — not being asked twice. Clients and funders increasingly ask how project information will ' +
  'be delivered. A practice that has no answer to that question stops appearing on shortlists, and ' +
  'usually never finds out that is why.\n\n' +
  'Second — competing on unequal terms. The international and regional firms bidding for work here ' +
  'already work this way. That is the standard our members are being compared against, whether or ' +
  'not anyone told them.\n\n' +
  'Third — paying for it on site. Coordination done by hand is slower and less complete. What it ' +
  'misses does not go away. It turns up during construction, which is the most expensive place to ' +
  'find it, and the architect usually gets the blame.\n\n' +
  'That is why I am standing in front of the Society rather than in front of individual practices. ' +
  'A practice can only solve this one firm at a time. A professional body can solve it for the ' +
  'profession."'
);

/* ---------------------------------------------------------- 7 WHAT I BUILT */
s = pres.addSlide();
kicker(s, 'TWO  ·  WHAT I HAVE BUILT');
title(s, 'Two parts, designed as one system');

card(s, M, 2.15, 5.95, 3.9, INK);
sq(s, M + 0.45, 2.55, ACC, 0.22);
s.addText('StingTools', {
  x: M + 0.45, y: 2.95, w: 5.0, h: 0.5, fontSize: 26, bold: true, color: PAPER,
  fontFace: H, isTextBox: true, margin: 0,
});
s.addText('Inside the design software', {
  x: M + 0.45, y: 3.45, w: 5.0, h: 0.3, fontSize: 13, color: 'E8A183',
  fontFace: B, italic: true, isTextBox: true, margin: 0,
});
s.addText(
  'Checks a model against data standards automatically. Keeps naming and ' +
  'classification consistent. Produces drawings, schedules and quantities from ' +
  'the model rather than by hand — and assembles handover information as the ' +
  'project is built, not after it.',
  {
    x: M + 0.45, y: 3.9, w: 5.05, h: 1.9, fontSize: 13.5, color: LIGHTTXT,
    fontFace: B, lineSpacing: 20, isTextBox: true, margin: 0,
  }
);

card(s, M + 6.4, 2.15, 5.95, 3.9, TINT);
sq(s, M + 6.85, 2.55, ACC, 0.22);
s.addText('PlanScape', {
  x: M + 6.85, y: 2.95, w: 5.0, h: 0.5, fontSize: 26, bold: true, color: INK,
  fontFace: H, isTextBox: true, margin: 0,
});
s.addText('The platform around it', {
  x: M + 6.85, y: 3.45, w: 5.0, h: 0.3, fontSize: 13, color: ACC2,
  fontFace: B, italic: true, isTextBox: true, margin: 0,
});
s.addText(
  'One place for project documents, issues and correspondence, so a whole team ' +
  'works from the same record. Priced per organisation rather than per person — ' +
  'and everyone outside your office joins free.',
  {
    x: M + 6.85, y: 3.9, w: 5.05, h: 1.9, fontSize: 13.5, color: INK2,
    fontFace: B, lineSpacing: 20, isTextBox: true, margin: 0,
  }
);

s.addText('Built in Kampala, from 2021, for the way we actually work here.', {
  x: M, y: 6.3, w: CW, h: 0.4, fontSize: 14.5, color: MUTED, fontFace: B,
  italic: true, isTextBox: true, margin: 0,
});
s.addNotes(
  'Keep this short — the demo does the real work. Ninety seconds maximum.\n\n' +
  'SAY: "There are two parts, and they were designed as one system.\n\n' +
  'StingTools sits inside the design software. It checks a model against data standards ' +
  'automatically, keeps naming and classification consistent, and produces drawings, schedules and ' +
  'quantities from the model instead of by hand. It also assembles handover information while the ' +
  'project is being built, rather than in a panic at the end.\n\n' +
  'PlanScape is the platform around it — one place for documents, issues and correspondence so the ' +
  'whole team works from the same record. It is priced per organisation, not per person, and anyone ' +
  'outside your office joins free. I will come back to why that matters for a small practice.\n\n' +
  'Both were started in 2021, here, for the conditions we actually work in."'
);

/* ---------------------------------------------------------- 8 WHERE IT STANDS */
s = pres.addSlide();
kicker(s, 'TWO  ·  WHAT I HAVE BUILT');
title(s, 'Where it stands today — plainly');

const cols = [
  ['In production', ACC, [
    'Document control and the shared project record',
    'Issues and correspondence',
    'The mobile application, working offline',
    'Model checking and audit',
    'Drawings, schedules, quantities, handover data',
  ]],
  ['Still in hand', SLATE, [
    'Serving many organisations from one system',
    'Mobile money payments',
    'Hosting sized for regional use',
    'An independent security review',
  ]],
  ['Not yet validated', MUTED, [
    'The engineering calculation engines',
    'Complete and tested, but never taken through independent professional validation',
    'Offered only as commissioned work, cross-checked by hand',
  ]],
];
cols.forEach(function (c, i) {
  const x = M + i * 4.0;
  sq(s, x, 2.18, c[1], 0.2);
  s.addText(c[0], {
    x: x, y: 2.5, w: 3.7, h: 0.45, fontSize: 19, bold: true, color: INK,
    fontFace: H, isTextBox: true, margin: 0,
  });
  c[2].forEach(function (item, j) {
    s.addText(item, {
      x: x, y: 3.05 + j * 0.58, w: 3.6, h: 0.54, fontSize: 12.5, color: INK2,
      fontFace: B, lineSpacing: 17, isTextBox: true, margin: 0,
    });
  });
});

card(s, M, 6.05, CW, 0.9, INK);
s.addText(
  'Production-ready for one deployment.  Not yet productised for a regional market.',
  {
    x: M + 0.45, y: 6.05, w: CW - 0.9, h: 0.9, fontSize: 18, bold: true,
    color: PAPER, fontFace: H, valign: 'middle', isTextBox: true, margin: 0,
  }
);
s.addNotes(
  'CRITICAL SLIDE. Volunteer every word of this before anyone asks. If the room discovers it instead ' +
  'of being told it, you lose them.\n\n' +
  'SAY: "Let me be plain about where this stands, because you will have read one document from me ' +
  'that says the platform is complete and another that asks for money to finish it. Both were true ' +
  'about different things and I have corrected the wording, but you deserve the answer directly.\n\n' +
  'In production, in daily use on live projects: the document control, the issues, the shared record, ' +
  'the mobile app that works offline, the model checking, the drawings and schedules and quantities ' +
  'and handover data.\n\n' +
  'Still in hand: serving many organisations from one system, payments, hosting sized for regional ' +
  'use, and an independent security review.\n\n' +
  'And not yet validated: the engineering calculation engines. They are complete and tested but they ' +
  'have never been taken through independent professional validation, so I only offer them as ' +
  'commissioned work with manual cross-checks alongside. I would rather say that than have an ' +
  'engineer find it out.\n\n' +
  'The honest summary is one line. It is production-ready for one deployment. It is not yet ' +
  'productised for a regional market. That difference is what the remaining work is."'
);

/* ---------------------------------------------------------- 9 DEMO BREAK */
s = pres.addSlide();
darkBg(s);
motif(s, M, 2.55, ACC);
s.addText('Rather than describe it', {
  x: M, y: 3.0, w: 11.5, h: 0.6, fontSize: 20, color: MUTED, fontFace: B,
  italic: true, isTextBox: true, margin: 0,
});
s.addText('Let me show you.', {
  x: M, y: 3.6, w: 11.5, h: 1.2, fontSize: 54, bold: true, color: PAPER,
  fontFace: H, isTextBox: true, margin: 0,
});
s.addText(
  'A real model  ·  tagged and audited  ·  drawings and schedules out  ·  quantities out',
  {
    x: M, y: 5.0, w: 11.5, h: 0.5, fontSize: 15, color: 'E8A183', fontFace: B,
    isTextBox: true, margin: 0,
  }
);
s.addNotes(
  'THE DEMONSTRATION. Six minutes maximum. Rehearsed twice, end to end, on the build you are actually ' +
  'running — never on something you have only seen work on your own machine.\n\n' +
  'Show architectural work, not engineering. Take a real model, tag it, run the audit, show what it ' +
  'catches, produce a drawing and a schedule, pull a quantity. Those are things every person in the ' +
  'room has done by hand at two in the morning.\n\n' +
  'SAY as you start: "This is a real project, not a sample file."\n\n' +
  'Narrate what you are doing, not what the software is doing. "This would normally take me an ' +
  'afternoon" is worth more than any feature name.\n\n' +
  'IF SOMETHING FAILS: say so, move on, and use the recording. Do not debug in front of the room. ' +
  '"That is the half I told you is still being finished" is a recoverable sentence. Silence while ' +
  'you fiddle is not.'
);

/* ---------------------------------------------------------- 10 CPD */
s = pres.addSlide();
kicker(s, 'THREE  ·  WHAT THE SOCIETY GAINS');
title(s, 'The first one is already your obligation');

s.addText('20', {
  x: M, y: 2.2, w: 2.6, h: 1.7, fontSize: 110, bold: true, color: ACC,
  fontFace: H, isTextBox: true, margin: 0,
});
s.addText(
  'CPD points every practising architect must earn each year to renew a practising licence',
  {
    x: M, y: 3.95, w: 4.6, h: 1.1, fontSize: 15, bold: true, color: INK,
    fontFace: B, lineSpacing: 21, isTextBox: true, margin: 0,
  }
);
s.addText(
  'Architects Registration (Continuing Professional\nDevelopment) Bye Laws, gazetted 2019',
  {
    x: M, y: 5.15, w: 4.6, h: 0.7, fontSize: 12, color: MUTED, fontFace: B,
    italic: true, lineSpacing: 16, isTextBox: true, margin: 0,
  }
);

const cpd = [
  ['It is permanent', 'This obligation does not go away, and it repeats every single year for every member in practice.'],
  ['This content is hard to find here', 'Most CPD available locally is a supplier presentation. Structured, hands-on training in information standards is not on offer.'],
  ['The Society would own it', 'Your programme, your accreditation, your name — delivered by one of your own members rather than an imported trainer.'],
];
cpd.forEach(function (c, i) {
  const y = 2.2 + i * 1.5;
  card(s, M + 5.3, y, 7.1, 1.28, i === 2 ? TINT2 : TINT);
  s.addText(c[0], {
    x: M + 5.65, y: y + 0.18, w: 6.4, h: 0.35, fontSize: 16, bold: true, color: ACC2,
    fontFace: H, isTextBox: true, margin: 0,
  });
  s.addText(c[1], {
    x: M + 5.65, y: y + 0.58, w: 6.4, h: 0.6, fontSize: 13, color: INK2, fontFace: B,
    lineSpacing: 18, isTextBox: true, margin: 0,
  });
});
s.addNotes(
  'This is your strongest argument. Slow down and let the number sit on the screen.\n\n' +
  'SAY: "The first thing the Society gains is not a favour I am doing you. It is something you are ' +
  'already obliged to do.\n\n' +
  'Every practising architect in Uganda needs twenty CPD points a year to renew a practising licence. ' +
  'That is the 2019 bye-laws, not a suggestion. Which means the Society has a permanent, annual ' +
  'obligation to put credible content in front of its members — forever.\n\n' +
  'And this particular content is hard to source here. Most of what is available locally is a supplier ' +
  'presenting a product. Structured, hands-on training in information standards is not really on offer ' +
  'in Kampala.\n\n' +
  'It would be your programme. Your accreditation, your name, delivered by one of your own members ' +
  'rather than someone flown in."\n\n' +
  'THEN ASK — do not assert: "I do not know what ARB accreditation would require for a course like ' +
  'this, or what points it would carry, or what your members currently pay for CPD. You do. That is ' +
  'one of the things I would like your guidance on."\n\n' +
  'Asking makes them co-owners of the idea. Claiming you already know makes you a vendor.'
);

/* ---------------------------------------------------------- 11 STANDARD */
s = pres.addSlide();
kicker(s, 'THREE  ·  WHAT THE SOCIETY GAINS');
title(s, 'A standard the Society could actually enforce');

s.addText(
  'Publishing a standard is the easy part.\nGetting anyone to comply with it is not.',
  {
    x: M, y: 2.2, w: 5.6, h: 1.3, fontSize: 23, bold: true, color: INK,
    fontFace: H, lineSpacing: 34, isTextBox: true, margin: 0,
  }
);
s.addText(
  'Standards die because compliance is expensive and nobody can check it. ' +
  'A document that sits on a website changes nothing about what arrives in an inbox.',
  {
    x: M, y: 3.65, w: 5.6, h: 1.5, fontSize: 14, color: INK2, fontFace: B,
    lineSpacing: 21, isTextBox: true, margin: 0,
  }
);

card(s, M + 6.4, 2.15, 5.95, 3.55, INK);
s.addText('What already ships', {
  x: M + 6.85, y: 2.5, w: 5.1, h: 0.4, fontSize: 19, bold: true, color: 'E8A183',
  fontFace: H, isTextBox: true, margin: 0,
});
const ships = [
  'Naming and classification schemes',
  'Drawing types, title blocks, view templates',
  'An audit that checks a model against them',
  'A report naming exactly what fails, and where',
];
ships.forEach(function (t, i) {
  sq(s, M + 6.85, 3.15 + i * 0.6, ACC, 0.13);
  s.addText(t, {
    x: M + 7.15, y: 3.03 + i * 0.6, w: 4.9, h: 0.5, fontSize: 13.5, color: LIGHTTXT,
    fontFace: B, isTextBox: true, margin: 0,
  });
});
s.addText(
  'A standard that software can check is a standard that gets used.',
  {
    x: M, y: 5.95, w: 11.5, h: 0.7, fontSize: 21, bold: true, color: ACC2,
    fontFace: H, italic: true, isTextBox: true, margin: 0,
  }
);
s.addNotes(
  'Aim this at the Chair of the Board of Practice. Standards and documentation quality are his remit.\n\n' +
  'SAY: "The second thing is a standard.\n\n' +
  'Publishing a standard is the easy part. Getting anyone to comply with it is the hard part, and it ' +
  'is where most of them die — because compliance is expensive and nobody can check it. A document on ' +
  'a website changes nothing about what actually arrives in an inbox.\n\n' +
  'What I would put on the table is different. The naming and classification schemes, the drawing ' +
  'types, the title blocks and templates already exist — and so does an audit that checks a model ' +
  'against them and tells you exactly what fails and where.\n\n' +
  'A standard that software can check is a standard that gets used. If the Society wanted to author ' +
  'a national standard for architectural information, it would be starting from a working ' +
  'implementation rather than a blank page."\n\n' +
  'IF ASKED whether you are proposing the Society adopt your naming scheme: "No. I am proposing it as ' +
  'a first draft for you to argue with. It should carry your name, not mine."'
);

/* ---------------------------------------------------------- 12 SMALL PRACTICE */
s = pres.addSlide();
kicker(s, 'THREE  ·  WHAT THE SOCIETY GAINS');
title(s, 'And it has to work for a four-person practice');

const econ = [
  ['Priced per organisation', 'Not per person. A practice pays once, not once for every member of staff.'],
  ['Everyone outside joins free', 'Client, contractor, quantity surveyor and consultants all join a project at no licence cost to anyone.'],
  ['Billed in shillings', 'Not in dollars, and not exposed to a rate you cannot control.'],
  ['Works with no connection', 'Site records queue on the phone and sync when there is signal again.'],
];
econ.forEach(function (e, i) {
  const x = M + (i % 2) * 6.4;
  const y = 2.2 + Math.floor(i / 2) * 1.75;
  card(s, x, y, 5.95, 1.5);
  sq(s, x + 0.4, y + 0.35, ACC, 0.18);
  s.addText(e[0], {
    x: x + 0.85, y: y + 0.25, w: 4.8, h: 0.38, fontSize: 16.5, bold: true, color: INK,
    fontFace: H, isTextBox: true, margin: 0,
  });
  s.addText(e[1], {
    x: x + 0.85, y: y + 0.68, w: 4.8, h: 0.7, fontSize: 13, color: INK2, fontFace: B,
    lineSpacing: 18, isTextBox: true, margin: 0,
  });
});
s.addText(
  'If the Society wanted a negotiated member rate, I would rather offer it than be asked for it.',
  {
    x: M, y: 6.05, w: CW, h: 0.6, fontSize: 16, bold: true, color: ACC2, fontFace: H,
    italic: true, isTextBox: true, margin: 0,
  }
);
s.addNotes(
  'SAY: "The third thing is that none of this matters unless it works for the practices you actually ' +
  'represent — which are mostly small.\n\n' +
  'It is priced per organisation, not per person. A four-person practice pays once, not four times.\n\n' +
  'Everyone outside your office joins free. The client, the contractor, the quantity surveyor, the ' +
  'other consultants — no licence cost to anybody. That one matters more than it sounds, because the ' +
  'usual reason coordination software fails on a project here is that nobody will pay for the other ' +
  'parties to be on it.\n\n' +
  'It is billed in shillings. And it works with no connection — site records queue on the phone and ' +
  'sync when there is signal.\n\n' +
  'And if the Society wanted a negotiated rate for members, I would much rather offer that now than ' +
  'be asked for it later."'
);

/* ---------------------------------------------------------- 13 LIMITS */
s = pres.addSlide();
kicker(s, 'WHAT I AM NOT GOING TO OVERSELL');
title(s, 'Three things you should hear from me');

const limits = [
  ['StingTools runs inside Revit', 'Many of you work in ArchiCAD. PlanScape itself is authoring-agnostic and works through IFC, and there is an ArchiCAD bridge — but it is early, and I am not going to pretend otherwise.'],
  ['The calculation engines are unvalidated', 'Complete and tested, but never taken through independent professional validation. Offered only as commissioned work, with manual cross-checks alongside. Your engineers would be the right people to validate them.'],
  ['It is not finished', 'Production-ready for one deployment. Serving many organisations across several countries is the work that remains, and it is the work I am here about.'],
];
limits.forEach(function (l, i) {
  const y = 2.15 + i * 1.55;
  card(s, M, y, CW, 1.35, i === 0 ? TINT2 : TINT);
  numTile(s, M + 0.4, y + 0.42, String(i + 1), INK);
  s.addText(l[0], {
    x: M + 1.15, y: y + 0.25, w: 4.3, h: 0.85, fontSize: 17, bold: true, color: INK,
    fontFace: H, lineSpacing: 22, isTextBox: true, margin: 0,
  });
  s.addText(l[1], {
    x: M + 5.7, y: y + 0.25, w: 6.3, h: 0.95, fontSize: 13, color: INK2, fontFace: B,
    lineSpacing: 18, isTextBox: true, margin: 0,
  });
});
s.addText(
  'You would have found all three in about four minutes. I would rather you heard them from me.',
  {
    x: M, y: 6.85, w: CW, h: 0.4, fontSize: 13.5, color: MUTED, fontFace: B,
    italic: true, isTextBox: true, margin: 0,
  }
);
s.addNotes(
  'The ArchiCAD point is the single most likely objection in a room of architects. Say it before ' +
  'anyone else does — disclosed it is honesty, discovered it is a credibility problem.\n\n' +
  'SAY: "Three things I want you to hear from me rather than find out.\n\n' +
  'First, StingTools runs inside Revit. A lot of you work in ArchiCAD. PlanScape itself does not care ' +
  'what you model in — it works through IFC — and there is an ArchiCAD bridge, but it is early and I ' +
  'am not going to stand here and pretend it is mature.\n\n' +
  'Second, the engineering calculation engines have never been independently validated. They work and ' +
  'they are tested, but I only offer them as commissioned work with manual checks in parallel. If the ' +
  'Society wanted to put engineers on validating them, that sign-off would carry real weight.\n\n' +
  'Third, it is not finished, and I said that earlier.\n\n' +
  'You would have found all three of those in about four minutes. I would rather you heard them from ' +
  'me."\n\n' +
  'This slide earns you more than any other. Do not rush it and do not apologise through it.'
);

/* ---------------------------------------------------------- 14 WHAT IS LEFT */
s = pres.addSlide();
kicker(s, 'WHAT REMAINS');
title(s, 'What it takes to finish, and what it costs');

const costs = [
  ['Platform completion', 'USD 36,200', 'Serving many organisations, payments, security review, testing'],
  ['Regional hosting', 'USD 12,000', 'Twelve months, sized to grow with use'],
  ['Training programme', 'USD 15,000', 'Three cohorts of about 25 architects and engineers'],
];
costs.forEach(function (c, i) {
  const x = M + i * 4.0;
  card(s, x, 2.2, 3.7, 2.5);
  s.addText(c[1], {
    x: x + 0.35, y: 2.5, w: 3.0, h: 0.6, fontSize: 27, bold: true, color: ACC,
    fontFace: H, isTextBox: true, margin: 0,
  });
  s.addText(c[0], {
    x: x + 0.35, y: 3.15, w: 3.0, h: 0.4, fontSize: 16, bold: true, color: INK,
    fontFace: H, isTextBox: true, margin: 0,
  });
  s.addText(c[2], {
    x: x + 0.35, y: 3.6, w: 3.0, h: 0.95, fontSize: 12.5, color: INK2, fontFace: B,
    lineSpacing: 17, isTextBox: true, margin: 0,
  });
});

card(s, M, 5.0, CW, 1.55, INK);
s.addText('USD 72,680', {
  x: M + 0.45, y: 5.25, w: 3.4, h: 0.65, fontSize: 30, bold: true, color: PAPER,
  fontFace: H, isTextBox: true, margin: 0,
});
s.addText('including 15% contingency', {
  x: M + 0.45, y: 5.92, w: 3.4, h: 0.35, fontSize: 12.5, color: MUTED, fontFace: B,
  italic: true, isTextBox: true, margin: 0,
});
s.addText(
  'These three are separable. Nobody has to find the whole figure — the training ' +
  'programme can be funded on its own, and it is the part that can start first.',
  {
    x: M + 4.4, y: 5.3, w: 7.6, h: 1.0, fontSize: 15.5, color: PAPER, fontFace: B,
    lineSpacing: 22, isTextBox: true, margin: 0,
  }
);
s.addNotes(
  'Do not linger on the total. The separability line is the important one — say it twice if needed.\n\n' +
  'SAY: "So what does finishing it actually take.\n\n' +
  'Thirty-six thousand two hundred dollars to complete the platform — that is the work to serve many ' +
  'organisations safely, payments, an independent security review, and testing. Twelve thousand for ' +
  'twelve months of hosting sized to grow. Fifteen thousand for three training cohorts of about ' +
  'twenty-five people each.\n\n' +
  'With contingency that is seventy-two thousand six hundred and eighty dollars, about two hundred ' +
  'and sixty-nine million shillings.\n\n' +
  'But here is the part I want you to hear. Those three are separable. Nobody has to find the whole ' +
  'figure. The training programme is fifteen thousand dollars, it stands on its own, and it is the ' +
  'part that can start first — and frankly it is the part that would prove the rest."\n\n' +
  'IF ASKED how the 36,200 breaks down: you have the itemised list in the proposal. Offer to walk ' +
  'through it rather than reciting it now.'
);

/* ---------------------------------------------------------- 15 FUNDING ROUTES */
s = pres.addSlide();
kicker(s, 'THE ASK');
title(s, 'There are doors you can open that I cannot');

const routes = [
  ['CPD cohort fees', 'Members already budget for CPD. It is required by law.'],
  ['Development partners', 'They fund professional bodies for capacity building. They will not fund me.'],
  ['Industry sponsorship', 'Sponsors pay for access to the profession. Only you can grant it.'],
  ['Innovation and ICT funding', 'A local software product with regional reach fits national priorities.'],
  ['University partnership', 'A joint application reaches research funding neither of us reaches alone.'],
  ['Prepaid subscriptions', 'Revenue rather than debt — but only once delivery is certain.'],
];
routes.forEach(function (r, i) {
  const x = M + (i % 3) * 4.0;
  const y = 2.2 + Math.floor(i / 3) * 1.85;
  card(s, x, y, 3.7, 1.6, TINT);
  s.addText(r[0], {
    x: x + 0.3, y: y + 0.25, w: 3.1, h: 0.55, fontSize: 15.5, bold: true, color: ACC2,
    fontFace: H, lineSpacing: 20, isTextBox: true, margin: 0,
  });
  s.addText(r[1], {
    x: x + 0.3, y: y + 0.82, w: 3.1, h: 0.65, fontSize: 12.5, color: INK2, fontFace: B,
    lineSpacing: 17, isTextBox: true, margin: 0,
  });
});
s.addText(
  'I have brought a page on each of these. I would like your view on which are worth pursuing — and who I should be speaking to.',
  {
    x: M, y: 6.15, w: CW, h: 0.6, fontSize: 15, bold: true, color: INK, fontFace: B,
    isTextBox: true, margin: 0,
  }
);
s.addNotes(
  'Hand out the funding routes page as you start this slide. Physically giving them something changes ' +
  'the register of the conversation.\n\n' +
  'SAY: "Which brings me to what I am actually asking for.\n\n' +
  'There are routes to funding this that a professional body can open and I cannot. A development ' +
  'partner will fund the Society for construction-sector capacity building — they will not fund me. ' +
  'A sponsor will pay to be associated with your CPD programme — they will not pay to be associated ' +
  'with mine. And members already budget for CPD, because the law requires them to.\n\n' +
  'There are also routes through innovation funding, through a university partnership, and through ' +
  'prepaid subscriptions — though I would not take money for something before I could guarantee ' +
  'delivery.\n\n' +
  'I have written a page on each of these so you are not starting from a blank sheet. What I would ' +
  'like is your view on which are worth pursuing, and who I should be talking to."\n\n' +
  'THEN STOP TALKING. Let the silence do the work. The thing you want out of this meeting is a name ' +
  'and an introduction, not a resolution to consider it.'
);

/* ---------------------------------------------------------- 16 THE ASK */
s = pres.addSlide();
darkBg(s);
kicker(s, 'IN SUMMARY', ACC);
title(s, 'Three things I am asking for', PAPER);

const asks = [
  ['Endorse the training programme', 'As a Society initiative for members — and let us look at whether it can be accredited for CPD.'],
  ['Name a technical counterpart', 'Someone in the ICT Cluster, so the evaluation of this is yours rather than mine.'],
  ['Help me find the funding route', 'Not your money. Your judgement on which door to knock on, and ideally an introduction.'],
];
asks.forEach(function (a, i) {
  const y = 2.3 + i * 1.32;
  numTile(s, M, y, String(i + 1), ACC);
  s.addText(a[0], {
    x: M + 0.85, y: y - 0.06, w: 4.6, h: 0.5, fontSize: 20, bold: true, color: PAPER,
    fontFace: H, isTextBox: true, margin: 0,
  });
  s.addText(a[1], {
    x: M + 5.6, y: y - 0.04, w: 6.4, h: 0.9, fontSize: 14, color: LIGHTTXT, fontFace: B,
    lineSpacing: 20, isTextBox: true, margin: 0,
  });
});
card(s, M, 6.25, CW, 0.8, '2E333A');
s.addText('None of these costs the Society money.', {
  x: M + 0.45, y: 6.25, w: CW - 0.9, h: 0.8, fontSize: 17, bold: true, color: 'E8A183',
  fontFace: H, valign: 'middle', isTextBox: true, margin: 0,
});
s.addNotes(
  'Have this word-perfect. It is the close, and it is the only part of the meeting they will repeat ' +
  'to anyone who was not there.\n\n' +
  'SAY: "So, three things.\n\n' +
  'One. Endorse the training programme as a Society initiative for your members, and let us find out ' +
  'together whether it can be accredited for CPD.\n\n' +
  'Two. Name someone in the ICT Cluster as a technical counterpart, so the assessment of whether this ' +
  'is any good is yours and not mine.\n\n' +
  'Three. Help me find the route to fund the rest. Not your money — your judgement about which door ' +
  'to knock on, and if you are willing, an introduction.\n\n' +
  'None of those costs the Society anything. Thank you — I would rather spend the remaining time on ' +
  'your questions than on my slides."\n\n' +
  'BEFORE THEY DISPERSE, get four things written down: who takes this forward, when they next meet, ' +
  'what they need from you before then, and whether you may approach a funder using the Society name.'
);

/* ---------------------------------------------------------- 17 CLOSE */
s = pres.addSlide();
darkBg(s);
motif(s, M, 2.6, ACC);
s.addText('Thank you', {
  x: M, y: 3.05, w: 11.5, h: 1.1, fontSize: 46, bold: true, color: PAPER,
  fontFace: H, isTextBox: true, margin: 0,
});
s.addText(
  'I would rather spend the rest of the time on your questions than on my slides.',
  {
    x: M, y: 4.2, w: 10.5, h: 0.5, fontSize: 17, color: LIGHTTXT, fontFace: B,
    italic: true, isTextBox: true, margin: 0,
  }
);
s.addText('Davis E. Mayanja', {
  x: M, y: 5.35, w: 6, h: 0.35, fontSize: 16, bold: true, color: PAPER, fontFace: B,
  isTextBox: true, margin: 0,
});
s.addText('mayanjadavis@gmail.com   ·   +256 787 472 999   ·   +256 702 053 999', {
  x: M, y: 5.75, w: 9, h: 0.35, fontSize: 13, color: MUTED, fontFace: B,
  isTextBox: true, margin: 0,
});
s.addNotes(
  'Leave this up during the discussion — it keeps your contact details on the screen for forty minutes.\n\n' +
  'During questions: answer the question asked, then stop. The commonest mistake in a room like this ' +
  'is answering a short question at length and talking yourself back into a weaker position.\n\n' +
  '"I do not know, and I will come back to you by Friday" is a complete and respectable answer. ' +
  'Never invent a number, a date, or a customer.'
);

pres.writeFile({ fileName: process.argv[2] }).then(function () {
  console.log('written', process.argv[2]);
});
