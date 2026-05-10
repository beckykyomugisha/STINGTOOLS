// Phase 179.2 — NDA acceptance modal for site photos.
//
// Surfaced when a list endpoint includes the photo id in its
// `ndaRequiredIds` array (or a /file fetch returns 403 nda_required).
// Shows the project's NDA boilerplate, captures one-tap acceptance,
// and POSTs to /accept-nda. After success, the caller refreshes the
// list so the lock disappears.
//
// Acceptance is per-photo + per-user; the server treats re-posts as
// idempotent so this is safe to call even if the cache desyncs.

import { useState } from 'react';
import {
  Modal, View, Text, ScrollView, TouchableOpacity, ActivityIndicator,
  StyleSheet,
} from 'react-native';
import { theme } from '@/utils/theme';
import { acceptPhotoNda } from '@/api/endpoints';

const DEFAULT_NDA_TEXT =
  'This photo is provided under a non-disclosure agreement.\n\n' +
  '• You will not redistribute, reproduce, or publish the photo or its ' +
  'derivative works outside the project team.\n' +
  '• You acknowledge the photo may contain commercially or contractually ' +
  'sensitive content.\n' +
  '• Your acceptance is logged with timestamp, IP, and user-agent for ' +
  'audit purposes.\n\n' +
  'By tapping "Accept & view" you confirm you have read and accept these ' +
  'terms for this photo.';

export interface NdaPromptModalProps {
  visible: boolean;
  projectId: string;
  photoId: string;
  ndaText?: string;
  onAccepted: () => void;
  onCancel: () => void;
}

export function NdaPromptModal({
  visible, projectId, photoId, ndaText, onAccepted, onCancel,
}: NdaPromptModalProps) {
  const [accepting, setAccepting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const onAccept = async () => {
    setAccepting(true);
    setError(null);
    try {
      await acceptPhotoNda(projectId, photoId);
      onAccepted();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to record acceptance');
    } finally {
      setAccepting(false);
    }
  };

  return (
    <Modal transparent visible={visible} animationType="fade" onRequestClose={onCancel}>
      <View style={styles.backdrop}>
        <View style={styles.card}>
          <Text style={styles.title}>NDA acceptance required</Text>
          <ScrollView style={styles.body}>
            <Text style={styles.bodyText}>{ndaText ?? DEFAULT_NDA_TEXT}</Text>
          </ScrollView>
          {error ? <Text style={styles.error}>{error}</Text> : null}
          <View style={styles.actions}>
            <TouchableOpacity onPress={onCancel} style={styles.cancel} disabled={accepting}>
              <Text style={styles.cancelText}>Cancel</Text>
            </TouchableOpacity>
            <TouchableOpacity onPress={onAccept} style={styles.accept} disabled={accepting}>
              {accepting
                ? <ActivityIndicator color="#fff" />
                : <Text style={styles.acceptText}>Accept &amp; view</Text>}
            </TouchableOpacity>
          </View>
        </View>
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  backdrop: { flex: 1, backgroundColor: 'rgba(0,0,0,0.5)', alignItems: 'center', justifyContent: 'center' },
  card: {
    width: '90%', maxWidth: 480, maxHeight: '80%',
    backgroundColor: theme.colors.surface,
    borderRadius: 8, padding: theme.spacing.lg,
  },
  title: { fontSize: 16, fontWeight: '700', color: theme.colors.text, marginBottom: theme.spacing.sm },
  body: { maxHeight: 320, marginBottom: theme.spacing.md },
  bodyText: { fontSize: 13, color: theme.colors.text, lineHeight: 19 },
  error: { color: '#C62828', fontSize: 12, marginBottom: theme.spacing.sm },
  actions: { flexDirection: 'row', justifyContent: 'flex-end', gap: theme.spacing.sm },
  cancel: { padding: theme.spacing.sm },
  cancelText: { color: theme.colors.textSecondary, fontSize: 14 },
  accept: {
    backgroundColor: theme.colors.accent,
    paddingHorizontal: theme.spacing.md,
    paddingVertical: theme.spacing.sm,
    borderRadius: 4, minWidth: 120, alignItems: 'center',
  },
  acceptText: { color: '#fff', fontSize: 14, fontWeight: '600' },
});
