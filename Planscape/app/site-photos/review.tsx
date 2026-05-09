// Phase 178 — PM/Admin/Owner review surface for PendingReview photos.
//
// Lists every photo currently in the PendingReview audience for the active
// project, grouped by Reason. Long-press to enter selection mode; bottom
// sheet drives bulk approve/reject with a shared caption. Approval blocks
// when caption.trim().length < 3 (mirrors server guard).
//
// Role gate: only PM / Admin / Owner see this screen. We compute the role
// from `getMyProjectAccess` (or fall back to the auth store's tenant role).

import { useEffect, useMemo, useState, useCallback } from 'react';
import {
  View,
  Text,
  StyleSheet,
  ScrollView,
  TouchableOpacity,
  ActivityIndicator,
  RefreshControl,
  Alert,
  TextInput,
  Modal,
  Image,
} from 'react-native';
import { useRouter } from 'expo-router';
import { theme } from '@/utils/theme';
import { useProjectStore } from '@/stores/projectStore';
import {
  listSitePhotos,
  approveSitePhoto,
  rejectSitePhoto,
  bulkApproveSitePhotos,
  getMyProjectAccess,
  getSitePhotoFile,
} from '@/api/endpoints';
import type { SitePhoto, SitePhotoReason } from '@/types/api';

const APPROVER_ROLES = new Set(['PM', 'Admin', 'Owner']);

interface ResolvedThumb { url: string; headers: Record<string, string>; }
type ResolvedThumbRecord = Record<string, ResolvedThumb>;

export default function ReviewSitePhotosScreen() {
  const router = useRouter();
  const projectId = useProjectStore((s) => s.active?.id);

  const [authorised, setAuthorised] = useState<boolean | null>(null);
  const [photos, setPhotos] = useState<SitePhoto[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [caption, setCaption] = useState('');
  const [acting, setActing] = useState(false);
  const [rejectModalFor, setRejectModalFor] = useState<SitePhoto | null>(null);
  const [rejectReason, setRejectReason] = useState('');
  const [thumbs, setThumbs] = useState<ResolvedThumbRecord>({});

  const loadAuth = useCallback(async () => {
    if (!projectId) return;
    try {
      const access = await getMyProjectAccess(projectId);
      const role = access.projectRole ?? '';
      setAuthorised(APPROVER_ROLES.has(role) || access.bypassesAcl);
    } catch {
      // If the access endpoint fails, fall back to a "no" — the server
      // will still 403 anyway and the UI is just a guard.
      setAuthorised(false);
    }
  }, [projectId]);

  const load = useCallback(async () => {
    if (!projectId) return;
    try {
      setError(null);
      const res = await listSitePhotos(projectId, { audience: 'PendingReview', pageSize: 200 });
      setPhotos(res.items);

      // Pre-resolve full URL + auth headers for thumbnails so <Image> can fetch them.
      const next: ResolvedThumbRecord = {};
      for (const p of res.items) {
        next[p.id] = await getSitePhotoFile(projectId, p.id);
      }
      setThumbs(next);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to load photos');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [projectId]);

  useEffect(() => { loadAuth(); }, [loadAuth]);
  useEffect(() => { if (authorised) load(); }, [authorised, load]);

  // Group by reason for the section list.
  const grouped = useMemo(() => {
    const out: Record<SitePhotoReason, SitePhoto[]> = {
      Progress: [], Issue: [], Defect: [], Safety: [], AsBuilt: [], Reference: [],
    };
    for (const p of photos) out[p.reason].push(p);
    return out;
  }, [photos]);

  // ── Authorization gate ──
  if (!projectId) {
    return (
      <View style={styles.empty}>
        <Text style={styles.emptyText}>Select a project first.</Text>
      </View>
    );
  }
  if (authorised === null) {
    return (
      <View style={styles.loading}>
        <ActivityIndicator size="large" color={theme.colors.accent} />
      </View>
    );
  }
  if (!authorised) {
    return (
      <View style={styles.empty}>
        <Text style={styles.emptyTitle}>Reviewer access only</Text>
        <Text style={styles.emptyText}>
          Photo approval is restricted to project managers, tenant admins, and owners.
        </Text>
        <TouchableOpacity style={styles.secondaryBtn} onPress={() => router.back()}>
          <Text style={styles.secondaryBtnText}>Back</Text>
        </TouchableOpacity>
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

  // ── Selection helpers ──
  function toggleSelect(id: string) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  }
  function clearSelection() { setSelected(new Set()); setCaption(''); }
  function selectAllIn(reason: SitePhotoReason) {
    setSelected((prev) => {
      const next = new Set(prev);
      grouped[reason].forEach((p) => next.add(p.id));
      return next;
    });
  }

  async function onBulkApprove() {
    if (selected.size === 0) return;
    if (caption.trim().length < 3) {
      Alert.alert('Caption required', 'Add a caption of at least 3 characters before approving.');
      return;
    }
    setActing(true);
    try {
      const ids = Array.from(selected);
      const res = await bulkApproveSitePhotos(projectId!, ids, caption.trim());
      Alert.alert(
        'Done',
        `${res.approved} approved · ${res.skipped} skipped`,
      );
      clearSelection();
      await load();
    } catch (err) {
      Alert.alert('Approve failed', err instanceof Error ? err.message : String(err));
    } finally {
      setActing(false);
    }
  }

  async function onSingleApprove(p: SitePhoto) {
    if (caption.trim().length < 3) {
      Alert.alert('Caption required', 'Add a caption of at least 3 characters before approving.');
      return;
    }
    setActing(true);
    try {
      await approveSitePhoto(projectId!, p.id, caption.trim());
      clearSelection();
      await load();
    } catch (err) {
      Alert.alert('Approve failed', err instanceof Error ? err.message : String(err));
    } finally {
      setActing(false);
    }
  }

  async function onConfirmReject() {
    if (!rejectModalFor) return;
    const reason = rejectReason.trim();
    if (reason.length < 3) {
      Alert.alert('Reason required', 'Tell the photographer why so they can fix and re-submit.');
      return;
    }
    setActing(true);
    try {
      await rejectSitePhoto(projectId!, rejectModalFor.id, reason);
      setRejectModalFor(null);
      setRejectReason('');
      await load();
    } catch (err) {
      Alert.alert('Reject failed', err instanceof Error ? err.message : String(err));
    } finally {
      setActing(false);
    }
  }

  async function onBulkReject() {
    if (selected.size === 0) return;
    if (caption.trim().length < 3) {
      Alert.alert('Reason required', 'Use the caption field as the rejection reason (≥3 chars).');
      return;
    }
    setActing(true);
    try {
      const ids = Array.from(selected);
      const reason = caption.trim();
      let rejected = 0;
      for (const id of ids) {
        try { await rejectSitePhoto(projectId!, id, reason); rejected++; } catch { /* keep going */ }
      }
      Alert.alert('Done', `${rejected} rejected.`);
      clearSelection();
      await load();
    } catch (err) {
      Alert.alert('Reject failed', err instanceof Error ? err.message : String(err));
    } finally {
      setActing(false);
    }
  }

  return (
    <View style={styles.root}>
      <ScrollView
        contentContainerStyle={styles.scroll}
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
            <Text style={styles.emptyTitle}>Nothing to review</Text>
            <Text style={styles.emptyText}>No photos are currently waiting for sign-off.</Text>
          </View>
        ) : (
          (Object.keys(grouped) as SitePhotoReason[]).map((reason) => {
            const rows = grouped[reason];
            if (rows.length === 0) return null;
            return (
              <View key={reason} style={styles.section}>
                <View style={styles.sectionHeader}>
                  <Text style={styles.sectionTitle}>{reason} · {rows.length}</Text>
                  <TouchableOpacity onPress={() => selectAllIn(reason)}>
                    <Text style={styles.sectionAction}>Select all</Text>
                  </TouchableOpacity>
                </View>
                {rows.map((p) => {
                  const isSel = selected.has(p.id);
                  return (
                    <TouchableOpacity
                      key={p.id}
                      style={[styles.row, isSel && styles.rowSelected]}
                      onPress={() => (selected.size > 0 ? toggleSelect(p.id) : router.push({ pathname: '/site-photos/gallery', params: { focus: p.id } }))}
                      onLongPress={() => toggleSelect(p.id)}
                      delayLongPress={250}
                    >
                      <View style={styles.thumbWrap}>
                        {thumbs[p.id] ? (
                          <Image
                            source={{ uri: thumbs[p.id].url, headers: thumbs[p.id].headers }}
                            style={styles.thumb}
                            resizeMode="cover"
                          />
                        ) : null}
                        {isSel ? (
                          <View style={styles.selectedBadge}>
                            <Text style={styles.selectedBadgeText}>✓</Text>
                          </View>
                        ) : null}
                      </View>
                      <View style={styles.rowBody}>
                        <Text style={styles.rowTitle} numberOfLines={2}>
                          {p.caption || `(no caption — ${p.reason})`}
                        </Text>
                        <Text style={styles.rowMeta}>
                          {formatTime(p.capturedAt)}
                          {p.levelCode ? ` · ${p.levelCode}` : ''}
                          {p.zoneCode ? ` · ${p.zoneCode}` : ''}
                        </Text>
                      </View>
                      <View style={styles.rowActions}>
                        <TouchableOpacity
                          style={styles.smallApprove}
                          onPress={() => onSingleApprove(p)}
                          disabled={acting}
                        >
                          <Text style={styles.smallApproveText}>Approve</Text>
                        </TouchableOpacity>
                        <TouchableOpacity
                          style={styles.smallReject}
                          onPress={() => { setRejectModalFor(p); setRejectReason(''); }}
                          disabled={acting}
                        >
                          <Text style={styles.smallRejectText}>Reject</Text>
                        </TouchableOpacity>
                      </View>
                    </TouchableOpacity>
                  );
                })}
              </View>
            );
          })
        )}
      </ScrollView>

      {/* Bulk action sheet */}
      {selected.size > 0 ? (
        <View style={styles.actionSheet}>
          <Text style={styles.actionLabel}>Caption applied to all approvals (or rejection reason)</Text>
          <TextInput
            style={styles.captionInput}
            placeholder="Add caption (≥3 chars)…"
            placeholderTextColor={theme.colors.disabled}
            value={caption}
            onChangeText={setCaption}
            multiline
          />
          <View style={styles.actionRow}>
            <TouchableOpacity style={styles.secondaryBtn} onPress={clearSelection} disabled={acting}>
              <Text style={styles.secondaryBtnText}>Cancel</Text>
            </TouchableOpacity>
            <TouchableOpacity style={styles.dangerBtn} onPress={onBulkReject} disabled={acting}>
              <Text style={styles.dangerBtnText}>Reject {selected.size}</Text>
            </TouchableOpacity>
            <TouchableOpacity
              style={[styles.primaryBtn, { flex: 2 }]}
              onPress={onBulkApprove}
              disabled={acting}
            >
              {acting ? <ActivityIndicator color="#fff" /> : (
                <Text style={styles.primaryBtnText}>Approve {selected.size}</Text>
              )}
            </TouchableOpacity>
          </View>
        </View>
      ) : null}

      {/* Reject modal */}
      <Modal
        visible={rejectModalFor !== null}
        transparent
        animationType="slide"
        onRequestClose={() => setRejectModalFor(null)}
      >
        <View style={styles.modalBackdrop}>
          <View style={styles.modalCard}>
            <Text style={styles.modalTitle}>Reject photo</Text>
            <Text style={styles.modalSubtitle}>
              Tell the photographer why so they can re-shoot.
            </Text>
            <TextInput
              style={styles.modalInput}
              placeholder="Reason (≥3 chars)"
              placeholderTextColor={theme.colors.disabled}
              value={rejectReason}
              onChangeText={setRejectReason}
              multiline
            />
            <View style={styles.modalRow}>
              <TouchableOpacity style={styles.secondaryBtn} onPress={() => setRejectModalFor(null)}>
                <Text style={styles.secondaryBtnText}>Cancel</Text>
              </TouchableOpacity>
              <TouchableOpacity style={styles.dangerBtn} onPress={onConfirmReject} disabled={acting}>
                {acting ? <ActivityIndicator color="#fff" /> : (
                  <Text style={styles.dangerBtnText}>Reject</Text>
                )}
              </TouchableOpacity>
            </View>
          </View>
        </View>
      </Modal>
    </View>
  );
}

function formatTime(iso: string): string {
  try { return new Date(iso).toLocaleString(); } catch { return iso; }
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: theme.colors.background },
  scroll: { padding: theme.spacing.md, paddingBottom: 220 },
  loading: { flex: 1, justifyContent: 'center', alignItems: 'center' },
  empty: { flex: 1, justifyContent: 'center', alignItems: 'center', padding: theme.spacing.lg },
  emptyTitle: { fontSize: theme.fontSize.lg, fontWeight: '700', color: theme.colors.text, marginBottom: theme.spacing.sm },
  emptyText: { color: theme.colors.textSecondary, textAlign: 'center', fontSize: theme.fontSize.md },
  emptyCard: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.lg,
    alignItems: 'center',
  },
  error: {
    backgroundColor: '#FFEBEE', color: theme.colors.danger,
    padding: theme.spacing.sm, borderRadius: theme.borderRadius.sm, marginBottom: theme.spacing.md,
  },
  section: { marginBottom: theme.spacing.lg },
  sectionHeader: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', marginBottom: theme.spacing.sm },
  sectionTitle: { fontSize: theme.fontSize.md, fontWeight: '700', color: theme.colors.text },
  sectionAction: { fontSize: theme.fontSize.sm, color: theme.colors.accent, fontWeight: '600' },
  row: {
    flexDirection: 'row', alignItems: 'center',
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md, padding: theme.spacing.sm,
    marginBottom: theme.spacing.sm,
  },
  rowSelected: { borderWidth: 2, borderColor: theme.colors.accent },
  thumbWrap: { width: 64, height: 64, borderRadius: theme.borderRadius.sm, overflow: 'hidden', backgroundColor: theme.colors.border, marginRight: theme.spacing.sm },
  thumb: { width: '100%', height: '100%' },
  selectedBadge: {
    position: 'absolute', top: 2, right: 2,
    backgroundColor: theme.colors.accent,
    width: 22, height: 22, borderRadius: 11,
    justifyContent: 'center', alignItems: 'center',
  },
  selectedBadgeText: { color: '#fff', fontWeight: '700' },
  rowBody: { flex: 1 },
  rowTitle: { fontSize: theme.fontSize.md, color: theme.colors.text, fontWeight: '500' },
  rowMeta: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary, marginTop: 2 },
  rowActions: { flexDirection: 'column', alignItems: 'flex-end' },
  smallApprove: {
    backgroundColor: theme.colors.success, paddingHorizontal: 10, paddingVertical: 6,
    borderRadius: theme.borderRadius.sm, marginBottom: 4,
  },
  smallApproveText: { color: '#fff', fontSize: theme.fontSize.xs, fontWeight: '700' },
  smallReject: {
    backgroundColor: theme.colors.surface, borderWidth: 1, borderColor: theme.colors.danger,
    paddingHorizontal: 10, paddingVertical: 6, borderRadius: theme.borderRadius.sm,
  },
  smallRejectText: { color: theme.colors.danger, fontSize: theme.fontSize.xs, fontWeight: '700' },

  actionSheet: {
    position: 'absolute', left: 0, right: 0, bottom: 0,
    backgroundColor: theme.colors.surface,
    borderTopLeftRadius: theme.borderRadius.lg, borderTopRightRadius: theme.borderRadius.lg,
    padding: theme.spacing.md,
    shadowColor: '#000', shadowOpacity: 0.15, shadowOffset: { width: 0, height: -2 }, shadowRadius: 8,
    elevation: 12,
  },
  actionLabel: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary, marginBottom: 4 },
  captionInput: {
    backgroundColor: theme.colors.background,
    borderRadius: theme.borderRadius.sm, borderWidth: 1, borderColor: theme.colors.border,
    padding: theme.spacing.sm, fontSize: theme.fontSize.md, color: theme.colors.text,
    minHeight: 56, textAlignVertical: 'top',
  },
  actionRow: { flexDirection: 'row', marginTop: theme.spacing.sm },
  primaryBtn: {
    backgroundColor: theme.colors.accent, paddingVertical: 12, paddingHorizontal: 16,
    borderRadius: theme.borderRadius.md, alignItems: 'center', justifyContent: 'center',
    marginLeft: theme.spacing.sm, flex: 1,
  },
  primaryBtnText: { color: '#fff', fontWeight: '600', fontSize: theme.fontSize.md },
  secondaryBtn: {
    backgroundColor: theme.colors.surface, borderWidth: 1, borderColor: theme.colors.border,
    paddingVertical: 12, paddingHorizontal: 16,
    borderRadius: theme.borderRadius.md, alignItems: 'center', justifyContent: 'center',
    marginRight: theme.spacing.sm,
  },
  secondaryBtnText: { color: theme.colors.text, fontWeight: '600', fontSize: theme.fontSize.md },
  dangerBtn: {
    backgroundColor: theme.colors.danger, paddingVertical: 12, paddingHorizontal: 16,
    borderRadius: theme.borderRadius.md, alignItems: 'center', justifyContent: 'center',
    marginRight: theme.spacing.sm,
  },
  dangerBtnText: { color: '#fff', fontWeight: '600', fontSize: theme.fontSize.md },

  modalBackdrop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.4)', justifyContent: 'flex-end' },
  modalCard: {
    backgroundColor: theme.colors.surface,
    borderTopLeftRadius: theme.borderRadius.lg, borderTopRightRadius: theme.borderRadius.lg,
    padding: theme.spacing.lg,
  },
  modalTitle: { fontSize: theme.fontSize.lg, fontWeight: '700', color: theme.colors.text },
  modalSubtitle: { fontSize: theme.fontSize.sm, color: theme.colors.textSecondary, marginTop: 2, marginBottom: theme.spacing.md },
  modalInput: {
    backgroundColor: theme.colors.background, borderRadius: theme.borderRadius.sm,
    borderWidth: 1, borderColor: theme.colors.border, padding: theme.spacing.sm,
    fontSize: theme.fontSize.md, color: theme.colors.text, minHeight: 80, textAlignVertical: 'top',
  },
  modalRow: { flexDirection: 'row', marginTop: theme.spacing.md },
});
