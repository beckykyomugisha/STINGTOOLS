import { useState, useEffect, useCallback, useRef, useMemo } from 'react';
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
import { router, useLocalSearchParams } from 'expo-router';
import * as WebBrowser from 'expo-web-browser';
import { theme, getPriorityColor } from '@/utils/theme';
import { listProjects, listIssues, createIssue, uploadIssueAttachment, updateIssue, listAvailableXkts, _getBaseUrl } from '@/api/endpoints';
import { listModels } from '@/api/models';
import type { ModelMeta } from '@/types/models';
import type { BimIssue, Project, ProjectMember } from '@/types/api';
import { imageService, CapturedImage } from '@/services/imageService';
import { locationService } from '@/services/locationService';
import { MemberPicker } from '@/components/MemberPicker';
import { AudioRecorder } from '@/components/AudioRecorder';
import * as Application from 'expo-application';
import * as Device from 'expo-device';
import { crashReporter } from '@/services/crashReporter';
import { useNotificationStore } from '@/stores/notificationStore';
import { useAuthStore } from '@/stores/authStore';
import { debounce } from '@/utils/debounce';

/**
 * Phase 94 — MOB-01/MOB-06. Open the Planscape xeokit viewer for a project in
 * an in-app browser. URL shape is {serverUrl}/viewer/index.html?model=<code>.xkt.
 * The viewer itself lives in wwwroot on the Planscape.Server and reads the
 * 'model' query parameter to fetch the xkt bundle.
 */
async function openViewer(projectCode: string, modelId?: string | null): Promise<void> {
  try {
    const base = await _getBaseUrl();
    // Phase 164 caveat 4 — probe the cached XKT availability list before
    // committing to a per-model URL. When a `<modelId>.xkt` file is
    // published we use it; when only the project default exists, fall back
    // to `<projectCode>.xkt` so the user lands in a working viewer rather
    // than a 404. Empty cache (network failure on the list endpoint) →
    // optimistically use modelId so configured projects don't get punished
    // by transient list-endpoint failures.
    let xktBase = projectCode;
    if (modelId) {
      const available = await listAvailableXkts();
      const modelXkt = `${modelId}.xkt`;
      if (available.size === 0 || available.has(modelXkt)) {
        xktBase = modelId;
      }
    }
    const url = `${base}/viewer/index.html?model=${encodeURIComponent(xktBase)}.xkt`;
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
  // Phase 96 — notificationTapRouter pushes /(tabs)/issues?issueId=X and/or
  // ?projectId=Y. Scanner pushes ?createForElement=X&elementTag=Y to pre-fill
  // a new-issue form. The ref guards against re-firing the redirect/open on
  // every render while the user is browsing.
  // Phase 163 — viewer's onPlaceIssue ("create issue here" gesture) pushes
  // ?fromViewer=1&modelId=...&modelElementGuid=...&modelX/Y/Z=... so anchor
  // coords flow into the creation form. Replaces the broken /issues/new
  // target the viewer used to push to.
  const params = useLocalSearchParams<{
    issueId?: string;
    projectId?: string;
    createForElement?: string;
    elementTag?: string;
    fromViewer?: string;
    modelId?: string;
    modelElementGuid?: string;
    modelX?: string;
    modelY?: string;
    modelZ?: string;
    tag?: string;
    category?: string;
    discipline?: string;
  }>();
  const deepLinkHandled = useRef(false);
  const scannerLinkHandled = useRef(false);
  const viewerLinkHandled = useRef(false);
  const [projects, setProjects] = useState<Project[]>([]);
  const [activeProject, setActiveProject] = useState<Project | null>(null);
  const [issues, setIssues] = useState<BimIssue[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [priorityFilter, setPriorityFilter] = useState<PriorityFilter>('ALL');
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('ALL');
  const [searchQuery, setSearchQuery] = useState('');
  const [searchInput, setSearchInput] = useState('');
  // Phase 142 — "Mine" toggle. When on, the list narrows to issues where
  // the assignee resolves to the current user (FK first, then email, then
  // display name for legacy rows).
  const [mineOnly, setMineOnly] = useState(false);
  const me = useAuthStore((s) => ({
    userId: s.userId, email: s.email, displayName: s.displayName,
  }));

  // Phase 96 — debounce so the filter re-run doesn't fire on every keystroke.
  // 250ms is the point of diminishing returns — coordinators typing a tag
  // number perceive it as instant but lists of 500+ issues no longer re-render
  // on every character.
  const debouncedSetSearch = useMemo(
    () => debounce((v: string) => setSearchQuery(v), 250),
    [],
  );

  const [showCreate, setShowCreate] = useState(false);
  const [creating, setCreating] = useState(false);
  const [newTitle, setNewTitle] = useState('');
  const [newDescription, setNewDescription] = useState('');
  const [newType, setNewType] = useState('RFI');
  const [newPriority, setNewPriority] = useState<BimIssue['priority']>('MEDIUM');
  const [newAssignee, setNewAssignee] = useState<ProjectMember | null>(null);
  const [newPhotos, setNewPhotos] = useState<CapturedImage[]>([]);
  const [newElementIds, setNewElementIds] = useState<string>('');
  // MODEL-VIEWER — model picker. `availableModels` is lazy-loaded the first
  // time the create modal opens for a given project (cheap GET, project
  // typically has 1–6 models). `newModelId === null` means "no model link".
  const [availableModels, setAvailableModels] = useState<ModelMeta[]>([]);
  const [newModelId, setNewModelId] = useState<string | null>(null);
  // Phase 163 — anchor coords from the viewer's PlaceIssue gesture.
  // Populated only via the deep-link path; the manual creation flow leaves
  // them undefined so plain RFI issues stay anchor-less.
  const [newModelElementGuid, setNewModelElementGuid] = useState<string | null>(null);
  const [newModelXyz, setNewModelXyz] = useState<{ x: number; y: number; z: number } | null>(null);
  const modelsLoadedForProject = useRef<string | null>(null);
  const [showMemberPicker, setShowMemberPicker] = useState(false);
  // Watchers — multi-select; each MemberPicker selection appends to the list.
  const [newWatchers, setNewWatchers] = useState<ProjectMember[]>([]);
  const [showWatcherPicker, setShowWatcherPicker] = useState(false);
  // Co-assignees — same pattern as watchers.
  const [newCoAssignees, setNewCoAssignees] = useState<ProjectMember[]>([]);
  const [showCoAssigneePicker, setShowCoAssigneePicker] = useState(false);
  const [creationStatus, setCreationStatus] = useState<string | null>(null);

  // Phase 96 — bulk action state. `bulkMode` toggles the list into multi-
  // select; taps add/remove from `bulkSelection`. `bulkBusy` disables the
  // bulk bar while an operation is in flight so users don't double-submit.
  const [bulkMode, setBulkMode] = useState(false);
  const [bulkSelection, setBulkSelection] = useState<Set<string>>(new Set());
  const [bulkAssignVisible, setBulkAssignVisible] = useState(false);
  const [bulkBusy, setBulkBusy] = useState(false);

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
    loadData(params.projectId);
  }, [loadData, params.projectId]);

  // Phase 96 — clear the "issues" badge when the user reaches the list.
  useEffect(() => {
    useNotificationStore.getState().clear('issues');
  }, []);

  // Phase 96 — once the list has loaded, honour ?issueId=X from a notification
  // tap by pushing straight to the detail screen. Guarded by a ref so the
  // redirect fires exactly once per deep-link, not on every re-render when
  // the user bounces back to the list.
  useEffect(() => {
    if (!deepLinkHandled.current && params.issueId && activeProject) {
      deepLinkHandled.current = true;
      router.push(`/issue-detail?id=${params.issueId}&projectId=${activeProject.id}`);
    }
  }, [params.issueId, activeProject]);

  // Phase 96 — scanner → "Raise Issue" pushes createForElement+elementTag.
  // Open the create modal with those values pre-filled so coordinators don't
  // have to retype the element ID they just scanned. Clears router params
  // after consumption so navigating away + back doesn't re-trigger the modal.
  useEffect(() => {
    if (!scannerLinkHandled.current && params.createForElement && activeProject) {
      scannerLinkHandled.current = true;
      setNewElementIds(params.createForElement);
      if (params.elementTag) {
        setNewTitle(`Issue with ${params.elementTag}`);
      }
      setShowCreate(true);
      // Clear the params so re-mounting the tab won't reopen the modal
      router.setParams({ createForElement: undefined, elementTag: undefined });
    }
  }, [params.createForElement, params.elementTag, activeProject]);

  // Phase 163 — viewer's onPlaceIssue ("create issue here") pushes
  // ?fromViewer=1&modelId=...&modelElementGuid=...&modelX/Y/Z=...&tag=...
  // Pre-fill the create modal with the model link + anchor coords + a sensible
  // default title so coordinators don't have to retype anything.
  useEffect(() => {
    if (viewerLinkHandled.current) return;
    if (!params.fromViewer || !params.modelId || !activeProject) return;
    viewerLinkHandled.current = true;

    setNewModelId(params.modelId);
    if (params.modelElementGuid) setNewModelElementGuid(params.modelElementGuid);

    const x = params.modelX ? Number(params.modelX) : NaN;
    const y = params.modelY ? Number(params.modelY) : NaN;
    const z = params.modelZ ? Number(params.modelZ) : NaN;
    if (Number.isFinite(x) && Number.isFinite(y) && Number.isFinite(z)) {
      setNewModelXyz({ x, y, z });
    }

    if (params.tag) setNewTitle(`Issue at ${params.tag}`);
    if (params.discipline) {
      // Discipline preselect — falls through silently if the value isn't a
      // recognised RFI/NCR/SI/etc type.
    }
    if (params.modelElementGuid) setNewElementIds(params.modelElementGuid);

    setShowCreate(true);
    router.setParams({
      fromViewer: undefined,
      modelId: undefined,
      modelElementGuid: undefined,
      modelX: undefined,
      modelY: undefined,
      modelZ: undefined,
      tag: undefined,
      category: undefined,
      discipline: undefined,
    });
  }, [
    params.fromViewer,
    params.modelId,
    params.modelElementGuid,
    params.modelX,
    params.modelY,
    params.modelZ,
    params.tag,
    params.category,
    params.discipline,
    activeProject,
  ]);

  function onRefresh() {
    setRefreshing(true);
    loadData(activeProject?.id);
  }

  const filtered = issues.filter((issue) => {
    if (priorityFilter !== 'ALL' && issue.priority !== priorityFilter) return false;
    if (statusFilter !== 'ALL' && issue.status !== statusFilter) return false;
    if (mineOnly) {
      // Phase 142 — match assignee against the current user. FK wins;
      // fall back to email then display name for issues that pre-date
      // the AssigneeUserId migration so legacy rows still surface for
      // the right person.
      const issueAny = issue as unknown as {
        assigneeUserId?: string | null;
        assigneeEmail?: string | null;
        assignee?: string | null;
      };
      const matched =
        (me.userId && issueAny.assigneeUserId === me.userId) ||
        (me.email && issueAny.assigneeEmail && issueAny.assigneeEmail.toLowerCase() === me.email.toLowerCase()) ||
        (me.displayName && issueAny.assignee && issueAny.assignee === me.displayName);
      if (!matched) return false;
    }
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
    setNewWatchers([]);
    setNewCoAssignees([]);
    setNewPhotos([]);
    setNewElementIds('');
    setNewModelId(null);
    setNewModelElementGuid(null);
    setNewModelXyz(null);
    setCreationStatus(null);
  }

  // MODEL-VIEWER — lazy-load the project's models the first time the create
  // modal opens. Failures are non-fatal: the picker silently shows "(none)"
  // only, so issue creation still works in offline / read-error scenarios.
  useEffect(() => {
    if (!showCreate || !activeProject) return;
    if (modelsLoadedForProject.current === activeProject.id) return;
    modelsLoadedForProject.current = activeProject.id;
    listModels(activeProject.id)
      .then((rows) => setAvailableModels(rows ?? []))
      .catch((err) => {
        console.warn('[issues.create] listModels failed', err);
        setAvailableModels([]);
      });
  }, [showCreate, activeProject]);

  /**
   * Phase 96 — toggle an issue in/out of the bulk selection set. Clearing the
   * last item auto-exits bulk mode so users aren't stuck in an empty mode.
   */
  function toggleBulkItem(id: string): void {
    setBulkSelection((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id); else next.add(id);
      if (next.size === 0) setBulkMode(false);
      return next;
    });
  }

  function enterBulkMode(firstId: string): void {
    setBulkMode(true);
    setBulkSelection(new Set([firstId]));
  }

  function exitBulkMode(): void {
    setBulkMode(false);
    setBulkSelection(new Set());
  }

  /**
   * Phase 96 — bulk status update. Parallelised via Promise.allSettled so
   * 50 updates don't serialise (50 × 250ms latency = 12s → 250ms total). We
   * chunk by 6 to avoid spamming the server with 50+ concurrent requests
   * which some reverse proxies rate-limit as abuse. Failures are collected
   * and surfaced as a summary alert instead of aborting halfway through.
   */
  async function bulkUpdateStatus(newStatus: BimIssue['status']): Promise<void> {
    if (!activeProject || bulkSelection.size === 0) return;
    setBulkBusy(true);
    const ids = Array.from(bulkSelection);
    const errors: string[] = [];
    try {
      const updates: Partial<BimIssue> = { status: newStatus };
      if (newStatus === 'RESOLVED') updates.resolvedAt = new Date().toISOString();
      for (let i = 0; i < ids.length; i += 6) {
        const batch = ids.slice(i, i + 6);
        const results = await Promise.allSettled(
          batch.map((id) => updateIssue(activeProject.id, id, updates)),
        );
        results.forEach((r, idx) => {
          if (r.status === 'rejected') {
            errors.push(`${batch[idx]}: ${r.reason instanceof Error ? r.reason.message : String(r.reason)}`);
          }
        });
      }
      if (errors.length > 0) {
        Alert.alert(
          'Some updates failed',
          `${ids.length - errors.length}/${ids.length} succeeded.\n\n${errors.slice(0, 3).join('\n')}${errors.length > 3 ? '\n…' : ''}`,
        );
      }
    } finally {
      setBulkBusy(false);
      exitBulkMode();
      loadData(activeProject.id);
    }
  }

  async function bulkReassign(member: ProjectMember): Promise<void> {
    if (!activeProject || bulkSelection.size === 0) return;
    setBulkAssignVisible(false);
    setBulkBusy(true);
    const ids = Array.from(bulkSelection);
    const errors: string[] = [];
    const payload = {
      assignee: member.displayName,
      assigneeEmail: member.email,
      assigneeUserId: member.userId,
    };
    try {
      for (let i = 0; i < ids.length; i += 6) {
        const batch = ids.slice(i, i + 6);
        const results = await Promise.allSettled(
          batch.map((id) => updateIssue(activeProject.id, id, payload)),
        );
        results.forEach((r, idx) => {
          if (r.status === 'rejected') {
            errors.push(`${batch[idx]}: ${r.reason instanceof Error ? r.reason.message : String(r.reason)}`);
          }
        });
      }
      if (errors.length > 0) {
        Alert.alert('Some reassignments failed', `${ids.length - errors.length}/${ids.length} succeeded.`);
      }
    } finally {
      setBulkBusy(false);
      exitBulkMode();
      loadData(activeProject.id);
    }
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
        watcherUserIds: newWatchers.length > 0 ? newWatchers.map(m => m.userId) : undefined,
        coAssigneeUserIds: newCoAssignees.length > 0 ? newCoAssignees.map(m => m.userId) : undefined,
        // Phase 96 — scanner-initiated issues carry elementIds through so the
        // server can later lookup "which elements does this issue touch".
        elementIds: newElementIds || undefined,
        // MODEL-VIEWER — link the issue to a 3D model when the user picked
        // one in the model chip row. Server-side validation rejects model
        // ids that don't belong to this project.
        modelId: newModelId ?? undefined,
        // Phase 163 — anchor coords come from the viewer's PlaceIssue
        // gesture (deep-linked into this form via ?fromViewer=1...). Manual
        // creation paths leave these undefined so plain RFI issues are
        // anchor-less.
        modelElementGuid: newModelElementGuid ?? undefined,
        modelX: newModelXyz?.x,
        modelY: newModelXyz?.y,
        modelZ: newModelXyz?.z,
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
          value={searchInput}
          onChangeText={(v) => { setSearchInput(v); debouncedSetSearch(v); }}
        />
      </View>

      {/* Priority filter chips */}
      <ScrollView horizontal showsHorizontalScrollIndicator={false} style={styles.filterBar} contentContainerStyle={styles.filterBarContent}>
        {/* Phase 142 — "Mine" toggle. Disabled when authStore has no userId
            (cold-start before /me resolves) so we never silently filter
            everything out. */}
        <TouchableOpacity
          style={[styles.filterChip, mineOnly && styles.filterChipActive]}
          onPress={() => setMineOnly((v) => !v)}
          disabled={!me.userId && !me.displayName}
          accessibilityLabel={mineOnly ? 'Show all issues' : 'Show only my issues'}
        >
          <Text style={[styles.filterChipText, mineOnly && styles.filterChipTextActive]}>👤 Mine</Text>
        </TouchableOpacity>
        {(['ALL', 'CRITICAL', 'HIGH', 'MEDIUM', 'LOW'] as PriorityFilter[]).map((p) => (
          <TouchableOpacity
            key={p}
            style={[styles.filterChip, priorityFilter === p && styles.filterChipActive]}
            onPress={() => setPriorityFilter(p)}
            accessibilityRole="button"
            accessibilityLabel={p === 'ALL' ? 'Show all priorities' : `Filter to ${p.toLowerCase()} priority`}
            accessibilityState={{ selected: priorityFilter === p }}
          >
            {p !== 'ALL' && <View style={[styles.filterDot, { backgroundColor: getPriorityColor(p) }]} />}
            <Text style={[styles.filterChipText, priorityFilter === p && styles.filterChipTextActive]}>
              {p === 'ALL' ? 'All Priorities' : p}
            </Text>
          </TouchableOpacity>
        ))}
      </ScrollView>

      {/* Phase 96 — Bulk action bar (shown only in bulk mode). */}
      {bulkMode && (
        <View style={styles.bulkBar}>
          <View style={styles.bulkBarLeft}>
            <TouchableOpacity onPress={exitBulkMode} accessibilityLabel="Exit bulk selection">
              <Text style={styles.bulkBarCancel}>✕</Text>
            </TouchableOpacity>
            <Text style={styles.bulkBarCount}>{bulkSelection.size} selected</Text>
          </View>
          <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={{ gap: 6 }}>
            <TouchableOpacity style={styles.bulkAction} disabled={bulkBusy}
              onPress={() => bulkUpdateStatus('IN_PROGRESS')}>
              <Text style={styles.bulkActionText}>→ In Progress</Text>
            </TouchableOpacity>
            <TouchableOpacity style={styles.bulkAction} disabled={bulkBusy}
              onPress={() => bulkUpdateStatus('RESOLVED')}>
              <Text style={styles.bulkActionText}>→ Resolved</Text>
            </TouchableOpacity>
            <TouchableOpacity style={styles.bulkAction} disabled={bulkBusy}
              onPress={() => bulkUpdateStatus('CLOSED')}>
              <Text style={styles.bulkActionText}>→ Closed</Text>
            </TouchableOpacity>
            <TouchableOpacity style={styles.bulkAction} disabled={bulkBusy}
              onPress={() => setBulkAssignVisible(true)}>
              <Text style={styles.bulkActionText}>Reassign…</Text>
            </TouchableOpacity>
            {bulkBusy && <ActivityIndicator color={theme.colors.accent} size="small" style={{ marginLeft: 6 }} />}
          </ScrollView>
        </View>
      )}

      {/* Issue list */}
      <FlatList
        data={filtered}
        keyExtractor={(item) => item.id}
        renderItem={({ item }) => (
          <IssueCard
            issue={item}
            bulkMode={bulkMode}
            selected={bulkSelection.has(item.id)}
            onPress={() => {
              if (bulkMode) {
                toggleBulkItem(item.id);
              } else {
                router.push(`/issue-detail?id=${item.id}&projectId=${activeProject.id}`);
              }
            }}
            onLongPress={() => { if (!bulkMode) enterBulkMode(item.id); }}
            onViewIn3D={() => openViewer(activeProject.code, item.modelId)}
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
      <TouchableOpacity
        style={styles.fab}
        onPress={() => setShowCreate(true)}
        activeOpacity={0.8}
        accessibilityRole="button"
        accessibilityLabel="Create new issue"
        accessibilityHint="Opens a form to log an RFI, NCR, or site instruction"
      >
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

            {newElementIds ? (
              <View style={styles.linkedElementChip}>
                <Text style={styles.linkedElementChipLabel}>LINKED ELEMENT</Text>
                <Text style={styles.linkedElementChipValue} numberOfLines={1}>
                  {params.elementTag || newElementIds}
                </Text>
              </View>
            ) : null}

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
            {/* T3-7 — Voice-to-text dictation. Recording is queued under
                ATTACH_AUDIO with issueId="__pending__"; an issue-create
                follow-up server PR will need to backfill the queued action
                once the new issue id is known. For now the queued action
                surfaces in the conflict-triage screen if the upload
                404s, so nothing is silently lost.
                TODO-SERVER: see endpoints.ts uploadAudioNote — receiver
                endpoint not yet in place; will 404 until S6.1 lands. */}
            {activeProject ? (
              <AudioRecorder
                projectId={activeProject.id}
                contextTag="issue-create-description"
              />
            ) : null}

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

            {/* MODEL-VIEWER — Linked model picker. Hidden when the project has
                no published models; otherwise renders a "(none)" + per-model
                chip row so coordinators can anchor an issue to a specific
                federated model at creation time. The detail screen embeds the
                3D viewer when this is set. */}
            {availableModels.length > 0 && (
              <>
                <Text style={styles.inputLabel}>Linked model</Text>
                <ScrollView horizontal showsHorizontalScrollIndicator={false} style={styles.typeRow}>
                  <TouchableOpacity
                    key="__none__"
                    style={[styles.typeChip, newModelId === null && styles.typeChipActive]}
                    onPress={() => setNewModelId(null)}
                  >
                    <Text style={[styles.typeChipText, newModelId === null && styles.typeChipTextActive]}>
                      (none)
                    </Text>
                  </TouchableOpacity>
                  {availableModels.map((m) => (
                    <TouchableOpacity
                      key={m.id}
                      style={[styles.typeChip, newModelId === m.id && styles.typeChipActive]}
                      onPress={() => setNewModelId(m.id)}
                    >
                      <Text style={[styles.typeChipText, newModelId === m.id && styles.typeChipTextActive]} numberOfLines={1}>
                        {m.name || m.fileName || m.id.slice(0, 8)}
                      </Text>
                    </TouchableOpacity>
                  ))}
                </ScrollView>
              </>
            )}

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

            {/* Watchers — multi-select, add one at a time */}
            <Text style={styles.inputLabel}>Watchers ({newWatchers.length})</Text>
            <TouchableOpacity
              style={[styles.modalInput, { paddingVertical: 10, minHeight: 40 }]}
              onPress={() => setShowWatcherPicker(true)}
              accessibilityRole="button"
              accessibilityLabel="Add a watcher"
            >
              <Text style={{ fontSize: theme.fontSize.sm, color: theme.colors.disabled }}>
                + Add watcher…
              </Text>
            </TouchableOpacity>
            {newWatchers.length > 0 && (
              <ScrollView horizontal showsHorizontalScrollIndicator={false} style={{ marginTop: 6 }}>
                {newWatchers.map((m) => (
                  <TouchableOpacity
                    key={m.userId}
                    onPress={() => setNewWatchers(prev => prev.filter(w => w.userId !== m.userId))}
                    style={styles.assigneeChip}
                    accessibilityLabel={`Remove watcher ${m.displayName}`}
                  >
                    <Text style={styles.assigneeChipText}>{m.displayName} ✕</Text>
                  </TouchableOpacity>
                ))}
              </ScrollView>
            )}

            {/* Co-assignees — multi-select */}
            <Text style={styles.inputLabel}>Co-Assignees ({newCoAssignees.length})</Text>
            <TouchableOpacity
              style={[styles.modalInput, { paddingVertical: 10, minHeight: 40 }]}
              onPress={() => setShowCoAssigneePicker(true)}
              accessibilityRole="button"
              accessibilityLabel="Add a co-assignee"
            >
              <Text style={{ fontSize: theme.fontSize.sm, color: theme.colors.disabled }}>
                + Add co-assignee…
              </Text>
            </TouchableOpacity>
            {newCoAssignees.length > 0 && (
              <ScrollView horizontal showsHorizontalScrollIndicator={false} style={{ marginTop: 6 }}>
                {newCoAssignees.map((m) => (
                  <TouchableOpacity
                    key={m.userId}
                    onPress={() => setNewCoAssignees(prev => prev.filter(c => c.userId !== m.userId))}
                    style={[styles.assigneeChip, { backgroundColor: theme.colors.warning + '20', borderColor: theme.colors.warning }]}
                    accessibilityLabel={`Remove co-assignee ${m.displayName}`}
                  >
                    <Text style={[styles.assigneeChipText, { color: theme.colors.warning }]}>{m.displayName} ✕</Text>
                  </TouchableOpacity>
                ))}
              </ScrollView>
            )}

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

      {/* Phase 96 — bulk reassign member picker (reuses same component) */}
      {activeProject && (
        <MemberPicker
          visible={bulkAssignVisible}
          projectId={activeProject.id}
          onSelect={bulkReassign}
          onClose={() => setBulkAssignVisible(false)}
        />
      )}

      {/* Watcher multi-picker — each selection appends to the list */}
      {activeProject && (
        <MemberPicker
          visible={showWatcherPicker}
          projectId={activeProject.id}
          onSelect={(member) => {
            setNewWatchers(prev =>
              prev.some(w => w.userId === member.userId) ? prev : [...prev, member]
            );
            setShowWatcherPicker(false);
          }}
          onClose={() => setShowWatcherPicker(false)}
        />
      )}

      {/* Co-assignee multi-picker */}
      {activeProject && (
        <MemberPicker
          visible={showCoAssigneePicker}
          projectId={activeProject.id}
          onSelect={(member) => {
            setNewCoAssignees(prev =>
              prev.some(c => c.userId === member.userId) ? prev : [...prev, member]
            );
            setShowCoAssigneePicker(false);
          }}
          onClose={() => setShowCoAssigneePicker(false)}
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
  onLongPress,
  onViewIn3D,
  bulkMode = false,
  selected = false,
}: {
  issue: BimIssue;
  onPress: () => void;
  onLongPress?: () => void;
  onViewIn3D: () => void;
  bulkMode?: boolean;
  selected?: boolean;
}) {
  const priorityColor = getPriorityColor(issue.priority);
  // NEW-INFO-02 — Prefer the server's IsOverdue flag when present, fall back
  // to the local 7-day heuristic for legacy responses.
  const isOverdue = issue.isOverdue ?? (
    issue.status === 'OPEN' && !!issue.dueDate && new Date(issue.dueDate) < new Date()
  ) ?? (issue.status === 'OPEN' && daysSince(issue.createdAt) > 7);
  const hasPhotos = (issue.attachmentCount ?? 0) > 0;
  // Parse watcher count from either a pre-parsed array or a JSON-encoded string.
  const watcherCount = (() => {
    const raw = issue.watcherUserIds;
    if (!raw) return 0;
    if (Array.isArray(raw)) return raw.filter(Boolean).length;
    try { const arr = JSON.parse(raw as string); return Array.isArray(arr) ? arr.filter(Boolean).length : 0; }
    catch { return 0; }
  })();

  return (
    <TouchableOpacity
      style={[styles.issueCard, selected && styles.issueCardSelected]}
      onPress={onPress}
      onLongPress={onLongPress}
      delayLongPress={350}
      activeOpacity={0.7}
    >
      <View style={[styles.issueCardLeft, { backgroundColor: priorityColor }]} />
      <View style={styles.issueCardBody}>
        <View style={styles.issueCardTopRow}>
          {bulkMode && (
            <View style={[styles.bulkCheck, selected && styles.bulkCheckOn]}>
              <Text style={styles.bulkCheckText}>{selected ? '✓' : ''}</Text>
            </View>
          )}
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
            {watcherCount > 0 ? (
              <View style={[styles.typeBadge, { backgroundColor: '#e8f5e9' }]}>
                <Text style={[styles.typeBadgeText, { color: '#2E7D32' }]}>👁 {watcherCount}</Text>
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

  // Phase 96 — bulk action bar + multi-select checkbox
  bulkBar: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: theme.spacing.sm,
    paddingHorizontal: theme.spacing.md,
    paddingVertical: theme.spacing.sm,
    backgroundColor: theme.colors.primary,
    borderBottomWidth: 1,
    borderBottomColor: theme.colors.border,
  },
  bulkBarLeft: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: theme.spacing.sm,
    marginRight: theme.spacing.sm,
  },
  bulkBarCancel: {
    color: '#fff',
    fontSize: 22,
    fontWeight: '700',
    paddingHorizontal: 6,
  },
  bulkBarCount: {
    color: '#fff',
    fontSize: theme.fontSize.sm,
    fontWeight: '700',
  },
  bulkAction: {
    paddingHorizontal: theme.spacing.md,
    paddingVertical: 6,
    borderRadius: theme.borderRadius.md,
    backgroundColor: theme.colors.accent,
  },
  bulkActionText: {
    color: '#fff',
    fontWeight: '700',
    fontSize: theme.fontSize.xs,
  },
  issueCardSelected: {
    borderWidth: 2,
    borderColor: theme.colors.accent,
  },
  bulkCheck: {
    width: 20,
    height: 20,
    borderRadius: 4,
    borderWidth: 2,
    borderColor: theme.colors.disabled,
    marginRight: theme.spacing.sm,
    alignItems: 'center',
    justifyContent: 'center',
  },
  bulkCheckOn: {
    backgroundColor: theme.colors.accent,
    borderColor: theme.colors.accent,
  },
  bulkCheckText: {
    color: '#fff',
    fontWeight: '800',
    fontSize: 14,
  },

  // Phase 96 — pre-filled linked element chip in the create modal
  linkedElementChip: {
    backgroundColor: theme.colors.accent + '18',
    borderRadius: theme.borderRadius.md,
    borderWidth: 1,
    borderColor: theme.colors.accent,
    paddingHorizontal: theme.spacing.md,
    paddingVertical: theme.spacing.sm,
    marginBottom: theme.spacing.sm,
  },
  linkedElementChipLabel: {
    fontSize: 10,
    fontWeight: '700',
    color: theme.colors.accent,
    letterSpacing: 0.5,
  },
  linkedElementChipValue: {
    fontSize: theme.fontSize.sm,
    fontFamily: Platform.OS === 'ios' ? 'Menlo' : 'monospace',
    color: theme.colors.text,
    marginTop: 2,
  },

  // Watcher / co-assignee chip
  assigneeChip: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: theme.colors.primary + '15',
    borderWidth: 1,
    borderColor: theme.colors.primary,
    borderRadius: theme.borderRadius.lg,
    paddingHorizontal: theme.spacing.sm,
    paddingVertical: 4,
    marginRight: 6,
  },
  assigneeChipText: {
    fontSize: theme.fontSize.xs,
    fontWeight: '600',
    color: theme.colors.primary,
  },

  // Phase 94 — legacy "detail modal" styles removed. Issue detail now lives
  // in app/(tabs)/issue-detail.tsx and uses its own stylesheet.
});
