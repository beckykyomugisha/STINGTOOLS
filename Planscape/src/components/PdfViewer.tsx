import React, { useEffect, useState } from 'react';
import { View, ActivityIndicator, Text, StyleSheet } from 'react-native';
import Pdf from 'react-native-pdf';
import { documentCache } from '../services/documentCache';
import { api } from '../services/apiClient';

interface Props { documentId: string }

export function PdfViewer({ documentId }: Props) {
  const [localUri, setLocalUri] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const url = api.downloadDocument(documentId);
    documentCache.downloadToCache(documentId, url)
      .then(setLocalUri)
      .catch(err => setError(err instanceof Error ? err.message : String(err)));
  }, [documentId]);

  if (error) return <View style={styles.center}><Text>Error: {error}</Text></View>;
  if (!localUri) return <View style={styles.center}><ActivityIndicator /></View>;
  return <Pdf source={{ uri: localUri }} style={styles.pdf} />;
}

const styles = StyleSheet.create({
  center: { flex: 1, justifyContent: 'center', alignItems: 'center' },
  pdf: { flex: 1 },
});
