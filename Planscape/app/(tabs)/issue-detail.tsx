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
} from '@/api/endpoints';
import { apiFetch, getToken } from '@/api/client';
import type { BimIssue, IssueAttachment, Project } from '@/types/api';
import { theme, getPriorityColor } from '@/utils/theme';
import { imageService, CapturedImage } from '@/services/imageService';
import { locationService } from '@/services/locationService';
import { enqueue } from '@/utils/offlineQueue';
import { crashReporter } from '@/services/crashReporter';

interface GalleryEntry {
  id: string;
  fileName: string;
  thumbnailUri: string | null;
  isLocal?: boolean;
}

export default function IssueDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();

  const [issue, setIssue] = useState<BimIssue | null>(null);
  const [project, setProject] = useState<Project | null>(null);
  const [attachments, setAttachments] = useState<IssueAttachment[]>([]);
  const [gallery, setGallery] = useState<GalleryEntry[]>([]);
  const [authToken, setAuthToken] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadDetail = useCallback(async () => {
    if (!id) return;
    try {
      setError(null);
      // Two-hop fetch: we don't know the projectId without the issue, and
      // we need the project object for the 3D viewer URL (code, not id).
      const allProjects = await listProjects();
      for (const p of allProjects) {
        try {
          const fetched = await apiFetch<BimIssue>(
            `/api/projects/${p.id}/issues/${id}`
          );
          if (fetched) {
            setIssue(fetched);
            setProject(p);
            break;
          }
        } catch (inner) {
          // 404 on wrong project — continue probing. Any non-404 surfaces below.
          const msg = inner instanceof Error ? inner.message : String(inner);
          if (!msg.includes('HTTP 404')) throw inner;
        }
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load issue');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [id]);

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

  async function onRefresh() {
    setRefreshing(true);
    await loadDetail();
  }

  /**
   * Open the Planscape xeokit viewer (wwwroot/viewer/index.html) in the
   * in-app browser, pointed at this issue's project. Uses expo-web-browser's
   * Chrome Custom Tabs on Android and SFSafariViewController on iOS — no
   * new native module required.
   */
  async function openIn3D() {
    if (!project) return;
    try {
      const base = await _getBaseUrl();
      const url = `${base}/viewer/index.html?model=${encodeURIComponent(project.code)}.xkt`;
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

      if (net.isConnected) {
        await uploadIssueAttachment({
          projectId: project.id,
          issueId: issue.id,
          uri: compressed.uri,
          fileName,
          contentType: mimeType,
          latitude: loc?.latitude,
          longitude: loc?.longitude,
        });
        await loadAttachments(project.id, issue.id);
      } else {
        // Offline — queue for replay. The scheduler will flush ATTACH_PHOTO
        // actions next time the app detects connectivity.
        await enqueue('ATTACH_PHOTO', {
          projectId: project.id,
          issueId: issue.id,
          localUri: compressed.uri,
          fileName,
          mimeType,
          latitude: loc?.latitude,
          longitude: loc?.longitude,
        });
        Alert.alert('Queued', 'Photo saved offline — will upload next time you are online.');
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
