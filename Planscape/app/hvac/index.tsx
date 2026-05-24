// Phase 188 (Tier 3) — HVAC dashboard for on-site engineers.
//
// Pulls the latest snapshot per kind from /api/projects/{id}/hvac/dashboard
// (sizing / balance / drift / loads / carbon) and renders one card per
// kind with the same RAG palette as the Healthcare tab. Pull-to-refresh.
//
// Five cards:
//   sizing  — equipment + duct/pipe auto-size totals
//   balance — Hardy-Cross run summary
//   drift   — count of stale-sized ducts
//   loads   — heating + cooling totals (kW)
//   carbon  — A1-A3 embodied + B7 refrigerant GWP (kgCO2e)

import { useEffect, useState, useCallback } from 'react';
import {
  View, Text, ScrollView, RefreshControl, StyleSheet,
  TouchableOpacity, ActivityIndicator,
} from 'react-native';
import { useRouter } from 'expo-router';
import { useProjectStore } from '@/stores/projectStore';
import { getHvacDashboard, HvacCard, HvacDashboard } from '@/api/endpoints';

type CardSpec = {
  id: 'sizing' | 'balance' | 'drift' | 'loads' | 'carbon';
  title: string;
  icon: string;
  metric: (c: HvacCard) => string;
};

const CARDS: CardSpec[] = [
  { id: 'sizing',  title: 'Sizing',         icon: '⚖',
    metric: c => c.inspected > 0 ? `${c.inspected} equipment · ${c.totalKw.toFixed(0)} kW` : 'no data' },
  { id: 'balance', title: 'Balance',        icon: '◑',
    metric: c => c.inspected > 0 ? `${c.pass}/${c.inspected} branches in spec` : 'no data' },
  { id: 'drift',   title: 'Drift',          icon: '⚠',
    metric: c => c.warn > 0 ? `${c.warn} ducts stale of ${c.inspected}` : 'no drift' },
  { id: 'loads',   title: 'Loads',          icon: '♨',
    metric: c => c.totalKw > 0 ? `${c.totalKw.toFixed(0)} kW total · ${c.inspected} spaces` : 'no data' },
  { id: 'carbon',  title: 'Plant Carbon',   icon: '⛁',
    metric: c => c.totalKw > 0 ? `${(c.totalKw / 1000).toFixed(2)} tCO₂e` : 'no data' },
];

function ragColor(r: string): string {
  if (r === 'R') return '#D32F2F';
  if (r === 'A') return '#EF6C00';
  if (r === 'G') return '#2E7D32';
  return '#9E9E9E';
}

function ageString(iso: string | null): string {
  if (!iso) return 'never';
  const d = new Date(iso);
  const diffMs = Date.now() - d.getTime();
  const mins = Math.floor(diffMs / 60000);
  if (mins < 1)   return 'just now';
  if (mins < 60)  return `${mins}m ago`;
  const hrs = Math.floor(mins / 60);
  if (hrs < 24)   return `${hrs}h ago`;
  const days = Math.floor(hrs / 24);
  return `${days}d ago`;
}

export default function HvacIndex() {
  const router = useRouter();
  const activeProject = useProjectStore((s) => s.active);
  const [loading,  setLoading]  = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [dash,     setDash]     = useState<HvacDashboard | null>(null);
  const [error,    setError]    = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!activeProject?.id) { setLoading(false); return; }
    setError(null);
    try {
      const d = await getHvacDashboard(activeProject.id);
      setDash(d);
    }
    catch (e: any) {
      // Empty dashboard is normal — desktop plugin may not have pushed yet.
      setDash(null);
      setError(e?.message ?? String(e));
    }
    finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [activeProject?.id]);

  useEffect(() => { load(); }, [load]);

  const onRefresh = useCallback(() => {
    setRefreshing(true);
    load();
  }, [load]);

  if (loading) return (
    <View style={styles.center}>
      <ActivityIndicator size="large" />
      <Text style={styles.muted}>Loading HVAC dashboard…</Text>
    </View>
  );

  return (
    <ScrollView
      style={styles.container}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} />}
    >
      <Text style={styles.header}>HVAC — {activeProject?.name ?? '(no project)'}</Text>

      {!dash && (
        <View style={styles.emptyState}>
          <Text style={styles.emptyTitle}>No snapshots yet</Text>
          <Text style={styles.emptyBody}>
            Push from the desktop STING HVAC panel → RPRT tab → "Push to API" to populate this dashboard.
          </Text>
          {error && <Text style={styles.errorText}>{error}</Text>}
        </View>
      )}

      {dash && CARDS.map((spec) => {
        const card = dash[spec.id];
        const colour = ragColor(card.rag);
        return (
          <TouchableOpacity
            key={spec.id}
            style={[styles.card, { borderLeftColor: colour }]}
            onPress={() => router.push({
              pathname: '/hvac/snapshots',
              params: { kind: spec.id, title: spec.title },
            })}
          >
            <View style={styles.cardRow}>
              <Text style={styles.cardIcon}>{spec.icon}</Text>
              <View style={styles.cardBody}>
                <Text style={styles.cardTitle}>{spec.title}</Text>
                <Text style={styles.cardMetric}>{spec.metric(card)}</Text>
                <Text style={styles.cardAge}>updated {ageString(card.latest)}</Text>
              </View>
              <View style={[styles.ragBadge, { backgroundColor: colour }]}>
                <Text style={styles.ragText}>{card.rag}</Text>
              </View>
            </View>
          </TouchableOpacity>
        );
      })}

      {dash && dash.last30d?.length > 0 && (
        <View style={styles.last30Section}>
          <Text style={styles.sectionHeader}>Last 30 days</Text>
          {dash.last30d.map((row) => (
            <View key={row.kind} style={styles.last30Row}>
              <Text style={styles.last30Kind}>{row.kind}</Text>
              <Text style={styles.last30Stat}>
                {row.total} runs · pass {row.pass} · warn {row.warn} · fail {row.fail}
              </Text>
            </View>
          ))}
        </View>
      )}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  container:    { flex: 1, backgroundColor: '#FAFAFA', padding: 12 },
  center:       { flex: 1, justifyContent: 'center', alignItems: 'center', padding: 32 },
  muted:        { marginTop: 12, color: '#757575', fontSize: 12 },
  header:       { fontSize: 16, fontWeight: '600', color: '#37474F', marginBottom: 12 },
  emptyState:   { backgroundColor: '#FFF', padding: 16, borderRadius: 8, marginTop: 8 },
  emptyTitle:   { fontSize: 14, fontWeight: '600', color: '#37474F', marginBottom: 4 },
  emptyBody:    { fontSize: 12, color: '#607D8B', lineHeight: 18 },
  errorText:    { marginTop: 8, fontSize: 11, color: '#D32F2F' },

  card:         { backgroundColor: '#FFF', padding: 12, borderRadius: 8,
                  marginBottom: 8, borderLeftWidth: 4 },
  cardRow:      { flexDirection: 'row', alignItems: 'center' },
  cardIcon:     { fontSize: 28, width: 40, textAlign: 'center', color: '#37474F' },
  cardBody:     { flex: 1, paddingHorizontal: 8 },
  cardTitle:    { fontSize: 14, fontWeight: '600', color: '#263238' },
  cardMetric:   { fontSize: 12, color: '#455A64', marginTop: 2 },
  cardAge:      { fontSize: 10, color: '#90A4AE', marginTop: 2 },
  ragBadge:     { width: 32, height: 32, borderRadius: 16,
                  justifyContent: 'center', alignItems: 'center' },
  ragText:      { color: '#FFF', fontWeight: 'bold', fontSize: 13 },

  last30Section:{ marginTop: 16 },
  sectionHeader:{ fontSize: 12, fontWeight: '600', color: '#607D8B',
                  textTransform: 'uppercase', marginBottom: 6 },
  last30Row:    { flexDirection: 'row', justifyContent: 'space-between',
                  paddingVertical: 4, paddingHorizontal: 8,
                  backgroundColor: '#FFF', borderRadius: 4, marginBottom: 2 },
  last30Kind:   { fontSize: 11, fontWeight: '600', color: '#37474F',
                  textTransform: 'capitalize', width: 70 },
  last30Stat:   { fontSize: 10, color: '#607D8B', flex: 1 },
});
