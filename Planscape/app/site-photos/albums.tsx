// Phase 179 — Photo albums on mobile.
//
// Lists every album visible to the caller, with pull-to-refresh and a
// FAB to create a new one (PM/Admin/Owner only — server enforces).
// Tap an album to open its detail screen.

import { useCallback, useEffect, useState } from 'react';
import {
  View,
  Text,
  StyleSheet,
  FlatList,
  RefreshControl,
  TouchableOpacity,
  Alert,
  Modal,
  TextInput,
  ActivityIndicator,
} from 'react-native';
import { useRouter } from 'expo-router';
import { theme } from '@/utils/theme';
import { useProjectStore } from '@/stores/projectStore';
import {
  listPhotoAlbums,
  createPhotoAlbum,
  type PhotoAlbum,
} from '@/api/endpoints';

export default function AlbumsScreen() {
  const router = useRouter();
  const projectId = useProjectStore((s) => s.active?.id);

  const [albums, setAlbums] = useState<PhotoAlbum[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [createOpen, setCreateOpen] = useState(false);
  const [newName, setNewName] = useState('');
  const [creating, setCreating] = useState(false);

  const load = useCallback(async () => {
    if (!projectId) return;
    try {
      setError(null);
      const rows = await listPhotoAlbums(projectId);
      setAlbums(rows);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to load albums');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [projectId]);

  useEffect(() => { void load(); }, [load]);

  const onCreate = async () => {
    if (!projectId || !newName.trim()) return;
    setCreating(true);
    try {
      const album = await createPhotoAlbum(projectId, { name: newName.trim(), visibility: 'Members' });
      setNewName('');
      setCreateOpen(false);
      router.push({ pathname: '/site-photos/album-detail', params: { albumId: album.id } });
    } catch (err: unknown) {
      Alert.alert('Create album', err instanceof Error ? err.message : 'Failed');
    } finally {
      setCreating(false);
    }
  };

  if (!projectId) {
    return <View style={styles.empty}><Text style={styles.emptyText}>Select a project first.</Text></View>;
  }
  if (loading) {
    return <View style={styles.empty}><ActivityIndicator size="large" color={theme.colors.accent} /></View>;
  }

  return (
    <View style={styles.root}>
      <FlatList
        data={albums}
        keyExtractor={(a) => a.id}
        refreshControl={
          <RefreshControl refreshing={refreshing} onRefresh={() => { setRefreshing(true); void load(); }}
            tintColor={theme.colors.accent} />
        }
        ListEmptyComponent={
          <View style={styles.empty}>
            <Text style={styles.emptyText}>{error ?? 'No albums yet — tap ＋ to create one.'}</Text>
          </View>
        }
        renderItem={({ item }) => (
          <TouchableOpacity
            style={styles.row}
            onPress={() => router.push({ pathname: '/site-photos/album-detail', params: { albumId: item.id } })}>
            <View style={styles.rowMain}>
              <Text style={styles.rowTitle}>{item.name}</Text>
              <Text style={styles.rowMeta}>
                {item.visibility} · {item.kind ?? 'Album'} · {item.photoCount} photo{item.photoCount === 1 ? '' : 's'}
                {item.isLocked ? ' · 🔒' : ''}
              </Text>
            </View>
            <Text style={styles.chevron}>›</Text>
          </TouchableOpacity>
        )}
      />
      <TouchableOpacity style={styles.fab} onPress={() => setCreateOpen(true)}>
        <Text style={styles.fabText}>＋</Text>
      </TouchableOpacity>

      <Modal transparent visible={createOpen} animationType="fade" onRequestClose={() => setCreateOpen(false)}>
        <View style={styles.modalBackdrop}>
          <View style={styles.modalCard}>
            <Text style={styles.modalTitle}>New album</Text>
            <TextInput
              style={styles.modalInput}
              placeholder="Album name"
              value={newName}
              onChangeText={setNewName}
              autoFocus
            />
            <View style={styles.modalRow}>
              <TouchableOpacity onPress={() => setCreateOpen(false)} style={styles.modalCancel}>
                <Text style={styles.modalCancelText}>Cancel</Text>
              </TouchableOpacity>
              <TouchableOpacity onPress={onCreate} style={styles.modalOk} disabled={creating}>
                <Text style={styles.modalOkText}>{creating ? 'Creating…' : 'Create'}</Text>
              </TouchableOpacity>
            </View>
          </View>
        </View>
      </Modal>
    </View>
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: theme.colors.background },
  empty: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: theme.spacing.xl },
  emptyText: { color: theme.colors.textSecondary, fontStyle: 'italic' },
  row: {
    flexDirection: 'row',
    backgroundColor: theme.colors.surface,
    padding: theme.spacing.md,
    borderBottomWidth: 1,
    borderBottomColor: theme.colors.border,
    alignItems: 'center',
  },
  rowMain: { flex: 1 },
  rowTitle: { fontSize: 15, fontWeight: '600', color: theme.colors.text },
  rowMeta: { fontSize: 11, color: theme.colors.textSecondary, marginTop: 2 },
  chevron: { fontSize: 22, color: theme.colors.textSecondary, marginLeft: 8 },
  fab: {
    position: 'absolute', bottom: 24, right: 24,
    width: 56, height: 56, borderRadius: 28,
    backgroundColor: theme.colors.accent,
    alignItems: 'center', justifyContent: 'center',
    elevation: 4, shadowColor: '#000', shadowOpacity: 0.2, shadowRadius: 4,
  },
  fabText: { color: '#fff', fontSize: 24, lineHeight: 28 },
  modalBackdrop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.4)', alignItems: 'center', justifyContent: 'center' },
  modalCard: {
    width: '85%', maxWidth: 360, backgroundColor: theme.colors.surface,
    padding: theme.spacing.lg, borderRadius: 8,
  },
  modalTitle: { fontSize: 16, fontWeight: '700', marginBottom: theme.spacing.md, color: theme.colors.text },
  modalInput: {
    borderWidth: 1, borderColor: theme.colors.border, borderRadius: 4,
    padding: theme.spacing.sm, fontSize: 14, marginBottom: theme.spacing.md,
    color: theme.colors.text,
  },
  modalRow: { flexDirection: 'row', justifyContent: 'flex-end', gap: theme.spacing.sm },
  modalCancel: { padding: theme.spacing.sm },
  modalCancelText: { color: theme.colors.textSecondary, fontSize: 14 },
  modalOk: { backgroundColor: theme.colors.accent, padding: theme.spacing.sm, borderRadius: 4, paddingHorizontal: theme.spacing.md },
  modalOkText: { color: '#fff', fontSize: 14, fontWeight: '600' },
});
