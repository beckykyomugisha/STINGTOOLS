import { useState, useEffect, useCallback } from 'react';
import {
  View,
  Text,
  StyleSheet,
  FlatList,
  RefreshControl,
  TouchableOpacity,
  ActivityIndicator,
  TextInput,
  Modal,
  ScrollView,
  KeyboardAvoidingView,
  Platform,
  Alert,
} from 'react-native';
import { router } from 'expo-router';
import * as WebBrowser from 'expo-web-browser';
import { theme, getPriorityColor } from '@/utils/theme';
import { listProjects, listIssues, createIssue, uploadIssueAttachment, _getBaseUrl } from '@/api/endpoints';
import type { BimIssue, Project, ProjectMember } from '@/types/api';
import { imageService, CapturedImage } from '@/services/imageService';
import { locationService } from '@/services/locationService';
import { MemberPicker } from '@/components/MemberPicker';
import * as Application from 'expo-application';
import * as Device from 'expo-device';
import { crashReporter } from '@/services/crashReporter';

/**
 * Phase 94 — MOB-01/MOB-06. Open the Planscape xeokit viewer for a project in
 * an in-app browser. URL shape is {serverUrl}/viewer/index.html?model=<code>.xkt.
 * The viewer itself lives in wwwroot on the Planscape.Server and reads the
 * 'model' query parameter to fetch the xkt bundle.
 */
async function openViewer(projectCode: string): Promise<void> {
  try {
    const base = await _getBaseUrl();
    const url = `${base}/viewer/index.html?model=${encodeURIComponent(projectCode)}.xkt`;
    await WebBrowser.openBrowserAsync(url, {
      // Corporate-themed in-app browser tab — falls back to Safari View
      // Controller on iOS and Custom Tabs on Android automatically.
      toolbarColor: theme.colors.primary,
      controlsColor: theme.colors.accent,
      dismissButtonStyle: 'close',
    });
  } catch (err) {
    Alert.alert('Viewer unavailable', err instanceof Error ? err.message : String(err));
  }
}

type PriorityFilter = 'ALL' | 'CRITICAL' | 'HIGH' | 'MEDIUM' | 'LOW';
type StatusFilter = 'ALL' | 'OPEN' | 'IN_PROGRESS' | 'RESOLVED' | 'CLOSED';

export default function IssuesScreen() {
  const [projects, setProjects] = useState<Project[]>([]);
  const [activeProject, setActiveProject] = useState<Project | null>(null);
  const [issues, setIssues] = useState<BimIssue[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [priorityFilter, setPriorityFilter] = useState<PriorityFilter>('ALL');
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('ALL');
  const [searchQuery, setSearchQuery] = useState('');

  const [showCreate, setShowCreate] = useState(false);
  const [creating, setCreating] = useState(false);
  const [newTitle, setNewTitle] = useState('');
  const [newDescription, setNewDescription] = useState('');
  const [newType, setNewType] = useState('RFI');
  const [newPriority, setNewPriority] = useState<BimIssue['priority']>('MEDIUM');
  const [newAssignee, setNewAssignee] = useState<ProjectMember | null>(null);
  const [newPhotos, setNewPhotos] = useState<CapturedImage[]>([]);
  const [showMemberPicker, setShowMemberPicker] = useState(false);
  const [creationStatus, setCreationStatus] = useState<string | null>(null);

  const loadData = useCallback(async (projectId?: string) => {
    try {
      setError(null);
      const projectList = await listProjects();
      setProjects(projectList);

      if (projectList.length === 0) {
        setLoading(false);
        return;
      }

      const target = projectId
        ? projectList.find((p) => p.id === projectId) ?? projectList[0]
        : projectList[0];

      setActiveProject(target);
      const data = await listIssues(target.id);
      setIssues(data);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to load issues';
      setError(msg);
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, []);

  useEffect(() => {
    loadData();
  }, [loadData]);

  function onRefresh() {
    setRefreshing(true);
    loadData(activeProject?.id);
  }

  const filtered = issues.filter((issue) => {
    if (priorityFilter !== 'ALL' && issue.priority !== priorityFilter) return false;
    if (statusFilter !== 'ALL' && issue.status !== statusFilter) return false;
    if (searchQuery) {
      const q = searchQuery.toLowerCase();
      return (
        issue.title.toLowerCase().includes(q) ||
        issue.issueCode.toLowerCase().includes(q) ||
        issue.type.toLowerCase().includes(q) ||
        (issue.assignee && issue.assignee.toLowerCase().includes(q))
      );
    }
    return true;
  });

  const counts = {
    open: issues.filter((i) => i.status === 'OPEN').length,
    inProgress: issues.filter((i) => i.status === 'IN_PROGRESS').length,
    resolved: issues.filter((i) => i.status === 'RESOLVED').length,
    closed: issues.filter((i) => i.status === 'CLOSED').length,
  };

  async function handleAddPhoto(source: 'camera' | 'library') {
    try {
      const captured = source === 'camera'
        ? await imageService.captureFromCamera()
        : await imageService.pickFromLibrary();
      if (!captured) return;
      const compressed = await imageService.compress(captured.uri).catch(() => captured);
      setNewPhotos(prev => [...prev, compressed]);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Photo capture failed');
    }
  }

  function handleRemovePhoto(index: number) {
    setNewPhotos(prev => prev.filter((_, i) => i !== index));
  }

  function resetCreateForm() {
    setNewTitle('');
    setNewDescription('');
    setNewType('RFI');
    setNewPriority('MEDIUM');
    setNewAssignee(null);
    setNewPhotos([]);
    setCreationStatus(null);
  }

  async function handleCreate() {
    if (!activeProject || !newTitle.trim()) return;
    setCreating(true);
    setCreationStatus('Capturing site location…');
    let location: { latitude: number; longitude: number; accuracy: number | null } | null = null;
    try {
      const loc = await locationService.getCurrent();
      if (loc) location = { latitude: loc.latitude, longitude: loc.longitude, accuracy: loc.accuracy };
    } catch (err) {
      // Permission denied or no signal — proceed without coordinates
      console.warn('[issues.create] location capture failed', err);
    }

    const deviceId = Application.getAndroidId?.() ?? (await Application.getIosIdForVendorAsync?.()) ?? null;

    try {
      setCreationStatus('Creating issue…');
      const created = await createIssue(activeProject.id, {
        title: newTitle.trim(),
        description: newDescription.trim(),
        type: newType,
        priority: newPriority,
        status: 'OPEN',
        assignee: newAssignee?.displayName ?? '',
        assigneeEmail: newAssignee?.email,
        assigneeUserId: newAssignee?.userId,
        latitude: location?.latitude,
        longitude: location?.longitude,
        locationAccuracy: location?.accuracy ?? undefined,
        deviceId: deviceId ?? undefined,
      });

      // Upload attachments after issue exists. Failures don't block the create.
      for (let i = 0; i < newPhotos.length; i++) {
        const p = newPhotos[i];
        setCreationStatus(`Uploading photo ${i + 1} of ${newPhotos.length}…`);
        try {
          await uploadIssueAttachment({
            projectId: activeProject.id,
            issueId: created.id,
            uri: p.uri,
            fileName: p.fileName ?? `site-${Date.now()}-${i}.jpg`,
            contentType: p.type ?? 'image/jpeg',
            latitude: location?.latitude,
            longitude: location?.longitude,
          });
        } catch (uploadErr) {
          console.warn(`[issues.create] photo ${i} upload failed`, uploadErr);
        }
      }
      setShowCreate(false);
      resetCreateForm();
      loadData(activeProject.id);
    } catch (err) {
      // NEW-INFO-14 — Explicit handling for the geofence 403 so the user sees
      // "Outside project boundary" rather than a raw HTTP error.
      const msg = err instanceof Error ? err.message : 'Failed to create issue';
      if (msg.includes('HTTP 403') || msg.toLowerCase().includes('geofence')
          || msg.toLowerCase().includes('outside the project')) {
        setError('Outside project geofence — move on site or ask your BIM manager to widen the boundary.');
      } else if (msg.includes('HTTP 400') && msg.toLowerCase().includes('latitude')) {
        setError('Invalid GPS reading — try again in a moment.');
      } else if (msg.includes('HTTP 400') && msg.toLowerCase().includes('assignee')) {
        setError('Chosen assignee is not a member of this project.');
      } else {
        setError(msg);
      }
    } finally {
      setCreating(false);
      setCreationStatus(null);
    }
  }

  if (loading) {
    return (
      <View style={styles.center}>
        <ActivityIndicator size="large" color={theme.colors.accent} />
        <Text style={styles.loadingText}>Loading issues...</Text>
      </View>
    );
  }

  if (error) {
    return (
      <View style={styles.center}>
        <Text style={styles.errorIcon}>!</Text>
        <Text style={styles.errorText}>{error}</Text>
        <TouchableOpacity style={styles.retryButton} onPress={() => { setLoading(true); loadData(); }}>
          <Text style={styles.retryButtonText}>Retry</Text>
        </TouchableOpacity>
      </View>
    );
  }

  if (!activeProject) {
    return (
      <View style={styles.center}>
        <Text style={styles.emptyText}>No projects found.</Text>
      </View>
    );
  }

  return (
    <View style={styles.root}>
      {/* Status summary strip */}
      <View style={styles.summaryStrip}>
        <SummaryChip label="Open" count={counts.open} color={theme.colors.danger} active={statusFilter === 'OPEN'} onPress={() => setStatusFilter(statusFilter === 'OPEN' ? 'ALL' : 'OPEN')} />
        <SummaryChip label="In Progress" count={counts.inProgress} color={theme.colors.warning} active={statusFilter === 'IN_PROGRESS'} onPress={() => setStatusFilter(statusFilter === 'IN_PROGRESS' ? 'ALL' : 'IN_PROGRESS')} />
        <SummaryChip label="Resolved" count={counts.resolved} color={theme.colors.success} active={statusFilter === 'RESOLVED'} onPress={() => setStatusFilter(statusFilter === 'RESOLVED' ? 'ALL' : 'RESOLVED')} />
        <SummaryChip label="Closed" count={counts.closed} color={theme.colors.disabled} active={statusFilter === 'CLOSED'} onPress={() => setStatusFilter(statusFilter === 'CLOSED' ? 'ALL' : 'CLOSED')} />
      </View>

      {/* Search bar */}
      <View style={styles.searchRow}>
        <TextInput
          style={styles.searchInput}
          placeholder="Search issues..."
          placeholderTextColor={theme.colors.disabled}
          value={searchQuery}
          onChangeText={setSearchQuery}
        />
      </View>

      {/* Priority filter chips */}
      <ScrollView horizontal showsHorizontalScrollIndicator={false} style={styles.filterBar} contentContainerStyle={styles.filterBarContent}>
        {(['ALL', 'CRITICAL', 'HIGH', 'MEDIUM', 'LOW'] as PriorityFilter[]).map((p) => (
          <TouchableOpacity
            key={p}
            style={[styles.filterChip, priorityFilter === p && styles.filterChipActive]}
            onPress={() => setPriorityFilter(p)}
          >
            {p !== 'ALL' && <View style={[styles.filterDot, { backgroundColor: getPriorityColor(p) }]} />}
            <Text style={[styles.filterChipText, priorityFilter === p && styles.filterChipTextActive]}>
              {p === 'ALL' ? 'All Priorities' : p}
            </Text>
          </TouchableOpacity>
        ))}
      </ScrollView>

      {/* Issue list */}
      <FlatList
        data={filtered}
        keyExtractor={(item) => item.id}
        renderItem={({ item }) => (
          <IssueCard
            issue={item}
            onPress={() => router.push(`/issue-detail?id=${item.id}`)}
            onViewIn3D={() => openViewer(activeProject.code)}
          />
        )}
        contentContainerStyle={styles.listContent}
        refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={theme.colors.accent} />}
        ListEmptyComponent={
          <View style={styles.emptyList}>
            <Text style={styles.emptyListText}>
              {issues.length === 0 ? 'No issues yet.' : 'No issues match filters.'}
            </Text>
          </View>
        }
      />

      {/* FAB — Create Issue */}
      <TouchableOpacity style={styles.fab} onPress={() => setShowCreate(true)} activeOpacity={0.8}>
        <Text style={styles.fabText}>+</Text>
      </TouchableOpacity>

      {/* Create Issue Modal */}
      <Modal visible={showCreate} animationType="slide" transparent>
        <KeyboardAvoidingView
          style={styles.modalOverlay}
          behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
        >
          <View style={styles.modalCard}>
            <Text style={styles.modalTitle}>New Issue</Text>

            <Text style={styles.inputLabel}>Title *</Text>
            <TextInput
              style={styles.modalInput}
              placeholder="Issue title"
              placeholderTextColor={theme.colors.disabled}
              value={newTitle}
              onChangeText={setNewTitle}
            />

            <Text style={styles.inputLabel}>Description</Text>
            <TextInput
              style={[styles.modalInput, styles.modalInputMulti]}
              placeholder="Describe the issue..."
              placeholderTextColor={theme.colors.disabled}
              value={newDescription}
              onChangeText={setNewDescription}
              multiline
              numberOfLines={3}
            />

            <Text style={styles.inputLabel}>Type</Text>
            <ScrollView horizontal showsHorizontalScrollIndicator={false} style={styles.typeRow}>
              {['RFI', 'NCR', 'SI', 'TQ', 'CLASH', 'DEFECT'].map((t) => (
                <TouchableOpacity
                  key={t}
                  style={[styles.typeChip, newType === t && styles.typeChipActive]}
                  onPress={() => setNewType(t)}
                >
                  <Text style={[styles.typeChipText, newType === t && styles.typeChipTextActive]}>{t}</Text>
                </TouchableOpacity>
              ))}
            </ScrollView>

            <Text style={styles.inputLabel}>Priority</Text>
            <View style={styles.priorityRow}>
              {(['CRITICAL', 'HIGH', 'MEDIUM', 'LOW'] as const).map((p) => (
                <TouchableOpacity
                  key={p}
                  style={[styles.priorityChip, { borderColor: getPriorityColor(p) }, newPriority === p && { backgroundColor: getPriorityColor(p) }]}
                  onPress={() => setNewPriority(p)}
                >
                  <Text style={[styles.priorityChipText, newPriority === p && styles.priorityChipTextActive]}>
                    {p}
                  </Text>
                </TouchableOpacity>
              ))}
            </View>

            <Text style={styles.inputLabel}>Assignee</Text>
            <TouchableOpacity
              style={styles.modalInput}
              onPress={() => setShowMemberPicker(true)}
              accessibilityRole="button"
              accessibilityLabel="Pick assignee"
            >
              <Text style={{
                fontSize: theme.fontSize.md,
                color: newAssignee ? theme.colors.text : theme.colors.disabled,
              }}>
                {newAssignee
                  ? `${newAssignee.displayName} (${newAssignee.email})`
                  : 'Tap to choose a project member'}
              </Text>
            </TouchableOpacity>

            <Text style={styles.inputLabel}>Photos ({newPhotos.length})</Text>
            <View style={{ flexDirection: 'row', gap: theme.spacing.sm, marginTop: 4 }}>
              <TouchableOpacity
                style={[styles.typeChip, { flexDirection: 'row' }]}
                onPress={() => handleAddPhoto('camera')}
                accessibilityRole="button"
                accessibilityLabel="Take a photo with the camera"
              >
                <Text style={styles.typeChipText}>📷  Camera</Text>
              </TouchableOpacity>
              <TouchableOpacity
                style={[styles.typeChip, { flexDirection: 'row' }]}
                onPress={() => handleAddPhoto('library')}
                accessibilityRole="button"
                accessibilityLabel="Pick a photo from library"
              >
                <Text style={styles.typeChipText}>🖼  Library</Text>
              </TouchableOpacity>
            </View>
            {newPhotos.length > 0 && (
              <ScrollView horizontal style={{ marginTop: theme.spacing.sm }}>
                {newPhotos.map((p, i) => (
                  <TouchableOpacity
                    key={`${p.uri}-${i}`}
                    onPress={() => handleRemovePhoto(i)}
                    accessibilityLabel={`Remove photo ${i + 1}`}
                  >
                    <View style={{
                      width: 64, height: 64, marginRight: 8,
                      borderRadius: 6, borderWidth: 1, borderColor: theme.colors.border,
                      alignItems: 'center', justifyContent: 'center',
                      backgroundColor: theme.colors.background,
                    }}>
                      <Text style={{ color: theme.colors.textSecondary, fontSize: 11 }}>{i + 1} ✕</Text>
                    </View>
                  </TouchableOpacity>
                ))}
              </ScrollView>
            )}

            {creationStatus && (
              <Text style={{
                marginTop: theme.spacing.sm,
                fontSize: theme.fontSize.xs,
                color: theme.colors.textSecondary,
                textAlign: 'center',
              }}>{creationStatus}</Text>
            )}

            <View style={styles.modalActions}>
              <TouchableOpacity
                style={styles.cancelButton}
                onPress={() => { setShowCreate(false); resetCreateForm(); }}
              >
                <Text style={styles.cancelButtonText}>Cancel</Text>
              </TouchableOpacity>
              <TouchableOpacity
                style={[styles.createButton, (!newTitle.trim() || creating) && styles.buttonDisabled]}
                onPress={handleCreate}
                disabled={!newTitle.trim() || creating}
              >
                {creating ? (
                  <ActivityIndicator color={theme.colors.surface} size="small" />
                ) : (
                  <Text style={styles.createButtonText}>Create</Text>
                )}
              </TouchableOpacity>
            </View>
          </View>
        </KeyboardAvoidingView>
      </Modal>

      {/* Member picker (NEW-MOB-13) */}
      {activeProject && (
        <MemberPicker
          visible={showMemberPicker}
          projectId={activeProject.id}
          selectedEmail={newAssignee?.email}
          onSelect={setNewAssignee}
          onClose={() => setShowMemberPicker(false)}
        />
      )}

      {/* Phase 94 — Legacy detail modal replaced by router.push('/issue-detail?id=...'). */}
    </View>
  );
}

function SummaryChip({ label, count, color, active, onPress }: {
  label: string; count: number; color: string; active: boolean; onPress: () => void;
}) {
  return (
    <TouchableOpacity
      style={[styles.summaryChip, active && { backgroundColor: color }]}
      onPress={onPress}
      activeOpacity={0.7}
    >
      <Text style={[styles.summaryCount, active && { color: '#fff' }]}>{count}</Text>
      <Text style={[styles.summaryLabel, active && { color: '#fff' }]}>{label}</Text>
    </TouchableOpacity>
  );
}

function IssueCard({
  issue,
  onPress,
  onViewIn3D,
}: {
  issue: BimIssue;
  onPress: () => void;
  onViewIn3D: () => void;
}) {
  const priorityColor = getPriorityColor(issue.priority);
  // NEW-INFO-02 — Prefer the server's IsOverdue flag when present, fall back
  // to the local 7-day heuristic for legacy responses.
  const isOverdue = issue.isOverdue ?? (
    issue.status === 'OPEN' && !!issue.dueDate && new Date(issue.dueDate) < new Date()
  ) ?? (issue.status === 'OPEN' && daysSince(issue.createdAt) > 7);
  const hasPhotos = (issue.attachmentCount ?? 0) > 0;

  return (
    <TouchableOpacity style={styles.issueCard} onPress={onPress} activeOpacity={0.7}>
      <View style={[styles.issueCardLeft, { backgroundColor: priorityColor }]} />
      <View style={styles.issueCardBody}>
        <View style={styles.issueCardTopRow}>
          <Text style={styles.issueCardCode}>{issue.issueCode}</Text>
          <View style={styles.issueCardBadges}>
            <View style={[styles.typeBadge]}>
              <Text style={styles.typeBadgeText}>{issue.type}</Text>
            </View>
            {hasPhotos ? (
              <View style={[styles.typeBadge, { backgroundColor: '#fff3e0' }]}>
                <Text style={[styles.typeBadgeText, { color: '#E8912D' }]}>📷 {issue.attachmentCount}</Text>
              </View>
            ) : null}
            <StatusBadge status={issue.status} small />
          </View>
        </View>
        <Text style={styles.issueCardTitle} numberOfLines={2}>{issue.title}</Text>
        <View style={styles.issueCardMeta}>
          {issue.assignee ? (
            <Text style={styles.issueCardAssignee}>{issue.assignee}</Text>
          ) : (
            <Text style={[styles.issueCardAssignee, { fontStyle: 'italic' }]}>Unassigned</Text>
          )}
          <Text style={[styles.issueCardDate, isOverdue && { color: '#D32F2F', fontWeight: '700' }]}>
            {isOverdue ? 'OVERDUE · ' : ''}
            {issue.dueDate ? `due ${formatDate(issue.dueDate)}` : formatDate(issue.createdAt)}
            {typeof issue.daysOpen === 'number' ? ` · ${issue.daysOpen}d` : ''}
          </Text>
        </View>
        {/* Phase 94 — MOB-06. Jumps into the xeokit viewer in-app browser. */}
        <TouchableOpacity
          style={styles.view3DButton}
          onPress={(e) => { e.stopPropagation(); onViewIn3D(); }}
          accessibilityRole="button"
          accessibilityLabel="View this issue in the 3D model viewer"
        >
          <Text style={styles.view3DButtonText}>🧊  View in 3D</Text>
        </TouchableOpacity>
      </View>
    </TouchableOpacity>
  );
}

function StatusBadge({ status, small }: { status: string; small?: boolean }) {
  const bgColor = status === 'OPEN' ? '#FFEBEE'
    : status === 'IN_PROGRESS' ? '#FFF3E0'
    : status === 'RESOLVED' ? '#E8F5E9'
    : '#F5F5F5';

  const textColor = status === 'OPEN' ? theme.colors.danger
    : status === 'IN_PROGRESS' ? theme.colors.warning
    : status === 'RESOLVED' ? theme.colors.success
    : theme.colors.textSecondary;

  const label = status.replace('_', ' ');

  return (
    <View style={[styles.statusBadge, { backgroundColor: bgColor }, small && styles.statusBadgeSmall]}>
      <Text style={[styles.statusBadgeText, { color: textColor }, small && styles.statusBadgeTextSmall]}>
        {label}
      </Text>
    </View>
  );
}

function formatDate(iso: string): string {
  if (!iso) return '—';
  try {
    const d = new Date(iso);
    return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });
  } catch (e) { crashReporter.warn('issues.tsx:579', { e: String(e) });
    return iso;
  }
}

function daysSince(iso: string): number {
  try {
    return Math.floor((Date.now() - new Date(iso).getTime()) / (1000 * 60 * 60 * 24));
  } catch (e) { crashReporter.warn('issues.tsx:587', { e: String(e) });
    return 0;
  }
}

const styles = StyleSheet.create({
  root: {
    flex: 1,
    backgroundColor: theme.colors.background,
  },
  center: {
    flex: 1,
    backgroundColor: theme.colors.background,
    alignItems: 'center',
    justifyContent: 'center',
    padding: theme.spacing.lg,
  },
  loadingText: {
    marginTop: theme.spacing.md,
    fontSize: theme.fontSize.md,
    color: theme.colors.textSecondary,
  },
  errorIcon: {
    fontSize: 40,
    fontWeight: '700',
    color: theme.colors.danger,
    width: 64,
    height: 64,
    lineHeight: 64,
    textAlign: 'center',
    borderRadius: 32,
    backgroundColor: '#FFEBEE',
    marginBottom: theme.spacing.md,
    overflow: 'hidden',
  },
  errorText: {
    fontSize: theme.fontSize.md,
    color: theme.colors.danger,
    textAlign: 'center',
    marginBottom: theme.spacing.md,
  },
  retryButton: {
    backgroundColor: theme.colors.accent,
    borderRadius: theme.borderRadius.md,
    paddingHorizontal: theme.spacing.lg,
    paddingVertical: theme.spacing.sm,
  },
  retryButtonText: {
    color: theme.colors.surface,
    fontSize: theme.fontSize.md,
    fontWeight: '600',
  },
  emptyText: {
    fontSize: theme.fontSize.xl,
    fontWeight: '600',
    color: theme.colors.text,
  },

  // Summary strip
  summaryStrip: {
    flexDirection: 'row',
    paddingHorizontal: theme.spacing.md,
    paddingTop: theme.spacing.md,
    paddingBottom: theme.spacing.sm,
    gap: theme.spacing.sm,
  },
  summaryChip: {
    flex: 1,
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    paddingVertical: theme.spacing.sm,
    alignItems: 'center',
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.06,
    shadowRadius: 3,
    elevation: 1,
  },
  summaryCount: {
    fontSize: theme.fontSize.lg,
    fontWeight: '700',
    color: theme.colors.text,
  },
  summaryLabel: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
    marginTop: 2,
  },

  // Search
  searchRow: {
    paddingHorizontal: theme.spacing.md,
    paddingVertical: theme.spacing.sm,
  },
  searchInput: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    borderWidth: 1,
    borderColor: theme.colors.border,
    paddingHorizontal: theme.spacing.md,
    paddingVertical: theme.spacing.sm,
    fontSize: theme.fontSize.md,
    color: theme.colors.text,
  },

  // Filter bar
  filterBar: {
    flexGrow: 0,
    marginBottom: theme.spacing.sm,
  },
  filterBarContent: {
    paddingHorizontal: theme.spacing.md,
    gap: theme.spacing.sm,
  },
  filterChip: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.lg,
    paddingHorizontal: theme.spacing.md,
    paddingVertical: theme.spacing.xs + 2,
    borderWidth: 1,
    borderColor: theme.colors.border,
  },
  filterChipActive: {
    backgroundColor: theme.colors.primary,
    borderColor: theme.colors.primary,
  },
  filterDot: {
    width: 8,
    height: 8,
    borderRadius: 4,
    marginRight: 6,
  },
  filterChipText: {
    fontSize: theme.fontSize.sm,
    fontWeight: '600',
    color: theme.colors.text,
  },
  filterChipTextActive: {
    color: theme.colors.surface,
  },

  // Issue list
  listContent: {
    paddingHorizontal: theme.spacing.md,
    paddingBottom: 80,
  },
  emptyList: {
    paddingVertical: theme.spacing.xl,
    alignItems: 'center',
  },
  emptyListText: {
    fontSize: theme.fontSize.md,
    color: theme.colors.textSecondary,
  },

  // Issue card
  issueCard: {
    flexDirection: 'row',
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.lg,
    marginBottom: theme.spacing.sm,
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.06,
    shadowRadius: 4,
    elevation: 2,
    overflow: 'hidden',
  },
  issueCardLeft: {
    width: 4,
  },
  issueCardBody: {
    flex: 1,
    padding: theme.spacing.md,
  },
  issueCardTopRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: theme.spacing.xs,
  },
  issueCardCode: {
    fontSize: theme.fontSize.sm,
    fontWeight: '600',
    color: theme.colors.textSecondary,
  },
  issueCardBadges: {
    flexDirection: 'row',
    gap: 6,
  },
  typeBadge: {
    backgroundColor: theme.colors.primary + '15',
    borderRadius: theme.borderRadius.sm,
    paddingHorizontal: 6,
    paddingVertical: 2,
  },
  typeBadgeText: {
    fontSize: theme.fontSize.xs,
    fontWeight: '600',
    color: theme.colors.primary,
  },
  issueCardTitle: {
    fontSize: theme.fontSize.md,
    fontWeight: '500',
    color: theme.colors.text,
    marginBottom: theme.spacing.xs,
  },
  issueCardMeta: {
    flexDirection: 'row',
    justifyContent: 'space-between',
  },
  issueCardAssignee: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
  },
  issueCardDate: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
  },

  // Phase 94 — View in 3D button
  view3DButton: {
    alignSelf: 'flex-start',
    marginTop: theme.spacing.sm,
    paddingHorizontal: theme.spacing.sm,
    paddingVertical: 4,
    borderRadius: theme.borderRadius.sm,
    borderWidth: 1,
    borderColor: theme.colors.accent,
    backgroundColor: theme.colors.accent + '12',
  },
  view3DButtonText: {
    fontSize: theme.fontSize.xs,
    fontWeight: '600',
    color: theme.colors.accent,
  },

  // Status badge
  statusBadge: {
    borderRadius: theme.borderRadius.sm,
    paddingHorizontal: 8,
    paddingVertical: 3,
  },
  statusBadgeSmall: {
    paddingHorizontal: 6,
    paddingVertical: 2,
  },
  statusBadgeText: {
    fontSize: theme.fontSize.xs,
    fontWeight: '600',
  },
  statusBadgeTextSmall: {
    fontSize: 9,
  },

  // FAB
  fab: {
    position: 'absolute',
    right: theme.spacing.md,
    bottom: theme.spacing.lg,
    width: 56,
    height: 56,
    borderRadius: 28,
    backgroundColor: theme.colors.accent,
    alignItems: 'center',
    justifyContent: 'center',
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 4 },
    shadowOpacity: 0.2,
    shadowRadius: 8,
    elevation: 6,
  },
  fabText: {
    fontSize: 28,
    color: theme.colors.surface,
    fontWeight: '400',
    marginTop: -2,
  },

  // Modal shared
  modalOverlay: {
    flex: 1,
    backgroundColor: 'rgba(0,0,0,0.5)',
    justifyContent: 'flex-end',
  },
  modalCard: {
    backgroundColor: theme.colors.surface,
    borderTopLeftRadius: theme.borderRadius.xl,
    borderTopRightRadius: theme.borderRadius.xl,
    padding: theme.spacing.lg,
    maxHeight: '85%',
  },
  modalTitle: {
    fontSize: theme.fontSize.xl,
    fontWeight: '600',
    color: theme.colors.text,
    marginBottom: theme.spacing.md,
  },
  inputLabel: {
    fontSize: theme.fontSize.sm,
    fontWeight: '600',
    color: theme.colors.textSecondary,
    marginBottom: theme.spacing.xs,
    marginTop: theme.spacing.sm,
  },
  modalInput: {
    backgroundColor: theme.colors.background,
    borderRadius: theme.borderRadius.md,
    borderWidth: 1,
    borderColor: theme.colors.border,
    paddingHorizontal: theme.spacing.md,
    paddingVertical: theme.spacing.sm,
    fontSize: theme.fontSize.md,
    color: theme.colors.text,
  },
  modalInputMulti: {
    minHeight: 80,
    textAlignVertical: 'top',
  },
  typeRow: {
    flexGrow: 0,
    marginTop: theme.spacing.xs,
  },
  typeChip: {
    paddingHorizontal: theme.spacing.md,
    paddingVertical: theme.spacing.xs + 2,
    borderRadius: theme.borderRadius.md,
    borderWidth: 1,
    borderColor: theme.colors.border,
    marginRight: theme.spacing.sm,
  },
  typeChipActive: {
    backgroundColor: theme.colors.primary,
    borderColor: theme.colors.primary,
  },
  typeChipText: {
    fontSize: theme.fontSize.sm,
    fontWeight: '600',
    color: theme.colors.text,
  },
  typeChipTextActive: {
    color: theme.colors.surface,
  },
  priorityRow: {
    flexDirection: 'row',
    gap: theme.spacing.sm,
    marginTop: theme.spacing.xs,
  },
  priorityChip: {
    flex: 1,
    paddingVertical: theme.spacing.xs + 2,
    borderRadius: theme.borderRadius.md,
    borderWidth: 2,
    alignItems: 'center',
  },
  priorityChipText: {
    fontSize: theme.fontSize.xs,
    fontWeight: '700',
    color: theme.colors.text,
  },
  priorityChipTextActive: {
    color: '#fff',
  },
  modalActions: {
    flexDirection: 'row',
    gap: theme.spacing.md,
    marginTop: theme.spacing.lg,
  },
  cancelButton: {
    flex: 1,
    paddingVertical: theme.spacing.sm + 4,
    borderRadius: theme.borderRadius.md,
    borderWidth: 1,
    borderColor: theme.colors.border,
    alignItems: 'center',
  },
  cancelButtonText: {
    fontSize: theme.fontSize.md,
    fontWeight: '600',
    color: theme.colors.textSecondary,
  },
  createButton: {
    flex: 1,
    paddingVertical: theme.spacing.sm + 4,
    borderRadius: theme.borderRadius.md,
    backgroundColor: theme.colors.accent,
    alignItems: 'center',
  },
  buttonDisabled: {
    opacity: 0.5,
  },
  createButtonText: {
    fontSize: theme.fontSize.md,
    fontWeight: '600',
    color: theme.colors.surface,
  },

  // Phase 94 — legacy "detail modal" styles removed. Issue detail now lives
  // in app/(tabs)/issue-detail.tsx and uses its own stylesheet.
});
