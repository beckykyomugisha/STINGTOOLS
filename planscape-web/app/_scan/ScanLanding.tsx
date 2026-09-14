'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { PageHeader } from '@/components/ui';
import { listProjects } from '@/lib/data';
import { api } from '@/lib/api';
import type { Project } from '@/lib/types';

/**
 * The landing page a scanned STING QR code opens.
 *
 * WHY THIS EXISTS
 * ---------------
 * The plugin stamps `https://app.planscape.build/e/{code}/{tag}` on assets and
 * `/s/{code}/{sheet}` on title blocks, so a stock phone camera can open it. Until
 * this page, those paths 404'd: the scan worked and the destination did not,
 * which is only marginally better than the custom scheme it replaced.
 *
 * THE PROJECT CODE PROBLEM, AND WHY IT IS NOT HIDDEN
 * --------------------------------------------------
 * The QR carries a project CODE (`PRJ_ORG_PROJECT_CODE_TXT`), because that is
 * what a Revit model knows about itself. Every API route here is keyed by project
 * GUID. So this page resolves code -> id by listing the projects the signed-in
 * user can see.
 *
 * That resolution can fail in three distinct ways, and this page says which:
 *   - not signed in        -> sign in, then come back to this exact URL
 *   - code matches nothing -> you may not be a member of that project
 *   - code matches several -> say so; do not silently pick one
 *
 * None of them is allowed to render as an empty list. A scan that quietly shows
 * nothing is indistinguishable from a scan that found nothing, and the two need
 * different actions from the person holding the phone.
 */

export type ScanKind = 'element' | 'sheet';

interface TaggedElementRow {
  id: string;
  tag1?: string;
  categoryName?: string;
  familyName?: string;
  level?: string;
  disc?: string;
}

interface SheetLookupRow {
  id: string;
  fileName: string;
  cdeStatus: string;
  revision?: string;
  uploadedAt?: string;
}

type State =
  | { phase: 'loading' }
  | { phase: 'anonymous' }
  | { phase: 'no-project'; code: string }
  | { phase: 'ambiguous'; code: string; matches: Project[] }
  | { phase: 'error'; message: string }
  | { phase: 'ready'; project: Project; elements?: TaggedElementRow[]; sheets?: SheetLookupRow[]; note?: string };

export function ScanLanding({
  kind,
  projectCode,
  value,
  revision,
}: {
  kind: ScanKind;
  projectCode: string;
  /** The ISO 19650 tag, or the sheet number. */
  value: string;
  revision?: string;
}) {
  const [state, setState] = useState<State>({ phase: 'loading' });

  const resolve = useCallback(async () => {
    setState({ phase: 'loading' });
    try {
      const projects = await listProjects();
      const matches = projects.filter(
        p => (p.code || '').toLowerCase() === projectCode.toLowerCase(),
      );

      if (matches.length === 0) return setState({ phase: 'no-project', code: projectCode });
      // Two projects sharing a code is a data problem, not something to paper
      // over by picking the first. Show both and let a person choose.
      if (matches.length > 1) return setState({ phase: 'ambiguous', code: projectCode, matches });

      const project = matches[0];

      if (kind === 'element') {
        const elements = await api<TaggedElementRow[]>(
          `/api/tagsync/elements/search?projectId=${project.id}&q=${encodeURIComponent(value)}`,
        );
        return setState({ phase: 'ready', project, elements });
      }

      const result = await api<{
        matched: number;
        revisionNarrowed: boolean;
        items: SheetLookupRow[];
      }>(
        `/api/projects/${project.id}/documents/by-sheet?number=${encodeURIComponent(value)}` +
          (revision ? `&revision=${encodeURIComponent(revision)}` : ''),
      );

      const note =
        revision && !result.revisionNarrowed && result.items.length > 0
          ? `The code says revision ${revision}, which is not in the register — the print you are holding may be superseded.`
          : undefined;

      return setState({ phase: 'ready', project, sheets: result.items, note });
    } catch (err) {
      const status = (err as { status?: number })?.status;
      // 401 is not "nothing found". It is "we do not know who you are yet", and
      // it has a different next step.
      if (status === 401) return setState({ phase: 'anonymous' });
      setState({ phase: 'error', message: err instanceof Error ? err.message : String(err) });
    }
  }, [kind, projectCode, value, revision]);

  useEffect(() => {
    void resolve();
  }, [resolve]);

  const heading = kind === 'element' ? value : `Sheet ${value}`;
  const subtitle = `Scanned from project ${projectCode}${revision ? ` · revision ${revision}` : ''}`;

  // AppShell is applied by the page components, not here: routes.test.ts requires
  // every page.tsx to render it, and nesting two would double the chrome.
  return (
    <>
      <PageHeader title={heading} description={subtitle} />
      <div style={{ padding: '1rem 0', maxWidth: 720 }}>
        {state.phase === 'loading' && <p>Looking this up…</p>}

        {state.phase === 'anonymous' && (
          <>
            <p>Sign in to open this {kind === 'element' ? 'asset' : 'drawing'}.</p>
            <p>
              <Link href={`/login?next=${encodeURIComponent(currentPath())}`}>Sign in</Link>
            </p>
          </>
        )}

        {state.phase === 'no-project' && (
          <>
            <p>
              No project you can see uses the code <strong>{state.code}</strong>.
            </p>
            <p>
              The code is stamped into the drawing, so it is almost certainly right — you
              are most likely not a member of that project yet. Ask its BIM manager to add
              you, then open this link again.
            </p>
          </>
        )}

        {state.phase === 'ambiguous' && (
          <>
            <p>
              More than one project uses the code <strong>{state.code}</strong>, so this
              link is ambiguous. Pick the right one:
            </p>
            <ul>
              {state.matches.map(p => (
                <li key={p.id}>
                  <Link href={`/projects/${p.id}`}>{p.name}</Link>
                </li>
              ))}
            </ul>
          </>
        )}

        {state.phase === 'error' && (
          <>
            {/* An error is not an empty result. Say which happened. */}
            <p>Could not complete the lookup.</p>
            <pre style={{ whiteSpace: 'pre-wrap' }}>{state.message}</pre>
            <button onClick={() => void resolve()}>Try again</button>
          </>
        )}

        {state.phase === 'ready' && (
          <>
            <p>
              Project: <Link href={`/projects/${state.project.id}`}>{state.project.name}</Link>
            </p>
            {state.note && <p><strong>{state.note}</strong></p>}

            {kind === 'element' &&
              (state.elements && state.elements.length > 0 ? (
                <ul>
                  {state.elements.map(e => (
                    <li key={e.id}>
                      <strong>{e.tag1 || '(untagged)'}</strong>
                      {e.categoryName ? ` · ${e.categoryName}` : ''}
                      {e.familyName ? ` · ${e.familyName}` : ''}
                      {e.level ? ` · ${e.level}` : ''}
                    </li>
                  ))}
                </ul>
              ) : (
                <p>
                  Nothing in {state.project.name} carries the tag <strong>{value}</strong>.
                  The model may not have been synced since this label was printed.
                </p>
              ))}

            {kind === 'sheet' &&
              (state.sheets && state.sheets.length > 0 ? (
                <ul>
                  {state.sheets.map(s => (
                    <li key={s.id}>
                      <Link href={`/projects/${state.project.id}/documents`}>{s.fileName}</Link>
                      {' · '}
                      {s.cdeStatus}
                      {s.revision ? ` · Rev ${s.revision}` : ''}
                    </li>
                  ))}
                </ul>
              ) : (
                <p>
                  No document in {state.project.name} carries sheet <strong>{value}</strong>.
                  It may not have been issued to the CDE yet.
                </p>
              ))}
          </>
        )}
      </div>
    </>
  );
}

function currentPath(): string {
  if (typeof window === 'undefined') return '/';
  return window.location.pathname + window.location.search;
}
