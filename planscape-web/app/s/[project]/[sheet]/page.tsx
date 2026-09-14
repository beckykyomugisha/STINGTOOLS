'use client';

import { AppShell } from '@/components/AppShell';
import { ScanLanding } from '../../../_scan/ScanLanding';

/**
 * Sheet deep link — `https://app.planscape.build/s/{projectCode}/{sheet}?r={rev}`.
 *
 * Stamped into the title block by `SheetQrStamper`. Resolves through
 * `GET /api/projects/{id}/documents/by-sheet`, which orders PUBLISHED first so the
 * top row is what a person on site should be building from.
 */
export default function SheetScanPage({
  params,
  searchParams,
}: {
  params: { project: string; sheet: string };
  searchParams: { r?: string };
}) {
  return (
    <AppShell>
      <ScanLanding
        kind="sheet"
        projectCode={decodeURIComponent(params.project)}
        value={decodeURIComponent(params.sheet)}
        revision={searchParams.r}
      />
    </AppShell>
  );
}
