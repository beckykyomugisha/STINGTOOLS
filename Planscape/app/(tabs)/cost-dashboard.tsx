/**
 * Feature gap 2 — Live Cost Dashboard
 * Shows total estimated cost, total actual cost, variance, and a per-discipline
 * breakdown table. Fetches from GET /api/projects/{id}/boq/snapshot.
 */

import React, { useState, useCallback, useEffect } from 'react';
import {
  View,
  Text,
  FlatList,
  StyleSheet,
  RefreshControl,
  ActivityIndicator,
  ScrollView,
} from 'react-native';
import { useLocalSearchParams } from 'expo-router';
import { theme } from '@/utils/theme';
import { getBoqSnapshot, listProjects } from '@/api/endpoints';
import type { BoqDisciplineRow, BoqSnapshotResponse } from '@/api/endpoints';

// ── helpers ──────────────────────────────────────────────────────────────────

function fmt(value: number): string {
  if (value >= 1_000_000) return `$${(value / 1_000_000).toFixed(2)}M`;
  if (value >= 1_000)     return `$${(value / 1_000).toFixed(1)}K`;
  return `$${value.toFixed(2)}`;
}

function variancePct(estimated: number, actual: number): number {
  if (estimated === 0) return 0;
  return ((actual - estimated) / estimated) * 100;
}

// ── Discipline row ────────────────────────────────────────────────────────────

function DisciplineRow({ row }: { row: BoqDisciplineRow }) {
  const pct = variancePct(row.estimated, row.actual);
  const varianceColor = pct > 5
    ? theme.colors.danger
    : pct < -5
    ? theme.colors.success ?? '#16a34a'
    : theme.colors.textSecondary;

  return (
    <View style={styles.tableRow}>
      <Text style={[styles.cell, styles.cellDisc]}>{row.discipline}</Text>
      <Text style={[styles.cell, styles.cellNum]}>{row.items}</Text>
      <Text style={[styles.cell, styles.cellMoney]}>{fmt(row.estimated)}</Text>
      <Text style={[styles.cell, styles.cellMoney]}>{fmt(row.actual)}</Text>
      <Text style={[styles.cell, styles.cellVariance, { color: varianceColor }]}>
        {pct > 0 ? '+' : ''}{pct.toFixed(1)}%
      </Text>
    </View>
  );
}

// ── Summary card ──────────────────────────────────────────────────────────────

function SummaryCard({
  label,
  value,
  accent,
}: {
  label: string;
  value: string;
  accent?: boolean;
}) {
  return (
    <View style={[styles.summaryCard, accent && styles.summaryCardAccent]}>
      <Text style={styles.summaryLabel}>{label}</Text>
      <Text style={[styles.summaryValue, accent && styles.summaryValueAccent]}>
        {value}
      </Text>
    </View>
  );
}

// ── Main screen ───────────────────────────────────────────────────────────────

export default function CostDashboardScreen() {
  const [data, setData]         = useState<BoqSnapshotResponse | null>(null);
  const [loading, setLoading]   = useState(true);
  const [refreshing, setRefresh]= useState(false);
  const [error, setError]       = useState<string | null>(null);
  const [projectId, setProjectId] = useState<string | null>(null);

  // Resolve the active project id
  useEffect(() => {
    listProjects()
      .then((projects) => {
        if (projects.length > 0) setProjectId(String(projects[0].id));
      })
      .catch(() => setError('Could not load project list.'));
  }, []);

  const loadData = useCallback(async (showRefresh = false) => {
    if (!projectId) return;
    if (showRefresh) setRefresh(true);
    else setLoading(true);
    setError(null);
    try {
      const snapshot = await getBoqSnapshot(projectId);
      setData(snapshot);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load BOQ snapshot.');
    } finally {
      setLoading(false);
      setRefresh(false);
    }
  }, [projectId]);

  useEffect(() => { loadData(); }, [loadData]);

  if (loading) {
    return (
      <View style={styles.centred}>
        <ActivityIndicator size="large" color={theme.colors.accent} />
        <Text style={styles.loadingText}>Loading cost data…</Text>
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

  const latest = data?.latest ?? null;

  if (!latest) {
    return (
      <ScrollView
        contentContainerStyle={styles.centred}
        refreshControl={
          <RefreshControl refreshing={refreshing} onRefresh={() => loadData(true)} />
        }
      >
        <Text style={styles.emptyTitle}>📊 No BOQ Snapshot</Text>
        <Text style={styles.emptyText}>
          Push a BOQ snapshot from Revit (BIM → Push BOQ →Cloud) or upload an IFC file to
          auto-seed a baseline cost estimate.
        </Text>
      </ScrollView>
    );
  }

  const variance    = latest.totalActual - latest.totalEstimated;
  const variancePctTotal = variancePct(latest.totalEstimated, latest.totalActual);

  return (
    <FlatList
      refreshControl={
        <RefreshControl refreshing={refreshing} onRefresh={() => loadData(true)} />
      }
      ListHeaderComponent={
        <View>
          {/* ── Summary cards ── */}
          <View style={styles.summaryRow}>
            <SummaryCard label="Estimated" value={fmt(latest.totalEstimated)} />
            <SummaryCard label="Actual"    value={fmt(latest.totalActual)} accent />
            <SummaryCard
              label="Variance"
              value={`${variancePctTotal > 0 ? '+' : ''}${variancePctTotal.toFixed(1)}%`}
            />
          </View>

          {/* ── Raw variance amount ── */}
          <View style={styles.varianceBanner}>
            <Text style={styles.varianceBannerText}>
              {variance >= 0 ? '▲ Over' : '▼ Under'} by {fmt(Math.abs(variance))}
            </Text>
          </View>

          {/* ── Last updated ── */}
          <Text style={styles.metaText}>
            Snapshot: {new Date(latest.createdAt).toLocaleString()} · by {latest.createdBy}
          </Text>

          {/* ── Table header ── */}
          <View style={styles.tableHeader}>
            <Text style={[styles.headerCell, styles.cellDisc]}>Discipline</Text>
            <Text style={[styles.headerCell, styles.cellNum]}>Items</Text>
            <Text style={[styles.headerCell, styles.cellMoney]}>Estimated</Text>
            <Text style={[styles.headerCell, styles.cellMoney]}>Actual</Text>
            <Text style={[styles.headerCell, styles.cellVariance]}>Var %</Text>
          </View>
        </View>
      }
      data={latest.disciplines}
      keyExtractor={(item) => item.discipline}
      renderItem={({ item }) => <DisciplineRow row={item} />}
      ListEmptyComponent={
        <Text style={styles.emptyText}>No per-discipline data in this snapshot.</Text>
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
  emptyTitle: {
    fontSize: 24,
    marginBottom: 8,
    color: theme.colors.text,
  },
  emptyText: {
    color: theme.colors.textSecondary,
    textAlign: 'center',
    fontSize: 13,
    lineHeight: 20,
  },
  listContainer: {
    backgroundColor: theme.colors.background,
    paddingBottom: 24,
  },
  summaryRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    padding: 12,
    gap: 8,
  },
  summaryCard: {
    flex: 1,
    backgroundColor: theme.colors.surface,
    borderRadius: 8,
    padding: 12,
    alignItems: 'center',
    borderWidth: 1,
    borderColor: theme.colors.border,
  },
  summaryCardAccent: {
    borderColor: theme.colors.accent,
  },
  summaryLabel: {
    fontSize: 11,
    color: theme.colors.textSecondary,
    marginBottom: 4,
    textTransform: 'uppercase',
    letterSpacing: 0.5,
  },
  summaryValue: {
    fontSize: 16,
    fontWeight: '700',
    color: theme.colors.text,
  },
  summaryValueAccent: {
    color: theme.colors.accent,
  },
  varianceBanner: {
    backgroundColor: theme.colors.surface,
    marginHorizontal: 12,
    borderRadius: 6,
    padding: 8,
    alignItems: 'center',
    marginBottom: 4,
  },
  varianceBannerText: {
    fontSize: 13,
    color: theme.colors.text,
    fontWeight: '600',
  },
  metaText: {
    fontSize: 11,
    color: theme.colors.textSecondary,
    paddingHorizontal: 12,
    marginBottom: 12,
  },
  tableHeader: {
    flexDirection: 'row',
    backgroundColor: theme.colors.surface,
    paddingHorizontal: 12,
    paddingVertical: 8,
    borderBottomWidth: 1,
    borderBottomColor: theme.colors.border,
  },
  headerCell: {
    fontSize: 11,
    fontWeight: '700',
    color: theme.colors.textSecondary,
    textTransform: 'uppercase',
    letterSpacing: 0.5,
  },
  tableRow: {
    flexDirection: 'row',
    paddingHorizontal: 12,
    paddingVertical: 10,
    borderBottomWidth: 1,
    borderBottomColor: theme.colors.border,
    backgroundColor: theme.colors.background,
  },
  cell: {
    fontSize: 13,
    color: theme.colors.text,
  },
  cellDisc: {
    flex: 1.5,
    fontWeight: '600',
  },
  cellNum: {
    width: 40,
    textAlign: 'right',
    color: theme.colors.textSecondary,
  },
  cellMoney: {
    width: 76,
    textAlign: 'right',
  },
  cellVariance: {
    width: 60,
    textAlign: 'right',
    fontWeight: '600',
  },
});
