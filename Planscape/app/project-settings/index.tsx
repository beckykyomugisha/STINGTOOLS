// Phase 144 — Project admin settings.
//
// First screen in the project-admin surface. Today it carries one toggle
// (Iso 19650 naming enforcement) but the page is laid out so additional
// admin booleans drop in next to it without restructuring.
//
// Permission gating is enforced server-side (PUT requires Iso19650Role
// K or C). The screen still lets non-admins read the current values for
// transparency.

import { useCallback, useEffect, useState } from 'react';
import {
  View,
  Text,
  ScrollView,
  StyleSheet,
  ActivityIndicator,
  Switch,
  Alert,
} from 'react-native';
import { theme } from '@/utils/theme';
import {
  getProjectSettings,
  updateProjectSettings,
  type ProjectSettings,
} from '@/api/endpoints';
import { useProjectStore } from '@/stores/projectStore';

export default function ProjectSettingsScreen() {
  const projectId = useProjectStore((s) => s.active?.id);
  const [settings, setSettings] = useState<ProjectSettings | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!projectId) return;
    try {
      setError(null);
      const s = await getProjectSettings(projectId);
      setSettings(s);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to load settings');
    } finally {
      setLoading(false);
    }
  }, [projectId]);

  useEffect(() => { load(); }, [load]);

  async function setAdminFlag(key: keyof ProjectSettings['admin'], value: boolean) {
    if (!projectId || !settings) return;
    // Optimistic update — flip the switch immediately, roll back on failure.
    const previous = settings.admin[key];
    setSettings({ ...settings, admin: { ...settings.admin, [key]: value } });
    setSaving(true);
    try {
      await updateProjectSettings(projectId, { [key]: value });
    } catch (err: unknown) {
      setSettings((s) => s ? { ...s, admin: { ...s.admin, [key]: previous } } : s);
      const msg = err instanceof Error ? err.message : 'Update failed';
      // 403 is expected for non-admin members — be specific so they understand.
      if (msg.includes('HTTP 403')) {
        Alert.alert(
          'Permission denied',
          'Only BIM Managers (role K) and Coordinators (role C) can change admin settings on this project.',
        );
      } else {
        Alert.alert('Update failed', msg);
      }
    } finally {
      setSaving(false);
    }
  }

  if (!projectId) {
    return (
      <View style={styles.empty}>
        <Text style={styles.emptyText}>Select a project on the dashboard first.</Text>
      </View>
    );
  }
  if (loading) {
    return <View style={styles.loading}><ActivityIndicator size="large" color={theme.colors.accent} /></View>;
  }
  if (!settings) {
    return (
      <View style={styles.empty}>
        <Text style={styles.emptyText}>{error ?? 'Settings unavailable.'}</Text>
      </View>
    );
  }

  return (
    <ScrollView style={styles.root} contentContainerStyle={styles.scroll}>
      {error ? <Text style={styles.error}>{error}</Text> : null}

      {/* ── ISO 19650 information governance ── */}
      <Section
        label="ISO 19650 Compliance"
        description="Information governance toggles for this project. Changes take effect on the next document upload."
      >
        <ToggleRow
          title="Enforce ISO 19650 file naming"
          subtitle="Reject document uploads whose file name doesn't match Project-Originator-Volume-Level-Type-Role-Class-Number. Photos and issue attachments are exempt."
          value={settings.admin.enforceIso19650Naming}
          disabled={saving}
          onChange={(v) => setAdminFlag('enforceIso19650Naming', v)}
        />
      </Section>

      {/* ── Read-only: SLAs ── */}
      <Section label="SLA hours (read-only)">
        <KV k="Critical" v={`${settings.slaHours.critical} h`} />
        <KV k="High" v={`${settings.slaHours.high} h`} />
        <KV k="Medium" v={`${settings.slaHours.medium} h`} />
        <KV k="Low" v={`${settings.slaHours.low} h`} />
      </Section>

      {/* ── Read-only: limits ── */}
      <Section label="Upload limits">
        <KV k="Max attachment" v={`${settings.limits.maxAttachmentMB} MB`} />
        <KV k="Max document" v={`${settings.limits.maxDocumentMB} MB`} />
        <KV k="Max photos / issue" v={String(settings.limits.maxPhotosPerIssue)} />
      </Section>

      {/* ── Read-only: geofence status ── */}
      <Section label="Geofence">
        <KV k="Boundary set" v={settings.geofence.hasBoundary ? 'Yes' : 'No'} />
        <KV k="Required by tenant" v={settings.geofence.requireBoundary ? 'Yes' : 'No'} />
      </Section>

      <Text style={styles.footer}>
        Permission to edit admin settings is gated by your project role (K or C).
      </Text>
    </ScrollView>
  );
}

function Section({ label, description, children }: {
  label: string;
  description?: string;
  children: React.ReactNode;
}) {
  return (
    <View style={styles.section}>
      <Text style={styles.sectionLabel}>{label}</Text>
      {description ? <Text style={styles.sectionDesc}>{description}</Text> : null}
      {children}
    </View>
  );
}

function ToggleRow({ title, subtitle, value, disabled, onChange }: {
  title: string;
  subtitle?: string;
  value: boolean;
  disabled?: boolean;
  onChange: (v: boolean) => void;
}) {
  return (
    <View style={styles.toggleRow}>
      <View style={{ flex: 1, paddingRight: theme.spacing.sm }}>
        <Text style={styles.toggleTitle}>{title}</Text>
        {subtitle ? <Text style={styles.toggleSub}>{subtitle}</Text> : null}
      </View>
      <Switch value={value} disabled={disabled} onValueChange={onChange} />
    </View>
  );
}

function KV({ k, v }: { k: string; v: string }) {
  return (
    <View style={styles.kvRow}>
      <Text style={styles.kvKey}>{k}</Text>
      <Text style={styles.kvValue}>{v}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: theme.colors.background },
  scroll: { padding: theme.spacing.md, paddingBottom: 80 },
  loading: { flex: 1, justifyContent: 'center', alignItems: 'center' },
  empty: { flex: 1, justifyContent: 'center', alignItems: 'center', padding: theme.spacing.lg },
  emptyText: { color: theme.colors.textSecondary, fontSize: theme.fontSize.md },
  error: {
    backgroundColor: '#FFEBEE', color: theme.colors.danger,
    padding: theme.spacing.sm, borderRadius: theme.borderRadius.sm,
    marginBottom: theme.spacing.md,
  },
  section: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.md,
    marginBottom: theme.spacing.md,
  },
  sectionLabel: {
    fontSize: theme.fontSize.sm, fontWeight: '700',
    color: theme.colors.text,
    textTransform: 'uppercase', letterSpacing: 0.5,
    marginBottom: theme.spacing.xs,
  },
  sectionDesc: {
    fontSize: theme.fontSize.xs, color: theme.colors.textSecondary,
    marginBottom: theme.spacing.sm,
  },
  toggleRow: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingVertical: theme.spacing.sm,
  },
  toggleTitle: { fontSize: theme.fontSize.sm, color: theme.colors.text, fontWeight: '600' },
  toggleSub: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary, marginTop: 2 },
  kvRow: { flexDirection: 'row', paddingVertical: 4 },
  kvKey: { fontSize: theme.fontSize.sm, color: theme.colors.textSecondary, width: 160 },
  kvValue: { fontSize: theme.fontSize.sm, color: theme.colors.text, flex: 1 },
  footer: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.disabled,
    textAlign: 'center',
    marginTop: theme.spacing.md,
  },
});
