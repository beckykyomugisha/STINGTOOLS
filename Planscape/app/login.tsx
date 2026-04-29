import { useState } from 'react';
import {
  View,
  Text,
  TextInput,
  TouchableOpacity,
  StyleSheet,
  KeyboardAvoidingView,
  Platform,
  ActivityIndicator,
  ScrollView,
} from 'react-native';
import { useRouter } from 'expo-router';
import { theme } from '@/utils/theme';
import { useAuth } from '@/hooks/useAuth';

export default function LoginScreen() {
  const router = useRouter();
  const { login, loading, error } = useAuth();

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [serverUrl, setServerUrl] = useState('');
  const [showServer, setShowServer] = useState(false);

  async function handleLogin() {
    const url = serverUrl.trim() || undefined;
    const ok = await login(email.trim(), password, url);
    if (ok) {
      router.replace('/(tabs)');
    }
  }

  return (
    <KeyboardAvoidingView
      style={styles.root}
      behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
    >
      <ScrollView
        contentContainerStyle={styles.scroll}
        keyboardShouldPersistTaps="handled"
      >
        <View style={styles.header}>
          <Text style={styles.brand}>Planscape</Text>
          <Text style={styles.tagline}>Planscape Field Companion</Text>
        </View>

        <View style={styles.card}>
          <Text style={styles.cardTitle}>Sign In</Text>

          {error ? <Text style={styles.error}>{error}</Text> : null}

          <Text style={styles.label}>Email</Text>
          <TextInput
            style={styles.input}
            placeholder="you@company.com"
            placeholderTextColor={theme.colors.disabled}
            autoCapitalize="none"
            keyboardType="email-address"
            textContentType="emailAddress"
            value={email}
            onChangeText={setEmail}
          />

          <Text style={styles.label}>Password</Text>
          <TextInput
            style={styles.input}
            placeholder="Enter password"
            placeholderTextColor={theme.colors.disabled}
            secureTextEntry
            textContentType="password"
            value={password}
            onChangeText={setPassword}
          />

          <TouchableOpacity
            onPress={() => setShowServer(!showServer)}
            accessibilityRole="button"
            accessibilityLabel={showServer ? 'Hide server settings' : 'Show custom server URL'}
            accessibilityState={{ expanded: showServer }}
          >
            <Text style={styles.toggle}>
              {showServer ? 'Hide server settings' : 'Custom server URL'}
            </Text>
          </TouchableOpacity>

          {showServer && (
            <>
              <Text style={styles.label}>Server URL</Text>
              <TextInput
                style={styles.input}
                placeholder="https://api.planscape.com"
                placeholderTextColor={theme.colors.disabled}
                autoCapitalize="none"
                keyboardType="url"
                value={serverUrl}
                onChangeText={setServerUrl}
              />
            </>
          )}

          <TouchableOpacity
            style={[styles.button, loading && styles.buttonDisabled]}
            onPress={handleLogin}
            disabled={loading || !email || !password}
            accessibilityRole="button"
            accessibilityLabel="Sign in"
            accessibilityState={{ disabled: loading || !email || !password, busy: loading }}
          >
            {loading ? (
              <ActivityIndicator color={theme.colors.surface} />
            ) : (
              <Text style={styles.buttonText}>Sign In</Text>
            )}
          </TouchableOpacity>
        </View>

        <Text style={styles.footer}>
          StingTools BIM Coordination Platform
        </Text>
      </ScrollView>
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  root: {
    flex: 1,
    backgroundColor: theme.colors.primary,
  },
  scroll: {
    flexGrow: 1,
    justifyContent: 'center',
    padding: theme.spacing.lg,
  },
  header: {
    alignItems: 'center',
    marginBottom: theme.spacing.xl,
  },
  brand: {
    fontSize: theme.fontSize.hero,
    fontWeight: '700',
    color: theme.colors.surface,
  },
  tagline: {
    fontSize: theme.fontSize.md,
    color: theme.colors.surface,
    opacity: 0.7,
    marginTop: theme.spacing.xs,
  },
  card: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.xl,
    padding: theme.spacing.lg,
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 4 },
    shadowOpacity: 0.15,
    shadowRadius: 12,
    elevation: 6,
  },
  cardTitle: {
    fontSize: theme.fontSize.xl,
    fontWeight: '600',
    color: theme.colors.text,
    marginBottom: theme.spacing.md,
  },
  label: {
    fontSize: theme.fontSize.sm,
    fontWeight: '600',
    color: theme.colors.textSecondary,
    marginBottom: theme.spacing.xs,
    marginTop: theme.spacing.md,
  },
  input: {
    backgroundColor: theme.colors.background,
    borderRadius: theme.borderRadius.md,
    borderWidth: 1,
    borderColor: theme.colors.border,
    paddingHorizontal: theme.spacing.md,
    paddingVertical: theme.spacing.sm + 4,
    fontSize: theme.fontSize.md,
    color: theme.colors.text,
  },
  toggle: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.accent,
    marginTop: theme.spacing.md,
    textAlign: 'center',
  },
  button: {
    backgroundColor: theme.colors.accent,
    borderRadius: theme.borderRadius.md,
    paddingVertical: theme.spacing.sm + 4,
    alignItems: 'center',
    marginTop: theme.spacing.lg,
  },
  buttonDisabled: {
    opacity: 0.6,
  },
  buttonText: {
    fontSize: theme.fontSize.lg,
    fontWeight: '600',
    color: theme.colors.surface,
  },
  error: {
    backgroundColor: '#FFEBEE',
    color: theme.colors.danger,
    fontSize: theme.fontSize.sm,
    padding: theme.spacing.sm,
    borderRadius: theme.borderRadius.sm,
    textAlign: 'center',
  },
  footer: {
    textAlign: 'center',
    color: theme.colors.surface,
    opacity: 0.5,
    fontSize: theme.fontSize.xs,
    marginTop: theme.spacing.xl,
  },
});
