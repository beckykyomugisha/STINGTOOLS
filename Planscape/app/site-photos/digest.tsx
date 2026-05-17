// T3-4 — Site Photo digest preview.
//
// Shows every ClientPortal-bound photo approved in the last 24 h on the
// active project, grouped by reason. The same payload that the daily
// client-digest email uses, surfaced in-app so site teams can preview
// what their client will see in the morning bulletin.
//
// Refresh:
//  - pull-to-refresh
//  - SignalR `SitePhotoApproved` (via realtimeClient) — new approvals appear
//    in real time without manual reload.

import { useCallback, useEffect, useState } from 'react';
import {
  View,
  Text,
  StyleSheet,
  ScrollView,
  RefreshControl,
  ActivityIndicator,
  Image,
  TouchableOpacity,
} from 'react-native';
import { useRouter } from 'expo-router';
import { theme } from '@/utils/theme';
import { useProjectStore } from '@/stores/projectStore';
import {
  getSitePhotoDigestPreview,
  getSitePhotoFile,
} from '@/api/endpoints';
import type { SitePhotoDigestPreview, SitePhotoReason } from '@/types/api';
import { realtime } from '@/services/realtimeClient';

interface ResolvedThumb { url: string; headers: Record<string, string>; }
type ThumbRecord = Record<string, ResolvedThumb>;

export default function DigestScreen() {
  const router = useRouter();
  const project = useProjectStore((s) => s.active);
  const projectId = project?.id;

  const [data, setData] = useState<SitePhotoDigestPreview | null>(null);
  const [thumbs, setThumbs] = useState<ThumbRecord>({});
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!projectId) return;
    try {
      setError(null);
      const preview = await getSitePhotoDigestPreview(projectId);
      setData(preview);

      // Resolve auth'd thumbnail URLs in parallel — the digest is small
      // (24-h window, typically <30 photos) so this is fine.
      const next: ThumbRecord = {};
      await Promise.all(
        preview.items.map(async (p) => {
          try {
            next[p.id] = await getSitePhotoFile(projectId, p.id);
          } catch { /* skip individual failures so one 403 doesn't poison the page */ }
        }),
      );
      setThumbs(next);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to load digest');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [projectId]);

  useEffect(() => { void load(); }, [load]);

  // Real-time refresh — when an approver approves a new photo elsewhere,
  // the server fires `SitePhotoApproved` and we reload to pick it up.
  useEffect(() => {
    if (!projectId || !realtime?.on) return;
    const unsub = realtime.on('SitePhotoApproved', (payload: { projectId?: string }) => {
      if (payload?.projectId && payload.projectId !== projectId) return;
      void load();
    });
    return unsub;
  }, [projectId, load]);

  if (!projectId) {
    return (
      <View style={styles.empty}>
        <Text style={styles.emptyText}>Select a project on the dashboard first.</Text>
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

  // Group items by reason for the section header view.
  const grouped = new Map<SitePhotoReason, SitePhotoDigestPreview['items']>();
  for (const item of data?.items ?? []) {
    const arr = grouped.get(item.reason) ?? [];
    arr.push(item);
    grouped.set(item.reason, arr);
  }

  return (
    <ScrollView
      style={styles.root}
      contentContainerStyle={styles.scroll}
      refreshControl={
        <RefreshControl
          refreshing={refreshing}
          onRefresh={() => { setRefreshing(true); void load(); }}
          tintColor={theme.colors.accent}
        />
      }
    >
      {error ? <Text style={styles.error}>{error}</Text> : null}

      <View style={styles.header}>
        <Text style={styles.title}>{project?.name ?? 'Project'} — Today's Digest</Text>
        <Text style={styles.subtitle}>
          {data ? `Window from ${formatTime(data.windowStart)} · ${data.count} photo${data.count === 1 ? '' : 's'}` : ''}
        </Text>
      </View>

      {data && data.count === 0 ? (
        <View style={styles.emptyCard}>
          <Text style={styles.emptyTitle}>No photos approved in the last 24 hours</Text>
          <Text style={styles.emptyHint}>
            Photos approved for the client portal will appear here once they're published.
          </Text>
        </View>
      ) : null}

      {Array.from(grouped.entries()).map(([reason, items]) => (
        <View key={reason} style={styles.section}>
          <Text style={styles.sectionTitle}>{reason} · {items.length}</Text>
          {items.map((p) => {
            const thumb = thumbs[p.id];
            return (
              <TouchableOpacity
                key={p.id}
                style={styles.card}
                onPress={() => router.push({ pathname: '/site-photos/gallery', params: { focus: p.id } })}
                accessibilityLabel={`Open photo ${p.caption ?? p.id}`}
              >
                {thumb ? (
                  <Image
                    source={{ uri: thumb.url, headers: thumb.headers }}
                    style={styles.thumb}
                    resizeMode="cover"
                  />
                ) : (
                  <View style={[styles.thumb, styles.thumbPlaceholder]} />
                )}
                <View style={styles.cardBody}>
                  {p.caption ? (
                    <Text style={styles.caption} numberOfLines={2}>{p.caption}</Text>
                  ) : (
                    <Text style={[styles.caption, styles.captionMuted]}>(no caption)</Text>
                  )}
                  <Text style={styles.meta}>
                    {p.levelCode ?? '—'} / {p.zoneCode ?? '—'} · captured {formatTime(p.capturedAt)}
                    {p.approvedAt ? ` · approved ${formatTime(p.approvedAt)}` : ''}
                  </Text>
                </View>
              </TouchableOpacity>
            );
          })}
        </View>
      ))}

      {data?.items.length ? (
        <Text style={styles.footer}>
          Pull down to refresh · Updates live as new approvals come in
        </Text>
      ) : null}
    </ScrollView>
  );
}

function formatTime(iso: string): string {
  try { return new Date(iso).toLocaleString(); } catch { return iso; }
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: theme.colors.background },
  scroll: { padding: theme.spacing.md, paddingBottom: theme.spacing.xl },
  loading: { flex: 1, justifyContent: 'center', alignItems: 'center' },
  empty: { flex: 1, justifyContent: 'center', alignItems: 'center', padding: theme.spacing.lg },
  emptyText: { color: theme.colors.textSecondary, fontSize: theme.fontSize.md },
  error: {
    backgroundColor: '#FFEBEE',
    color: theme.colors.danger,
    padding: theme.spacing.sm,
    borderRadius: theme.borderRadius.sm,
    marginBottom: theme.spacing.md,
  },

  header: { marginBottom: theme.spacing.md },
  title: { fontSize: theme.fontSize.lg, fontWeight: '700', color: theme.colors.text },
  subtitle: { fontSize: theme.fontSize.sm, color: theme.colors.textSecondary, marginTop: 2 },

  emptyCard: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.lg,
    alignItems: 'center',
  },
  emptyTitle: {
    fontSize: theme.fontSize.md,
    fontWeight: '600',
    color: theme.colors.text,
    marginBottom: theme.spacing.xs,
  },
  emptyHint: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.textSecondary,
    textAlign: 'center',
  },

  section: { marginBottom: theme.spacing.md },
  sectionTitle: {
    fontSize: theme.fontSize.sm,
    fontWeight: '700',
    color: theme.colors.text,
    textTransform: 'uppercase',
    letterSpacing: 0.5,
    marginBottom: theme.spacing.xs,
  },
  card: {
    flexDirection: 'row',
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    overflow: 'hidden',
    marginBottom: theme.spacing.sm,
  },
  thumb: { width: 96, height: 96 },
  thumbPlaceholder: { backgroundColor: theme.colors.border },
  cardBody: { flex: 1, padding: theme.spacing.sm, justifyContent: 'center' },
  caption: { fontSize: theme.fontSize.sm, color: theme.colors.text, fontWeight: '500' },
  captionMuted: { fontStyle: 'italic', color: theme.colors.textSecondary, fontWeight: '400' },
  meta: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary, marginTop: 4 },

  footer: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.disabled,
    textAlign: 'center',
    marginTop: theme.spacing.md,
  },
});
