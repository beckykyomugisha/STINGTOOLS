/**
 * Phase 94 — Full-screen issue detail (MOB-01 + MOB-06).
 *
 * Pushed from app/(tabs)/issues.tsx via router.push('/issue-detail?id=<id>').
 * Unlike the legacy [id].tsx that lives under app/issues/, this route is
 * nested under the tab group so the bottom-tab bar stays visible — the
 * BIM coordinator can bounce between a raised issue and the dashboard
 * without losing context. Registered with `href: null` in _layout.tsx
 * so it does NOT appear in the bottom tab bar.
 *
 * Adds two capabilities the old modal lacked:
 *   1. A scrollable photo gallery rendered with FlatList, fed from the
 *      server's /api/projects/{pid}/issues/{iid}/attachments endpoint
 *      (Planscape.Server IssuesController — see CLAUDE.md IssuesController
 *      reference). Raw file binary comes from the same endpoint path sans
 *      the /thumbnail suffix.
 *   2. An "Attach Photo" button driven by expo-image-picker with explicit
 *      camera + media-library permission prompts (scanner.tsx pattern).
 *      Uploads go through the standard multipart endpoint when online; when
 *      offline they queue as an ATTACH_PHOTO action via offlineQueue.
 */

import { useEffect, useState, useCallback } from 'react';
import {
  View,
  Text,
  StyleSheet,
  FlatList,
  Image,
  TouchableOpacity,
  ActivityIndicator,
  ScrollView,
  Alert,
  Platform,
  RefreshControl,
  Linking,
  Platform,
} from 'react-native';
import { useLocalSearchParams, Stack, router } from 'expo-router';
import NetInfo from '@react-native-community/netinfo';
import * as WebBrowser from 'expo-web-browser';
import {
  _getBaseUrl,
  listIssueAttachments,
  getAttachmentThumbnailUrl,
  uploadIssueAttachment,
  listProjects,
  updateIssue,
  listProjectMembers,
} from '@/api/endpoints';
import { apiFetch, getToken } from '@/api/client';
import type { BimIssue, IssueAttachment, Project, ProjectMember } from '@/types/api';
import { theme, getPriorityColor } from '@/utils/theme';
import { imageService } from '@/services/imageService';
import { locationService } from '@/services/locationService';
import { enqueue, onSyncComplete } from '@/utils/offlineQueue';
import { crashReporter } from '@/services/crashReporter';
import { useAuthStore } from '@/stores/authStore';

interface GalleryEntry {
  id: string;
  fileName: string;
  thumbnailUri: string | null;
  isLocal?: boolean;
}

export default function IssueDetailScreen() {
  // Phase 96 — accept optional projectId so the notification router (or any
  // screen that already knows the project) can skip the O(n) probe across
  // every project the user has access to. Large orgs routinely have 20+
  // projects; probing each one serially produced a multi-second load time.
  const { id, projectId: paramProjectId } = useLocalSearchParams<{ id: string; projectId?: string }>();

  const [issue, setIssue] = useState<BimIssue | null>(null);
  const [project, setProject] = useState<Project | null>(null);
  const [attachments, setAttachments] = useState<IssueAttachment[]>([]);
  const [gallery, setGallery] = useState<GalleryEntry[]>([]);
  const [authToken, setAuthToken] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [transitioning, setTransitioning] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [currentUserRole, setCurrentUserRole] = useState<string | null>(null);

  const authUserId = useAuthStore((s) => s.userId);

  const loadDetail = useCallback(async () => {
    if (!id) return;
    try {
      setError(null);

      // Phase 96 fast-path: caller supplied the projectId, so skip the probe
      // entirely. notificationTapRouter always has it; scanner deep-links
      // always have it; only the legacy /issues/[id].tsx route without
      // projectId falls back to the scan below.
      if (paramProjectId) {
        const allProjects = await listProjects();
        const match = allProjects.find((p) => p.id === paramProjectId);
        if (match) {
          try {
            const fetched = await apiFetch<BimIssue>(`/api/projects/${match.id}/issues/${id}`);
            if (fetched) {
              setIssue(fetched);
              setProject(match);
              return;
            }
          } catch (inner) {
            // Fall through to the full-scan below if the hint was stale
            crashReporter.warn('issue-detail.tsx:loadDetail.paramProjectIdMiss', {
              projectId: paramProjectId, err: String(inner),
            });
          }
        }
      }

      // Fallback — probe each project until we hit (worst case O(n) HTTP calls).
      // Bounded to 3 concurrent requests via Promise.all so a 20-project user
      // doesn't wait for serial round-trips.
      const allProjects = await listProjects();
      for (let i = 0; i < allProjects.length; i += 3) {
        const batch = allProjects.slice(i, i + 3);
        const results = await Promise.all(batch.map(async (p) => {
          try {
            const fetched = await apiFetch<BimIssue>(`/api/projects/${p.id}/issues/${id}`);
            return fetched ? { p, fetched } : null;
          } catch {
            return null;
          }
        }));
        const hit = results.find((r) => r !== null);
        if (hit) {
          setIssue(hit.fetched);
          setProject(hit.p);
          return;
        }
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load issue');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [id, paramProjectId]);

  const loadAttachments = useCallback(async (projectId: string, issueId: string) => {
    try {
      const list = await listIssueAttachments(projectId, issueId);
      setAttachments(list);
      const tok = await getToken();
      setAuthToken(tok);
      const entries: GalleryEntry[] = [];
      for (const a of list) {
        const isImage = (a.contentType || '').startsWith('image/');
        entries.push({
          id: a.id,
          fileName: a.fileName,
          thumbnailUri: isImage
            ? await getAttachmentThumbnailUrl(projectId, issueId, a.id, 300)
            : null,
        });
      }
      setGallery(entries);
    } catch (err) {
      crashReporter.warn('issue-detail.tsx:loadAttachments', { e: String(err) });
    }
  }, []);

  useEffect(() => {
    loadDetail();
  }, [loadDetail]);

  useEffect(() => {
    if (issue && project) {
      loadAttachments(project.id, issue.id);
    }
  }, [issue, project, loadAttachments]);

  /**
   * Phase 96 — look up the current user's project role so action gating can
   * hide edit affordances from read-only members. Falls back silently if the
   * endpoint 403s — treating "unknown role" as "member" (least privilege).
   */
  useEffect(() => {
    (async () => {
      if (!project || !authUserId) return;
      try {
        const members = await listProjectMembers(project.id);
        const mine = members.find((m) => m.userId === authUserId);
        if (mine) setCurrentUserRole(mine.projectRole);
      } catch (err) {
        crashReporter.warn('issue-detail.tsx:loadRole', { e: String(err) });
      }
    })();
  }, [project, authUserId]);

  /**
   * Phase 96 — refresh the attachment gallery when the offline queue finishes
   * a drain. Without this hook the LOCAL-tagged optimistic tiles would remain
   * stale until the user manually pulled-to-refresh.
   */
  useEffect(() => {
    if (!issue || !project) return;
    const unsub = onSyncComplete((result) => {
      if (result.succeeded > 0) {
        loadAttachments(project.id, issue.id);
      }
    });
    return unsub;
  }, [issue, project, loadAttachments]);

  async function onRefresh() {
    setRefreshing(true);
    await loadDetail();
  }

  /**
   * Open the Planscape xeokit viewer (wwwroot/viewer/index.html) in the
   * in-app browser, pointed at this issue's project. Uses expo-web-browser's
   * Chrome Custom Tabs on Android and SFSafariViewController on iOS — no
   * new native module required.
   *
   * Phase 96 — when the issue has model anchor fields (modelElementGuid,
   * modelX/Y/Z), append them as query params so the viewer can zoom to the
   * element on load. xeokit viewer reads ?element=<guid> to select+frame
   * a specific entity and ?camera=<x>,<y>,<z> to position the camera.
   */
  async function openIn3D() {
    if (!project || !issue) return;
    try {
      const base = await _getBaseUrl();
      const params = new URLSearchParams();
      params.set('model', `${project.code}.xkt`);
      if (issue.modelElementGuid) params.set('element', issue.modelElementGuid);
      if (typeof issue.modelX === 'number' && typeof issue.modelY === 'number' && typeof issue.modelZ === 'number') {
        params.set('camera', `${issue.modelX},${issue.modelY},${issue.modelZ}`);
      }
      if (issue.elementIds) params.set('highlight', issue.elementIds);
      // Always fit-zoom so the coordinator isn't dumped at origin when the
      // viewer opens and the element is off-screen.
      params.set('zoom', 'fit');
      const url = `${base}/viewer/index.html?${params.toString()}`;
      await WebBrowser.openBrowserAsync(url, {
        toolbarColor: theme.colors.primary,
        controlsColor: theme.colors.accent,
        dismissButtonStyle: 'close',
      });
    } catch (err) {
      Alert.alert('Viewer unavailable', err instanceof Error ? err.message : String(err));
    }
  }

  /**
   * Phase 96 — role-gated status transition. Any member can advance an issue
   * they are assigned to; OPEN→CLOSED jumps require coordinator+ roles to
   * prevent rank-and-file users from silently closing NCRs without resolution.
   * Returns `true` if the role can perform the transition.
   */
  function canTransition(from: string, to: string): boolean {
    const role = (currentUserRole ?? '').toLowerCase();
    const isCoordinator = role === 'admin' || role === 'owner'
      || role === 'coordinator' || role === 'manager' || role === 'bim_manager'
      || role === 'bim manager';
    // Anyone can progress through the normal funnel
    if (from === 'OPEN' && to === 'IN_PROGRESS') return true;
    if (from === 'IN_PROGRESS' && to === 'RESOLVED') return true;
    // Re-opening a closed issue is safe — doesn't destroy data
    if ((from === 'CLOSED' || from === 'RESOLVED') && to === 'OPEN') return true;
    // Only coordinators can skip steps or close issues
    return isCoordinator;
  }

  async function transitionTo(newStatus: BimIssue['status']) {
    if (!issue || !project || transitioning) return;
    if (!canTransition(issue.status, newStatus)) {
      Alert.alert(
        'Permission denied',
        `Your project role (${currentUserRole ?? 'unknown'}) cannot make this transition. Ask a BIM Coordinator to close or reassign this issue.`,
      );
      return;
    }
    setTransitioning(true);
    try {
      const net = await NetInfo.fetch();
      const updates: Partial<BimIssue> = { status: newStatus };
      if (newStatus === 'RESOLVED') updates.resolvedAt = new Date().toISOString();
      if (net.isConnected) {
        const updated = await updateIssue(project.id, issue.id, updates);
        setIssue(updated);
      } else {
        await enqueue('UPDATE_ISSUE', {
          projectId: project.id,
          issueId: issue.id,
          updates,
        });
        // Optimistic UI so the user sees the change immediately
        setIssue({ ...issue, ...updates, updatedAt: new Date().toISOString() } as BimIssue);
        Alert.alert('Queued', 'Status change saved offline — will sync next time you are online.');
      }
    } catch (err) {
      Alert.alert('Update failed', err instanceof Error ? err.message : String(err));
    } finally {
      setTransitioning(false);
    }
  }

  /**
   * Camera-vs-library picker — scanner.tsx uses the same confirmation UX.
   * The underlying imageService already prompts for OS-level permissions and
   * returns null on denial, so we just surface that as a no-op.
   */
  function handleAttachPress() {
    Alert.alert('Attach Photo', 'How would you like to add a photo?', [
      { text: 'Camera', onPress: () => pickAndUpload('camera') },
      { text: 'Library', onPress: () => pickAndUpload('library') },
      { text: 'Cancel', style: 'cancel' },
    ]);
  }

  async function pickAndUpload(source: 'camera' | 'library') {
    if (!issue || !project) return;
    setUploading(true);
    try {
      const captured = source === 'camera'
        ? await imageService.captureFromCamera()
        : await imageService.pickFromLibrary();
      if (!captured) {
        setUploading(false);
        return;
      }
      const compressed = await imageService.compress(captured.uri).catch(() => captured);

      // Optimistic gallery tile so the user sees immediate feedback, even
      // when the upload will be queued for later replay.
      const localId = `local-${Date.now()}`;
      setGallery((prev) => [
        ...prev,
        { id: localId, fileName: compressed.fileName ?? 'New photo', thumbnailUri: compressed.uri, isLocal: true },
      ]);

      // NEW-INFO-03 — GPS best-effort so the server's geofence/EXIF logic can
      // stamp the attachment with site coordinates.
      let loc: { latitude: number; longitude: number } | null = null;
      try {
        const cur = await locationService.getCurrent();
        if (cur) loc = { latitude: cur.latitude, longitude: cur.longitude };
      } catch (e) {
        crashReporter.warn('issue-detail.tsx:pickAndUpload.location', { e: String(e) });
      }

      const net = await NetInfo.fetch();
      const fileName = compressed.fileName ?? `photo-${Date.now()}.jpg`;
      const mimeType = compressed.type ?? 'image/jpeg';

      // Phase 96 — Wi-Fi awareness. If the user is on a metered cellular
      // connection AND the compressed file is over 5MB, give them the choice
      // of waiting for Wi-Fi (queue) or proceeding anyway.
      const decision = await imageService.classifyUpload(compressed.fileSize);

      async function queueForLater(): Promise<void> {
        await enqueue('ATTACH_PHOTO', {
          projectId: project!.id,
          issueId: issue!.id,
          localUri: compressed.uri,
          fileName,
          mimeType,
          latitude: loc?.latitude,
          longitude: loc?.longitude,
        });
      }

      async function uploadNow(): Promise<void> {
        await uploadIssueAttachment({
          projectId: project!.id,
          issueId: issue!.id,
          uri: compressed.uri,
          fileName,
          contentType: mimeType,
          latitude: loc?.latitude,
          longitude: loc?.longitude,
        });
        await loadAttachments(project!.id, issue!.id);
      }

      if (!net.isConnected) {
        await queueForLater();
        Alert.alert('Queued', 'Photo saved offline — will upload next time you are online.');
      } else if (decision.reason === 'blocked_cellular') {
        // Let the coordinator decide — on a £10k-per-minute site that photo
        // may be urgent enough to warrant the mobile data cost.
        Alert.alert(
          'Large photo on cellular',
          `This photo is ${decision.sizeMB.toFixed(1)} MB and you are on mobile data. Save it to sync on Wi-Fi, or upload now?`,
          [
            { text: 'Wait for Wi-Fi', onPress: async () => {
              await queueForLater();
              Alert.alert('Queued', 'Will upload automatically when Wi-Fi is available.');
            } },
            { text: 'Upload now', onPress: async () => {
              try { await uploadNow(); }
              catch (e) { Alert.alert('Upload failed', e instanceof Error ? e.message : String(e)); }
            } },
            { text: 'Cancel', style: 'cancel', onPress: () => {
              // Strip the optimistic tile we added above
              setGallery((prev) => prev.filter((g) => g.id !== localId));
            } },
          ],
        );
      } else {
        await uploadNow();
      }
    } catch (err) {
      Alert.alert('Upload failed', err instanceof Error ? err.message : String(err));
    } finally {
      setUploading(false);
    }
  }

  if (loading) {
    return (
      <View style={styles.center}>
        <ActivityIndicator size="large" color={theme.colors.accent} />
      </View>
    );
  }

  if (error || !issue || !project) {
    return (
      <View style={styles.center}>
        <Text style={styles.errorText}>{error ?? 'Issue not found'}</Text>
        <TouchableOpacity style={styles.retryButton} onPress={() => router.back()}>
          <Text style={styles.retryButtonText}>Back</Text>
        </TouchableOpacity>
      </View>
    );
  }

  const slaHours = issue.priority === 'CRITICAL' ? 4
    : issue.priority === 'HIGH' ? 24
    : issue.priority === 'MEDIUM' ? 168
    : 336;
  const ageHours = Math.floor((Date.now() - new Date(issue.createdAt).getTime()) / (1000 * 60 * 60));
  const slaBreached = (issue.status === 'OPEN' || issue.status === 'IN_PROGRESS') && ageHours > slaHours;

  return (
    <View style={styles.root}>
      <Stack.Screen options={{ title: issue.issueCode || 'Issue', headerShown: true }} />
      <ScrollView
        contentContainerStyle={styles.scrollContent}
        refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={theme.colors.accent} />}
      >
        {/* Header */}
        <View style={styles.header}>
          <View style={styles.headerRow}>
            <View style={[styles.priorityBadge, { backgroundColor: getPriorityColor(issue.priority) }]}>
              <Text style={styles.priorityText}>{issue.priority}</Text>
            </View>
            <View style={styles.statusBadge}>
              <Text style={styles.statusText}>{issue.status.replace('_', ' ')}</Text>
            </View>
            <Text style={styles.typeText}>{issue.type}</Text>
          </View>
          <Text style={styles.code}>{issue.issueCode}</Text>
          <Text style={styles.title}>{issue.title}</Text>
          {issue.description ? (
            <Text style={styles.description}>{issue.description}</Text>
          ) : null}
        </View>

        {/* SLA Strip */}
        <View style={[styles.slaStrip, slaBreached && styles.slaStripBreached]}>
          <Text style={styles.slaLabel}>
            {slaBreached ? 'SLA BREACHED' : 'SLA'}
          </Text>
          <Text style={styles.slaValue}>
            {ageHours}h open · {slaHours}h threshold
          </Text>
        </View>

        {/* Fields grid */}
        <View style={styles.grid}>
          <Field label="Type" value={issue.type} />
          <Field label="Priority" value={issue.priority} />
          <Field label="Status" value={issue.status} />
          <Field label="Discipline" value={issue.discipline || '—'} />
          <Field label="Assignee" value={issue.assignee || 'Unassigned'} />
          <Field label="Revision" value={issue.revision || '—'} />
          <Field label="Created" value={formatDate(issue.createdAt)} />
          <Field label="Updated" value={formatDate(issue.updatedAt)} />
        </View>

        {/* Phase 142 — GPS strip. We capture coords at create time but never
            surfaced them; managers walking the site need a one-tap path to
            their device's map app. Falls back to platform-appropriate URL
            schemes (geo: on Android, maps:// on iOS, Google Maps web on web). */}
        {(issue.latitude != null && issue.longitude != null) && (
          <TouchableOpacity
            style={styles.gpsStrip}
            onPress={() => openInMaps(issue.latitude!, issue.longitude!, issue.issueCode)}
            accessibilityLabel="Open issue location in maps"
          >
            <Text style={styles.gpsEmoji}>📍</Text>
            <View style={{ flex: 1 }}>
              <Text style={styles.gpsTitle}>
                {issue.latitude!.toFixed(5)}, {issue.longitude!.toFixed(5)}
              </Text>
              <Text style={styles.gpsSub}>
                {(issue.locationAccuracy ?? 0) > 0
                  ? `±${Math.round(issue.locationAccuracy ?? 0)} m · tap to open in maps`
                  : 'EXIF · tap to open in maps'}
              </Text>
            </View>
            <Text style={styles.gpsArrow}>›</Text>
          </TouchableOpacity>
        )}

        {/* Phase 96 — status transition bar. Hidden on CLOSED unless the
            coordinator wants to re-open. Buttons disabled in-flight to
            prevent double-submit. Wifi-aware: the underlying transitionTo
            queues offline when disconnected. */}
        <View style={styles.transitionBar}>
          <Text style={styles.transitionLabel}>Advance to:</Text>
          <View style={styles.transitionButtons}>
            {issue.status === 'OPEN' && (
              <TransitionButton label="In Progress" color={theme.colors.warning}
                disabled={transitioning} onPress={() => transitionTo('IN_PROGRESS')} />
            )}
            {(issue.status === 'OPEN' || issue.status === 'IN_PROGRESS') && (
              <TransitionButton label="Resolved" color={theme.colors.success}
                disabled={transitioning} onPress={() => transitionTo('RESOLVED')} />
            )}
            {issue.status === 'RESOLVED' && (
              <TransitionButton label="Close" color={theme.colors.disabled}
                disabled={transitioning} onPress={() => transitionTo('CLOSED')} />
            )}
            {(issue.status === 'CLOSED' || issue.status === 'RESOLVED') && (
              <TransitionButton label="Re-open" color={theme.colors.danger}
                disabled={transitioning} onPress={() => transitionTo('OPEN')} />
            )}
            {transitioning && <ActivityIndicator color={theme.colors.accent} size="small" />}
          </View>
        </View>

        {/* Action bar */}
        <View style={styles.actionBar}>
          <TouchableOpacity
            style={[styles.actionButton, styles.actionButtonPrimary]}
            onPress={openIn3D}
            accessibilityRole="button"
            accessibilityLabel="Open the 3D model viewer for this project"
          >
            <Text style={styles.actionButtonPrimaryText}>🧊  View in 3D</Text>
          </TouchableOpacity>
          <TouchableOpacity
            style={[styles.actionButton, styles.actionButtonSecondary]}
            onPress={handleAttachPress}
            disabled={uploading}
            accessibilityRole="button"
            accessibilityLabel="Attach a photo to this issue"
          >
            {uploading ? (
              <ActivityIndicator color={theme.colors.surface} size="small" />
            ) : (
              <Text style={styles.actionButtonSecondaryText}>📷  Attach Photo</Text>
            )}
          </TouchableOpacity>
        </View>

        {/* Photo gallery */}
        <View style={styles.gallerySection}>
          <Text style={styles.galleryHeader}>
            Photos ({gallery.length})
          </Text>
          {gallery.length === 0 ? (
            <Text style={styles.galleryEmpty}>No photos yet. Tap Attach Photo to add one.</Text>
          ) : (
            <FlatList
              horizontal
              data={gallery}
              keyExtractor={(g) => g.id}
              showsHorizontalScrollIndicator={false}
              contentContainerStyle={styles.galleryList}
              renderItem={({ item }) => (
                <View style={styles.tile}>
                  {item.thumbnailUri ? (
                    <Image
                      source={{
                        uri: item.thumbnailUri,
                        headers: !item.isLocal && authToken
                          ? { Authorization: `Bearer ${authToken}` }
                          : undefined,
                      }}
                      style={styles.thumb}
                      resizeMode="cover"
                    />
                  ) : (
                    <View style={[styles.thumb, styles.fileFallback]}>
                      <Text style={styles.fileIcon}>📄</Text>
                    </View>
                  )}
                  <Text numberOfLines={1} style={styles.caption}>{item.fileName}</Text>
                  {item.isLocal ? (
                    <Text style={styles.localBadge}>LOCAL</Text>
                  ) : null}
                </View>
              )}
            />
          )}
        </View>

        {/* Linked elements */}
        {issue.elementIds ? (
          <View style={styles.elementsBox}>
            <Text style={styles.elementsLabel}>Linked Elements</Text>
            <Text style={styles.elementsValue}>{issue.elementIds}</Text>
          </View>
        ) : null}
      </ScrollView>
    </View>
  );
}

function Field({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.field}>
      <Text style={styles.fieldLabel}>{label}</Text>
      <Text style={styles.fieldValue}>{value}</Text>
    </View>
  );
}

function TransitionButton({
  label, color, disabled, onPress,
}: { label: string; color: string; disabled: boolean; onPress: () => void }) {
  return (
    <TouchableOpacity
      style={[styles.transitionButton, { borderColor: color, backgroundColor: color + '18' }, disabled && { opacity: 0.5 }]}
      onPress={onPress}
      disabled={disabled}
      accessibilityRole="button"
      accessibilityLabel={`Move issue to ${label}`}
    >
      <Text style={[styles.transitionButtonText, { color }]}>{label}</Text>
    </TouchableOpacity>
  );
}

// Phase 142 — open the issue's GPS in the device's native map app.
// Android understands `geo:lat,lng?q=lat,lng(label)`; iOS Apple Maps
// understands `maps://?ll=lat,lng&q=label`; everything else falls back to
// Google Maps web. We never throw — a missing map app on a degoogled
// Android still opens the URL handler chooser.
function openInMaps(lat: number, lng: number, label?: string) {
  const safeLabel = encodeURIComponent(label ?? 'Issue');
  const url = Platform.select({
    ios: `maps://?ll=${lat},${lng}&q=${safeLabel}`,
    android: `geo:${lat},${lng}?q=${lat},${lng}(${safeLabel})`,
    default: `https://www.google.com/maps/search/?api=1&query=${lat},${lng}`,
  })!;
  Linking.canOpenURL(url)
    .then((ok) => Linking.openURL(ok ? url : `https://www.google.com/maps/search/?api=1&query=${lat},${lng}`))
    .catch(() => Linking.openURL(`https://www.google.com/maps/search/?api=1&query=${lat},${lng}`));
}

function formatDate(iso: string): string {
  if (!iso) return '—';
  try {
    return new Date(iso).toLocaleDateString(undefined, {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
    });
  } catch (e) {
    crashReporter.warn('issue-detail.tsx:formatDate', { e: String(e) });
    return iso;
  }
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: theme.colors.background },
  center: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    padding: theme.spacing.lg,
    backgroundColor: theme.colors.background,
  },
  errorText: {
    fontSize: theme.fontSize.md,
    color: theme.colors.danger,
    marginBottom: theme.spacing.md,
  },
  retryButton: {
    backgroundColor: theme.colors.accent,
    borderRadius: theme.borderRadius.md,
    paddingHorizontal: theme.spacing.lg,
    paddingVertical: theme.spacing.sm,
  },
  retryButtonText: { color: theme.colors.surface, fontWeight: '600' },

  scrollContent: { paddingBottom: theme.spacing.xl },

  header: {
    backgroundColor: theme.colors.surface,
    padding: theme.spacing.lg,
    borderBottomWidth: 1,
    borderBottomColor: theme.colors.border,
  },
  headerRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: theme.spacing.sm,
    marginBottom: theme.spacing.sm,
  },
  priorityBadge: {
    borderRadius: theme.borderRadius.sm,
    paddingHorizontal: 10,
    paddingVertical: 4,
  },
  priorityText: { color: '#fff', fontSize: theme.fontSize.xs, fontWeight: '700' },
  statusBadge: {
    backgroundColor: theme.colors.background,
    borderRadius: theme.borderRadius.sm,
    paddingHorizontal: 8,
    paddingVertical: 3,
  },
  statusText: { fontSize: theme.fontSize.xs, fontWeight: '600', color: theme.colors.text },
  typeText: { fontSize: theme.fontSize.xs, fontWeight: '600', color: theme.colors.textSecondary },
  code: {
    fontSize: theme.fontSize.sm,
    fontWeight: '600',
    color: theme.colors.textSecondary,
    marginBottom: 4,
  },
  title: {
    fontSize: theme.fontSize.xl,
    fontWeight: '700',
    color: theme.colors.text,
    marginBottom: theme.spacing.sm,
  },
  description: {
    fontSize: theme.fontSize.md,
    color: theme.colors.text,
    lineHeight: 22,
  },

  slaStrip: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    padding: theme.spacing.md,
    backgroundColor: theme.colors.surface,
    borderBottomWidth: 1,
    borderBottomColor: theme.colors.border,
  },
  slaStripBreached: {
    backgroundColor: '#FFEBEE',
  },
  slaLabel: {
    fontSize: theme.fontSize.xs,
    fontWeight: '700',
    color: theme.colors.textSecondary,
    letterSpacing: 0.5,
  },
  slaValue: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.text,
    fontFamily: Platform.OS === 'ios' ? 'Menlo' : 'monospace',
  },

  grid: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: theme.spacing.sm,
    padding: theme.spacing.md,
  },
  field: {
    width: '47%',
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.sm,
  },
  fieldLabel: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
    marginBottom: 2,
  },
  fieldValue: {
    fontSize: theme.fontSize.md,
    fontWeight: '500',
    color: theme.colors.text,
  },

  // Phase 142 — GPS strip
  gpsStrip: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.md,
    marginHorizontal: theme.spacing.md,
    marginBottom: theme.spacing.md,
    borderLeftWidth: 4,
    borderLeftColor: theme.colors.accent,
  },
  gpsEmoji: { fontSize: 22, marginRight: theme.spacing.sm },
  gpsTitle: { fontSize: theme.fontSize.sm, color: theme.colors.text, fontWeight: '600' },
  gpsSub: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary, marginTop: 2 },
  gpsArrow: { fontSize: theme.fontSize.xl, color: theme.colors.textSecondary, paddingHorizontal: theme.spacing.sm },

  transitionBar: {
    paddingHorizontal: theme.spacing.md,
    paddingVertical: theme.spacing.sm,
    backgroundColor: theme.colors.surface,
    borderBottomWidth: 1,
    borderBottomColor: theme.colors.border,
  },
  transitionLabel: {
    fontSize: theme.fontSize.xs,
    fontWeight: '700',
    color: theme.colors.textSecondary,
    letterSpacing: 0.5,
    marginBottom: 6,
  },
  transitionButtons: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: theme.spacing.sm,
    alignItems: 'center',
  },
  transitionButton: {
    paddingHorizontal: theme.spacing.md,
    paddingVertical: theme.spacing.xs + 2,
    borderRadius: theme.borderRadius.md,
    borderWidth: 1.5,
  },
  transitionButtonText: {
    fontSize: theme.fontSize.sm,
    fontWeight: '700',
  },
  actionBar: {
    flexDirection: 'row',
    gap: theme.spacing.sm,
    paddingHorizontal: theme.spacing.md,
    paddingVertical: theme.spacing.sm,
  },
  actionButton: {
    flex: 1,
    paddingVertical: theme.spacing.sm + 2,
    borderRadius: theme.borderRadius.md,
    alignItems: 'center',
    justifyContent: 'center',
  },
  actionButtonPrimary: {
    backgroundColor: theme.colors.primary,
  },
  actionButtonPrimaryText: {
    color: theme.colors.surface,
    fontSize: theme.fontSize.md,
    fontWeight: '600',
  },
  actionButtonSecondary: {
    backgroundColor: theme.colors.accent,
  },
  actionButtonSecondaryText: {
    color: theme.colors.surface,
    fontSize: theme.fontSize.md,
    fontWeight: '600',
  },

  gallerySection: {
    padding: theme.spacing.md,
  },
  galleryHeader: {
    fontSize: theme.fontSize.sm,
    fontWeight: '600',
    color: theme.colors.textSecondary,
    marginBottom: theme.spacing.sm,
  },
  galleryEmpty: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.textSecondary,
    fontStyle: 'italic',
  },
  galleryList: {
    paddingRight: theme.spacing.md,
  },
  tile: {
    width: 104,
    marginRight: theme.spacing.sm,
  },
  thumb: {
    width: 104,
    height: 104,
    borderRadius: theme.borderRadius.md,
    backgroundColor: theme.colors.surface,
    borderWidth: 1,
    borderColor: theme.colors.border,
  },
  fileFallback: {
    alignItems: 'center',
    justifyContent: 'center',
  },
  fileIcon: { fontSize: 36 },
  caption: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
    marginTop: 4,
  },
  localBadge: {
    fontSize: 9,
    fontWeight: '700',
    color: theme.colors.warning,
    letterSpacing: 0.5,
  },

  elementsBox: {
    margin: theme.spacing.md,
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.md,
  },
  elementsLabel: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
    marginBottom: 4,
  },
  elementsValue: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.text,
    fontFamily: Platform.OS === 'ios' ? 'Menlo' : 'monospace',
  },
});
