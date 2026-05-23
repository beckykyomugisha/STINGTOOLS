// Phase 179 — Photo annotation surface.
//
// The user picks an annotation kind (arrow / circle / text), drops it
// onto the displayed photo, and submits. Coordinates normalise to 0..1
// over the photo extent so server / desktop / PDF render identical
// overlays.
//
// MVP: a single shape per annotation is captured per submit. Future
// work: multi-shape sketch + freehand polylines.

import { useEffect, useRef, useState } from 'react';
import {
  View, Text, StyleSheet, Image, TouchableOpacity, Alert, TextInput,
  ActivityIndicator, ScrollView, GestureResponderEvent,
} from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { theme } from '@/utils/theme';
import { useProjectStore } from '@/stores/projectStore';
import { createPhotoAnnotation, getSitePhotoFile } from '@/api/endpoints';

type ShapeKind = 'arrow' | 'circle' | 'text';

interface PendingShape {
  kind: ShapeKind;
  x1: number; y1: number;
  x2?: number; y2?: number;
  text?: string;
}

export default function AnnotateScreen() {
  const router = useRouter();
  const { photoId } = useLocalSearchParams<{ photoId?: string }>();
  const projectId = useProjectStore((s) => s.active?.id);

  const [imageUri, setImageUri] = useState<string | null>(null);
  const [imageHeaders, setImageHeaders] = useState<Record<string, string>>({});
  const [kind, setKind] = useState<ShapeKind>('arrow');
  const [shape, setShape] = useState<PendingShape | null>(null);
  const [text, setText] = useState('');
  const [summary, setSummary] = useState('');
  const [saving, setSaving] = useState(false);
  const [extent, setExtent] = useState<{ w: number; h: number }>({ w: 1, h: 1 });

  useEffect(() => {
    void (async () => {
      if (!projectId || !photoId) return;
      try {
        const f = await getSitePhotoFile(projectId, photoId);
        setImageUri(f.url);
        setImageHeaders(f.headers);
      } catch (err: unknown) {
        Alert.alert('Photo', err instanceof Error ? err.message : 'Failed to load');
      }
    })();
  }, [projectId, photoId]);

  const onPress = (e: GestureResponderEvent) => {
    const { locationX, locationY } = e.nativeEvent;
    const xn = locationX / extent.w;
    const yn = locationY / extent.h;
    if (kind === 'text') {
      setShape({ kind, x1: xn, y1: yn, text });
    } else if (!shape) {
      setShape({ kind, x1: xn, y1: yn });
    } else {
      setShape({ ...shape, x2: xn, y2: yn });
    }
  };

  const onSave = async () => {
    if (!projectId || !photoId || !shape) return;
    if (kind !== 'text' && (shape.x2 == null || shape.y2 == null)) {
      Alert.alert('Annotation', 'Tap once for the start, again for the end.');
      return;
    }
    setSaving(true);
    try {
      const shapesJson = JSON.stringify([
        kind === 'arrow' ? { kind: 'arrow', x1: shape.x1, y1: shape.y1, x2: shape.x2, y2: shape.y2, color: '#E65C00', width: 2 }
          : kind === 'circle' ? { kind: 'circle', cx: shape.x1, cy: shape.y1, r: Math.hypot((shape.x2 ?? 0) - shape.x1, (shape.y2 ?? 0) - shape.y1), color: '#C62828', width: 2 }
          : { kind: 'text', x: shape.x1, y: shape.y1, text, color: '#000', size: 12 },
      ]);
      await createPhotoAnnotation(projectId, photoId, shapesJson, summary.trim() || undefined);
      router.back();
    } catch (err: unknown) {
      Alert.alert('Save annotation', err instanceof Error ? err.message : 'Failed');
    } finally {
      setSaving(false);
    }
  };

  if (!projectId || !photoId) return <View style={styles.empty}><Text>Missing photo.</Text></View>;
  if (!imageUri) return <View style={styles.empty}><ActivityIndicator color={theme.colors.accent} /></View>;

  return (
    <ScrollView style={styles.root}>
      <View style={styles.toolbar}>
        {(['arrow', 'circle', 'text'] as ShapeKind[]).map((k) => (
          <TouchableOpacity
            key={k}
            style={[styles.tool, kind === k && styles.toolActive]}
            onPress={() => { setKind(k); setShape(null); }}>
            <Text style={[styles.toolText, kind === k && styles.toolTextActive]}>{k}</Text>
          </TouchableOpacity>
        ))}
      </View>
      {kind === 'text' && (
        <TextInput
          style={styles.input}
          placeholder="Text to drop on photo"
          value={text}
          onChangeText={setText}
        />
      )}
      <TouchableOpacity activeOpacity={1} onPress={onPress}
        onLayout={(e) => setExtent({ w: e.nativeEvent.layout.width, h: e.nativeEvent.layout.height })}>
        <Image source={{ uri: imageUri, headers: imageHeaders }} style={styles.image} resizeMode="contain" />
      </TouchableOpacity>
      <View style={styles.footer}>
        <Text style={styles.hint}>
          {shape == null
            ? 'Tap the photo to drop the first point.'
            : kind !== 'text' && shape.x2 == null
              ? 'Tap again to drop the end point.'
              : 'Ready to save.'}
        </Text>
        <TextInput
          style={styles.input}
          placeholder="Summary (optional)"
          value={summary}
          onChangeText={setSummary}
        />
        <TouchableOpacity style={styles.save} onPress={onSave} disabled={saving || !shape}>
          <Text style={styles.saveText}>{saving ? 'Saving…' : 'Save annotation'}</Text>
        </TouchableOpacity>
      </View>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: theme.colors.background },
  empty: { flex: 1, alignItems: 'center', justifyContent: 'center' },
  toolbar: {
    flexDirection: 'row', padding: theme.spacing.sm, gap: theme.spacing.sm,
    backgroundColor: theme.colors.surface, borderBottomWidth: 1, borderBottomColor: theme.colors.border,
  },
  tool: {
    paddingVertical: 6, paddingHorizontal: 12, borderRadius: 4,
    borderWidth: 1, borderColor: theme.colors.border,
  },
  toolActive: { backgroundColor: theme.colors.accent, borderColor: theme.colors.accent },
  toolText: { color: theme.colors.text, fontSize: 13 },
  toolTextActive: { color: '#fff' },
  image: { width: '100%', aspectRatio: 4/3, backgroundColor: '#000' },
  footer: { padding: theme.spacing.md, gap: theme.spacing.sm },
  input: {
    borderWidth: 1, borderColor: theme.colors.border, borderRadius: 4,
    padding: theme.spacing.sm, fontSize: 13, color: theme.colors.text,
    backgroundColor: theme.colors.surface,
  },
  hint: { fontSize: 12, color: theme.colors.textSecondary, fontStyle: 'italic' },
  save: { backgroundColor: theme.colors.accent, padding: theme.spacing.md, borderRadius: 4, alignItems: 'center' },
  saveText: { color: '#fff', fontWeight: '700', fontSize: 14 },
});
