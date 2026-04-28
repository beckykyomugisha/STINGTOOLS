// Phase 142 — Site Diary detail.
//
// Shows the full report. Manager can acknowledge a SUBMITTED diary.

import { useEffect, useState } from 'react';
import {
  View,
  Text,
  ScrollView,
  TouchableOpacity,
  StyleSheet,
  ActivityIndicator,
  Alert,
} from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { theme } from '@/utils/theme';
import { getSiteDiary, acknowledgeSiteDiary, submitSiteDiary, type SiteDiaryDetail } from '@/api/endpoints';
import { useProjectStore } from '@/stores/projectStore';

export default function DiaryDetailScreen() {
  const router = useRouter();
  const { id } = useLocalSearchParams<{ id: string }>();
  const projectId = useProjectStore((s) => s.active?.id);
  const [diary, setDiary] = useState<SiteDiaryDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [acting, setActing] = useState(false);

  useEffect(() => { void load(); }, [id, projectId]);

  async function load() {
    if (!projectId || !id) return;
    try {
      setLoading(true);
      const d = await getSiteDiary(projectId, id);
      setDiary(d);
    } catch (err: unknown) {
      Alert.alert('Failed to load', err instanceof Error ? err.message : String(err));
    } finally {
      setLoading(false);
    }
  }

  async function onSubmit() {
    if (!projectId || !id) return;
    setActing(true);
    try {
      await submitSiteDiary(projectId, id);
      await load();
    } catch (err: unknown) {
      Alert.alert('Submit failed', err instanceof Error ? err.message : String(err));
    } finally { setActing(false); }
  }

  async function onAcknowledge() {
    if (!projectId || !id) return;
    setActing(true);
    try {
      await acknowledgeSiteDiary(projectId, id);
      await load();
    } catch (err: unknown) {
      Alert.alert('Acknowledge failed', err instanceof Error ? err.message : String(err));
    } finally { setActing(false); }
  }

  if (loading) {
    return <View style={styles.loading}><ActivityIndicator size="large" color={theme.colors.accent} /></View>;
  }
  if (!diary) return <View style={styles.loading}><Text>Diary not found.</Text></View>;

  return (
    <ScrollView style={styles.root} contentContainerStyle={styles.scroll}>
      <View style={styles.header}>
        <Text style={styles.date}>{formatDate(diary.diaryDate)}</Text>
        <View style={[styles.statusPill, { backgroundColor: statusColor(diary.status) }]}>
          <Text style={styles.statusText}>{diary.status}</Text>
        </View>
      </View>
      <Text style={styles.author}>{diary.authorName}{diary.authorRole ? ` · ${diary.authorRole}` : ''}</Text>

      <Section label="Conditions">
        <KV k="Weather" v={diary.weather ?? '—'} />
        <KV k="Temperature" v={diary.temperatureCelsius != null ? `${diary.temperatureCelsius}°C` : '—'} />
        <KV k="Wind" v={diary.windSpeedKph != null ? `${diary.windSpeedKph} km/h` : '—'} />
        <KV k="Rainfall" v={diary.rainfallMm != null ? `${diary.rainfallMm} mm` : '—'} />
      </Section>

      <Section label="Resources">
        <KV k="Manpower" v={String(diary.manpowerCount)} />
      </Section>

      {diary.narrative ? <Section label="Narrative"><Text style={styles.body}>{diary.narrative}</Text></Section> : null}
      {diary.safetyIncidents ? <Section label="Safety"><Text style={styles.body}>{diary.safetyIncidents}</Text></Section> : null}
      {diary.delaysAndDisruption ? <Section label="Delays & disruption"><Text style={styles.body}>{diary.delaysAndDisruption}</Text></Section> : null}
      {diary.visitorsLog ? <Section label="Visitors"><Text style={styles.body}>{diary.visitorsLog}</Text></Section> : null}

      {diary.attachments.length > 0 && (
        <Section label={`Attachments (${diary.attachments.length})`}>
          {diary.attachments.map((a) => (
            <Text key={a.id} style={styles.attachment} numberOfLines={1}>
              📎 {a.fileName ?? a.documentId}{a.caption ? ` — ${a.caption}` : ''}
            </Text>
          ))}
        </Section>
      )}

      <View style={styles.timestampBlock}>
        <Text style={styles.timestamp}>Created {formatDate(diary.createdAt)}</Text>
        {diary.submittedAt ? <Text style={styles.timestamp}>Submitted {formatDate(diary.submittedAt)}</Text> : null}
        {diary.acknowledgedAt ? (
          <Text style={styles.timestamp}>Acknowledged {formatDate(diary.acknowledgedAt)} by {diary.acknowledgedBy}</Text>
        ) : null}
      </View>

      {diary.status === 'DRAFT' && (
        <TouchableOpacity
          style={[styles.button, acting && styles.buttonDisabled]}
          onPress={onSubmit}
          disabled={acting}
          accessibilityLabel="Submit diary"
        >
          {acting ? <ActivityIndicator color="#fff" /> : <Text style={styles.buttonText}>Submit</Text>}
        </TouchableOpacity>
      )}
      {diary.status === 'SUBMITTED' && (
        <TouchableOpacity
          style={[styles.button, styles.buttonAck, acting && styles.buttonDisabled]}
          onPress={onAcknowledge}
          disabled={acting}
          accessibilityLabel="Acknowledge diary"
        >
          {acting ? <ActivityIndicator color="#fff" /> : <Text style={styles.buttonText}>Acknowledge</Text>}
        </TouchableOpacity>
      )}
    </ScrollView>
  );
}

function Section({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <View style={styles.section}>
      <Text style={styles.sectionLabel}>{label}</Text>
      {children}
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

function statusColor(s: SiteDiaryDetail['status']): string {
  switch (s) {
    case 'DRAFT': return theme.colors.disabled;
    case 'SUBMITTED': return theme.colors.accent;
    case 'ACKNOWLEDGED': return theme.colors.success;
    case 'ARCHIVED': return theme.colors.textSecondary;
    default: return theme.colors.disabled;
  }
}

function formatDate(iso: string): string {
  try { return new Date(iso).toLocaleString(); } catch { return iso; }
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: theme.colors.background },
  scroll: { padding: theme.spacing.md, paddingBottom: 80 },
  loading: { flex: 1, justifyContent: 'center', alignItems: 'center' },
  header: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  date: { fontSize: theme.fontSize.lg, fontWeight: '700', color: theme.colors.text },
  statusPill: { paddingHorizontal: 8, paddingVertical: 2, borderRadius: 4 },
  statusText: { color: '#fff', fontSize: theme.fontSize.xs, fontWeight: '700' },
  author: { fontSize: theme.fontSize.sm, color: theme.colors.textSecondary, marginTop: 4, marginBottom: theme.spacing.lg },
  section: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.md,
    marginBottom: theme.spacing.md,
  },
  sectionLabel: {
    fontSize: theme.fontSize.sm, fontWeight: '700',
    color: theme.colors.text, marginBottom: theme.spacing.sm,
    textTransform: 'uppercase', letterSpacing: 0.5,
  },
  kvRow: { flexDirection: 'row', paddingVertical: 4 },
  kvKey: { fontSize: theme.fontSize.sm, color: theme.colors.textSecondary, width: 100 },
  kvValue: { fontSize: theme.fontSize.sm, color: theme.colors.text, flex: 1 },
  body: { fontSize: theme.fontSize.sm, color: theme.colors.text, lineHeight: 20 },
  attachment: { fontSize: theme.fontSize.sm, color: theme.colors.text, paddingVertical: 4 },
  timestampBlock: { marginTop: theme.spacing.sm, marginBottom: theme.spacing.lg },
  timestamp: { fontSize: theme.fontSize.xs, color: theme.colors.disabled, marginBottom: 2 },
  button: {
    backgroundColor: theme.colors.accent,
    paddingVertical: theme.spacing.md,
    borderRadius: theme.borderRadius.md,
    alignItems: 'center',
  },
  buttonAck: { backgroundColor: theme.colors.success },
  buttonText: { color: '#fff', fontWeight: '700', fontSize: theme.fontSize.md },
  buttonDisabled: { opacity: 0.5 },
});
