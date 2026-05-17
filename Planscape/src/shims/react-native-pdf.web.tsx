import React from 'react';
import { View, Text, StyleSheet } from 'react-native';

// Web stub — react-native-pdf is native-only; on web show an iframe fallback.
export default function Pdf({ source, style }: { source?: { uri?: string }; style?: any }) {
  const uri = source?.uri;
  if (!uri) return <View style={[styles.center, style]}><Text>No PDF source</Text></View>;
  return (
    <View style={[{ flex: 1 }, style]}>
      <iframe src={uri} style={{ flex: 1, width: '100%', height: '100%', border: 'none' }} title="PDF" />
    </View>
  );
}

const styles = StyleSheet.create({
  center: { flex: 1, justifyContent: 'center', alignItems: 'center' },
});
