// Shared visual system for all generated CPD documents.
// Drawing-sheet vernacular: Archivo display, Source Serif body, IBM Plex Mono for codes.
// Theme-aware across all three viewer states (explicit light, explicit dark, unset/system).

export const FONTS = 'https://fonts.googleapis.com/css2?family=Archivo:wght@500;600;700&family=Source+Serif+4:opsz,wght@8..60,400;8..60,600&family=IBM+Plex+Mono:wght@400;500;600&display=swap';

const LIGHT = `--paper:#FCFCFE;--panel:#F3F4F9;--ink:#14183A;--body:#2C3252;--muted:#666E8C;
  --rule:#D6D9E6;--rule-soft:#E6E8F1;--accent:#D97A16;--accent-soft:#FBF0E2;
  --flag:#B23A2B;--flag-soft:#FAEDEB;--ok:#1B6B50;--ok-soft:#E7F3ED;
  --share:#2F5FA8;--share-soft:#EBF1FA;--pub:#6B4396;--pub-soft:#F1EBF8;`;

const DARK = `--paper:#0E1124;--panel:#171B33;--ink:#EDEFF8;--body:#C3C8DE;--muted:#8990AC;
  --rule:#2A3050;--rule-soft:#212745;--accent:#F0A44E;--accent-soft:#2A2036;
  --flag:#E4796A;--flag-soft:#2E1D1C;--ok:#4FC79C;--ok-soft:#14291F;
  --share:#7BA6E8;--share-soft:#1A2440;--pub:#B695DD;--pub-soft:#241B33;`;

export const CSS = `
:root{${LIGHT}}
@media (prefers-color-scheme:dark){:root:not([data-theme="light"]){${DARK}}}
:root[data-theme="dark"]{${DARK}}
*{box-sizing:border-box}
body{margin:0;background:var(--paper);color:var(--body);font-family:"Source Serif 4",Georgia,serif;font-size:17px;line-height:1.62;-webkit-font-smoothing:antialiased}
.sheet{max-width:1000px;margin:0 auto;padding:0 24px 88px}
.titleblock{border-bottom:2px solid var(--ink);padding:42px 0 22px}
.tb-meta{display:flex;flex-wrap:wrap;gap:8px 26px;font-family:"IBM Plex Mono",monospace;font-size:11px;letter-spacing:.14em;text-transform:uppercase;color:var(--muted);margin-bottom:20px}
.tb-meta b{color:var(--accent);font-weight:600}
h1{font-family:Archivo,system-ui,sans-serif;font-weight:700;color:var(--ink);font-size:clamp(34px,5.6vw,58px);line-height:1.03;letter-spacing:-.025em;margin:0 0 15px;text-wrap:balance}
.standfirst{font-size:19.5px;line-height:1.5;max-width:60ch;margin:0}
.standfirst em{color:var(--ink);font-style:normal;font-weight:600;box-shadow:inset 0 -.5em 0 var(--accent-soft)}
.specs{display:grid;grid-template-columns:repeat(auto-fit,minmax(130px,1fr));gap:1px;background:var(--rule);border:1px solid var(--rule);border-radius:10px;overflow:hidden;margin:26px 0 0}
.spec{background:var(--panel);padding:14px 17px}
.spec dt{font-family:"IBM Plex Mono",monospace;font-size:10px;letter-spacing:.14em;text-transform:uppercase;color:var(--muted);margin-bottom:5px}
.spec dd{margin:0;font-family:Archivo,sans-serif;font-size:16px;font-weight:600;color:var(--ink);line-height:1.3}
.spec dd small{display:block;font-family:"Source Serif 4",serif;font-weight:400;font-size:13px;color:var(--muted);margin-top:2px}
section{display:grid;grid-template-columns:70px minmax(0,1fr);gap:0 30px;padding-top:50px;border-top:1px solid var(--rule-soft);margin-top:50px}
section.plainsec{display:block;padding-top:44px}
.rail{font-family:"IBM Plex Mono",monospace;font-size:19px;font-weight:600;color:var(--accent);padding-top:.35em;line-height:1}
h2{font-family:Archivo,sans-serif;font-weight:600;color:var(--ink);font-size:clamp(23px,3vw,31px);line-height:1.13;letter-spacing:-.018em;margin:0 0 17px;text-wrap:balance}
h3{font-family:Archivo,sans-serif;font-weight:600;color:var(--ink);font-size:18px;margin:28px 0 8px;line-height:1.3}
h4{font-family:Archivo,sans-serif;font-weight:600;color:var(--ink);font-size:16px;margin:0 0 6px;line-height:1.3}
p{margin:0 0 15px;max-width:66ch}
strong{color:var(--ink);font-weight:600}
code{font-family:"IBM Plex Mono",monospace;font-size:.87em;background:var(--panel);border:1px solid var(--rule-soft);border-radius:4px;padding:1px 5px;color:var(--ink)}
a{color:var(--accent);text-underline-offset:2px}
a:focus-visible,summary:focus-visible{outline:2px solid var(--accent);outline-offset:3px}
.kicker{font-family:"IBM Plex Mono",monospace;font-size:11px;letter-spacing:.16em;text-transform:uppercase;color:var(--accent);font-weight:600;margin:0 0 10px}
.tablewrap{overflow-x:auto;margin:18px 0;border:1px solid var(--rule);border-radius:10px}
table{border-collapse:collapse;width:100%;background:var(--panel);font-size:15px;min-width:400px}
th{font-family:"IBM Plex Mono",monospace;font-size:10px;font-weight:600;letter-spacing:.13em;text-transform:uppercase;color:var(--muted);text-align:left;padding:11px 15px;border-bottom:1px solid var(--rule);white-space:nowrap}
td{padding:11px 15px;border-bottom:1px solid var(--rule-soft);vertical-align:top;line-height:1.45}
tr:last-child td{border-bottom:0}
td.mono,th.mono{font-family:"IBM Plex Mono",monospace;font-size:12.5px;color:var(--ink);white-space:nowrap;font-variant-numeric:tabular-nums}
td b{color:var(--ink)}
table.codes td.mono{font-weight:600;color:var(--accent)}
tr.tot td{background:var(--accent-soft);font-weight:600}
pre.diag{font-family:"IBM Plex Mono",monospace;font-size:13px;line-height:1.7;background:var(--panel);border:1px solid var(--rule);border-radius:10px;padding:20px 22px;overflow-x:auto;color:var(--ink);margin:18px 0}
pre.diag b{color:var(--accent);font-weight:600}
blockquote{margin:20px 0;padding:19px 23px;border-left:3px solid var(--accent);background:var(--accent-soft);border-radius:0 8px 8px 0;max-width:64ch}
blockquote p{font-family:Archivo,sans-serif;font-size:17.5px;line-height:1.45;color:var(--ink);font-weight:500;margin:0}
.callout{border-radius:0 8px 8px 0;padding:14px 18px;margin:16px 0;max-width:64ch;font-size:16px;line-height:1.5}
.callout .lbl{font-family:"IBM Plex Mono",monospace;font-size:10px;letter-spacing:.14em;text-transform:uppercase;font-weight:600;display:block;margin-bottom:5px}
.callout p{margin:0 0 8px}.callout p:last-child{margin-bottom:0}
.c-trap{background:var(--flag-soft);border-left:3px solid var(--flag)}.c-trap .lbl{color:var(--flag)}
.c-ans{background:var(--ok-soft);border-left:3px solid var(--ok)}.c-ans .lbl{color:var(--ok)}
.c-teach{background:var(--share-soft);border-left:3px solid var(--share)}.c-teach .lbl{color:var(--share)}
.c-note{background:var(--panel);border-left:3px solid var(--rule)}.c-note .lbl{color:var(--muted)}
.goldrule{border:2px solid var(--accent);border-radius:12px;background:var(--accent-soft);padding:23px 27px;margin:28px 0}
.goldrule .lbl{font-family:"IBM Plex Mono",monospace;font-size:11px;letter-spacing:.15em;text-transform:uppercase;color:var(--accent);font-weight:600;margin:0 0 10px}
.goldrule p{font-family:Archivo,sans-serif;font-size:19px;line-height:1.42;color:var(--ink);font-weight:500;margin:0 0 10px;max-width:62ch}
.goldrule p:last-child{margin-bottom:0;font-family:"Source Serif 4",serif;font-size:16px;font-weight:400;color:var(--body);line-height:1.55}
.split{display:grid;grid-template-columns:1fr 1fr;gap:16px;margin:20px 0}
.card{border-radius:10px;padding:18px 20px;border:1px solid var(--rule);background:var(--panel)}
.card.share{background:var(--share-soft);border-color:var(--share)}
.card.pub{background:var(--pub-soft);border-color:var(--pub)}
.card .who{font-family:"IBM Plex Mono",monospace;font-size:10.5px;letter-spacing:.12em;text-transform:uppercase;font-weight:600;display:block;margin-bottom:8px}
.card.share .who{color:var(--share)}.card.pub .who{color:var(--pub)}
.card p{font-size:15px;line-height:1.5;margin:0 0 8px}.card p:last-child{margin-bottom:0}
.card .cd{font-family:"IBM Plex Mono",monospace;font-weight:600;color:var(--ink)}
ul.plain{padding-left:20px;margin:13px 0;max-width:64ch}
ul.plain li{margin-bottom:9px;line-height:1.5}
ul.plain li b{color:var(--ink)}
ol.num{list-style:none;counter-reset:n;padding:0;margin:0;display:grid;gap:9px}
ol.num li{counter-increment:n;display:grid;grid-template-columns:28px 1fr;gap:13px;align-items:baseline;font-size:16px;line-height:1.5}
ol.num li::before{content:counter(n);font-family:"IBM Plex Mono",monospace;font-size:12px;font-weight:600;color:var(--accent);border:1px solid var(--rule);border-radius:6px;text-align:center;padding:2px 0;background:var(--panel)}
ol.num.warn li::before{color:var(--flag)}
ol.num b{color:var(--ink)}
.q{padding:22px 0;border-bottom:1px solid var(--rule-soft)}
.q:last-of-type{border-bottom:0}
.qhead{display:flex;align-items:baseline;gap:12px;margin-bottom:9px;flex-wrap:wrap}
.qn{font-family:"IBM Plex Mono",monospace;font-size:13px;font-weight:600;color:var(--accent);letter-spacing:.04em}
.marks,.lo{font-family:"IBM Plex Mono",monospace;font-size:10.5px;letter-spacing:.1em;text-transform:uppercase;color:var(--muted)}
.marks{border:1px solid var(--rule);border-radius:999px;padding:2px 9px}
.stem{font-size:16.5px;line-height:1.55;margin:0 0 12px;max-width:64ch}
ul.opts{list-style:none;padding:0;margin:0;display:grid;gap:5px;max-width:60ch}
ul.opts li{display:grid;grid-template-columns:26px 1fr;gap:10px;font-size:16px;line-height:1.45}
ul.opts .k{font-family:"IBM Plex Mono",monospace;font-size:12.5px;color:var(--muted);font-weight:600;padding-top:2px}
ul.opts li.correct .k{color:var(--ok)}
.sectlabel{font-family:"IBM Plex Mono",monospace;font-size:11px;letter-spacing:.16em;text-transform:uppercase;color:var(--muted);font-weight:600;border-bottom:1px solid var(--rule);padding-bottom:9px;margin:36px 0 4px}
.endpaper{text-align:center;font-family:"IBM Plex Mono",monospace;font-size:12px;letter-spacing:.2em;text-transform:uppercase;color:var(--muted);padding:24px 0;border-top:1px solid var(--rule);border-bottom:1px solid var(--rule);margin-top:24px}
.closer{margin-top:54px;padding:34px 36px;border:2px solid var(--ink);border-radius:12px;background:var(--panel)}
.closer .lbl{font-family:"IBM Plex Mono",monospace;font-size:11px;letter-spacing:.15em;text-transform:uppercase;color:var(--muted);margin:0 0 14px}
footer{margin-top:52px;padding-top:20px;border-top:1px solid var(--rule-soft);font-family:"IBM Plex Mono",monospace;font-size:11.5px;color:var(--muted);line-height:1.9}
.toc{display:grid;grid-template-columns:repeat(auto-fit,minmax(210px,1fr));gap:1px;background:var(--rule);border:1px solid var(--rule);border-radius:10px;overflow:hidden;margin:26px 0}
.toc a{background:var(--panel);padding:13px 16px;text-decoration:none;display:block;color:var(--body)}
.toc a:hover{background:var(--accent-soft)}
.toc .n{font-family:"IBM Plex Mono",monospace;font-size:10.5px;color:var(--accent);font-weight:600;display:block;margin-bottom:3px}
.toc .t{font-family:Archivo,sans-serif;font-size:14.5px;font-weight:600;color:var(--ink)}
@media (max-width:720px){
  body{font-size:16px}.sheet{padding:0 18px 68px}
  section{grid-template-columns:1fr;gap:0;padding-top:36px;margin-top:36px}
  .rail{padding:0 0 10px;font-size:15px}
  .split{grid-template-columns:1fr}
  .closer,.goldrule{padding:22px 20px}
}
@media print{
  body{background:#fff;color:#111;font-size:10.5pt}
  .sheet{max-width:none;padding:0}
  section{break-inside:avoid;page-break-inside:avoid}
  .q,.card,.callout,table{break-inside:avoid}
  h1{font-size:26pt}h2{font-size:15pt}
  a{color:#111;text-decoration:none}
  .toc{display:none}
}
`;

export const escapeHtml = (s) => String(s ?? '')
  .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

// Minimal inline markdown: **bold**, *italic*, `code`, and literal \n -> <br>
export function md(s) {
  return escapeHtml(s)
    .replace(/`([^`]+)`/g, '<code>$1</code>')
    .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
    .replace(/(^|[^*])\*([^*]+)\*/g, '$1<em>$2</em>')
    .replace(/\n/g, '<br>');
}

export function page({ title, favicon = '', body }) {
  return `<title>${escapeHtml(title)}</title>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="${FONTS}">
<style>${CSS}</style>
<div class="sheet">
${body}
</div>`;
}

export const titleBlock = ({ meta = [], h1, standfirst = '', specs = [], extra = '' }) => `
<header class="titleblock">
  <div class="tb-meta">${meta.map(m => `<span>${m}</span>`).join('')}</div>
  <h1>${h1}</h1>
  ${standfirst ? `<p class="standfirst">${standfirst}</p>` : ''}
  ${specs.length ? `<dl class="specs">${specs.map(s => `<div class="spec"><dt>${escapeHtml(s.k)}</dt><dd>${escapeHtml(s.v)}${s.sub ? `<small>${escapeHtml(s.sub)}</small>` : ''}</dd></div>`).join('')}</dl>` : ''}
  ${extra}
</header>`;

export const sec = (rail, inner) => `<section><div class="rail">${escapeHtml(rail)}</div><div class="body">${inner}</div></section>`;

export const table = (headers, rows, cls = '') => `
<div class="tablewrap"><table${cls ? ` class="${cls}"` : ''}>
${headers ? `<thead><tr>${headers.map(h => `<th${/^\s*(#|Code|Mk|%)/.test(h) ? ' class="mono"' : ''}>${escapeHtml(h)}</th>`).join('')}</tr></thead>` : ''}
<tbody>${rows.map(r => `<tr${r.cls ? ` class="${r.cls}"` : ''}>${(r.cells || r).map(c =>
  typeof c === 'object' ? `<td${c.mono ? ' class="mono"' : ''}>${c.html ?? md(c.t)}</td>` : `<td>${md(c)}</td>`
).join('')}</tr>`).join('')}</tbody>
</table></div>`;

export const callout = (kind, label, html) =>
  `<div class="callout c-${kind}"><span class="lbl">${escapeHtml(label)}</span>${html}</div>`;
