// Phase 178 — Project-wide site photo gallery.
//
// Filterable thumbnail grid (Reason / Level / Zone / date range). Tap a
// thumbnail to open the full-screen viewer with caption + metadata. PM /
// Admin / Owner reviewers see Approve / Reject controls inline; the
// Withdraw button surfaces only when the photo is currently ClientPortal.

import { useEffect, useMemo, useState, useCallback } from 'react';
import {
  View,
  Text,
  StyleSheet,
  ScrollView,
  TouchableOpacity,
  ActivityIndicator,
  RefreshControl,
  TextInput,
  Image,
  Modal,
  Alert,
  Dimensions,
} from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { theme } from '@/utils/theme';
import { useProjectStore } from '@/stores/projectStore';
import { NdaPromptModal } from '@/components/site-photos/NdaPromptModal';
import {
  listSitePhotos,
  getSitePhotoFile,
  approveSitePhoto,
  rejectSitePhoto,
  withdrawSitePhoto,
  getMyProjectAccess,
} from '@/api/endpoints';
import type {
  SitePhoto,
  SitePhotoReason,
  SitePhotoAudience,
  SitePhotoListFilters,
} from '@/types/api';

const REASON_OPTIONS: Array<SitePhotoReason | 'All'> = [
  'All', 'Progress', 'Issue', 'Defect', 'Safety', 'AsBuilt', 'Reference',
];
const AUDIENCE_OPTIONS: Array<SitePhotoAudience | 'All'> = [
  'All', 'Internal', 'PendingReview', 'Approved', 'ClientPortal', 'Withdrawn',
];

interface ResolvedThumb { url: string; headers: Record<string, string>; }
type ResolvedThumbRecord = Record<string, ResolvedThumb>;

const APPROVER_ROLES = new Set(['PM', 'Admin', 'Owner']);

const SCREEN_WIDTH = Dimensions.get('window').width;
const COLS = 3;
const GUTTER = theme.spacing.xs;
const THUMB_SIZE = (SCREEN_WIDTH - theme.spacing.md * 2 - GUTTER * (COLS - 1)) / COLS;

export default function GalleryScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ focus?: string }>();
  const projectId = useProjectStore((s) => s.active?.id);

  const [reason, setReason] = useState<SitePhotoReason | 'All'>('All');
  const [audience, setAudience] = useState<SitePhotoAudience | 'All'>('All');
  const [levelCode, setLevelCode] = useState('');
  const [zoneCode, setZoneCode] = useState('');

  const [photos, setPhotos] = useState<SitePhoto[]>([]);
  const [thumbs, setThumbs] = useState<ResolvedThumbRecord>({});
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [focused, setFocused] = useState<SitePhoto | null>(null);
  const [isApprover, setIsApprover] = useState(false);
  // Phase 179.2 — NDA-gated photos in the current page; tile click
  // routes through the NdaPromptModal until acceptance lands.
  const [ndaRequired, setNdaRequired] = useState<Set<string>>(new Set());
  const [ndaPrompt, setNdaPrompt] = useState<string | null>(null);

  const filters: SitePhotoListFilters = useMemo(() => ({
    reason: reason === 'All' ? undefined : reason,
    audience: audience === 'All' ? undefined : audience,
    levelCode: levelCode.trim() || undefined,
    zoneCode: zoneCode.trim() || undefined,
    pageSize: 200,
  }), [reason, audience, levelCode, zoneCode]);

  const loadAuth = useCallback(async () => {
    if (!projectId) return;
    try {
      const access = await getMyProjectAccess(projectId);
      const role = access.projectRole ?? '';
      setIsApprover(APPROVER_ROLES.has(role) || access.bypassesAcl);
    } catch { /* default false */ }
  }, [projectId]);

  const load = useCallback(async () => {
    if (!projectId) return;
    try {
      setError(null);
      const res = await listSitePhotos(projectId, filters);
      setPhotos(res.items);
      // Phase 179.2 — surface the lock badge on tiles whose photo
      // requires NDA acceptance. The id is removed from this set
      // after the user accepts and the list refreshes.
      setNdaRequired(new Set(res.ndaRequiredIds ?? []));

      const next: ResolvedThumbRecord = {};
      for (const p of res.items) {
        // Skip thumbnail fetch for NDA-gated photos — the server would
        // return 403 nda_required and Image would render a broken icon.
        if ((res.ndaRequiredIds ?? []).includes(p.id)) continue;
        next[p.id] = await getSitePhotoFile(projectId, p.id);
      }
      setThumbs(next);

      // If the route was opened with ?focus=<id>, jump straight into viewer.
      if (params.focus) {
        const target = res.items.find((x) => x.id === params.focus);
        if (target) setFocused(target);
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to load photos');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [projectId, filters, params.focus]);

  useEffect(() => { loadAuth(); }, [loadAuth]);
  useEffect(() => { load(); }, [load]);

  if (!projectId) {
    return (
      <View style={styles.empty}>
        <Text style={styles.emptyText}>Select a project first.</Text>
      </View>
    );
  }
  if (loading) {
    return (
      <View style={styles.loading}>
        <ActivityIndicator size="large" color={theme.colors.accent} />
      </View>
    );
  }

  return (
    <View style={styles.root}>
      {/* T3-4 — Actions row. Currently exposes the daily digest preview;
          add additional gallery-level actions here as new ones land. */}
      <View style={styles.actionsRow}>
        <TouchableOpacity
          style={styles.actionButton}
          onPress={() => router.push('/site-photos/digest')}
          accessibilityLabel="View today's digest"
        >
          <Text style={styles.actionButtonText}>View today&apos;s digest</Text>
        </TouchableOpacity>
      </View>

      {/* Filter strip */}
      <ScrollView horizontal showsHorizontalScrollIndicator={false} style={styles.filterBar}>
        {REASON_OPTIONS.map((r) => (
          <TouchableOpacity
            key={`r-${r}`}
            style={[styles.chip, reason === r && styles.chipActive]}
            onPress={() => setReason(r)}
          >
            <Text style={[styles.chipText, reason === r && styles.chipTextActive]}>{r}</Text>
          </TouchableOpacity>
        ))}
      </ScrollView>
      <ScrollView horizontal showsHorizontalScrollIndicator={false} style={styles.filterBar}>
        {AUDIENCE_OPTIONS.map((a) => (
          <TouchableOpacity
            key={`a-${a}`}
            style={[styles.chip, audience === a && styles.chipActive]}
            onPress={() => setAudience(a)}
          >
            <Text style={[styles.chipText, audience === a && styles.chipTextActive]}>{a}</Text>
          </TouchableOpacity>
        ))}
      </ScrollView>
      <View style={styles.filterRow}>
        <TextInput
          style={styles.filterInput}
          placeholder="Level (L01)"
          placeholderTextColor={theme.colors.disabled}
          value={levelCode}
          onChangeText={setLevelCode}
          autoCapitalize="characters"
        />
        <TextInput
          style={styles.filterInput}
          placeholder="Zone (Z01)"
          placeholderTextColor={theme.colors.disabled}
          value={zoneCode}
          onChangeText={setZoneCode}
          autoCapitalize="characters"
        />
      </View>

      {/* Grid */}
      <ScrollView
        style={{ flex: 1 }}
        contentContainerStyle={styles.gridScroll}
        refreshControl={
          <RefreshControl
            refreshing={refreshing}
            onRefresh={() => { setRefreshing(true); load(); }}
            tintColor={theme.colors.accent}
          />
        }
      >
        {error ? <Text style={styles.error}>{error}</Text> : null}
        {photos.length === 0 ? (
          <View style={styles.emptyCard}>
            <Text style={styles.emptyTitle}>No photos match these filters</Text>
            <Text style={styles.emptyText}>Try clearing reason / audience / level / zone.</Text>
          </View>
        ) : (
          <View style={styles.grid}>
            {photos.map((p) => {
              const isNdaGated = ndaRequired.has(p.id);
              return (
                <TouchableOpacity
                  key={p.id}
                  style={styles.gridCell}
                  onPress={() => isNdaGated ? setNdaPrompt(p.id) : setFocused(p)}
                >
                  {isNdaGated ? (
                    <View style={[styles.thumb, styles.ndaThumb]}>
                      <Text style={styles.ndaLock}>🔒</Text>
                      <Text style={styles.ndaLabel}>NDA</Text>
                    </View>
                  ) : thumbs[p.id] ? (
                    <Image
                      source={{ uri: thumbs[p.id].url, headers: thumbs[p.id].headers }}
                      style={styles.thumb}
                      resizeMode="cover"
                    />
                  ) : (
                    <View style={[styles.thumb, styles.thumbPlaceholder]} />
                  )}
                  <View style={styles.gridCellMeta}>
                    <Text style={styles.gridCellReason} numberOfLines={1}>{p.reason}</Text>
                    <Text style={styles.gridCellDate} numberOfLines={1}>{shortDate(p.capturedAt)}</Text>
                  </View>
                </TouchableOpacity>
              );
            })}
          </View>
        )}
      </ScrollView>

      {/* Viewer modal */}
      {focused ? (
        <PhotoViewer
          photo={focused}
          thumb={thumbs[focused.id]}
          isApprover={isApprover}
          projectId={projectId}
          onClose={() => setFocused(null)}
          onChanged={async () => { await load(); setFocused(null); }}
        />
      ) : null}

      {/* Phase 179.2 — NDA acceptance modal */}
      {ndaPrompt && projectId ? (
        <NdaPromptModal
          visible
          projectId={projectId}
          photoId={ndaPrompt}
          onCancel={() => setNdaPrompt(null)}
          onAccepted={async () => {
            const acceptedId = ndaPrompt;
            setNdaPrompt(null);
            await load();
            const target = photos.find((p) => p.id === acceptedId);
            if (target) setFocused(target);
          }}
        />
      ) : null}
    </View>
  );
}

function PhotoViewer({
  photo, thumb, isApprover, projectId, onClose, onChanged,
}: {
  photo: SitePhoto;
  thumb: ResolvedThumb | undefined;
  isApprover: boolean;
  projectId: string;
  onClose: () => void;
  onChanged: () => Promise<void>;
}) {
  const [working, setWorking] = useState(false);
  const [caption, setCaption] = useState(photo.caption ?? '');
  const [rejectReason, setRejectReason] = useState('');

  async function approve() {
    if (caption.trim().length < 3) {
      Alert.alert('Caption required', 'Add a caption of at least 3 characters before approving.');
      return;
    }
    setWorking(true);
    try {
      await approveSitePhoto(projectId, photo.id, caption.trim());
      await onChanged();
    } catch (err) {
      Alert.alert('Approve failed', err instanceof Error ? err.message : String(err));
    } finally { setWorking(false); }
  }
  async function reject() {
    const r = rejectReason.trim();
    if (r.length < 3) {
      Alert.alert('Reason required', 'Tell the photographer why so they can re-shoot.');
      return;
    }
    setWorking(true);
    try {
      await rejectSitePhoto(projectId, photo.id, r);
      await onChanged();
    } catch (err) {
      Alert.alert('Reject failed', err instanceof Error ? err.message : String(err));
    } finally { setWorking(false); }
  }
  async function withdraw() {
    setWorking(true);
    try {
      await withdrawSitePhoto(projectId, photo.id);
      await onChanged();
    } catch (err) {
      Alert.alert('Withdraw failed', err instanceof Error ? err.message : String(err));
    } finally { setWorking(false); }
  }

  return (
    <Modal visible animationType="slide" onRequestClose={onClose}>
      <View style={styles.viewerRoot}>
        <ScrollView contentContainerStyle={{ paddingBottom: theme.spacing.xl }}>
          <View style={styles.viewerHeader}>
            <TouchableOpacity onPress={onClose}><Text style={styles.viewerClose}>✕</Text></TouchableOpacity>
            <Text style={styles.viewerHeaderText}>{photo.reason} · {photo.audience}</Text>
            <View style={{ width: 32 }} />
          </View>
          {thumb ? (
            <Image
              source={{ uri: thumb.url, headers: thumb.headers }}
              style={styles.viewerImage}
              resizeMode="contain"
            />
          ) : null}
          <View style={styles.viewerMeta}>
            <Meta label="Captured" value={formatTime(photo.capturedAt)} />
            <Meta label="Level / Zone" value={`${photo.levelCode ?? '—'} / ${photo.zoneCode ?? '—'}`} />
            {photo.latitude !== null && photo.longitude !== null ? (
              <Meta label="GPS" value={`${photo.latitude!.toFixed(5)}, ${photo.longitude!.toFixed(5)}`} />
            ) : null}
            {photo.anchorIssueId ? <Meta label="Linked issue" value={photo.anchorIssueId} /> : null}
            {photo.rejectedReason ? <Meta label="Last reject reason" value={photo.rejectedReason} /> : null}
          </View>

          {isApprover && photo.audience === 'PendingReview' ? (
            <View style={styles.viewerActions}>
              <Text style={styles.viewerActionLabel}>Caption (≥3 chars)</Text>
              <TextInput
                style={styles.viewerCaption}
                placeholder="Add caption"
                placeholderTextColor={theme.colors.disabled}
                value={caption}
                onChangeText={setCaption}
                multiline
              />
              <Text style={styles.viewerActionLabel}>Reject reason (if rejecting)</Text>
              <TextInput
                style={styles.viewerCaption}
                placeholder="e.g. blurry — please retake"
                placeholderTextColor={theme.colors.disabled}
                value={rejectReason}
                onChangeText={setRejectReason}
                multiline
              />
              <View style={styles.viewerButtonRow}>
                <TouchableOpacity style={styles.viewerReject} onPress={reject} disabled={working}>
                  <Text style={styles.viewerRejectText}>Reject</Text>
                </TouchableOpacity>
                <TouchableOpacity style={styles.viewerApprove} onPress={approve} disabled={working}>
                  {working ? <ActivityIndicator color="#fff" /> : <Text style={styles.viewerApproveText}>Approve</Text>}
                </TouchableOpacity>
              </View>
            </View>
          ) : null}

          {isApprover && photo.audience === 'ClientPortal' ? (
            <View style={styles.viewerActions}>
              <Text style={styles.viewerActionLabel}>This photo is published to the client portal.</Text>
              <TouchableOpacity style={styles.viewerWithdraw} onPress={withdraw} disabled={working}>
                {working ? <ActivityIndicator color="#fff" /> : (
                  <Text style={styles.viewerWithdrawText}>Withdraw from portal</Text>
                )}
              </TouchableOpacity>
            </View>
          ) : null}

          {photo.caption ? (
            <View style={styles.captionBlock}>
              <Text style={styles.captionLabel}>Caption</Text>
              <Text style={styles.captionBody}>{photo.caption}</Text>
            </View>
          ) : null}
        </ScrollView>
      </View>
    </Modal>
  );
}

function Meta({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.metaRow}>
      <Text style={styles.metaLabel}>{label}</Text>
      <Text style={styles.metaValue}>{value}</Text>
    </View>
  );
}

function shortDate(iso: string): string {
  try { return new Date(iso).toLocaleDateString(); } catch { return iso; }
}
function formatTime(iso: string): string {
  try { return new Date(iso).toLocaleString(); } catch { return iso; }
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: theme.colors.background },
  loading: { flex: 1, justifyContent: 'center', alignItems: 'center' },
  empty: { flex: 1, justifyContent: 'center', alignItems: 'center', padding: theme.spacing.lg },
  emptyTitle: { fontSize: theme.fontSize.lg, fontWeight: '700', color: theme.colors.text, marginBottom: theme.spacing.sm },
  emptyText: { color: theme.colors.textSecondary, textAlign: 'center' },
  emptyCard: { backgroundColor: theme.colors.surface, borderRadius: theme.borderRadius.md, padding: theme.spacing.lg, alignItems: 'center', margin: theme.spacing.md },
  error: { backgroundColor: '#FFEBEE', color: theme.colors.danger, padding: theme.spacing.sm, borderRadius: theme.borderRadius.sm, margin: theme.spacing.md },

  actionsRow: {
    flexDirection: 'row',
    paddingHorizontal: theme.spacing.md,
    paddingTop: theme.spacing.sm,
  },
  actionButton: {
    paddingHorizontal: 12,
    paddingVertical: 8,
    borderRadius: theme.borderRadius.sm,
    backgroundColor: theme.colors.accent,
  },
  actionButtonText: { color: '#fff', fontSize: theme.fontSize.sm, fontWeight: '600' },

  filterBar: { paddingHorizontal: theme.spacing.md, paddingTop: theme.spacing.sm, maxHeight: 44 },
  chip: {
    paddingHorizontal: 12, paddingVertical: 6, borderRadius: 16,
    backgroundColor: theme.colors.surface, borderWidth: 1, borderColor: theme.colors.border,
    marginRight: theme.spacing.xs,
  },
  chipActive: { backgroundColor: theme.colors.accent, borderColor: theme.colors.accent },
  chipText: { color: theme.colors.text, fontSize: theme.fontSize.sm, fontWeight: '600' },
  chipTextActive: { color: '#fff' },

  filterRow: { flexDirection: 'row', paddingHorizontal: theme.spacing.md, paddingTop: theme.spacing.sm },
  filterInput: {
    flex: 1, marginRight: theme.spacing.sm,
    backgroundColor: theme.colors.surface, borderRadius: theme.borderRadius.sm,
    borderWidth: 1, borderColor: theme.colors.border,
    paddingHorizontal: theme.spacing.sm, paddingVertical: theme.spacing.sm,
    fontSize: theme.fontSize.sm, color: theme.colors.text,
  },

  gridScroll: { padding: theme.spacing.md },
  grid: { flexDirection: 'row', flexWrap: 'wrap' },
  gridCell: {
    width: THUMB_SIZE, marginRight: GUTTER, marginBottom: GUTTER,
    backgroundColor: theme.colors.surface, borderRadius: theme.borderRadius.sm, overflow: 'hidden',
  },
  thumb: { width: '100%', height: THUMB_SIZE },
  thumbPlaceholder: { backgroundColor: theme.colors.border },
  // Phase 179.2 — locked tile for NDA-gated photos.
  ndaThumb: { backgroundColor: '#37474F', alignItems: 'center', justifyContent: 'center' },
  ndaLock: { fontSize: 28 },
  ndaLabel: { color: '#fff', fontSize: 11, fontWeight: '700', letterSpacing: 1, marginTop: 4 },
  gridCellMeta: { paddingHorizontal: 6, paddingVertical: 4 },
  gridCellReason: { fontSize: theme.fontSize.xs, fontWeight: '700', color: theme.colors.text },
  gridCellDate: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary },

  // Viewer
  viewerRoot: { flex: 1, backgroundColor: '#000' },
  viewerHeader: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', padding: theme.spacing.md, paddingTop: 48 },
  viewerClose: { color: '#fff', fontSize: 24, width: 32, textAlign: 'center' },
  viewerHeaderText: { color: '#fff', fontSize: theme.fontSize.md, fontWeight: '600' },
  viewerImage: { width: SCREEN_WIDTH, height: SCREEN_WIDTH * 1.2, backgroundColor: '#000' },
  viewerMeta: { backgroundColor: '#1A1A1A', padding: theme.spacing.md },
  metaRow: { flexDirection: 'row', justifyContent: 'space-between', marginBottom: 4 },
  metaLabel: { color: '#9E9E9E', fontSize: theme.fontSize.sm },
  metaValue: { color: '#fff', fontSize: theme.fontSize.sm, flexShrink: 1, textAlign: 'right' },
  viewerActions: { backgroundColor: theme.colors.surface, padding: theme.spacing.md },
  viewerActionLabel: { fontSize: theme.fontSize.sm, color: theme.colors.textSecondary, marginTop: theme.spacing.sm, marginBottom: 4 },
  viewerCaption: {
    backgroundColor: theme.colors.background, borderRadius: theme.borderRadius.sm,
    borderWidth: 1, borderColor: theme.colors.border, padding: theme.spacing.sm,
    fontSize: theme.fontSize.md, color: theme.colors.text, minHeight: 60, textAlignVertical: 'top',
  },
  viewerButtonRow: { flexDirection: 'row', marginTop: theme.spacing.md },
  viewerApprove: {
    flex: 2, backgroundColor: theme.colors.success,
    paddingVertical: 12, borderRadius: theme.borderRadius.md,
    alignItems: 'center', justifyContent: 'center', marginLeft: theme.spacing.sm,
  },
  viewerApproveText: { color: '#fff', fontWeight: '600', fontSize: theme.fontSize.md },
  viewerReject: {
    flex: 1, backgroundColor: theme.colors.surface,
    borderWidth: 1, borderColor: theme.colors.danger,
    paddingVertical: 12, borderRadius: theme.borderRadius.md,
    alignItems: 'center', justifyContent: 'center',
  },
  viewerRejectText: { color: theme.colors.danger, fontWeight: '600', fontSize: theme.fontSize.md },
  viewerWithdraw: {
    backgroundColor: theme.colors.danger, paddingVertical: 12, borderRadius: theme.borderRadius.md,
    alignItems: 'center', justifyContent: 'center', marginTop: theme.spacing.sm,
  },
  viewerWithdrawText: { color: '#fff', fontWeight: '600', fontSize: theme.fontSize.md },

  captionBlock: { backgroundColor: theme.colors.surface, padding: theme.spacing.md },
  captionLabel: { fontSize: theme.fontSize.sm, color: theme.colors.textSecondary },
  captionBody: { fontSize: theme.fontSize.md, color: theme.colors.text, marginTop: 4 },
});
