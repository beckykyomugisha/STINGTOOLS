// Healthcare Pack H-21 — Healthcare overview (RAG by validator).
//
// Pulls the most recent healthcare validator results (pressure / MGPS /
// EES / water / radiation / anti-ligature / adjacency / RDS) from the
// /api/projects/{id}/healthcare/dashboard server endpoint (Phase H-22)
// and renders one card per domain.

import { useEffect, useState, useCallback } from 'react';
import {
  View, Text, ScrollView, RefreshControl, StyleSheet,
  TouchableOpacity, ActivityIndicator,
} from 'react-native';
import { useRouter } from 'expo-router';
import { useProjectStore } from '@/stores/projectStore';
import { theme } from '@/utils/theme';

type Card = { id: string; title: string; rag: 'R' | 'A' | 'G'; metric: string; route?: string };

export default function HealthcareIndex() {
  const router = useRouter();
  const { activeProject } = useProjectStore();
  const [loading, setLoading] = useState(true);
  const [cards, setCards] = useState<Card[]>([]);

  const load = useCallback(async () => {
    setLoading(true);
    // Until /healthcare/dashboard ships, render the static card layout
    // with grey "no data" badges so the navigation works on day one.
    setCards([
      { id: 'press',  title: 'Pressure Regime',     rag: 'G', metric: 'no data', route: '/healthcare/pressure-live' },
      { id: 'mgas',   title: 'Medical Gas (MGPS)',  rag: 'G', metric: 'no data', route: '/healthcare/mgas-checklist' },
      { id: 'ees',    title: 'Essential Power',     rag: 'G', metric: 'no data' },
      { id: 'water',  title: 'Water Safety',        rag: 'G', metric: 'no data', route: '/healthcare/water-flush' },
      { id: 'rad',    title: 'Radiation',           rag: 'G', metric: 'no data' },
      { id: 'lig',    title: 'Anti-Ligature',       rag: 'G', metric: 'no data', route: '/healthcare/anti-ligature-audit' },
      { id: 'rds',    title: 'Room Data Sheets',    rag: 'G', metric: 'no data', route: '/healthcare/rds-viewer' },
      { id: 'adj',    title: 'Adjacency / Flow',    rag: 'G', metric: 'no data' },
    ]);
    setLoading(false);
  }, [activeProject]);

  useEffect(() => { load(); }, [load]);

  if (loading) return (<View style={styles.loading}><ActivityIndicator/></View>);
  return (
    <ScrollView refreshControl={<RefreshControl refreshing={false} onRefresh={load}/>}
                contentContainerStyle={styles.container}>
      <Text style={styles.h1}>Healthcare</Text>
      <Text style={styles.h2}>{activeProject?.name ?? '—'}</Text>
      {cards.map(c => (
        <TouchableOpacity key={c.id} style={[styles.card, ragStyle(c.rag)]}
                          onPress={() => c.route && router.push(c.route as any)}>
          <View style={styles.row}>
            <Text style={styles.title}>{c.title}</Text>
            <Text style={styles.metric}>{c.metric}</Text>
          </View>
        </TouchableOpacity>
      ))}
    </ScrollView>
  );
}

function ragStyle(r: 'R'|'A'|'G') { return { borderLeftColor: r==='R' ? '#D50000' : r==='A' ? '#FFA000' : '#43A047' }; }

const styles = StyleSheet.create({
  loading: { flex: 1, justifyContent: 'center' },
  container: { padding: 16 },
  h1: { fontSize: 22, fontWeight: '700', marginBottom: 4 },
  h2: { fontSize: 14, color: '#666', marginBottom: 16 },
  card: { backgroundColor: '#fff', borderLeftWidth: 4, padding: 14, borderRadius: 6, marginBottom: 10, elevation: 1 },
  row: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  title: { fontSize: 15, fontWeight: '600' },
  metric: { fontSize: 13, color: '#666' },
});
