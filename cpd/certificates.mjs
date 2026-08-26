#!/usr/bin/env node
// Print-ready CPD certificates, one A4 landscape page per delegate.
// Usage: node cpd/certificates.mjs [delegates.csv] [--out=dist/certificates.html]
// CSV columns (header required, order free): name, registration, board, date, result, marks
//   result: PASS | FAIL | (blank = derived from marks against the pass mark)
// Only passing delegates get a certificate; the run reports who was withheld and why.

import { readFileSync, writeFileSync, existsSync, mkdirSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { FONTS, escapeHtml } from './lib/theme.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const load = f => JSON.parse(readFileSync(join(HERE, 'data', f), 'utf8'));
const CO = load('course.json'), A = load('assessment.json');

const args = process.argv.slice(2).filter(a => !a.startsWith('--'));
const arg = (k, d) => (process.argv.find(a => a.startsWith(`--${k}=`)) || `=${d}`).split('=').pop();
const csvPath = args[0] || join(HERE, 'data', 'delegates.sample.csv');
const outPath = join(HERE, arg('out', 'dist/certificates.html'));

// RFC4180-ish parser: handles quoted fields containing commas and escaped quotes.
function parseCsv(text) {
  const rows = []; let row = [], cell = '', q = false;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (q) {
      if (c === '"') { if (text[i + 1] === '"') { cell += '"'; i++; } else q = false; }
      else cell += c;
    } else if (c === '"') q = true;
    else if (c === ',') { row.push(cell); cell = ''; }
    else if (c === '\n') { row.push(cell); rows.push(row); row = []; cell = ''; }
    else if (c !== '\r') cell += c;
  }
  if (cell || row.length) { row.push(cell); rows.push(row); }
  return rows.filter(r => r.some(c => c.trim()));
}

if (!existsSync(csvPath)) { console.error(`No delegate file at ${csvPath}`); process.exit(1); }
const rows = parseCsv(readFileSync(csvPath, 'utf8'));
const head = rows.shift().map(h => h.trim().toLowerCase());
const col = n => head.indexOf(n);

const delegates = rows.map((r, n) => {
  const get = k => (col(k) >= 0 ? (r[col(k)] || '').trim() : '');
  const marks = get('marks') === '' ? null : Number(get('marks'));
  const declared = get('result').toUpperCase();
  const passed = declared ? declared === 'PASS' : (marks !== null && marks >= A.passMark);
  return { line: n + 2, name: get('name'), registration: get('registration'), board: get('board'),
           date: get('date') || new Date().toISOString().slice(0, 10), marks, declared, passed };
});

const issue = [], withheld = [];
for (const d of delegates) {
  if (!d.name) withheld.push({ ...d, why: 'no name in row' });
  else if (!d.registration) withheld.push({ ...d, why: 'no professional registration number — the certificate is issued against it' });
  else if (!d.passed) withheld.push({ ...d, why: d.marks === null ? 'no marks and no declared result' : `${d.marks}/${A.totalMarks} is below the pass mark of ${A.passMark}` });
  else issue.push(d);
}

const serial = d => {
  // Deterministic, human-checkable serial: course-year-initials-registration tail.
  const yr = d.date.slice(0, 4);
  const ini = d.name.split(/\s+/).map(w => w[0]).join('').toUpperCase().slice(0, 3);
  const tail = d.registration.replace(/\W/g, '').slice(-4).toUpperCase();
  return `${CO.code}-${yr}-${ini}${tail}`;
};

const cert = d => `
<div class="cert">
  <div class="edge"></div>
  <header>
    <div class="brand">${escapeHtml(CO.provider.name)}</div>
    <div class="loc">${escapeHtml(CO.provider.location)}</div>
  </header>
  <p class="awarded">Certificate of Completion</p>
  <p class="name">${escapeHtml(d.name)}</p>
  <p class="reg">Professional registration <b>${escapeHtml(d.registration)}</b>${d.board ? ` &nbsp;·&nbsp; ${escapeHtml(d.board)}` : ''}</p>
  <p class="body-txt">has completed the accredited course</p>
  <p class="course">${escapeHtml(CO.title)}</p>
  <p class="sub">${escapeHtml(CO.subtitle)}</p>
  <div class="facts">
    <div><span>Contact hours</span><b>${CO.contactHours}</b></div>
    <div><span>CPD points</span><b>${CO.pointsSought}</b></div>
    <div><span>Assessment</span><b>${d.marks !== null ? `${d.marks}/${A.totalMarks}` : 'Passed'}</b></div>
    <div><span>Date</span><b>${escapeHtml(d.date)}</b></div>
  </div>
  <div class="sign">
    <div class="sigline"><span></span><small>${escapeHtml(CO.trainer.name)} · ${escapeHtml(CO.trainer.role)}</small></div>
    <div class="serial">${escapeHtml(serial(d))}<small>Verify at ${escapeHtml(CO.provider.email)}</small></div>
  </div>
</div>`;

const html = `<title>${escapeHtml(CO.code)} Certificates</title>
<link rel="stylesheet" href="${FONTS}">
<style>
:root{--ink:#14183A;--body:#2C3252;--muted:#666E8C;--accent:#D97A16;--rule:#D6D9E6;--paper:#fff}
*{box-sizing:border-box}
body{margin:0;background:#EDEFF5;font-family:"Source Serif 4",Georgia,serif;color:var(--body)}
.bar{max-width:1100px;margin:0 auto;padding:22px 24px;font-family:"IBM Plex Mono",monospace;font-size:12px;color:#4A5069;letter-spacing:.04em}
.bar b{color:var(--accent)}
.cert{position:relative;width:297mm;height:210mm;margin:0 auto 18px;background:var(--paper);padding:22mm 24mm;
  display:flex;flex-direction:column;box-shadow:0 6px 28px rgba(20,24,58,.13);overflow:hidden}
.edge{position:absolute;inset:0;border:1.6mm solid var(--accent);opacity:.13;pointer-events:none}
.cert::after{content:"";position:absolute;inset:7mm;border:.3mm solid var(--rule);pointer-events:none}
header{display:flex;justify-content:space-between;align-items:baseline;border-bottom:.6mm solid var(--ink);padding-bottom:5mm}
.brand{font-family:Archivo,sans-serif;font-weight:700;font-size:17pt;color:var(--ink);letter-spacing:-.02em}
.loc{font-family:"IBM Plex Mono",monospace;font-size:8.5pt;letter-spacing:.18em;text-transform:uppercase;color:var(--muted)}
.awarded{font-family:"IBM Plex Mono",monospace;font-size:9pt;letter-spacing:.3em;text-transform:uppercase;color:var(--accent);margin:12mm 0 0;font-weight:600}
.name{font-family:Archivo,sans-serif;font-weight:700;font-size:34pt;line-height:1.05;color:var(--ink);margin:3mm 0 2mm;letter-spacing:-.025em}
.reg{font-size:11pt;color:var(--muted);margin:0 0 8mm}
.reg b{color:var(--ink);font-family:"IBM Plex Mono",monospace;font-size:10pt}
.body-txt{font-size:11.5pt;margin:0 0 2mm;color:var(--muted)}
.course{font-family:Archivo,sans-serif;font-weight:600;font-size:20pt;color:var(--ink);margin:0;letter-spacing:-.015em}
.sub{font-size:11.5pt;color:var(--muted);margin:1mm 0 0;font-style:italic}
.facts{display:grid;grid-template-columns:repeat(4,1fr);gap:6mm;margin-top:auto;padding:6mm 0;border-top:.3mm solid var(--rule)}
.facts div{display:flex;flex-direction:column;gap:1.5mm}
.facts span{font-family:"IBM Plex Mono",monospace;font-size:7.5pt;letter-spacing:.16em;text-transform:uppercase;color:var(--muted)}
.facts b{font-family:Archivo,sans-serif;font-size:15pt;color:var(--ink);font-weight:600}
.sign{display:flex;justify-content:space-between;align-items:flex-end;padding-top:4mm;border-top:.6mm solid var(--ink)}
.sigline span{display:block;width:70mm;border-bottom:.3mm solid var(--ink);height:11mm}
.sigline small,.serial small{display:block;font-family:"IBM Plex Mono",monospace;font-size:7.5pt;color:var(--muted);letter-spacing:.06em;margin-top:1.5mm}
.serial{font-family:"IBM Plex Mono",monospace;font-size:10pt;color:var(--accent);font-weight:600;text-align:right}
@media print{
  body{background:#fff}.bar{display:none}
  .cert{margin:0;box-shadow:none;page-break-after:always;break-after:page}
  @page{size:A4 landscape;margin:0}
}
</style>
<div class="bar">${escapeHtml(CO.code)} · <b>${issue.length}</b> certificate${issue.length === 1 ? '' : 's'} · ${withheld.length} withheld · generated ${new Date().toISOString().slice(0, 10)} · <b>Print to PDF at A4 landscape, margins none</b></div>
${issue.map(cert).join('\n')}`;

mkdirSync(dirname(outPath), { recursive: true });
writeFileSync(outPath, html);

console.log(`\n${CO.code} certificates`);
console.log(`  source   ${csvPath}`);
console.log(`  issued   ${issue.length}`);
issue.forEach(d => console.log(`    ✓ ${d.name.padEnd(26)} ${serial(d)}`));
if (withheld.length) {
  console.log(`  withheld ${withheld.length}`);
  withheld.forEach(d => console.log(`    ✗ ${(d.name || `(row ${d.line})`).padEnd(26)} ${d.why}`));
}
console.log(`  written  ${outPath}\n`);
