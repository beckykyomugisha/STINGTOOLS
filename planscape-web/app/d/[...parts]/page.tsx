'use client';

import { AppShell } from '@/components/AppShell';
import { PageHeader } from '@/components/ui';
import { ScanLanding } from '../../_scan/ScanLanding';
import { parseDocPath, prettyScale, prettySheetOfTotal } from '@/lib/docPayload';

/**
 * Document deep link — the rich form stamped by `SheetQrStamper`:
 *
 *   https://app.planscape.build/D/{iso19650-id}/{suitability}/{cde}/{date}
 *                                /{zone}/{sheetOfTotal}/{lod}/{paper}/{scale}/{initials}/{sig}
 *
 * Keyed by the full ISO 19650 identifier (SHT_TAG_1_TXT) rather than the bare
 * sheet number, and carrying the issue record IN the code.
 *
 * WHY THE FACTS RENDER BEFORE THE LOOKUP
 * --------------------------------------
 * Everything above the fold here comes out of the URL itself — no network, no
 * sign-in, no project membership. That is the whole reason the facts are in the
 * code: the person scanning is usually on a site with no signal, holding a print
 * they need to trust. The register lookup below it is the enrichment, not the
 * point, and it is allowed to fail without taking the useful half with it.
 */
export default function DocumentScanPage({ params }: { params: { parts: string[] } }) {
  const payload = parseDocPath(params.parts);

  if (!payload) {
    return (
      <AppShell>
        <PageHeader
          title="Unreadable code"
          description="This link carries no document identifier."
        />
        <div style={{ padding: '1rem 0', maxWidth: 720 }}>
          <p>
            A document link must start with the ISO 19650 identifier, as in
            <code> /d/PRJ-PLNS-L02-DR-A-A-L1-001-P02</code>.
          </p>
          <p>
            Nothing was looked up, because there is nothing here to look up. If you
            scanned this from a drawing, the code was produced by something other than
            StingTools — or truncated.
          </p>
        </div>
      </AppShell>
    );
  }

  const { docId, projectCode, revision, sheetNumber, facts } = payload;

  const rows: [string, string | undefined][] = [
    ['Suitability', facts.suitability],
    ['CDE state', facts.cdeState],
    ['Issued', formatDate(facts.issueDate)],
    ['Zone', facts.zone],
    ['Sheet', prettySheetOfTotal(facts.sheetOfTotal)],
    ['LOD', facts.lod],
    ['Paper', facts.paperSize],
    ['Scale', prettyScale(facts.scale)],
    ['Drawn · checked · approved', facts.initials?.replace(/\./g, ' · ')],
  ];
  const carried = rows.filter(([, v]) => v);

  return (
    <AppShell>
      <PageHeader
        title={sheetNumber ? `Sheet ${sheetNumber}` : docId}
        description={`${docId}${revision ? ` · revision ${revision}` : ''}`}
      />

      <div style={{ padding: '1rem 0', maxWidth: 720 }}>
        <section
          style={{
            border: '1px solid #ddd',
            borderRadius: 6,
            padding: '0.75rem 1rem',
            marginBottom: '1.5rem',
          }}
        >
          <h2 style={{ fontSize: '0.95rem', margin: '0 0 0.5rem' }}>
            Printed on this drawing
          </h2>
          <p style={{ fontSize: '0.8rem', color: '#666', margin: '0 0 0.75rem' }}>
            Read from the code itself — no network needed. These are what the drawing
            said when it was plotted, not what the register says now.
          </p>

          {carried.length === 0 ? (
            <p style={{ margin: 0 }}>
              This code carries only the identifier. The printed cell was too small for
              the issue record — widen it to about 31 mm and re-stamp to carry it.
            </p>
          ) : (
            <dl style={{ display: 'grid', gridTemplateColumns: 'auto 1fr', gap: '0.25rem 1rem', margin: 0 }}>
              {carried.map(([label, value]) => (
                <div key={label} style={{ display: 'contents' }}>
                  <dt style={{ color: '#666' }}>{label}</dt>
                  <dd style={{ margin: 0, fontWeight: 500 }}>{value}</dd>
                </div>
              ))}
            </dl>
          )}
        </section>

        {/* The register lookup. Needs a sheet number: without one there is nothing
            to match on, and a search on the whole identifier would return nothing
            and read as "this drawing is not in the register" — a wrong answer
            rather than an absent one. */}
        {sheetNumber && projectCode ? (
          <ScanLanding
            kind="sheet"
            projectCode={projectCode}
            value={sheetNumber}
            revision={revision}
            hideHeader
          />
        ) : (
          <p style={{ color: '#666' }}>
            The identifier does not split into a project code and sheet number, so the
            register was not searched. Everything above still came out of the code.
          </p>
        )}
      </div>
    </AppShell>
  );
}

/** yyyyMMdd -> yyyy-MM-dd. Anything else is shown as-is rather than guessed at. */
function formatDate(v?: string): string | undefined {
  if (!v) return undefined;
  return /^\d{8}$/.test(v) ? `${v.slice(0, 4)}-${v.slice(4, 6)}-${v.slice(6, 8)}` : v;
}
