/**
 * GAP-C — Schedule / P6 Live Link screen
 * Shows the last P6 sync status, a Sync Now button, and a FlatList of
 * recent sync logs. Pull-to-refresh is supported.
 * Follows the same pattern as cost-dashboard.tsx.
 */

import React, { useState, useCallback, useEffect } from 'react';
import {
  View,
  Text,
  FlatList,
  StyleSheet,
  RefreshControl,
  ActivityIndicator,
  TouchableOpacity,
  Alert,
} from 'react-native';
import { theme } from '@/utils/theme';
import {
  getP6Status,
  getP6Logs,
  triggerP6Sync,
  listProjects,
} from '@/api/endpoints';
import type { P6StatusResponse, P6SyncLogEntry } from '@/api/endpoints';

// ── helpers ───────────────────────────────────────────────────────────────────

function fmtDate(iso: string | null): string {
  if (!iso) return 'Never';
  return new Date(iso).toLocaleString();
}

// ── Status card ───────────────────────────────────────────────────────────────

function StatusCard({ status }: { status: P6StatusResponse }) {
  const hasError = Boolean(status.errorMessage);
  return (
    <View style={[styles.card, hasError && styles.cardError]}>
      <Text style={styles.cardTitle}>Last P6 Sync</Text>
      <View style={styles.cardRow}>
        <Text style={styles.cardLabel}>Synced at</Text>
        <Text style={styles.cardValue}>{fmtDate(status.lastSyncedAt)}</Text>
      </View>
      <View style={styles.cardRow}>
        <Text style={styles.cardLabel}>Activities polled</Text>
        <Text style={styles.cardValue}>{status.activitiesPolled}</Text>
      </View>
      <View style={styles.cardRow}>
        <Text style={styles.cardLabel}>Elements updated</Text>
        <Text style={styles.cardValue}>{status.elementsUpdated}</Text>
      </View>
      {hasError && (
        <View style={styles.errorBadge}>
          <Text style={styles.errorBadgeText}>Error: {status.errorMessage}</Text>
        </View>
      )}
    </View>
  );
}

// ── Log row ───────────────────────────────────────────────────────────────────

function LogRow({ entry }: { entry: P6SyncLogEntry }) {
  return (
    <View style={styles.logRow}>
      <View style={styles.logMain}>
        <Text style={styles.logDate}>{fmtDate(entry.syncedAt)}</Text>
        {entry.error ? (
          <Text style={styles.logError}>{entry.error}</Text>
        ) : (
          <Text style={styles.logOk}>
            {entry.activitiesPolled} polled · {entry.elementsUpdated} updated
          </Text>
        )}
      </View>
      <View style={[styles.logDot, entry.error ? styles.logDotError : styles.logDotOk]} />
    </View>
  );
}

// ── Main screen ───────────────────────────────────────────────────────────────

export default function ScheduleScreen() {
  const [status,     setStatus]     = useState<P6StatusResponse | null>(null);
  const [logs,       setLogs]       = useState<P6SyncLogEntry[]>([]);
  const [loading,    setLoading]    = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [syncing,    setSyncing]    = useState(false);
  const [error,      setError]      = useState<string | null>(null);
  const [projectId,  setProjectId]  = useState<string | null>(null);

  // Resolve the active project id.
  useEffect(() => {
    listProjects()
      .then((projects) => {
        if (projects.length > 0) setProjectId(String(projects[0].id));
      })
      .catch(() => setError('Could not load project list.'));
  }, []);

  const loadData = useCallback(async (showRefresh = false) => {
    if (!projectId) return;
    if (showRefresh) setRefreshing(true);
    else setLoading(true);
    setError(null);
    try {
      const [s, l] = await Promise.all([
        getP6Status(projectId),
        getP6Logs(projectId).catch(() => [] as P6SyncLogEntry[]),
      ]);
      setStatus(s);
      setLogs(l);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load P6 status.');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [projectId]);

  useEffect(() => { loadData(); }, [loadData]);

  const handleSyncNow = useCallback(async () => {
    if (!projectId || syncing) return;
    setSyncing(true);
    try {
      await triggerP6Sync(projectId);
      // The Hangfire job is now queued — the POST returns immediately.
      // Keep the button disabled and show an informational alert.
      Alert.alert(
        'Sync Queued',
        'P6 sync has been queued. Results will appear shortly.\n\nThe status will refresh automatically in 30 seconds.',
      );
      // Auto-refresh after ~30 seconds so the user sees results without
      // needing to pull-to-refresh manually.
      setTimeout(() => {
        loadData();
      }, 30_000);
    } catch (e) {
      Alert.alert('Sync Failed', e instanceof Error ? e.message : 'Could not trigger sync.');
    } finally {
      setSyncing(false);
    }
  }, [projectId, syncing, loadData]);

  if (loading) {
    return (
      <View style={styles.centred}>
        <ActivityIndicator size="large" color={theme.colors.accent} />
        <Text style={styles.loadingText}>Loading P6 status…</Text>
      </View>
    );
  }

  if (error) {
    return (
      <View style={styles.centred}>
        <Text style={styles.errorText}>{error}</Text>
      </View>
    );
  }

  if (status && !status.isConfigured) {
    return (
      <View style={styles.centred}>
        <Text style={styles.notConfiguredText}>
          P6 live link is not configured — contact your BIM manager.
        </Text>
      </View>
    );
  }

  return (
    <FlatList
      refreshControl={
        <RefreshControl refreshing={refreshing} onRefresh={() => loadData(true)} />
      }
      ListHeaderComponent={
        <View>
          {status && <StatusCard status={status} />}
          <TouchableOpacity
            style={[styles.syncButton, syncing && styles.syncButtonDisabled]}
            onPress={handleSyncNow}
            disabled={syncing}
          >
            <Text style={styles.syncButtonText}>
              {syncing ? 'Enqueueing…' : 'Sync Now'}
            </Text>
          </TouchableOpacity>
          <Text style={styles.sectionHeader}>Recent Sync Logs</Text>
        </View>
      }
      data={logs}
      keyExtractor={(item, index) => `${item.syncedAt}-${index}`}
      renderItem={({ item }) => <LogRow entry={item} />}
      ListEmptyComponent={
        <Text style={styles.emptyText}>No sync logs yet.</Text>
      }
      contentContainerStyle={styles.listContainer}
    />
  );
}

// ── Styles ────────────────────────────────────────────────────────────────────

const styles = StyleSheet.create({
  centred: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    padding: 24,
    backgroundColor: theme.colors.background,
  },
  loadingText: {
    marginTop: 12,
    color: theme.colors.textSecondary,
    fontSize: 14,
  },
  errorText: {
    color: theme.colors.danger,
    textAlign: 'center',
    fontSize: 14,
  },
  notConfiguredText: {
    color: theme.colors.textSecondary,
    textAlign: 'center',
    fontSize: 14,
    paddingHorizontal: 24,
  },
  listContainer: {
    backgroundColor: theme.colors.background,
    paddingBottom: 24,
  },
  card: {
    margin: 12,
    backgroundColor: theme.colors.surface,
    borderRadius: 8,
    padding: 14,
    borderWidth: 1,
    borderColor: theme.colors.border,
  },
  cardError: {
    borderColor: theme.colors.danger,
  },
  cardTitle: {
    fontSize: 13,
    fontWeight: '700',
    color: theme.colors.text,
    marginBottom: 10,
    textTransform: 'uppercase',
    letterSpacing: 0.5,
  },
  cardRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    marginBottom: 6,
  },
  cardLabel: {
    fontSize: 13,
    color: theme.colors.textSecondary,
  },
  cardValue: {
    fontSize: 13,
    color: theme.colors.text,
    fontWeight: '600',
  },
  errorBadge: {
    marginTop: 8,
    backgroundColor: '#fee2e2',
    borderRadius: 4,
    padding: 6,
  },
  errorBadgeText: {
    fontSize: 12,
    color: theme.colors.danger,
  },
  syncButton: {
    marginHorizontal: 12,
    marginBottom: 16,
    backgroundColor: theme.colors.accent,
    borderRadius: 8,
    paddingVertical: 12,
    alignItems: 'center',
  },
  syncButtonDisabled: {
    opacity: 0.6,
  },
  syncButtonText: {
    fontSize: 15,
    fontWeight: '700',
    color: '#ffffff',
  },
  sectionHeader: {
    fontSize: 11,
    fontWeight: '700',
    color: theme.colors.textSecondary,
    textTransform: 'uppercase',
    letterSpacing: 0.5,
    paddingHorizontal: 12,
    paddingBottom: 6,
    borderBottomWidth: 1,
    borderBottomColor: theme.colors.border,
  },
  logRow: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingHorizontal: 12,
    paddingVertical: 10,
    borderBottomWidth: 1,
    borderBottomColor: theme.colors.border,
    backgroundColor: theme.colors.background,
  },
  logMain: {
    flex: 1,
  },
  logDate: {
    fontSize: 12,
    color: theme.colors.textSecondary,
    marginBottom: 2,
  },
  logOk: {
    fontSize: 13,
    color: theme.colors.text,
  },
  logError: {
    fontSize: 13,
    color: theme.colors.danger,
  },
  logDot: {
    width: 8,
    height: 8,
    borderRadius: 4,
    marginLeft: 10,
  },
  logDotOk: {
    backgroundColor: '#16a34a',
  },
  logDotError: {
    backgroundColor: theme.colors.danger,
  },
  emptyText: {
    textAlign: 'center',
    color: theme.colors.textSecondary,
    fontSize: 13,
    padding: 24,
  },
});
