import { View, Text, StyleSheet } from 'react-native';
import { theme } from '@/utils/theme';

export default function LoginScreen() {
  return (
    <View style={styles.container}>
      <Text style={styles.title}>Planscape</Text>
      <Text style={styles.subtitle}>Login form will appear here.</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: theme.colors.primary,
    alignItems: 'center',
    justifyContent: 'center',
    padding: theme.spacing.lg,
  },
  title: {
    fontSize: theme.fontSize.hero,
    fontWeight: '700',
    color: theme.colors.surface,
    marginBottom: theme.spacing.sm,
  },
  subtitle: {
    fontSize: theme.fontSize.md,
    color: theme.colors.surface,
    opacity: 0.7,
  },
});
