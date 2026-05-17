/**
 * Clash automation rules admin screen.
 *
 * Read-only listing + enable/disable toggle for per-project clash
 * automation rules. Full CRUD (create, edit criteria, change actions)
 * is exposed via the web admin — keeping the mobile UI light avoids
 * a complex form on a small screen for a workflow that's rare on-site.
 *
 * Behaviour with zero rules: the server applies a sensible default
 * (auto-issue + push + webhook for CRITICAL clashes only). That
 * fallback is documented here in the empty state so users understand
 * what's running when their list is empty.
 */
import { useState, useEffect, useCallback } from 'react';
import {
  View,
  Text,
  ScrollView,
  Switch,
  StyleSheet,
  ActivityIndicator,
  RefreshControl,
} from 'react-native';
import { Stack } from 'expo-router';
import { theme } from '@/utils/theme';
import { apiFetch } from '@/api/client';
import { useProjectStore } from '@/stores/projectStore';

interface Rule {
  id: string;
  name: string;
  enabled: boolean;
  priority: number;
  minSeverity?: string | null;
  disciplineA?: string | null;
  disciplineB?: string | null;
  kind?: string | null;
  minOverlapVolumeMm3?: number | null;
  levelCode?: string | null;
  autoCreateIssue: boolean;
  autoAssignTo?: string | null;
  issuePriority?: string | null;
  notifyPush: boolean;
  notifyUsers?: string | null;
  fireWebhook: boolean;
}

export default function ClashRulesScreen() {
  const project = useProjectStore((s) => s.active);
  const [rules, setRules] = useState<Rule[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!project) {
      setLoading(false);
      return;
    }
    try {
      const data = await apiFetch<Rule[]>(`/api/projects/${project.id}/clash-rules`);
      setRules(data);
      setError(null);
    } catch (err: any) {
      setError(err?.message || 'Failed to load rules');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [project]);

  useEffect(() => {
    load();
  }, [load]);

  async function toggle(rule: Rule) {
    if (!project) return;
    const previous = rules;
    const updated = { ...rule, enabled: !rule.enabled };
    // Optimistic update — revert if PUT fails so the UI never lies about server state.
    setRules(rules.map((r) => (r.id === rule.id ? updated : r)));
    try {
      await apiFetch(`/api/projects/${project.id}/clash-rules/${rule.id}`, {
        method: 'PUT',
        body: JSON.stringify(updated),
      });
    } catch (err) {
      console.warn('Failed to toggle clash rule', err);
      setRules(previous);
    }
  }

  return (
    <View style={{ flex: 1, backgroundColor: theme.colors.background }}>
      <Stack.Screen
        options={{
          headerShown: true,
          title: 'Clash Rules',
          headerStyle: { backgroundColor: theme.colors.primary },
          headerTintColor: '#fff',
        }}
      />
      {loading ? (
        <View style={s.center}>
          <ActivityIndicator color={theme.colors.primary} />
        </View>
      ) : (
        <ScrollView
          refreshControl={
            <RefreshControl
              refreshing={refreshing}
              onRefresh={() => {
                setRefreshing(true);
                load();
              }}
            />
          }
        >
          {error && (
            <View style={s.error}>
              <Text style={s.errorText}>{error}</Text>
            </View>
          )}
          {rules.length === 0 ? (
            <View style={s.center}>
              <Text style={s.empty}>No automation rules configured.</Text>
              <Text style={s.emptySub}>
                Default behaviour: auto-create issue, push notification, and webhook for
                CRITICAL clashes only.
              </Text>
              <Text style={s.emptySub}>Configure custom rules via web admin.</Text>
            </View>
          ) : (
            rules.map((r) => (
              <View key={r.id} style={s.card}>
                <View style={s.cardHeader}>
                  <View style={{ flex: 1 }}>
                    <Text style={s.name}>{r.name}</Text>
                    <Text style={s.priority}>Priority {r.priority}</Text>
                  </View>
                  <Switch
                    value={r.enabled}
                    onValueChange={() => toggle(r)}
                    trackColor={{ false: theme.colors.disabled, true: theme.colors.success }}
                  />
                </View>
                <View style={s.criteria}>
                  {r.minSeverity && <Text style={s.chip}>{`>= ${r.minSeverity}`}</Text>}
                  {r.kind && <Text style={s.chip}>{r.kind}</Text>}
                  {r.disciplineA && <Text style={s.chip}>A: {r.disciplineA}</Text>}
                  {r.disciplineB && <Text style={s.chip}>B: {r.disciplineB}</Text>}
                  {r.levelCode && <Text style={s.chip}>{r.levelCode}</Text>}
                  {r.minOverlapVolumeMm3 != null && (
                    <Text style={s.chip}>{`>= ${formatVolume(r.minOverlapVolumeMm3)}`}</Text>
                  )}
                  {!r.minSeverity &&
                    !r.kind &&
                    !r.disciplineA &&
                    !r.disciplineB &&
                    !r.levelCode &&
                    r.minOverlapVolumeMm3 == null && (
                      <Text style={s.chipMuted}>matches all clashes</Text>
                    )}
                </View>
                <View style={s.actions}>
                  {r.autoCreateIssue && (
                    <Text style={s.action}>
                      Auto-issue
                      {r.issuePriority ? ` (${r.issuePriority})` : ''}
                      {r.autoAssignTo ? ` → ${r.autoAssignTo}` : ''}
                    </Text>
                  )}
                  {r.notifyPush && (
                    <Text style={s.action}>
                      Push{r.notifyUsers ? ` (${r.notifyUsers.split(',').length})` : ''}
                    </Text>
                  )}
                  {r.fireWebhook && <Text style={s.action}>Webhook</Text>}
                  {!r.autoCreateIssue && !r.notifyPush && !r.fireWebhook && (
                    <Text style={s.actionMuted}>No actions configured</Text>
                  )}
                </View>
              </View>
            ))
          )}
        </ScrollView>
      )}
    </View>
  );
}

function formatVolume(mm3: number): string {
  if (mm3 >= 1_000_000_000) return `${(mm3 / 1_000_000_000).toFixed(1)} m^3`;
  if (mm3 >= 1_000_000) return `${(mm3 / 1_000_000).toFixed(1)} dm^3`;
  return `${mm3.toFixed(0)} mm^3`;
}

const s = StyleSheet.create({
  center: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    padding: 20,
    minHeight: 240,
  },
  empty: {
    fontSize: 16,
    fontWeight: '700',
    color: theme.colors.text,
    marginBottom: 8,
  },
  emptySub: {
    fontSize: 13,
    color: theme.colors.textSecondary,
    textAlign: 'center',
    marginBottom: 4,
  },
  error: {
    backgroundColor: theme.colors.danger,
    padding: 12,
    margin: 12,
    borderRadius: 6,
  },
  errorText: { color: '#fff', fontSize: 13 },
  card: {
    backgroundColor: theme.colors.surface,
    margin: 12,
    padding: 14,
    borderRadius: 8,
    borderWidth: 1,
    borderColor: theme.colors.border,
  },
  cardHeader: { flexDirection: 'row', alignItems: 'center', marginBottom: 8 },
  name: { fontSize: 15, fontWeight: '700', color: theme.colors.text },
  priority: { fontSize: 11, color: theme.colors.textSecondary, marginTop: 2 },
  criteria: { flexDirection: 'row', flexWrap: 'wrap', gap: 6, marginBottom: 8 },
  chip: {
    fontSize: 11,
    color: theme.colors.text,
    backgroundColor: theme.colors.background,
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderRadius: 10,
    overflow: 'hidden',
  },
  chipMuted: {
    fontSize: 11,
    color: theme.colors.textSecondary,
    fontStyle: 'italic',
  },
  actions: { flexDirection: 'row', flexWrap: 'wrap', gap: 12 },
  action: { fontSize: 12, color: theme.colors.accent, fontWeight: '600' },
  actionMuted: { fontSize: 12, color: theme.colors.textSecondary, fontStyle: 'italic' },
});
