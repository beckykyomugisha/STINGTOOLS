// Phase 180 — Pair timeline.
//
// Photos are bucketed by their `pairKey` (perceptual-hash bucket the
// classifier writes at capture time). This screen shows every photo in
// the bucket in chronological order, so progress at the same camera
// position over time turns into a swipeable strip. Captures the
// before/after value the data already has but no UI surfaced.

import { useCallback, useEffect, useState } from 'react';
import {
  View, Text, StyleSheet, ScrollView, RefreshControl, Image,
  ActivityIndicator, Dimensions,
} from 'react-native';
import { useLocalSearchParams } from 'expo-router';
import { theme } from '@/utils/theme';
import { useProjectStore } from '@/stores/projectStore';
import { listSitePhotos, getSitePhotoFile } from '@/api/endpoints';
import type { SitePhoto } from '@/types/api';

const SCREEN_WIDTH = Dimensions.get('window').width;
const TILE = SCREEN_WIDTH - theme.spacing.md * 2;

interface ResolvedThumb { url: string; headers: Record<string, string>; }

export default function PairTimelineScreen() {
  const { pairKey } = useLocalSearchParams<{ pairKey?: string }>();
  const projectId = useProjectStore((s) => s.active?.id);

  const [items, setItems] = useState<SitePhoto[]>([]);
  const [thumbs, setThumbs] = useState<Record<string, ResolvedThumb>>({});
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);

  const load = useCallback(async () => {
    if (!projectId || !pairKey) return;
    try {
      const res = await listSitePhotos(projectId, { pageSize: 200 });
      const filtered = res.items
        .filter((p) => p.pairKey === pairKey)
        .sort((a, b) => new Date(a.capturedAt).getTime() - new Date(b.capturedAt).getTime());
      setItems(filtered);
      const ndaSet = new Set(res.ndaRequiredIds ?? []);
      const next: Record<string, ResolvedThumb> = {};
      await Promise.all(filtered.map(async (p) => {
        if (ndaSet.has(p.id)) return;
        try { next[p.id] = await getSitePhotoFile(projectId, p.id); }
        catch { /* skip */ }
      }));
      setThumbs(next);
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [projectId, pairKey]);

  useEffect(() => { void load(); }, [load]);

  if (!projectId || !pairKey) {
    return <View style={styles.empty}><Text>Missing pair key.</Text></View>;
  }
  if (loading) {
    return <View style={styles.empty}><ActivityIndicator size="large" color={theme.colors.accent} /></View>;
  }
  if (items.length === 0) {
    return <View style={styles.empty}><Text style={styles.emptyText}>No photos in this pair group yet.</Text></View>;
  }

  return (
    <ScrollView
      style={styles.root}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={() => { setRefreshing(true); void load(); }} />}>
      <View style={styles.header}>
        <Text style={styles.title}>Timeline · {items.length} photo{items.length === 1 ? '' : 's'}</Text>
        <Text style={styles.meta}>Pair key: {pairKey.slice(0, 16)}…</Text>
      </View>
      {items.map((p) => {
        const t = thumbs[p.id];
        return (
          <View key={p.id} style={styles.card}>
            <Text style={styles.cardDate}>{new Date(p.capturedAt).toLocaleString()}</Text>
            {t ? (
              <Image source={{ uri: t.url, headers: t.headers }} style={styles.image} resizeMode="cover" />
            ) : (
              <View style={[styles.image, styles.placeholder]}>
                <Text style={{ color: '#fff' }}>🔒</Text>
              </View>
            )}
            <Text style={styles.cardCaption}>{p.caption ?? '(no caption)'}</Text>
            <Text style={styles.cardMeta}>
              {p.reason} · {p.levelCode ?? '—'} / {p.zoneCode ?? '—'}
            </Text>
          </View>
        );
      })}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: theme.colors.background },
  empty: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: theme.spacing.xl },
  emptyText: { color: theme.colors.textSecondary, fontStyle: 'italic' },
  header: { padding: theme.spacing.md, backgroundColor: theme.colors.surface, marginBottom: theme.spacing.sm },
  title: { fontSize: 18, fontWeight: '700', color: theme.colors.text },
  meta: { fontSize: 11, color: theme.colors.textSecondary, marginTop: 4 },
  card: { padding: theme.spacing.md, backgroundColor: theme.colors.surface, marginBottom: theme.spacing.xs },
  cardDate: { fontSize: 12, fontWeight: '600', color: theme.colors.text, marginBottom: 4 },
  image: { width: TILE, height: TILE * 0.75, borderRadius: 4, backgroundColor: '#000', marginBottom: 6 },
  placeholder: { alignItems: 'center', justifyContent: 'center' },
  cardCaption: { fontSize: 13, color: theme.colors.text },
  cardMeta: { fontSize: 11, color: theme.colors.textSecondary, marginTop: 2 },
});
