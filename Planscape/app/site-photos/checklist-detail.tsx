// Phase 179 — Single photo checklist with item fulfilment.

import { useCallback, useEffect, useState } from 'react';
import {
  View, Text, StyleSheet, FlatList, RefreshControl, TouchableOpacity, ActivityIndicator, Alert,
} from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { theme } from '@/utils/theme';
import { useProjectStore } from '@/stores/projectStore';
import {
  getPhotoChecklist, type PhotoChecklistDetail,
} from '@/api/endpoints';

export default function ChecklistDetailScreen() {
  const router = useRouter();
  const { checklistId } = useLocalSearchParams<{ checklistId?: string }>();
  const projectId = useProjectStore((s) => s.active?.id);

  const [data, setData] = useState<PhotoChecklistDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!projectId || !checklistId) return;
    try {
      setError(null);
      const d = await getPhotoChecklist(projectId, checklistId);
      setData(d);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to load');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [projectId, checklistId]);

  useEffect(() => { void load(); }, [load]);

  const onCaptureFor = (itemTitle: string, defaultReason: string) => {
    if (!projectId) return;
    // Hand off to capture; the user comes back here when the photo
    // lands and taps "fulfil with photo …" on the corresponding item.
    Alert.alert('Capture for item', `Open the camera and capture a photo for "${itemTitle}".\nReason will default to "${defaultReason}".`, [
      { text: 'Cancel', style: 'cancel' },
      {
        text: 'Open camera',
        onPress: () => router.push({
          pathname: '/site-photos/capture',
          params: { projectId, context: 'checklist' },
        }),
      },
    ]);
  };

  if (!projectId || !checklistId) return <View style={styles.empty}><Text>Missing checklist.</Text></View>;
  if (loading) return <View style={styles.empty}><ActivityIndicator size="large" color={theme.colors.accent} /></View>;
  if (!data) return <View style={styles.empty}><Text>{error ?? 'Not found'}</Text></View>;

  return (
    <FlatList
      style={styles.root}
      data={data.items}
      keyExtractor={(i) => i.id}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={() => { setRefreshing(true); void load(); }} />}
      ListHeaderComponent={
        <View style={styles.header}>
          <Text style={styles.title}>{data.checklist.name}</Text>
          {data.checklist.description ? <Text style={styles.desc}>{data.checklist.description}</Text> : null}
          <Text style={styles.meta}>
            {data.checklist.kind ?? 'Custom'} · {data.checklist.status} · {data.items.filter(i => i.fulfilledByPhotoId || i.isWaived).length}/{data.items.length} done
          </Text>
        </View>
      }
      renderItem={({ item }) => {
        const done = !!item.fulfilledByPhotoId || item.isWaived;
        return (
          <View style={[styles.item, done && styles.itemDone]}>
            <View style={styles.itemMain}>
              <Text style={[styles.itemTitle, done && styles.itemTitleDone]}>{item.title}</Text>
              {item.description ? <Text style={styles.itemDesc}>{item.description}</Text> : null}
              <Text style={styles.itemMeta}>
                Default reason: {item.defaultReason}{item.isRequired ? ' · required' : ' · optional'}
                {item.isWaived ? ` · waived (${item.waivedReason ?? 'n/a'})` : ''}
              </Text>
            </View>
            {!done && (
              <TouchableOpacity style={styles.captureBtn} onPress={() => onCaptureFor(item.title, item.defaultReason)}>
                <Text style={styles.captureBtnText}>📷</Text>
              </TouchableOpacity>
            )}
          </View>
        );
      }}
    />
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: theme.colors.background },
  empty: { padding: theme.spacing.xl, alignItems: 'center' },
  header: { padding: theme.spacing.md, backgroundColor: theme.colors.surface, marginBottom: theme.spacing.xs },
  title: { fontSize: 18, fontWeight: '700', color: theme.colors.text },
  desc: { fontSize: 13, color: theme.colors.textSecondary, marginTop: 4 },
  meta: { fontSize: 11, color: theme.colors.textSecondary, marginTop: 4 },
  item: {
    flexDirection: 'row', alignItems: 'center',
    backgroundColor: theme.colors.surface,
    padding: theme.spacing.md,
    borderBottomWidth: 1, borderBottomColor: theme.colors.border,
  },
  itemDone: { backgroundColor: '#F1F8E9' },
  itemMain: { flex: 1 },
  itemTitle: { fontSize: 14, fontWeight: '600', color: theme.colors.text },
  itemTitleDone: { textDecorationLine: 'line-through', color: theme.colors.textSecondary },
  itemDesc: { fontSize: 12, color: theme.colors.textSecondary, marginTop: 2 },
  itemMeta: { fontSize: 10, color: theme.colors.textSecondary, marginTop: 2 },
  captureBtn: {
    backgroundColor: theme.colors.accent, padding: theme.spacing.sm, borderRadius: 24,
    width: 44, height: 44, alignItems: 'center', justifyContent: 'center',
  },
  captureBtnText: { fontSize: 18 },
});
