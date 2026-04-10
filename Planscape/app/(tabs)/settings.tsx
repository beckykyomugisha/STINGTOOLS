import { useState, useEffect, useCallback } from 'react';
import {
  View,
  Text,
  StyleSheet,
  ScrollView,
  TouchableOpacity,
  TextInput,
  Alert,
  Platform,
  ActivityIndicator,
  Switch,
} from 'react-native';
import { useRouter } from 'expo-router';
import * as Notifications from 'expo-notifications';
import * as Device from 'expo-device';
import { theme } from '@/utils/theme';
import { getBaseUrl, setBaseUrl, clearTokens } from '@/api/client';
import { getMe } from '@/api/endpoints';
import { useOfflineQueue } from '@/hooks/useOfflineQueue';
import type { UserProfile } from '@/types/api';

Notifications.setNotificationHandler({
  handleNotification: async () => ({
    shouldShowAlert: true,
    shouldPlaySound: true,
    shouldSetBadge: true,
  }),
});

export default function SettingsScreen() {
  const router = useRouter();
  const { queue, pending, syncing, lastResult, sync, clear } = useOfflineQueue();

  const [user, setUser] = useState<UserProfile | null>(null);
  const [serverUrl, setServerUrl] = useState('');
  const [editingUrl, setEditingUrl] = useState(false);
  const [urlDraft, setUrlDraft] = useState('');
  const [loadingUser, setLoadingUser] = useState(true);

  // Push notification state
  const [pushToken, setPushToken] = useState<string | null>(null);
  const [pushEnabled, setPushEnabled] = useState(false);
  const [registeringPush, setRegisteringPush] = useState(false);

  // Load user profile and server URL on mount
  useEffect(() => {
    loadSettings();
  }, []);

  const loadSettings = useCallback(async () => {
    try {
      const [url, profile] = await Promise.all([getBaseUrl(), getMe()]);
      setServerUrl(url);
      setUrlDraft(url);
      setUser(profile);
    } catch {
      // Profile load may fail if token expired — that's fine
    } finally {
      setLoadingUser(false);
    }
  }, []);

  // ── Server URL ──

  function startEditUrl() {
    setUrlDraft(serverUrl);
    setEditingUrl(true);
  }

  async function saveUrl() {
    const trimmed = urlDraft.trim().replace(/\/+$/, '');
    if (!trimmed) return;
    await setBaseUrl(trimmed);
    setServerUrl(trimmed);
    setEditingUrl(false);
  }

  // ── Push notifications ──

  async function registerForPush() {
    setRegisteringPush(true);
    try {
      if (!Device.isDevice) {
        Alert.alert('Physical device required', 'Push notifications only work on physical devices.');
        return;
      }

      const { status: existing } = await Notifications.getPermissionsAsync();
      let finalStatus = existing;

      if (existing !== 'granted') {
        const { status } = await Notifications.requestPermissionsAsync();
        finalStatus = status;
      }

      if (finalStatus !== 'granted') {
        Alert.alert('Permission denied', 'Enable notifications in your device settings.');
        setPushEnabled(false);
        return;
      }

      if (Platform.OS === 'android') {
        await Notifications.setNotificationChannelAsync('default', {
          name: 'Default',
          importance: Notifications.AndroidImportance.MAX,
        });
      }

      const tokenData = await Notifications.getExpoPushTokenAsync();
      setPushToken(tokenData.data);
      setPushEnabled(true);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to register';
      Alert.alert('Push registration failed', msg);
    } finally {
      setRegisteringPush(false);
    }
  }

  function togglePush(value: boolean) {
    if (value) {
      registerForPush();
    } else {
      setPushEnabled(false);
      setPushToken(null);
    }
  }

  // ── Logout ──

  async function handleLogout() {
    Alert.alert('Logout', 'Are you sure you want to sign out?', [
      { text: 'Cancel', style: 'cancel' },
      {
        text: 'Logout',
        style: 'destructive',
        onPress: async () => {
          await clearTokens();
          router.replace('/login');
        },
      },
    ]);
  }

  // ── Offline sync ──

  async function handleSync() {
    const result = await sync();
    if (result.failed > 0) {
      Alert.alert(
        'Sync incomplete',
        `${result.succeeded} of ${result.total} actions synced. ${result.failed} failed — will retry next time.`
      );
    } else if (result.total > 0) {
      Alert.alert('Sync complete', `${result.succeeded} actions synced successfully.`);
    } else {
      Alert.alert('Nothing to sync', 'The offline queue is empty.');
    }
  }

  function handleClearQueue() {
    if (pending === 0) return;
    Alert.alert(
      'Clear offline queue',
      `This will discard ${pending} unsent action${pending !== 1 ? 's' : ''}. This cannot be undone.`,
      [
        { text: 'Cancel', style: 'cancel' },
        { text: 'Clear', style: 'destructive', onPress: clear },
      ]
    );
  }

  return (
    <ScrollView style={styles.root} contentContainerStyle={styles.scroll}>
      {/* ── Profile ── */}
      <View style={styles.sectionCard}>
        <Text style={styles.sectionTitle}>Profile</Text>
        {loadingUser ? (
          <ActivityIndicator color={theme.colors.accent} />
        ) : user ? (
          <>
            <InfoRow label="Name" value={user.displayName} />
            <InfoRow label="Email" value={user.email} />
            <InfoRow label="Role" value={user.role} />
            <InfoRow label="Organisation" value={user.tenantName} />
          </>
        ) : (
          <Text style={styles.mutedText}>Not signed in</Text>
        )}
      </View>

      {/* ── Server ── */}
      <View style={styles.sectionCard}>
        <Text style={styles.sectionTitle}>Server</Text>
        {editingUrl ? (
          <View style={styles.urlEditRow}>
            <TextInput
              style={styles.urlInput}
              value={urlDraft}
              onChangeText={setUrlDraft}
              autoCapitalize="none"
              autoCorrect={false}
              keyboardType="url"
              placeholder="https://api.stingbim.com"
              placeholderTextColor={theme.colors.disabled}
            />
            <TouchableOpacity style={styles.smallBtn} onPress={saveUrl}>
              <Text style={styles.smallBtnText}>Save</Text>
            </TouchableOpacity>
            <TouchableOpacity
              style={[styles.smallBtn, styles.smallBtnOutline]}
              onPress={() => setEditingUrl(false)}
            >
              <Text style={[styles.smallBtnText, { color: theme.colors.textSecondary }]}>Cancel</Text>
            </TouchableOpacity>
          </View>
        ) : (
          <TouchableOpacity onPress={startEditUrl}>
            <InfoRow label="API endpoint" value={serverUrl} />
            <Text style={styles.tapHint}>Tap to change</Text>
          </TouchableOpacity>
        )}
      </View>

      {/* ── Offline Queue ── */}
      <View style={styles.sectionCard}>
        <Text style={styles.sectionTitle}>Offline Queue</Text>
        <InfoRow label="Pending actions" value={String(pending)} />
        {lastResult && (
          <InfoRow
            label="Last sync"
            value={`${lastResult.succeeded} ok, ${lastResult.failed} failed of ${lastResult.total}`}
          />
        )}
        {queue.length > 0 && (
          <View style={styles.queueList}>
            {queue.slice(0, 5).map((action) => (
              <View key={action.id} style={styles.queueItem}>
                <View
                  style={[
                    styles.queueDot,
                    { backgroundColor: action.synced ? theme.colors.success : theme.colors.accent },
                  ]}
                />
                <View style={styles.queueContent}>
                  <Text style={styles.queueType}>{formatActionType(action.type)}</Text>
                  <Text style={styles.queueTime}>{formatTime(action.createdAt)}</Text>
                </View>
              </View>
            ))}
            {queue.length > 5 && (
              <Text style={styles.mutedText}>+{queue.length - 5} more</Text>
            )}
          </View>
        )}
        <View style={styles.buttonRow}>
          <TouchableOpacity
            style={[styles.actionBtn, syncing && styles.actionBtnDisabled]}
            onPress={handleSync}
            disabled={syncing}
          >
            {syncing ? (
              <ActivityIndicator size="small" color={theme.colors.surface} />
            ) : (
              <Text style={styles.actionBtnText}>Sync Now</Text>
            )}
          </TouchableOpacity>
          <TouchableOpacity
            style={[styles.actionBtn, styles.actionBtnDanger, pending === 0 && styles.actionBtnDisabled]}
            onPress={handleClearQueue}
            disabled={pending === 0}
          >
            <Text style={styles.actionBtnText}>Clear Queue</Text>
          </TouchableOpacity>
        </View>
      </View>

      {/* ── Push Notifications ── */}
      <View style={styles.sectionCard}>
        <Text style={styles.sectionTitle}>Push Notifications</Text>
        <View style={styles.switchRow}>
          <Text style={styles.switchLabel}>Enable notifications</Text>
          {registeringPush ? (
            <ActivityIndicator size="small" color={theme.colors.accent} />
          ) : (
            <Switch
              value={pushEnabled}
              onValueChange={togglePush}
              trackColor={{ false: theme.colors.border, true: theme.colors.accent }}
              thumbColor={theme.colors.surface}
            />
          )}
        </View>
        {pushToken && (
          <View style={styles.tokenBox}>
            <Text style={styles.tokenLabel}>Device token</Text>
            <Text style={styles.tokenValue} numberOfLines={1} ellipsizeMode="middle">
              {pushToken}
            </Text>
          </View>
        )}
        <Text style={styles.mutedText}>
          Receive alerts for issue assignments, SLA breaches, and compliance changes.
        </Text>
      </View>

      {/* ── About ── */}
      <View style={styles.sectionCard}>
        <Text style={styles.sectionTitle}>About</Text>
        <InfoRow label="App" value="Planscape by StingBIM" />
        <InfoRow label="Version" value="1.0.0" />
        <InfoRow label="Platform" value={Platform.OS} />
      </View>

      {/* ── Logout ── */}
      <TouchableOpacity style={styles.logoutBtn} onPress={handleLogout}>
        <Text style={styles.logoutBtnText}>Sign Out</Text>
      </TouchableOpacity>

      <View style={{ height: theme.spacing.xl }} />
    </ScrollView>
  );
}

// ── Helper components ──

function InfoRow({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.infoRow}>
      <Text style={styles.infoLabel}>{label}</Text>
      <Text style={styles.infoValue}>{value}</Text>
    </View>
  );
}

function formatActionType(type: string): string {
  switch (type) {
    case 'CREATE_ISSUE':
      return 'Create issue';
    case 'UPDATE_ISSUE':
      return 'Update issue';
    case 'TRANSITION_CDE':
      return 'CDE transition';
    default:
      return type;
  }
}

function formatTime(iso: string): string {
  try {
    const d = new Date(iso);
    return d.toLocaleString(undefined, {
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  } catch {
    return iso;
  }
}

// ── Styles ──

const styles = StyleSheet.create({
  root: {
    flex: 1,
    backgroundColor: theme.colors.background,
  },
  scroll: {
    padding: theme.spacing.md,
    paddingBottom: theme.spacing.xl,
  },
  sectionCard: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.lg,
    padding: theme.spacing.md,
    marginBottom: theme.spacing.md,
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.06,
    shadowRadius: 4,
    elevation: 2,
  },
  sectionTitle: {
    fontSize: theme.fontSize.lg,
    fontWeight: '600',
    color: theme.colors.text,
    marginBottom: theme.spacing.sm,
  },
  infoRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingVertical: theme.spacing.xs + 2,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: theme.colors.border,
  },
  infoLabel: {
    fontSize: theme.fontSize.md,
    color: theme.colors.textSecondary,
  },
  infoValue: {
    fontSize: theme.fontSize.md,
    fontWeight: '500',
    color: theme.colors.text,
    flexShrink: 1,
    textAlign: 'right',
    marginLeft: theme.spacing.md,
  },
  mutedText: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.textSecondary,
    marginTop: theme.spacing.xs,
  },
  tapHint: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.accent,
    marginTop: 2,
  },

  // URL editing
  urlEditRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: theme.spacing.sm,
  },
  urlInput: {
    flex: 1,
    backgroundColor: theme.colors.background,
    borderRadius: theme.borderRadius.md,
    paddingHorizontal: theme.spacing.sm,
    paddingVertical: theme.spacing.sm,
    fontSize: theme.fontSize.md,
    color: theme.colors.text,
    borderWidth: 1,
    borderColor: theme.colors.border,
  },
  smallBtn: {
    backgroundColor: theme.colors.accent,
    borderRadius: theme.borderRadius.md,
    paddingHorizontal: theme.spacing.md,
    paddingVertical: theme.spacing.sm,
  },
  smallBtnOutline: {
    backgroundColor: 'transparent',
    borderWidth: 1,
    borderColor: theme.colors.border,
  },
  smallBtnText: {
    fontSize: theme.fontSize.sm,
    fontWeight: '600',
    color: theme.colors.surface,
  },

  // Offline queue
  queueList: {
    marginTop: theme.spacing.sm,
    marginBottom: theme.spacing.sm,
  },
  queueItem: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingVertical: theme.spacing.xs,
  },
  queueDot: {
    width: 8,
    height: 8,
    borderRadius: 4,
    marginRight: theme.spacing.sm,
  },
  queueContent: {
    flex: 1,
    flexDirection: 'row',
    justifyContent: 'space-between',
  },
  queueType: {
    fontSize: theme.fontSize.sm,
    fontWeight: '500',
    color: theme.colors.text,
  },
  queueTime: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
  },
  buttonRow: {
    flexDirection: 'row',
    gap: theme.spacing.sm,
    marginTop: theme.spacing.sm,
  },
  actionBtn: {
    flex: 1,
    backgroundColor: theme.colors.accent,
    borderRadius: theme.borderRadius.md,
    paddingVertical: theme.spacing.sm + 2,
    alignItems: 'center',
  },
  actionBtnDanger: {
    backgroundColor: theme.colors.danger,
  },
  actionBtnDisabled: {
    opacity: 0.5,
  },
  actionBtnText: {
    fontSize: theme.fontSize.md,
    fontWeight: '600',
    color: theme.colors.surface,
  },

  // Push notifications
  switchRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingVertical: theme.spacing.xs,
  },
  switchLabel: {
    fontSize: theme.fontSize.md,
    color: theme.colors.text,
  },
  tokenBox: {
    backgroundColor: theme.colors.background,
    borderRadius: theme.borderRadius.sm,
    padding: theme.spacing.sm,
    marginTop: theme.spacing.sm,
  },
  tokenLabel: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
    marginBottom: 2,
  },
  tokenValue: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.text,
    fontFamily: Platform.OS === 'ios' ? 'Menlo' : 'monospace',
  },

  // Logout
  logoutBtn: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.lg,
    padding: theme.spacing.md,
    alignItems: 'center',
    borderWidth: 1,
    borderColor: theme.colors.danger,
    marginBottom: theme.spacing.md,
  },
  logoutBtnText: {
    fontSize: theme.fontSize.lg,
    fontWeight: '600',
    color: theme.colors.danger,
  },
});
