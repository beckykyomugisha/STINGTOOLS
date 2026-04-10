import { View, Text, StyleSheet } from 'react-native';
import { theme } from '@/utils/theme';

export default function IssuesScreen() {
  return (
    <View style={styles.container}>
      <Text style={styles.title}>Issues</Text>
      <Text style={styles.subtitle}>BIM issue list with priority filtering will appear here.</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: theme.colors.background,
    alignItems: 'center',
    justifyContent: 'center',
    padding: theme.spacing.lg,
  },
  title: {
    fontSize: theme.fontSize.xxl,
    fontWeight: '700',
    color: theme.colors.text,
    marginBottom: theme.spacing.sm,
  },
  subtitle: {
    fontSize: theme.fontSize.md,
    color: theme.colors.textSecondary,
    textAlign: 'center',
  },
});
