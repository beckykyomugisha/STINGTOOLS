import { useEffect, useState } from 'react';
import { View, Text, Image, StyleSheet, ScrollView, ActivityIndicator } from 'react-native';
import { theme } from '@/utils/theme';
import { listIssueAttachments, getAttachmentThumbnailUrl } from '@/api/endpoints';
import type { IssueAttachment } from '@/types/api';
import { getToken } from '@/api/client';

/**
 * NEW-INFO-01 — Horizontal thumbnail strip shown inside the issue detail modal.
 * Lists attachments, requests the 300 px JPEG thumbnail variant for each image,
 * and falls back to a filename chip for non-images / missing thumbnails.
 */
export function AttachmentStrip({ projectId, issueId }: { projectId: string; issueId: string }) {
  const [items, setItems] = useState<IssueAttachment[]>([]);
  const [loading, setLoading] = useState(true);
  const [urls, setUrls] = useState<Record<string, string>>({});
  const [authToken, setAuthToken] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const list = await listIssueAttachments(projectId, issueId);
        if (cancelled) return;
        setItems(list);
        const tok = await getToken();
        setAuthToken(tok);
        const urlMap: Record<string, string> = {};
        await Promise.all(list.map(async a => {
          if ((a.contentType || '').startsWith('image/')) {
            urlMap[a.id] = await getAttachmentThumbnailUrl(projectId, issueId, a.id, 300);
          }
        }));
        if (!cancelled) setUrls(urlMap);
      } catch {
        if (!cancelled) setItems([]);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [projectId, issueId]);

  if (loading) {
    return <ActivityIndicator color={theme.colors.accent} style={{ marginVertical: 12 }} />;
  }
  if (items.length === 0) {
    return <Text style={styles.empty}>No photos or files attached.</Text>;
  }

  return (
    <View>
      <Text style={styles.label}>Attachments ({items.length})</Text>
      <ScrollView horizontal showsHorizontalScrollIndicator={false} style={{ marginTop: 6 }}>
        {items.map(a => (
          <View key={a.id} style={styles.tile}>
            {urls[a.id] ? (
              <Image
                source={{ uri: urls[a.id], headers: authToken ? { Authorization: `Bearer ${authToken}` } : undefined }}
                style={styles.thumb}
                resizeMode="cover"
              />
            ) : (
              <View style={[styles.thumb, styles.fileFallback]}>
                <Text style={styles.fileIcon}>📄</Text>
              </View>
            )}
            <Text numberOfLines={1} style={styles.caption}>{a.fileName}</Text>
          </View>
        ))}
      </ScrollView>
    </View>
  );
}

const styles = StyleSheet.create({
  label: { fontSize: 13, fontWeight: '600', color: theme.colors.textSecondary, marginTop: 12 },
  empty: { fontSize: 13, color: theme.colors.textSecondary, fontStyle: 'italic', marginTop: 8 },
  tile: { width: 96, marginRight: 8 },
  thumb: { width: 96, height: 96, borderRadius: 6, backgroundColor: theme.colors.background, borderWidth: 1, borderColor: theme.colors.border },
  fileFallback: { alignItems: 'center', justifyContent: 'center' },
  fileIcon: { fontSize: 32 },
  caption: { fontSize: 10, color: theme.colors.textSecondary, marginTop: 4 },
});
