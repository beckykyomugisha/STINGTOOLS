# Site Photos — Mobile slice

Phase 178, slice 3. Mobile UI + offline queue for the Six-Reason / 5-state
audience workflow. Server contract is in
`Planscape.Server/src/Planscape.API/Controllers/SitePhotosController.cs`.

## Routes

- `/site-photos/capture?projectId=…[&anchorIssueId=…][&anchorElementGuid=…][&context=…]`
- `/site-photos/review`
- `/site-photos/gallery[?focus=<photoId>]`

## Files

| Path | Role |
|---|---|
| `app/site-photos/_layout.tsx` | Stack layout for the route group |
| `app/site-photos/capture.tsx` | Rationale → live camera → reason confirm → save / queue |
| `app/site-photos/review.tsx` | PM/Admin/Owner approve/reject (single + bulk + caption) |
| `app/site-photos/gallery.tsx` | Filterable grid + full-screen viewer + reviewer actions |
| `src/components/SitePhotoFab.tsx` | Persistent FAB injected on dashboard / diary |
| `src/components/site-photos/classifier.ts` | Pure auto-classifier scoring function |
| `src/api/endpoints.ts` | `captureSitePhoto`, `listSitePhotos`, `approveSitePhoto`, … |
| `src/utils/offlineQueue.ts` | `CAPTURE_SITE_PHOTO` action + queued-photo file helpers |
| `src/types/api.ts` | `SitePhoto`, `SitePhotoReason`, `SitePhotoAudience`, … |

## Auto-classifier — extending the signals

The classifier (`classifier.ts`) is a pure function so its input contract
is stable. Add a new signal in three steps:

1. **Extend `ClassifierInput`.** Keep all new fields optional so existing
   call sites compile. Use cheap, synchronously-available values only —
   the classifier must never block the shutter.
2. **Add a rule branch in `classifyCapture`.** Higher-priority rules go
   first; document the rationale in a comment. The result must include
   the rule name in `signals.rule` so the server-side audit log can
   group by it later (e.g. `signals.rule === "diary-progress"`).
3. **Wire the signal in the capture screen.** The capture screen
   currently feeds: `context`, `hasAnchorIssueId`, `hasAnchorElementGuid`,
   `hour` (local clock), `hasGps`, `hasActiveWorkPackage`. To plumb a new
   signal, set the corresponding state during the capture stage and pass
   it into `classifyCapture` inside the shutter handler.

Existing rules (highest weight first):

| Rule name | Trigger | Reason | Confidence |
|---|---|---|---|
| `anchor-issue` | FAB launched against an issue | Issue | 0.92 |
| `anchor-element` | FAB launched against a model element | Defect | 0.78 |
| `diary-progress` | From diary screen, working hours, GPS available | Progress | 0.70 |
| `late-afternoon` | From dashboard between 16-20h | Progress | 0.55 |
| `handover-window` | 8-10h or 16-18h with active work-package | AsBuilt | 0.50 |
| `fallback` | Anything else | Reference | 0.30 |

`signals` is serialised to JSON and posted as the multipart field
`classifierSignals`. The server stores it on `SitePhoto.ClassifierSignals`
verbatim — you can run `SELECT classifier_signals -> 'rule' FROM site_photos`
to see how often a rule fires in production and tune from there.

## Audience routing recap

| Reason | Default audience on capture |
|---|---|
| Progress, AsBuilt | PendingReview (queues for PM review) |
| Issue | Internal + auto-create RFI server-side |
| Defect | Internal + auto-create NCR |
| Safety | Internal + auto-create SAFETY (HIGH priority, 4h SLA) |
| Reference | Internal silent — never reaches the client |

## Offline queue

`CAPTURE_SITE_PHOTO` actions persist the photo bytes to
`FileSystem.documentDirectory + 'queued-photos/<uuid>.jpg'` (NOT base64
in AsyncStorage). On flush, replay multipart-POSTs to the capture
endpoint with `queuedClient: true` and deletes the local file on success.
Cap at 200 queued before warning, 500 hard.

`reapOrphanQueuedPhotos()` runs opportunistically to clean up files
referenced by neither the live nor the failed queue (e.g. after a
permanent-failure side-queue eviction).
