'use client';

import { AppShell } from '@/components/AppShell';
import { ScanLanding } from '../../../_scan/ScanLanding';

/**
 * Element deep link — `https://app.planscape.build/e/{projectCode}/{tag}`.
 *
 * What `StingQrFormat.BuildElementUrl` stamps into every element QR the plugin
 * generates. Before this route existed the URL 404'd: the scan worked and the
 * destination did not, which is only marginally better than the custom scheme it
 * replaced.
 *
 * The `?u=` UniqueId the payload may also carry is deliberately ignored — it is a
 * Revit-side host id, not a cross-host key (see ContractDtos.cs), and the tag is
 * what this server can actually search on.
 */
export default function ElementScanPage({
  params,
}: {
  params: { project: string; tag: string };
}) {
  return (
    <AppShell>
      <ScanLanding
        kind="element"
        projectCode={decodeURIComponent(params.project)}
        value={decodeURIComponent(params.tag)}
      />
    </AppShell>
  );
}
