/**
 * Members — Project Team Roster screen (BCC Team tab equivalent).
 *
 * Shows every project member with their ISO 19650 role, discipline,
 * open issue count, and overdue action count.  Tapping a row opens
 * a bottom-sheet detail with name / role / email / workload bar.
 *
 * Data: GET /api/projects/{id}/members  (ProjectMembersController)
 *       GET /api/projects/{id}/issues   (open issues per assignee)
 */

import { useCallback, useEffect, useRef, useState } from 'react';
import {
  View,
  Text,
  ScrollView,
  RefreshControl,
  TouchableOpacity,
  StyleSheet,
  ActivityIndicator,
  Modal,
  Animated,
  Dimensions,
} from 'react-native';
import { Stack } from 'expo-router';
import { theme } from '@/utils/theme';
import { listProjectMembersFull, listIssues, type ProjectMemberRow } from '@/api/endpoints';
import { useProjectStore } from '@/stores/projectStore';
import type { BimIssue } from '@/types/api';

// ─── ISO 19650 role config ────────────────────────────────────────────────────

const ISO_ROLE_LABELS: Record<string, string> = {
  AP: 'Appointing Party',
  LAP: 'Lead Appointed Party',
  TTM: 'Task Team Member',
  BIMmgr: 'BIM Manager',
  BIMco: 'BIM Coordinator',
  IM: 'Information Manager',
  // legacy short codes from project-settings/members.tsx
  K: 'Key Person',
  C: 'Contractor',
  TI: 'Technical Inspector',
  L: 'Lead',
};

const ROLE_COLORS: Record<string, string> = {
  AP: '#7B2FBE',     // purple
  LAP: '#1A237E',    // navy
  TTM: '#1565C0',    // blue
  BIMmgr: '#E65100', // orange
  BIMco: '#00695C',  // teal
  IM: '#2E7D32',     // green
  K: '#5D4037',
  C: '#37474F',
  TI: '#4527A0',
  L: '#AD1457',
};

const DEFAULT_ROLE_COLOR = '#455A64';

function roleColor(code: string): string {
  return ROLE_COLORS[code] ?? DEFAULT_ROLE_COLOR;
}

function roleLabel(code: string): string {
  return ISO_ROLE_LABELS[code] ?? code;
}

// ─── Avatar helpers ───────────────────────────────────────────────────────────

function initials(name: string): string {
  const parts = String(name || '?').trim().split(/\s+/).slice(0, 2);
  return parts.map((p) => p[0]?.toUpperCase() ?? '').join('') || '?';
}

function avatarBg(name: string, isoRole: string): string {
  if (isoRole && ROLE_COLORS[isoRole]) return ROLE_COLORS[isoRole];
  // deterministic hue from name when role has no mapped color
  let h = 0;
  for (const ch of String(name || '')) h = (h * 31 + ch.charCodeAt(0)) | 0;
  return `hsl(${Math.abs(h) % 360}, 40%, 35%)`;
}

// ─── Types ────────────────────────────────────────────────────────────────────

interface MemberStats {
  openIssues: number;
  overdueCount: number;
}

// ─── Screen ──────────────────────────────────────────────────────────────────

export default function MembersScreen() {
  const projectId = useProjectStore((s) => s.active?.id);

  const [members, setMembers] = useState<ProjectMemberRow[]>([]);
  const [stats, setStats] = useState<Record<string, MemberStats>>({});
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<ProjectMemberRow | null>(null);

  const load = useCallback(async () => {
    if (!projectId) return;
    try {
      setError(null);
      const [rows, issues] = await Promise.all([
        listProjectMembersFull(projectId),
        listIssues(projectId).catch(() => [] as BimIssue[]),
      ]);
      setMembers(rows);

      // Build per-assignee stats from the issue list
      const map: Record<string, MemberStats> = {};
      for (const issue of issues) {
        if (!issue.assigneeEmail && !issue.assignee) continue;
        const key = issue.assigneeEmail ?? issue.assignee;
        if (!map[key]) map[key] = { openIssues: 0, overdueCount: 0 };
        if (issue.status === 'OPEN' || issue.status === 'IN_PROGRESS') {
          map[key].openIssues++;
          if (issue.isOverdue) map[key].overdueCount++;
        }
      }
      // Also index by displayName for members whose assignee field uses name
      for (const m of rows) {
        const byEmail = map[m.email] ?? map[m.displayName];
        if (byEmail && !map[m.email]) map[m.email] = byEmail;
      }
      setStats(map);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to load team');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [projectId]);

  useEffect(() => { void load(); }, [load]);

  if (!projectId) {
    return (
      <View style={s.center}>
        <Text style={s.emptyText}>Select a project on the dashboard first.</Text>
      </View>
    );
  }

  if (loading) {
    return (
      <View style={s.center}>
        <ActivityIndicator size="large" color={theme.colors.accent} />
      </View>
    );
  }

  const memberStats = (m: ProjectMemberRow): MemberStats =>
    stats[m.email] ?? stats[m.displayName] ?? { openIssues: 0, overdueCount: 0 };

  return (
    <>
      <Stack.Screen
        options={{
          headerShown: true,
          title: `Team${members.length > 0 ? ` (${members.length})` : ''}`,
          headerStyle: { backgroundColor: theme.colors.surface },
          headerTitleStyle: { color: theme.colors.text, fontWeight: '700' },
        }}
      />
      <ScrollView
        style={s.root}
        contentContainerStyle={s.scroll}
        refreshControl={
          <RefreshControl
            refreshing={refreshing}
            onRefresh={() => { setRefreshing(true); void load(); }}
            tintColor={theme.colors.accent}
          />
        }
      >
        {error ? (
          <View style={s.errorBox}>
            <Text style={s.errorText}>{error}</Text>
          </View>
        ) : null}

        {members.length === 0 ? (
          <View style={s.emptyCard}>
            <Text style={s.emptyTitle}>No team members</Text>
            <Text style={s.emptySubtitle}>Invite via the web portal.</Text>
          </View>
        ) : (
          members.map((m) => {
            const ms = memberStats(m);
            return (
              <TouchableOpacity
                key={m.id}
                style={s.row}
                onPress={() => setSelected(m)}
                accessibilityRole="button"
                accessibilityLabel={`View ${m.displayName} detail`}
              >
                {/* Avatar */}
                <View style={[s.avatar, { backgroundColor: avatarBg(m.displayName, m.iso19650Role) }]}>
                  <Text style={s.avatarText}>{initials(m.displayName)}</Text>
                </View>

                {/* Info */}
                <View style={s.info}>
                  <Text style={s.name} numberOfLines={1}>{m.displayName}</Text>
                  <View style={s.roleRow}>
                    {m.iso19650Role ? (
                      <View style={[s.roleChip, { backgroundColor: roleColor(m.iso19650Role) }]}>
                        <Text style={s.roleChipText}>{m.iso19650Role}</Text>
                      </View>
                    ) : null}
                    <Text style={s.roleLabel} numberOfLines={1}>
                      {m.iso19650Role ? roleLabel(m.iso19650Role) : m.projectRole || '—'}
                    </Text>
                  </View>
                  {m.allowedDisciplines ? (
                    <Text style={s.discipline} numberOfLines={1}>
                      {m.allowedDisciplines}
                    </Text>
                  ) : null}
                </View>

                {/* Badges */}
                <View style={s.badges}>
                  <View style={[s.badge, ms.openIssues > 0 && s.badgeActive]}>
                    <Text style={[s.badgeText, ms.openIssues > 0 && s.badgeTextActive]}>
                      {ms.openIssues} open
                    </Text>
                  </View>
                  {ms.overdueCount > 0 ? (
                    <View style={[s.badge, s.badgeDanger]}>
                      <Text style={[s.badgeText, s.badgeDangerText]}>
                        {ms.overdueCount} overdue
                      </Text>
                    </View>
                  ) : null}
                </View>
              </TouchableOpacity>
            );
          })
        )}
      </ScrollView>

      {/* Member detail sheet */}
      <MemberDetailSheet
        member={selected}
        stats={selected ? memberStats(selected) : { openIssues: 0, overdueCount: 0 }}
        onClose={() => setSelected(null)}
      />
    </>
  );
}

// ─── Member Detail Sheet ──────────────────────────────────────────────────────

const SCREEN_HEIGHT = Dimensions.get('window').height;

function MemberDetailSheet({
  member, stats, onClose,
}: {
  member: ProjectMemberRow | null;
  stats: MemberStats;
  onClose: () => void;
}) {
  const slideAnim = useRef(new Animated.Value(SCREEN_HEIGHT)).current;

  useEffect(() => {
    if (member) {
      Animated.spring(slideAnim, {
        toValue: 0,
        useNativeDriver: true,
        tension: 60,
        friction: 10,
      }).start();
    } else {
      Animated.timing(slideAnim, {
        toValue: SCREEN_HEIGHT,
        duration: 220,
        useNativeDriver: true,
      }).start();
    }
  }, [member, slideAnim]);

  if (!member) return null;

  const maxIssues = 10; // workload bar scale
  const workloadPct = Math.min((stats.openIssues / maxIssues) * 100, 100);
  const workloadColor =
    workloadPct >= 80 ? theme.colors.danger
    : workloadPct >= 50 ? theme.colors.warning
    : theme.colors.success;

  return (
    <Modal
      transparent
      visible={!!member}
      animationType="none"
      onRequestClose={onClose}
    >
      <TouchableOpacity style={ds.backdrop} activeOpacity={1} onPress={onClose} />
      <Animated.View style={[ds.sheet, { transform: [{ translateY: slideAnim }] }]}>
        {/* Handle */}
        <View style={ds.handle} />

        {/* Avatar + name */}
        <View style={ds.header}>
          <View style={[ds.avatar, { backgroundColor: avatarBg(member.displayName, member.iso19650Role) }]}>
            <Text style={ds.avatarText}>{initials(member.displayName)}</Text>
          </View>
          <View style={{ flex: 1 }}>
            <Text style={ds.name}>{member.displayName}</Text>
            <Text style={ds.email}>{member.email}</Text>
          </View>
        </View>

        {/* Role chips */}
        <View style={ds.chipRow}>
          {member.iso19650Role ? (
            <View style={[ds.chip, { backgroundColor: roleColor(member.iso19650Role) }]}>
              <Text style={ds.chipText}>{roleLabel(member.iso19650Role)}</Text>
            </View>
          ) : null}
          {member.projectRole ? (
            <View style={[ds.chip, { backgroundColor: theme.colors.primary }]}>
              <Text style={ds.chipText}>{member.projectRole}</Text>
            </View>
          ) : null}
        </View>

        {/* Workload bar */}
        <View style={ds.section}>
          <Text style={ds.sectionTitle}>Workload</Text>
          <View style={ds.workloadRow}>
            <View style={ds.workloadBar}>
              <View style={[ds.workloadFill, { width: `${workloadPct}%` as any, backgroundColor: workloadColor }]} />
            </View>
            <Text style={[ds.workloadPct, { color: workloadColor }]}>
              {stats.openIssues} open
            </Text>
          </View>
          {stats.overdueCount > 0 ? (
            <Text style={ds.overdueNote}>
              ⚠ {stats.overdueCount} overdue action{stats.overdueCount !== 1 ? 's' : ''}
            </Text>
          ) : null}
        </View>

        {/* Access summary */}
        {(member.allowedDisciplines || member.allowedCdeStates) ? (
          <View style={ds.section}>
            <Text style={ds.sectionTitle}>Access</Text>
            {member.allowedDisciplines ? (
              <Text style={ds.accessLine}>
                Disciplines: {member.allowedDisciplines}
              </Text>
            ) : null}
            {member.allowedCdeStates ? (
              <Text style={ds.accessLine}>
                CDE states: {member.allowedCdeStates}
              </Text>
            ) : null}
            {member.allowedSuitabilities ? (
              <Text style={ds.accessLine}>
                Suitabilities: {member.allowedSuitabilities}
              </Text>
            ) : null}
          </View>
        ) : null}

        {/* Close */}
        <TouchableOpacity style={ds.closeBtn} onPress={onClose}>
          <Text style={ds.closeBtnText}>Close</Text>
        </TouchableOpacity>
      </Animated.View>
    </Modal>
  );
}

// ─── Styles ───────────────────────────────────────────────────────────────────

const s = StyleSheet.create({
  root: { flex: 1, backgroundColor: theme.colors.background },
  scroll: { padding: theme.spacing.md, paddingBottom: theme.spacing.xl },
  center: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    padding: theme.spacing.lg,
    backgroundColor: theme.colors.background,
  },
  emptyText: { color: theme.colors.textSecondary, fontSize: theme.fontSize.md },
  emptyCard: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.xl,
    alignItems: 'center',
  },
  emptyTitle: { fontSize: theme.fontSize.md, fontWeight: '600', color: theme.colors.text },
  emptySubtitle: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.textSecondary,
    marginTop: theme.spacing.xs,
  },
  errorBox: {
    backgroundColor: '#FFEBEE',
    borderRadius: theme.borderRadius.sm,
    padding: theme.spacing.sm,
    marginBottom: theme.spacing.md,
  },
  errorText: { color: theme.colors.danger, fontSize: theme.fontSize.sm },

  // Member row
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.md,
    marginBottom: theme.spacing.sm,
    gap: theme.spacing.sm,
  },
  avatar: {
    width: 44,
    height: 44,
    borderRadius: 22,
    alignItems: 'center',
    justifyContent: 'center',
    flexShrink: 0,
  },
  avatarText: { color: '#fff', fontWeight: '700', fontSize: 15 },
  info: { flex: 1, minWidth: 0 },
  name: {
    fontSize: theme.fontSize.md,
    fontWeight: '700',
    color: theme.colors.text,
    marginBottom: 2,
  },
  roleRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    marginBottom: 2,
  },
  roleChip: {
    paddingHorizontal: 6,
    paddingVertical: 1,
    borderRadius: 8,
  },
  roleChipText: { color: '#fff', fontSize: 10, fontWeight: '700' },
  roleLabel: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
    flex: 1,
  },
  discipline: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.disabled,
  },
  badges: {
    alignItems: 'flex-end',
    gap: 4,
    flexShrink: 0,
  },
  badge: {
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderRadius: 10,
    backgroundColor: theme.colors.background,
    borderWidth: 1,
    borderColor: theme.colors.border,
  },
  badgeActive: {
    backgroundColor: theme.colors.primary + '22',
    borderColor: theme.colors.primary,
  },
  badgeText: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
    fontWeight: '600',
  },
  badgeTextActive: { color: theme.colors.primary },
  badgeDanger: {
    backgroundColor: theme.colors.danger + '18',
    borderColor: theme.colors.danger,
  },
  badgeDangerText: { color: theme.colors.danger },
});

const ds = StyleSheet.create({
  backdrop: {
    ...StyleSheet.absoluteFillObject,
    backgroundColor: 'rgba(0,0,0,0.45)',
  },
  sheet: {
    position: 'absolute',
    bottom: 0,
    left: 0,
    right: 0,
    backgroundColor: theme.colors.surface,
    borderTopLeftRadius: theme.borderRadius.xl,
    borderTopRightRadius: theme.borderRadius.xl,
    padding: theme.spacing.lg,
    paddingBottom: theme.spacing.xl,
  },
  handle: {
    width: 36,
    height: 4,
    borderRadius: 2,
    backgroundColor: theme.colors.border,
    alignSelf: 'center',
    marginBottom: theme.spacing.md,
  },
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: theme.spacing.md,
    marginBottom: theme.spacing.md,
  },
  avatar: {
    width: 56,
    height: 56,
    borderRadius: 28,
    alignItems: 'center',
    justifyContent: 'center',
  },
  avatarText: { color: '#fff', fontWeight: '700', fontSize: 20 },
  name: { fontSize: theme.fontSize.lg, fontWeight: '700', color: theme.colors.text },
  email: { fontSize: theme.fontSize.sm, color: theme.colors.textSecondary, marginTop: 2 },

  chipRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: theme.spacing.xs,
    marginBottom: theme.spacing.md,
  },
  chip: {
    paddingHorizontal: 10,
    paddingVertical: 4,
    borderRadius: 12,
  },
  chipText: { color: '#fff', fontSize: theme.fontSize.xs, fontWeight: '700' },

  section: { marginBottom: theme.spacing.md },
  sectionTitle: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
    fontWeight: '700',
    textTransform: 'uppercase',
    letterSpacing: 0.5,
    marginBottom: 6,
  },
  workloadRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: theme.spacing.sm,
  },
  workloadBar: {
    flex: 1,
    height: 8,
    borderRadius: 4,
    backgroundColor: theme.colors.border,
    overflow: 'hidden',
  },
  workloadFill: { height: 8, borderRadius: 4 },
  workloadPct: { fontSize: theme.fontSize.sm, fontWeight: '700', width: 60, textAlign: 'right' },
  overdueNote: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.danger,
    marginTop: 4,
  },
  accessLine: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.text,
    marginBottom: 2,
  },

  closeBtn: {
    backgroundColor: theme.colors.accent,
    borderRadius: theme.borderRadius.md,
    paddingVertical: theme.spacing.sm + 2,
    alignItems: 'center',
    marginTop: theme.spacing.sm,
  },
  closeBtnText: { color: '#fff', fontWeight: '700', fontSize: theme.fontSize.md },
});
