#!/usr/bin/env node
// Generates the whole CPD document set from cpd/data/*.json into cpd/dist/.
// Usage: node cpd/build.mjs [--variant=N] [--out=DIR]
//   --variant=N  select assessment/exercise scenario variant (0 = primary, 1 = resit)

import { readFileSync, writeFileSync, mkdirSync, readdirSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { page, titleBlock, sec, table, callout, md, escapeHtml } from './lib/theme.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const arg = (k, d) => (process.argv.find(a => a.startsWith(`--${k}=`)) || `=${d}`).split('=').pop();
const VARIANT = Number(arg('variant', 0));
const OUT = join(HERE, arg('out', 'dist'));

const load = f => JSON.parse(readFileSync(join(HERE, 'data', f), 'utf8'));
const C = load('codes.json'), CO = load('course.json'), A = load('assessment.json'), EX = load('exercises.json');

mkdirSync(OUT, { recursive: true });
const written = [];
const emit = (name, html) => { writeFileSync(join(OUT, name), html); written.push(name); };

const V = (item) => {
  const vs = item.variants || [{}];
  return { ...item, ...vs[Math.min(VARIANT, vs.length - 1)] };
};
const fill = (tpl, v) => tpl.replace(/\{(\w+)\}/g, (_, k) => v[k] ?? `{${k}}`);
const vTag = VARIANT ? ` · VARIANT ${VARIANT}` : '';
const vSuffix = VARIANT ? `-v${VARIANT}` : '';
const stdMeta = [
  `${escapeHtml(CO.provider.name)} · ${escapeHtml(CO.provider.location)}`,
  escapeHtml(CO.code),
  `v${escapeHtml(CO.version)} · ${escapeHtml(CO.updated)}${vTag}`,
];

/* ───────────────────────── 1. FIELD GUIDE ───────────────────────── */
function fieldGuide() {
  const nameExample = C.nameFields.map(f => f.example).join(C.nameSeparator);
  const w = Math.max(...C.nameFields.map(f => f.example.length));
  const diag = [`<b>${C.nameFields.map(f => f.example).join(`</b> ${C.nameSeparator} <b>`)}</b>`,
    ...C.nameFields.slice().reverse().map((f, i) => {
      const idx = C.nameFields.length - 1 - i;
      const lead = C.nameFields.slice(0, idx).map(x => ' '.repeat(x.example.length)).join('   ');
      return `${lead} ${'│'} ${'└'}── ${f.name}: ${f.what}`;
    })].join('\n');

  const s = C.codeFamilies.suitability, a = C.codeFamilies.authorization;

  const body = titleBlock({
    meta: [...stdMeta, `Issue <b>Delegate pack</b>`],
    h1: 'The ISO 19650 Field&nbsp;Guide',
    standfirst: `Not a summary of the standard — the reference you keep beside your keyboard for the decisions it forces on you <em>every week</em>: what to call a file, what code to put on it, which folder it belongs in, and who is allowed to move it.`,
    extra: `<div class="goldrule"><p class="lbl">One rule governs everything here</p>
      <p>Where this guide and your project's BEP disagree, <strong>the BEP wins.</strong></p>
      <p>${escapeHtml(C.governingRule.split('. ').slice(1).join('. '))}</p></div>`
  })
  + sec('01', `<h2>The three parties</h2>
      <p>Every ISO 19650 appointment has the same three roles, whatever the contract calls them.</p>
      ${table(['Party', 'Usually', 'What they do'], C.parties.map(p => [`**${p.name}**`, p.usually, p.does]))}
      ${callout('trap', 'The trap', `<p>"Lead appointed party" <em>sounds</em> like the person in charge, so people assume it means the client. It does not — it means the lead <strong>supplier</strong>. The client is the appointing party.</p>`)}
      <p><strong>Information manager is a function, not a party.</strong> It may sit with the appointing party, the lead appointed party, or a third party. Do not treat it as a fourth box on the diagram.</p>`)

  + sec('02', `<h2>The information requirement chain</h2>
      <pre class="diag">${C.requirementChain.map(r => `<b>${r.code}</b>`).join('  ──►  ')}</pre>
      ${table(['Code', 'Name', 'Question it answers', 'Set by'], C.requirementChain.map(r => [{ t: r.code, mono: true }, r.name, r.question, r.setBy]))}
      <blockquote><p>The response to the EIR is the BEP. The EIR is the demand; the BEP is the offer. If you have received an EIR and not written a BEP, you have not responded to your client.</p></blockquote>`)

  + sec('03', `<h2>Level of Information Need</h2>
      <p><strong>Level of Information Need</strong> defines how much information is enough — and sets a <em>ceiling</em> so you do not deliver more than the purpose requires. It is determined by <strong>purpose</strong>: what decision is this information going to support?</p>
      ${callout('trap', 'Counter-intuitive', `<p><strong>Over-delivery is a failure, not generosity.</strong> Modelling a door to manufacturing detail at concept stage costs fee, slows the model, and gives the recipient false confidence in information that is not yet reliable.</p>`)}`)

  + sec('04', `<h2>The container name</h2>
      <pre class="diag">${escapeHtml(nameExample)}</pre>
      ${table(['#', 'Field', 'What it is', 'Notes'], C.nameFields.map(f => [{ t: String(f.pos), mono: true }, `**${f.name}**`, f.what, f.notes]))}
      ${callout('trap', 'Not fields', `<p><strong>${C.nameNotFields.join(' and ')} are not fields in the name.</strong> ${escapeHtml(C.nameNotFieldsNote)}</p>`)}`)

  + sec('05', `<h2>Type codes</h2>
      <p>The most commonly used set. <strong>Confirm against your project BEP.</strong></p>
      ${table(null, pair(C.typeCodes.map(t => [{ t: t.code, mono: true }, t.name])), 'codes')}
      ${callout('trap', 'The one people get wrong', `<p>${md(C.typeCodes.find(t => t.warn).warn)}</p>`)}`)

  + sec('06', `<h2>Role codes</h2>
      ${table(null, pair(C.roleCodes.map(r => [{ t: r.code, mono: true }, r.name])), 'codes')}
      <p>${C.roleCodesLocal.map(r => `Projects frequently add codes — <code>${r.code}</code> for ${r.name.toLowerCase()} is a common local addition. ${escapeHtml(r.note.split('. ').pop())}`).join(' ')}</p>`)

  + sec('07', `<h2>Suitability — for SHARED information</h2>
      <p>Suitability answers exactly one question: <strong>${escapeHtml(s.question)}</strong></p>
      ${table(['Code', 'Meaning', 'The recipient may'], s.codes.map(c => [{ t: c.code, mono: true }, c.meaning, c.recipientMay]))}
      <blockquote><p>S1 versus S2 is the distinction that matters commercially. S1 carries an <em>expectation of use</em> — you are inviting other teams to commit design decisions against your information. S2 carries no such expectation.</p></blockquote>
      <p><strong>Codes above ${s.variesAbove} vary.</strong> ${escapeHtml(s.variationNote)}</p>`)

  + sec('08', `<h2>Authorization — for PUBLISHED information</h2>
      ${callout('trap', 'This is where most people go wrong', `<p>Once information is authorised and moves into Published, <strong>it no longer carries a suitability code.</strong> It carries an authorization code.</p>`)}
      <div class="split">
        <div class="card share"><span class="who">Shared</span><h4>S codes</h4>
          <p>Describe the <strong>suitability</strong> of shared information — what the recipient may do with it.</p>
          <p class="cd">${s.stableCore.join(' · ')}</p></div>
        <div class="card pub"><span class="who">Published</span><h4>A and B codes</h4>
          ${a.codes.map(c => `<p><span class="cd">${escapeHtml(c.code)}</span> — <strong>${escapeHtml(c.meaning)}.</strong> ${escapeHtml(c.detail)}</p>`).join('')}</div>
      </div>
      <blockquote><p>${md(C.codeFamilyRule.split('. ').slice(0, 2).join('.<br>') + '.')}</p></blockquote>
      <p>${md(C.codeFamilyRule.split('. ').slice(2).join('. '))}</p>`)

  + sec('09', `<h2>Revision codes</h2>
      ${table(['Prefix', 'Stage', 'Sequence'], C.revisionCodes.map(r => [{ t: r.prefix, mono: true }, r.stage, { t: r.sequence, mono: true }]))}
      ${callout('trap', 'Remember this', `<p>${md(C.revisionRule)}</p>`)}`)

  + sec('10', `<h2>The Common Data Environment</h2>
      ${table(['State', 'Who may write', 'Who may read', 'What lives here'], C.cdeStates.map(x => [`**${x.name}**`, x.write, x.read, x.holds]))}
      <h3>The transitions are the point</h3>
      <pre class="diag">${C.cdeTransitions.map(t => `${t.from} ──<b>${t.action}</b>──►`).join(' ')} ARCHIVED</pre>
      <p><strong>Nothing skips a state.</strong> Information does not go from WIP straight to Published, however urgent the request.</p>`)

  + sec('11', `<h2>What actually makes a CDE</h2>
      ${table(['#', 'Test', 'If it fails'], C.cdeTests.map(t => [{ t: String(t.n), mono: true }, `**${t.test}**`, t.ifFails]))}
      <blockquote><p>A shared folder is a place to put files. A CDE is a set of rules about who may change a file's status, and a record of every time it happened. If you cannot answer <em>"who approved this, and when"</em>, you do not have a CDE — you have a folder with an ambitious name.</p></blockquote>`)

  + sec('12', `<h2>The six-point self-audit</h2>
      <p>Run this against your current project. Fifteen minutes, and it is not comfortable.</p>
      ${table(['#', 'Question', '✓ / ✗'], C.selfAudit.map(q => [{ t: String(q.n), mono: true }, q.q + (q.critical ? ' **(critical)**' : ''), '▢']))}
      <p><strong>${escapeHtml(C.selfAuditRule)}</strong></p>`)

  + sec('13', `<h2>The BEP</h2>
      ${table([' ', 'Pre-appointment BEP', 'Post-appointment BEP'], [
        ['**When**', 'Submitted with the tender', 'After contract award'],
        ['**Purpose**', 'Show you *can* deliver — proposed approach, capability, capacity', 'Confirm how you *will* deliver, in detail'],
        ['**Contains**', 'Proposed methods, team, capability assessment', 'Confirmed methods, MIDP, agreed codes and conventions']])}
      <h3>What it must settle</h3>
      <ul class="plain">${C.bepMustSettle.map(x => `<li>${md(x)}</li>`).join('')}</ul>
      <blockquote><p>The BEP is the project's dictionary. When this field guide and the BEP disagree, the BEP is right — but only if it actually says something. A BEP with <code>[FILL: …]</code> still in it settles nothing.</p></blockquote>`)

  + sec('14', `<h2>MIDP and TIDP</h2>
      ${table([' ', 'TIDP', 'MIDP'], [
        ['**Name**', 'Task Information Delivery Plan', 'Master Information Delivery Plan'],
        ['**Produced by**', 'Each task team, for its own work', 'The lead appointed party'],
        ['**Covers**', "One team's deliverables", 'The whole project'],
        ['**Built how**', 'Written by the team', '**Aggregated from all the TIDPs**']])}
      <p><strong>TIDPs are the inputs; the MIDP is the consolidated output.</strong> A MIDP that was not built from TIDPs is a wish list.</p>
      <h3>The minimum viable MIDP row</h3>
      <ul class="plain">${C.midpMinimumRow.map(r => `<li><b>${escapeHtml(r.label)}</b> — ${escapeHtml(r.why)}</li>`).join('')}</ul>`)

  + sec('15', `<h2>Security — ISO 19650-5</h2>
      <p>ISO 19650-5 requires a <strong>security-minded approach</strong>: assess whether the project, the asset or its information is sensitive, and if so apply proportionate controls over access, transmission, and disposal at the end of the appointment.</p>
      <p><strong>The practical minimum:</strong> know whether your project has been assessed as sensitive. If it has, the BEP must set out the controls — and "we email drawings to whoever asks" is not one of them.</p>`)

  + sec('16', `<h2>The ${C.commonMistakes.length} mistakes</h2>
      <ol class="num warn">${C.commonMistakes.map(m => `<li><span>${md(m)}</span></li>`).join('')}</ol>`)

  + sec('17', `<h2>Glossary</h2>
      ${table(['Term', 'Meaning'], C.glossary.map(g => [`**${g.term}**`, g.means]))}`)

  + sec('18', `<h2>Where to look next</h2>
      <ul class="plain"><li><b>Your project BEP.</b> First, always, for anything project-specific.</li>
      ${C.standards.map(s2 => `<li><b>${escapeHtml(s2.ref)}</b> — ${escapeHtml(s2.covers)}</li>`).join('')}
      <li><b>Your national annex</b>, where one exists, for the code tables that apply locally</li></ul>`)

  + `<footer>${escapeHtml(CO.provider.name)} · ${escapeHtml(CO.provider.location)} · Course ${escapeHtml(CO.code)}<br>Issued to course delegates. May be reproduced for use within the delegate's own practice.<br>Generated from cpd/data/codes.json v${escapeHtml(C.version)}</footer>`;

  emit('field-guide.html', page({ title: 'The ISO 19650 Field Guide', body }));
}
const pair = (rows) => { const out = []; const half = Math.ceil(rows.length / 2);
  for (let i = 0; i < half; i++) out.push([...(rows[i] || ['', '']), ...(rows[i + half] || ['', ''])]); return out; };

/* ───────────────────────── 2. ASSESSMENT ───────────────────────── */
function assessment() {
  const items = A.items.map(V);
  const mcq = items.filter(i => i.type === 'mcq'), wr = items.filter(i => i.type === 'written');
  const total = items.reduce((s, i) => s + i.marks, 0);

  const stemOf = (i) => i.stemTemplate ? md(fill(i.stemTemplate, i)) : md(i.stem);
  const answerOf = (i) => {
    if (i.answerTemplate) return fill(i.answerTemplate, i);
    if (i.answerFrom === 'cdeTests') return C.cdeTests.map(t => `• ${t.test}`).join('\n');
    if (i.answerFrom === 'midpMinimumRow') return C.midpMinimumRow.map((r, n) => `${n + 1}. ${r.label} — ${r.why}`).join('\n');
    return i.answer || '';
  };

  const qBlock = (i, withAnswer) => `<div class="q">
    <div class="qhead"><span class="qn">${i.id}</span><span class="marks">${i.marks} mark${i.marks > 1 ? 's' : ''}</span><span class="lo">${i.lo}</span></div>
    <p class="stem">${stemOf(i)}</p>
    ${i.options ? `<ul class="opts">${i.options.map((o, n) => `<li${withAnswer && n === i.answer ? ' class="correct"' : ''}><span class="k">${'ABCD'[n]}</span><span>${md(o)}${withAnswer && n === i.answer ? ' &nbsp;<strong>◄ correct</strong>' : ''}</span></li>`).join('')}</ul>` : ''}
    ${withAnswer && !i.options ? callout('ans', 'Model answer', `<p>${md(answerOf(i))}</p>`) : ''}
    ${withAnswer && i.marking ? i.marking.map(m => `<p class="stem" style="font-size:15.5px;color:var(--body)"><span class="lo" style="margin-right:7px">Marking</span>${md(m)}</p>`).join('') : ''}
    ${withAnswer && i.note ? callout('note', 'Marker\'s note', `<p>${md(i.note)}</p>`) : ''}
    ${withAnswer && i.reject ? callout('trap', 'Do not accept', `<p>${md(i.reject)}</p>`) : ''}
  </div>`;

  const specs = [
    { k: 'Questions', v: String(items.length), sub: `${mcq.length} MCQ · ${wr.length} written` },
    { k: 'Total', v: `${total} marks` },
    { k: 'Pass', v: `${A.passMark} / ${total}`, sub: `${(A.passMark / total * 100).toFixed(0)}%` },
    { k: 'Time', v: `${A.timeMinutes} min` },
    { k: 'Conditions', v: 'Closed book' },
  ];

  // Delegate paper
  emit(`assessment-paper${vSuffix}.html`, page({
    title: `${CO.code} Assessment Paper${vTag}`,
    body: titleBlock({ meta: [...stdMeta, 'Issue <b>Delegate paper</b>'], h1: 'Written Assessment',
      standfirst: `${CO.title} — ${CO.subtitle}. ${A.conditions}`, specs })
      + `<p class="sectlabel">Section A — Multiple choice · circle one · 2 marks each</p>${mcq.map(i => qBlock(i, false)).join('')}`
      + `<p class="sectlabel">Section B — Short answer · marks as shown</p>${wr.map(i => qBlock(i, false)).join('')}`
      + `<p class="endpaper">End of paper — ${total} marks</p>`
      + `<footer>${escapeHtml(CO.code)}${vTag} · ${escapeHtml(CO.provider.name)}</footer>`
  }));

  // Marker's copy
  const map = CO.outcomes.filter(o => !o.assessedBy).map(o => {
    const qs = items.filter(i => i.lo === o.id);
    const m = qs.reduce((s, i) => s + i.marks, 0);
    return [`**${o.id}** — ${o.text.slice(0, 62)}…`, { t: qs.map(q => q.id).join(', '), mono: true }, { t: String(m), mono: true }, { t: (m / total * 100).toFixed(1) + '%', mono: true }];
  });
  map.push({ cls: 'tot', cells: ['**Total**', '', { t: `**${total}**`, mono: true }, { t: '**100%**', mono: true }] });

  emit(`assessment-marking${vSuffix}.html`, page({
    title: `${CO.code} Marking Scheme${vTag}`,
    body: titleBlock({ meta: [...stdMeta, 'Issue <b>Marker\'s copy</b>'], h1: 'Marking Scheme',
      standfirst: `Model answers, the errors markers actually see, and the outcome map the accreditation panel will check. <em>Not issued to delegates.</em>`, specs })
      + `<p class="sectlabel">Section A</p>${mcq.map(i => qBlock(i, true)).join('')}`
      + `<p class="sectlabel">Section B</p>${wr.map(i => qBlock(i, true)).join('')}`
      + `<section class="plainsec"><p class="kicker">Coverage</p><h2>Outcome map</h2>${table(['Learning outcome', 'Questions', 'Marks', '%'], map)}
         <p>${CO.outcomes.filter(o => o.assessedBy).map(o => `<strong>${o.id}</strong> is assessed by ${escapeHtml(o.assessedBy)}`).join('')} State this explicitly in the accreditation submission so the panel does not read it as an omission.</p></section>`
      + `<section class="plainsec"><p class="kicker">Administration</p><h2>Running and defending the paper</h2><ul class="plain">${A.administration.map(x => `<li>${md(x)}</li>`).join('')}</ul>
         <p><strong>Retention:</strong> ${escapeHtml(A.retentionYears)} years. ${escapeHtml(A.retentionNote)}</p></section>`
      + `<div class="closer"><p class="lbl">What to do with the results</p>
         <p>The marks are for the board. <strong>The pattern is for you.</strong> Track which questions the room gets wrong — a cohort that collectively fails Q12 has told you the CDE concept did not land, and Module 3 needs rewriting before the next seminar.</p>
         <p>This is the only feedback loop you get on teaching quality. Adjust the module, not the question.</p></div>`
      + `<footer>${escapeHtml(CO.code)}${vTag} · generated from cpd/data/assessment.json v${escapeHtml(A.version)}</footer>`
  }));
}

/* ───────────────────────── 3. EXERCISE WORKBOOK ───────────────────────── */
function workbook() {
  const exBlock = (e, withAnswers) => {
    const inner = [];
    if (e.brief) inner.push(`<p>${md(e.brief)}</p>`);
    if (e.items) {
      const heads = e.id === 'EX2'
        ? ['#', 'Document', 'State', 'Code', 'Rev']
        : ['#', 'Container to name', withAnswers ? 'Model answer' : 'Your answer'];
      const rows = e.items.map(i => e.id === 'EX2'
        ? [{ t: String(i.n), mono: true }, i.ask,
           { t: withAnswers ? (i.ambiguous ? '**?**' : i.state) : '', mono: !withAnswers },
           { t: withAnswers ? i.code : '', mono: true }, { t: withAnswers ? i.rev : '', mono: true }]
        : [{ t: String(i.n), mono: true }, i.ask, { t: withAnswers ? `\`${i.answer}\`` : '', mono: false }]);
      inner.push(table(heads, rows));
      if (withAnswers) inner.push(`<h3>Why</h3><ul class="plain">${e.items.map(i => `<li><b>${i.n}.</b> ${md(i.why)}</li>`).join('')}</ul>`);
    }
    if (e.parts) inner.push(table(['Part', 'Mk', 'Task'].concat(withAnswers ? ['What a full-mark answer contains'] : []),
      e.parts.map(p => [{ t: p.part, mono: true }, { t: String(p.marks), mono: true }, p.ask].concat(withAnswers ? [p.answer] : []))));
    if (e.fromData === 'selfAudit') inner.push(table(['#', 'Question', '✓ / ✗'],
      C.selfAudit.map(q => [{ t: String(q.n), mono: true }, q.q + (q.critical ? ' **(critical)**' : ''), '▢'])) + `<p><strong>${escapeHtml(C.selfAuditRule)}</strong></p>`);
    if (withAnswers && e.marking) inner.push(callout('ans', 'Marking', `<p>${md(e.marking)}</p>`));
    if (withAnswers && e.teachingNote) inner.push(callout('teach', 'Teaching note', `<p>${md(e.teachingNote)}</p>`));
    return sec(e.id.replace('EX', 'EX '), `<h2>${escapeHtml(e.title)}</h2>
      <p class="lo" style="display:block;margin:-10px 0 16px">${e.mins} minutes · ${escapeHtml(e.method)} · ${e.collected ? 'collected and marked' : 'NOT collected'}</p>
      ${inner.join('')}`);
  };

  for (const withAnswers of [false, true]) {
    emit(withAnswers ? `workbook-answers${vSuffix}.html` : `workbook${vSuffix}.html`, page({
      title: withAnswers ? `${CO.code} Workbook — Model Answers${vTag}` : `${CO.code} Exercise Workbook${vTag}`,
      body: titleBlock({
        meta: [...stdMeta, `Issue <b>${withAnswers ? "Instructor's copy" : 'Delegate workbook'}</b>`],
        h1: withAnswers ? 'Workbook — Model Answers' : 'Exercise Workbook',
        standfirst: withAnswers
          ? `Model answers, marking guidance and teaching notes for all four exercises. <em>Not issued to delegates.</em>`
          : `Four exercises for ${escapeHtml(CO.title)}. Exercises 1–3 are collected and marked; <em>Exercise 4 is private to you and is never collected.</em>`,
        specs: [
          { k: 'Exercises', v: String(EX.exercises.length) },
          { k: 'Collected', v: String(EX.exercises.filter(e => e.collected).length) },
          { k: 'Bench time', v: `${EX.exercises.reduce((s, e) => s + e.mins, 0)} min` },
          { k: 'Variant', v: VARIANT ? `#${VARIANT}` : 'Primary' },
        ]
      }) + EX.exercises.map(e => exBlock(e, withAnswers)).join('')
        + `<footer>${escapeHtml(CO.code)}${vTag} · generated from cpd/data/exercises.json v${escapeHtml(EX.version)}</footer>`
    }));
  }
}

/* ───────────────────────── 4. SYLLABUS ───────────────────────── */
function syllabus() {
  const rs = CO.runsheet.map(r => r.break
    ? { cls: '', cells: [{ t: r.start, mono: true }, `*${r.title} — ${r.mins} min*`, ''] }
    : [{ t: `${r.start}\n${r.mins} min`, mono: true }, `**${r.title}**<br>${md(r.body)}${r.exercise ? `<br><span class="lo">${r.exercise}</span>` : ''}`, r.mode]);

  emit('syllabus.html', page({
    title: `${CO.title} — Syllabus`,
    body: titleBlock({
      meta: [...stdMeta, 'Status <b>For submission</b>'],
      h1: escapeHtml(CO.title),
      standfirst: `${escapeHtml(CO.subtitle)} — a ${CO.contactHours}-hour accredited course, written to satisfy <em>two readers</em>: the accreditation panel, and the engineer in the room at ${CO.startTime}.`,
      specs: [
        { k: 'Duration', v: `${CO.contactHours} hours`, sub: `${CO.startTime}–${CO.endTime}` },
        { k: 'Points sought', v: `${CO.pointsSought} CPD`, sub: '1 per contact hour' },
        { k: 'Delegates', v: `${CO.delegates.min}–${CO.delegates.max}`, sub: 'hands-on cap' },
        { k: 'Assessment', v: `${A.passMark}/${A.totalMarks}`, sub: `${(A.passMark / A.totalMarks * 100).toFixed(0)}% pass` },
        { k: 'First board', v: CO.boards[0].split(' ')[0], sub: 'Kenya' },
      ]
    })
    + sec('A3', `<h2>Learning outcomes</h2><p>On completion, a delegate will be able to:</p>
        <ol class="num">${CO.outcomes.map(o => `<li><span><b>${o.verb}</b> ${md(o.text)}</span></li>`).join('')}</ol>`)
    + sec('A4', `<h2>Run sheet</h2>${table(['Time', 'Session', 'Method'], rs)}
        <p><strong>Optional clinic ${CO.clinic.start}</strong> (${CO.clinic.mins} min) — ${escapeHtml(CO.clinic.note)}</p>`)
    + sec('A6', `<h2>Delegate pack</h2><ol class="num">${CO.packItems.map(p => `<li><span>${escapeHtml(p)}</span></li>`).join('')}</ol>`)
    + sec('A7', `<h2>Trainer</h2><p><strong>${escapeHtml(CO.trainer.name)}</strong>, ${escapeHtml(CO.trainer.role)}, ${escapeHtml(CO.trainer.firm)}.</p><p>${escapeHtml(CO.trainer.evidence)}</p>`)
    + sec('A8', `<h2>The vendor-neutrality boundary</h2>
        ${callout('trap', 'Read before submitting', `<p><strong>A CPD board will reject a course that reads as a product demonstration — and it should.</strong> Three rules must hold:</p>
        <ol class="num">${CO.neutralityRules.map(r => `<li><span>${md(r)}</span></li>`).join('')}</ol>`)}`)
    + `<footer>${escapeHtml(CO.code)} · generated from cpd/data/course.json v${escapeHtml(CO.version)}</footer>`
  }));
}

/* ───────────────────────── 5. TEMPLATES (generic, de-projected) ───────────────────────── */
function templates() {
  const bep = `# BIM Execution Plan

**Project:** [FILL: project name]  ·  **Project code:** [FILL: 3–6 chars]
**Appointing party:** [FILL]  ·  **Lead appointed party:** [FILL]
**Revision:** P01  ·  **Date:** [FILL]  ·  **Status:** [pre-appointment | post-appointment]

> Generated from \`cpd/data/codes.json\` v${C.version}. Every code table below is the
> widely-used default set. **Edit them to match this project, then delete this note** —
> once issued, this document is authoritative for the project.

## 1. Purpose and scope
[FILL: which appointment this BEP covers, and which EIR it responds to]

## 2. Roles and responsibilities
${C.parties.map(p => `- **${p.name}** — [FILL: organisation]. ${p.does}`).join('\n')}
- **Information management function** performed by: [FILL: named role]

## 3. Information requirements
${C.requirementChain.map(r => `- **${r.code}** — ${r.name}. ${r.question} *Set by ${r.setBy}.* [FILL: reference]`).join('\n')}

**Level of Information Need** is determined by purpose at each exchange. [FILL: per-stage table]

## 4. Naming convention
\`${C.nameFields.map(f => `[${f.name}]`).join(C.nameSeparator)}\`

Worked example: \`${C.nameFields.map(f => f.example).join(C.nameSeparator)}\`

${C.nameFields.map(f => `| ${f.pos} | **${f.name}** | ${f.notes} | [FILL: project values] |`).join('\n')}

> ${C.nameNotFieldsNote}

### 4.1 Role codes
${C.roleCodes.map(r => `\`${r.code}\` ${r.name}`).join(' · ')}

### 4.2 Type codes
${C.typeCodes.map(t => `\`${t.code}\` ${t.name}`).join(' · ')}

## 5. CDE states and permissions
${C.cdeStates.map(s => `- **${s.name}** — write: ${s.write}; read: ${s.read}. ${s.holds}`).join('\n')}

Transitions: ${C.cdeTransitions.map(t => `${t.from} —${t.action} (${t.by})→ ${t.to}`).join('; ')}.
**Nothing skips a state.**

**Approver authorised to publish:** [FILL: named role] · **Deputy:** [FILL]

## 6. Suitability codes (SHARED)
${C.codeFamilies.suitability.codes.map(c => `- \`${c.code}\` ${c.meaning} — ${c.recipientMay}`).join('\n')}

## 7. Authorization codes (PUBLISHED)
${C.codeFamilies.authorization.codes.map(c => `- \`${c.code}\` ${c.meaning} — ${c.detail}`).join('\n')}

> ${C.codeFamilyRule}

## 8. Revision codes
${C.revisionCodes.map(r => `- \`${r.prefix}\` ${r.stage} — ${r.sequence}`).join('\n')}

> ${C.revisionRule}

## 9. Software, formats and coordinates
- Authoring software and versions: [FILL]
- Exchange formats: [FILL: IFC version, native, PDF]
- **Coordinate system and project base point: [FILL — agreed at mobilisation, never changed]**
- Units: millimetres

## 10. Information delivery planning
The MIDP is at [FILL: location] and is aggregated from the TIDPs of: [FILL: task teams]

## 11. Quality assurance and model validation
How compliance is checked: [FILL] · By whom: [FILL] · How often: [FILL]

## 12. Security (ISO 19650-5)
Sensitivity assessed: [ yes / no ]. If yes, controls: [FILL]

---
### Clause test
**Could two task teams read any clause above and still disagree?** If yes, it fails.
Delete every \`as appropriate\`, \`as required\` and \`to be agreed\` before issuing.
`;
  writeFileSync(join(OUT, 'BEP_TEMPLATE.md'), bep); written.push('BEP_TEMPLATE.md');

  const csv = [C.midpColumns.join(',')];
  const ex = [
    ['Z-001', 'BIM/Info Mgmt', '[ORIG]', 'BIM Execution Plan (BEP)', 'RP', 'Mobilisation', 'n/a', 'DOCX/PDF', 'A1', 'Published', '[date]', '', '[name]', 'TIDP-Z', 'G', 'Baseline at mobilisation'],
    ['Z-002', 'BIM/Info Mgmt', '[ORIG]', 'Master Information Delivery Plan', 'SH', 'Mobilisation', 'n/a', 'XLSX', 'A1', 'Published', '[date]', '', '[name]', 'TIDP-Z', 'G', 'Aggregated from all TIDPs'],
    ['A-001', 'Architecture', '[ORIG]', 'GA plans - all levels', 'DR', 'Stage 3', '[LOIN]', 'PDF/DWG', 'S1', 'Shared', '[date]', '', '[name]', 'TIDP-A', 'A', 'Issued for coordination'],
    ['M-001', 'Mechanical', '[ORIG]', 'Mechanical services model', 'M3', 'Stage 4', '[LOIN]', 'RVT/IFC', 'S1', 'Shared', '[date]', '', '[name]', 'TIDP-M', 'A', 'Coordination model'],
  ];
  ex.forEach(r => csv.push(r.map(c => /[",]/.test(c) ? `"${c.replace(/"/g, '""')}"` : c).join(',')));
  csv.push('', '# Minimum viable row: ' + C.midpMinimumRow.map(r => r.label).join(' / '));
  csv.push('# A row missing any of what/who/when cannot control delivery.');
  writeFileSync(join(OUT, 'MIDP_TEMPLATE.csv'), csv.join('\n')); written.push('MIDP_TEMPLATE.csv');
}

/* ───────────────────────── 6. INDEX ───────────────────────── */
function index() {
  const docs = [
    ['syllabus.html', 'Syllabus', 'Course identity, outcomes, run sheet, neutrality boundary'],
    [`assessment-paper${vSuffix}.html`, 'Assessment paper', 'Delegate copy — questions only'],
    [`assessment-marking${vSuffix}.html`, 'Marking scheme', "Marker's copy — model answers and outcome map"],
    [`workbook${vSuffix}.html`, 'Exercise workbook', 'Delegate copy — four exercises'],
    [`workbook-answers${vSuffix}.html`, 'Workbook answers', "Instructor's copy — model answers and teaching notes"],
    ['field-guide.html', 'Field guide', 'Delegate desk reference'],
    ['BEP_TEMPLATE.md', 'BEP template', 'Generic, code tables pre-filled'],
    ['MIDP_TEMPLATE.csv', 'MIDP template', 'Spreadsheet with worked rows'],
    ['certificates.html', 'Certificates', 'Print-ready, one per delegate'],
  ];
  emit('index.html', page({
    title: `${CO.code} Course Pack`,
    body: titleBlock({
      meta: [...stdMeta, `Generated <b>${new Date().toISOString().slice(0, 10)}</b>`],
      h1: `${escapeHtml(CO.code)} Course Pack`,
      standfirst: `Every document generated from <code>cpd/data/*.json</code>. Change a code once, rebuild, and <em>every document agrees</em>.`,
      specs: [
        { k: 'Documents', v: String(docs.length) },
        { k: 'Variant', v: VARIANT ? `#${VARIANT}` : 'Primary' },
        { k: 'Codes', v: `v${C.version}` },
        { k: 'Marks', v: `${A.totalMarks}`, sub: `pass ${A.passMark}` },
      ]
    })
    + `<div class="toc">${docs.map(([h, t, d]) => `<a href="${h}"><span class="n">${escapeHtml(h)}</span><span class="t">${escapeHtml(t)}</span><br><span style="font-size:13px;color:var(--muted)">${escapeHtml(d)}</span></a>`).join('')}</div>`
    + `<section class="plainsec"><p class="kicker">How this works</p><h2>One source, many documents</h2>
       <p>The code tables live in <code>cpd/data/codes.json</code> and nowhere else. The field guide, the BEP template, the workbook answers and the assessment all render from it, so they cannot drift apart — which is exactly how three of the earlier hand-written documents came to contradict each other on the published-information codes.</p>
       <pre class="diag">cpd/data/codes.json ──┬──► field-guide.html
                      ├──► BEP_TEMPLATE.md
                      ├──► MIDP_TEMPLATE.csv
                      ├──► workbook-answers.html
                      └──► assessment-marking.html

<b>node cpd/validate.mjs</b>  checks the rest of the repo against it</pre>
       <h3>Commands</h3>
       <ul class="plain">
         <li><code>node cpd/build.mjs</code> — build the primary pack</li>
         <li><code>node cpd/build.mjs --variant=1</code> — build an equivalent resit paper</li>
         <li><code>node cpd/validate.mjs</code> — fail on any drift between the repo and the source of truth</li>
         <li><code>node cpd/certificates.mjs delegates.csv</code> — print-ready certificates</li>
       </ul></section>`
    + `<footer>Generated ${new Date().toISOString()} · ${escapeHtml(CO.provider.name)}</footer>`
  }));
}

fieldGuide(); assessment(); workbook(); syllabus(); templates(); index();
console.log(`Built ${written.length} files into cpd/${arg('out', 'dist')}/${VARIANT ? ` (variant ${VARIANT})` : ''}`);
written.sort().forEach(f => console.log('  ·', f));
