// Phase 180 — Camera-roll bulk import.
//
// A foreman dumps end-of-day captures from the device library:
//   1. Pick N images via expo-image-picker.
//   2. Pre-classify each via the on-device classifier.
//   3. Show a row-per-image with reason chip + caption editor.
//   4. Bulk upload (one-at-a-time, sequential — most phones throttle
//      multiple concurrent uploads).
//
// Failures fall through to the existing offline queue so a flaky link
// doesn't lose the batch.

import { useState } from 'react';
import {
  View, Text, StyleSheet, ScrollView, TouchableOpacity, Image,
  ActivityIndicator, Alert, TextInput,
} from 'react-native';
import * as ImagePicker from 'expo-image-picker';
import { useRouter } from 'expo-router';
import { theme } from '@/utils/theme';
import { useProjectStore } from '@/stores/projectStore';
import {
  classifyCapture, REASONS,
} from '@/components/site-photos/classifier';
import { captureSitePhoto } from '@/api/endpoints';
import type { SitePhotoReason, SitePhotoCaptureMeta } from '@/types/api';

interface PendingRow {
  uri: string;
  reason: SitePhotoReason;
  caption: string;
  status: 'idle' | 'uploading' | 'done' | 'failed';
  error?: string;
}

export default function BulkImportScreen() {
  const router = useRouter();
  const projectId = useProjectStore((s) => s.active?.id);
  const [rows, setRows] = useState<PendingRow[]>([]);
  const [picking, setPicking] = useState(false);
  const [uploading, setUploading] = useState(false);

  const onPick = async () => {
    setPicking(true);
    try {
      const r = await ImagePicker.launchImageLibraryAsync({
        mediaTypes: ImagePicker.MediaTypeOptions.Images,
        allowsMultipleSelection: true,
        selectionLimit: 30,
        quality: 0.85,
      });
      if (r.canceled) return;
      const next: PendingRow[] = r.assets.map((a) => ({
        uri: a.uri,
        // Run the classifier with library context so the default
        // reason isn't a wild guess. Classifier is < 1 ms.
        reason: classifyCapture({
          context: 'gallery',
          hasAnchorIssueId: false,
          hasAnchorElementGuid: false,
          hour: new Date().getHours(),
          hasGps: false,
          hasActiveWorkPackage: false,
        }).reason,
        caption: '',
        status: 'idle',
      }));
      setRows(next);
    } finally {
      setPicking(false);
    }
  };

  const onUploadAll = async () => {
    if (!projectId || rows.length === 0) return;
    setUploading(true);
    for (let i = 0; i < rows.length; i++) {
      // Mutate by index for React-state friendliness — each iteration
      // sets exactly one row's status field.
      const r = rows[i];
      if (r.status === 'done') continue;
      setRows((prev) => prev.map((x, idx) => idx === i ? { ...x, status: 'uploading' } : x));
      try {
        const meta: SitePhotoCaptureMeta = {
          reason: r.reason,
          caption: r.caption.trim() || undefined,
          source: 'mobile',
          capturedAt: new Date().toISOString(),
        };
        await captureSitePhoto({
          projectId,
          uri: r.uri,
          fileName: `bulk-${Date.now()}-${i}.jpg`,
          contentType: 'image/jpeg',
          meta: { ...meta, queuedClient: false },
        });
        setRows((prev) => prev.map((x, idx) => idx === i ? { ...x, status: 'done' } : x));
      } catch (err: unknown) {
        const msg = err instanceof Error ? err.message : 'Upload failed';
        setRows((prev) => prev.map((x, idx) => idx === i ? { ...x, status: 'failed', error: msg } : x));
      }
    }
    setUploading(false);
    Alert.alert('Bulk import',
      `${rows.filter(r => r.status === 'done').length} uploaded · ${rows.filter(r => r.status === 'failed').length} failed.`);
  };

  return (
    <View style={styles.root}>
      <View style={styles.toolbar}>
        <TouchableOpacity style={styles.toolBtn} onPress={onPick} disabled={picking}>
          <Text style={styles.toolBtnText}>📁 {picking ? 'Picking…' : 'Pick from camera roll'}</Text>
        </TouchableOpacity>
        {rows.length > 0 && (
          <TouchableOpacity
            style={[styles.toolBtnPrimary, uploading && { opacity: 0.5 }]}
            onPress={onUploadAll} disabled={uploading}>
            <Text style={styles.toolBtnPrimaryText}>
              {uploading ? 'Uploading…' : `Upload ${rows.length}`}
            </Text>
          </TouchableOpacity>
        )}
      </View>
      <ScrollView style={styles.list}>
        {rows.length === 0 ? (
          <View style={styles.empty}>
            <Text style={styles.emptyText}>Pick photos from the camera roll to get started.</Text>
          </View>
        ) : null}
        {rows.map((r, i) => (
          <View key={i} style={styles.row}>
            <Image source={{ uri: r.uri }} style={styles.thumb} />
            <View style={styles.body}>
              <View style={styles.chips}>
                {REASONS.map((rr) => (
                  <TouchableOpacity
                    key={rr}
                    onPress={() => setRows((prev) => prev.map((x, idx) => idx === i ? { ...x, reason: rr } : x))}
                    style={[styles.chip, r.reason === rr && styles.chipActive]}>
                    <Text style={[styles.chipText, r.reason === rr && styles.chipTextActive]}>{rr}</Text>
                  </TouchableOpacity>
                ))}
              </View>
              <TextInput
                style={styles.captionInput}
                placeholder="Caption (optional)"
                value={r.caption}
                onChangeText={(t) => setRows((prev) => prev.map((x, idx) => idx === i ? { ...x, caption: t } : x))}
              />
              <Text style={[styles.status, r.status === 'failed' && styles.statusFailed,
                            r.status === 'done' && styles.statusDone]}>
                {r.status === 'idle' ? 'Ready' :
                 r.status === 'uploading' ? 'Uploading…' :
                 r.status === 'done' ? '✓ Uploaded' : `✗ ${r.error ?? 'Failed'}`}
              </Text>
            </View>
            {r.status === 'uploading' ? <ActivityIndicator color={theme.colors.accent} /> : null}
          </View>
        ))}
      </ScrollView>
    </View>
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: theme.colors.background },
  toolbar: {
    flexDirection: 'row', padding: theme.spacing.sm, gap: theme.spacing.sm,
    backgroundColor: theme.colors.surface, borderBottomWidth: 1, borderBottomColor: theme.colors.border,
  },
  toolBtn: { flex: 1, padding: theme.spacing.sm, borderRadius: 4, borderWidth: 1, borderColor: theme.colors.border, alignItems: 'center' },
  toolBtnText: { fontSize: 13, fontWeight: '600', color: theme.colors.text },
  toolBtnPrimary: { padding: theme.spacing.sm, borderRadius: 4, backgroundColor: theme.colors.accent, alignItems: 'center', paddingHorizontal: theme.spacing.md },
  toolBtnPrimaryText: { color: '#fff', fontSize: 13, fontWeight: '700' },
  list: { flex: 1 },
  empty: { padding: theme.spacing.xl, alignItems: 'center' },
  emptyText: { color: theme.colors.textSecondary, fontStyle: 'italic' },
  row: {
    flexDirection: 'row', padding: theme.spacing.sm, gap: theme.spacing.sm,
    borderBottomWidth: 1, borderBottomColor: theme.colors.border,
    backgroundColor: theme.colors.surface,
  },
  thumb: { width: 80, height: 80, borderRadius: 4, backgroundColor: '#000' },
  body: { flex: 1 },
  chips: { flexDirection: 'row', flexWrap: 'wrap', gap: 4, marginBottom: 4 },
  chip: { paddingVertical: 3, paddingHorizontal: 8, borderRadius: 10, borderWidth: 1, borderColor: theme.colors.border },
  chipActive: { backgroundColor: theme.colors.accent, borderColor: theme.colors.accent },
  chipText: { fontSize: 10, color: theme.colors.text },
  chipTextActive: { color: '#fff', fontWeight: '600' },
  captionInput: { borderWidth: 1, borderColor: theme.colors.border, borderRadius: 4, paddingHorizontal: 6, paddingVertical: 4, fontSize: 12, marginTop: 2 },
  status: { fontSize: 11, color: theme.colors.textSecondary, marginTop: 2 },
  statusFailed: { color: '#C62828' },
  statusDone: { color: '#2E7D32', fontWeight: '600' },
});
