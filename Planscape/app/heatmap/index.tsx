// Phase 144 — Cross-discipline tag completeness heatmap.
//
// Grid view: rows = disciplines, columns = the 10 ISO 19650 tag tokens
// (DISC / LOC / ZONE / LVL / SYS / FUNC / PROD / SEQ + STATUS + REV).
// Each cell colours red/amber/green by completeness percent so the BIM
// Coordinator sees instantly which discipline is letting which token
// slip. Long-press a row to drill down into the discipline (future —
// currently navigates to issue list filtered by discipline).

import { useCallback, useEffect, useState } from 'react';
import {
  View,
  Text,
  ScrollView,
  RefreshControl,
  StyleSheet,
  ActivityIndicator,
} from 'react-native';
import { theme } from '@/utils/theme';
import { getTagHeatmap, type TagHeatmap, type TagToken } from '@/api/endpoints';
import { useProjectStore } from '@/stores/projectStore';

export default function HeatmapScreen() {
  const projectId = useProjectStore((s) => s.active?.id);
  const [data, setData] = useState<TagHeatmap | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!projectId) return;
    try {
      setError(null);
      setData(await getTagHeatmap(projectId));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to load heatmap');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [projectId]);

  useEffect(() => { load(); }, [load]);

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
  if (!data || data.totalElements === 0) {
    return (
      <View style={styles.empty}>
        <Text style={styles.emptyTitle}>No tagged elements yet</Text>
        <Text style={styles.emptyHint}>Run a tag sync from the Revit plugin.</Text>
      </View>
    );
  }

  // Per-token average across all disciplines — handy as a top-row summary.
  const tokenAverages = data.tokens.map((t) => {
    const sum = data.disciplines.reduce((acc, d) => acc + d.cells[t], 0);
    return data.disciplines.length === 0 ? 0 : Math.round(sum / data.disciplines.length);
  });

  const cellW = 56; // each token column
  const headerW = 110; // discipline column

  return (
    <ScrollView
      style={styles.root}
      refreshControl={
        <RefreshControl refreshing={refreshing} onRefresh={() => { setRefreshing(true); load(); }} />
      }
    >
      <View style={styles.summary}>
        <Text style={styles.summaryTitle}>
          {data.totalElements.toLocaleString()} elements · {data.disciplines.length} disciplines
        </Text>
        <Text style={styles.summaryHint}>Cells show % of elements with that token populated. Tap or scroll horizontally to inspect.</Text>
      </View>

      {error ? <Text style={styles.error}>{error}</Text> : null}

      <ScrollView horizontal showsHorizontalScrollIndicator>
        <View>
          {/* Column header row */}
          <View style={styles.row}>
            <View style={[styles.headerCell, { width: headerW }]}>
              <Text style={styles.headerText}>Discipline</Text>
              <Text style={styles.headerSub}>n</Text>
            </View>
            {data.tokens.map((t) => (
              <View key={t} style={[styles.headerCell, { width: cellW }]}>
                <Text style={styles.headerText}>{t}</Text>
                <Text style={styles.headerSub}>{tokenAverages[data.tokens.indexOf(t)]}%</Text>
              </View>
            ))}
          </View>

          {/* Data rows */}
          {data.disciplines.map((d) => (
            <View key={d.discipline} style={styles.row}>
              <View style={[styles.discCell, { width: headerW }]}>
                <Text style={styles.discText}>{d.discipline}</Text>
                <Text style={styles.discSub}>{d.elementCount.toLocaleString()}</Text>
              </View>
              {data.tokens.map((t) => {
                const pct = d.cells[t] ?? 0;
                return (
                  <View
                    key={t}
                    style={[
                      styles.dataCell,
                      { width: cellW, backgroundColor: cellColor(pct) },
                    ]}
                  >
                    <Text style={[styles.dataText, { color: cellTextColor(pct) }]}>{pct}%</Text>
                  </View>
                );
              })}
            </View>
          ))}
        </View>
      </ScrollView>

      <View style={styles.legend}>
        <LegendChip color={cellColor(95)} label="≥90%" />
        <LegendChip color={cellColor(75)} label="70–89%" />
        <LegendChip color={cellColor(60)} label="50–69%" />
        <LegendChip color={cellColor(30)} label="<50%" />
      </View>
    </ScrollView>
  );
}

function LegendChip({ color, label }: { color: string; label: string }) {
  return (
    <View style={styles.legendItem}>
      <View style={[styles.legendSwatch, { backgroundColor: color }]} />
      <Text style={styles.legendText}>{label}</Text>
    </View>
  );
}

// 4-tier RAG palette: green / lime / amber / red. Thresholds tuned to the
// typical BIM Manager mental model — anything <50% is acute neglect, 70%+
// is acceptable on day-1 of a new model, 90%+ is "ready for IE".
function cellColor(pct: number): string {
  if (pct >= 90) return theme.colors.success;
  if (pct >= 70) return '#9CCC65';   // lime
  if (pct >= 50) return theme.colors.warning;
  return theme.colors.danger;
}
function cellTextColor(pct: number): string {
  // Light-on-dark for the saturated reds/greens; black on the amber band.
  return pct >= 50 && pct < 70 ? '#222' : '#fff';
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: theme.colors.background },
  loading: { flex: 1, justifyContent: 'center', alignItems: 'center' },
  empty: { flex: 1, justifyContent: 'center', alignItems: 'center', padding: theme.spacing.lg },
  emptyText: { color: theme.colors.textSecondary, fontSize: theme.fontSize.md },
  emptyTitle: { fontSize: theme.fontSize.md, fontWeight: '600', color: theme.colors.text },
  emptyHint: { fontSize: theme.fontSize.sm, color: theme.colors.textSecondary, marginTop: 4 },
  summary: {
    backgroundColor: theme.colors.surface,
    padding: theme.spacing.md,
    margin: theme.spacing.md,
    borderRadius: theme.borderRadius.md,
  },
  summaryTitle: { fontSize: theme.fontSize.md, fontWeight: '700', color: theme.colors.text },
  summaryHint: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary, marginTop: 4 },
  error: {
    backgroundColor: '#FFEBEE', color: theme.colors.danger,
    padding: theme.spacing.sm, borderRadius: theme.borderRadius.sm,
    marginHorizontal: theme.spacing.md,
    marginBottom: theme.spacing.md,
  },
  row: { flexDirection: 'row' },
  headerCell: {
    paddingVertical: theme.spacing.sm,
    paddingHorizontal: 4,
    backgroundColor: theme.colors.primary,
    borderRightWidth: 1,
    borderColor: 'rgba(255,255,255,0.1)',
    alignItems: 'center',
    justifyContent: 'center',
  },
  headerText: { color: '#fff', fontSize: theme.fontSize.xs, fontWeight: '700' },
  headerSub: { color: 'rgba(255,255,255,0.6)', fontSize: 9, marginTop: 2 },
  discCell: {
    paddingVertical: theme.spacing.sm,
    paddingHorizontal: theme.spacing.sm,
    backgroundColor: theme.colors.surface,
    borderRightWidth: 1,
    borderBottomWidth: 1,
    borderColor: theme.colors.border,
    justifyContent: 'center',
  },
  discText: { fontSize: theme.fontSize.sm, color: theme.colors.text, fontWeight: '600' },
  discSub: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary, marginTop: 2 },
  dataCell: {
    paddingVertical: theme.spacing.sm,
    borderRightWidth: 1,
    borderBottomWidth: 1,
    borderColor: 'rgba(0,0,0,0.05)',
    alignItems: 'center',
    justifyContent: 'center',
  },
  dataText: { fontSize: theme.fontSize.sm, fontWeight: '700' },
  legend: {
    flexDirection: 'row',
    justifyContent: 'center',
    flexWrap: 'wrap',
    gap: theme.spacing.sm,
    padding: theme.spacing.md,
  },
  legendItem: { flexDirection: 'row', alignItems: 'center', gap: 4 },
  legendSwatch: { width: 16, height: 16, borderRadius: 3 },
  legendText: { fontSize: theme.fontSize.xs, color: theme.colors.textSecondary },
});
