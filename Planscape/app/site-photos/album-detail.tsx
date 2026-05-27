// Phase 179 — Single album detail screen.
//
// Shows the photos in an album as a 3-column grid; supports lock /
// unlock (PM+ only — server gates) and "Share link" issuance for
// outside-tenant viewing.

import { useCallback, useEffect, useState } from 'react';
import {
  View, Text, StyleSheet, ScrollView, RefreshControl, ActivityIndicator,
  TouchableOpacity, Image, Alert, Dimensions, Share,
} from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { theme } from '@/utils/theme';
import { useProjectStore } from '@/stores/projectStore';
import {
  getPhotoAlbum, lockPhotoAlbum, createPhotoShareLink,
  getSitePhotoFile, removePhotoFromAlbum,
  type PhotoAlbumDetail,
} from '@/api/endpoints';

const SCREEN_WIDTH = Dimensions.get('window').width;
const COLS = 3;
const GUTTER = theme.spacing.xs;
const TILE = (SCREEN_WIDTH - theme.spacing.md * 2 - GUTTER * (COLS - 1)) / COLS;

interface ResolvedThumb { url: string; headers: Record<string, string>; }

export default function AlbumDetailScreen() {
  const router = useRouter();
  const { albumId } = useLocalSearchParams<{ albumId?: string }>();
  const projectId = useProjectStore((s) => s.active?.id);

  const [detail, setDetail] = useState<PhotoAlbumDetail | null>(null);
  const [thumbs, setThumbs] = useState<Record<string, ResolvedThumb>>({});
  const [refreshing, setRefreshing] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!projectId || !albumId) return;
    try {
      setError(null);
      const d = await getPhotoAlbum(projectId, albumId);
      setDetail(d);
      const next: Record<string, ResolvedThumb> = {};
      await Promise.all(
        d.photos.map(async (p) => {
          try { next[p.photoId] = await getSitePhotoFile(projectId, p.photoId); }
          catch { /* ignore */ }
        }),
      );
      setThumbs(next);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to load');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [projectId, albumId]);

  useEffect(() => { void load(); }, [load]);

  const onLockToggle = async () => {
    if (!projectId || !detail) return;
    try {
      await lockPhotoAlbum(projectId, detail.album.id, !detail.album.isLocked);
      await load();
    } catch (err: unknown) {
      Alert.alert('Lock', err instanceof Error ? err.message : 'Failed');
    }
  };

  const onShare = async () => {
    if (!projectId || !detail) return;
    try {
      const link = await createPhotoShareLink(projectId, { albumId: detail.album.id, label: detail.album.name });
      const url = `Token: ${link.token}\nExpires: ${link.expiresAt}\nForce-redacted: ${link.forceRedacted}`;
      await Share.share({ message: `Planscape album share — ${detail.album.name}\n\n${url}` });
    } catch (err: unknown) {
      Alert.alert('Share link', err instanceof Error ? err.message : 'Failed');
    }
  };

  const onRemovePhoto = async (photoId: string) => {
    if (!projectId || !detail) return;
    Alert.alert('Remove from album', 'Remove this photo from the album?', [
      { text: 'Cancel', style: 'cancel' },
      {
        text: 'Remove', style: 'destructive',
        onPress: async () => {
          try {
            await removePhotoFromAlbum(projectId, detail.album.id, photoId);
            await load();
          } catch (err: unknown) {
            Alert.alert('Remove', err instanceof Error ? err.message : 'Failed');
          }
        },
      },
    ]);
  };

  if (!projectId || !albumId) return <View style={styles.empty}><Text>Missing project / album.</Text></View>;
  if (loading) return <View style={styles.empty}><ActivityIndicator size="large" color={theme.colors.accent} /></View>;

  return (
    <ScrollView
      style={styles.root}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={() => { setRefreshing(true); void load(); }} />}>
      <View style={styles.header}>
        <Text style={styles.title}>{detail?.album.name}</Text>
        {detail?.album.description ? <Text style={styles.desc}>{detail.album.description}</Text> : null}
        <Text style={styles.meta}>
          {detail?.album.visibility} · {detail?.photos.length ?? 0} photo{(detail?.photos.length ?? 0) === 1 ? '' : 's'}
          {detail?.album.isLocked ? ' · 🔒 locked' : ''}
        </Text>
        <View style={styles.actions}>
          <TouchableOpacity style={styles.button} onPress={onLockToggle}>
            <Text style={styles.buttonText}>{detail?.album.isLocked ? 'Unlock' : 'Lock'}</Text>
          </TouchableOpacity>
          <TouchableOpacity style={styles.buttonPrimary} onPress={onShare}>
            <Text style={styles.buttonPrimaryText}>🔗 Share link</Text>
          </TouchableOpacity>
        </View>
        {error ? <Text style={styles.error}>{error}</Text> : null}
      </View>
      <View style={styles.grid}>
        {detail?.photos.map((p) => {
          const t = thumbs[p.photoId];
          return (
            <TouchableOpacity
              key={p.photoId}
              style={styles.tile}
              onLongPress={() => onRemovePhoto(p.photoId)}>
              {t ? <Image source={{ uri: t.url, headers: t.headers }} style={styles.tileImg} resizeMode="cover" /> : <View style={[styles.tileImg, styles.tilePlaceholder]} />}
            </TouchableOpacity>
          );
        })}
      </View>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: theme.colors.background },
  empty: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: theme.spacing.xl },
  header: { padding: theme.spacing.md, backgroundColor: theme.colors.surface, marginBottom: theme.spacing.sm },
  title: { fontSize: 18, fontWeight: '700', color: theme.colors.text },
  desc: { fontSize: 13, color: theme.colors.textSecondary, marginTop: 4 },
  meta: { fontSize: 11, color: theme.colors.textSecondary, marginTop: 4 },
  actions: { flexDirection: 'row', gap: theme.spacing.sm, marginTop: theme.spacing.md },
  button: {
    paddingVertical: 8, paddingHorizontal: 14,
    backgroundColor: theme.colors.background, borderWidth: 1, borderColor: theme.colors.border,
    borderRadius: 4,
  },
  buttonText: { color: theme.colors.text, fontSize: 13, fontWeight: '600' },
  buttonPrimary: {
    paddingVertical: 8, paddingHorizontal: 14,
    backgroundColor: theme.colors.accent, borderRadius: 4,
  },
  buttonPrimaryText: { color: '#fff', fontSize: 13, fontWeight: '600' },
  error: { color: '#C62828', marginTop: theme.spacing.sm },
  grid: { flexDirection: 'row', flexWrap: 'wrap', padding: theme.spacing.md, gap: GUTTER },
  tile: { width: TILE, height: TILE, marginBottom: GUTTER, borderRadius: 4, overflow: 'hidden' },
  tileImg: { width: '100%', height: '100%' },
  tilePlaceholder: { backgroundColor: '#E0E0E0' },
});
