import { useState, useEffect, useCallback, useMemo } from 'react';
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
  Alert,
} from 'react-native';
import { theme, getCDEColor } from '@/utils/theme';
import { listProjects, listDocuments, transitionCDE, requestDocumentApproval, decideDocumentApproval, getMyProjectAccess, type MyProjectAccess } from '@/api/endpoints';
import type { DocumentRecord, Project, CDEStatus } from '@/types/api';
import { crashReporter } from '@/services/crashReporter';
import { useAuthStore } from '@/stores/authStore';

const CDE_STATES: CDEStatus[] = ['WIP', 'SHARED', 'PUBLISHED', 'ARCHIVE'];

const VALID_TRANSITIONS: Record<CDEStatus, CDEStatus[]> = {
  WIP: ['SHARED'],
  SHARED: ['WIP', 'PUBLISHED'],
  PUBLISHED: ['ARCHIVE'],
  ARCHIVE: [],
};

/**
 * Phase 96 — ISO 19650-2 §5.6 approval gates. Transitions into SHARED and
 * PUBLISHED state require Task Information Manager / BIM Coordinator sign-off
 * before the document is released on the CDE. Mobile routes these through
 * the approval workflow endpoints instead of directly calling transitionCDE.
 */
const TRANSITIONS_REQUIRING_APPROVAL = new Set<string>([
  'WIP->SHARED',
  'SHARED->PUBLISHED',
]);

function requiresApproval(from: CDEStatus, to: CDEStatus): boolean {
  return TRANSITIONS_REQUIRING_APPROVAL.has(`${from}->${to}`);
}

const SUITABILITY_LABELS: Record<string, string> = {
  S0: 'Initial / WIP',
  S1: 'For Coordination',
  S2: 'For Information',
  S3: 'For Review & Comment',
  S4: 'For Stage Approval',
  S5: 'For Manufacture',
  S6: 'For PIM Authorization',
  S7: 'For AIM Authorization',
};

type CDEFilter = 'ALL' | CDEStatus;

export default function DocumentsScreen() {
  const [projects, setProjects] = useState<Project[]>([]);
  const [activeProject, setActiveProject] = useState<Project | null>(null);
  const [documents, setDocuments] = useState<DocumentRecord[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [cdeFilter, setCdeFilter] = useState<CDEFilter>('ALL');
  const [selectedDoc, setSelectedDoc] = useState<DocumentRecord | null>(null);
  const [transitioning, setTransitioning] = useState(false);
  // Phase 177 — per-folder ACL slice for the active user; null = unloaded.
  const [acl, setAcl] = useState<MyProjectAccess | null>(null);

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
      // Phase 177 — fetch ACL slice in parallel with docs so the chip filter
      // can hide CDE states the user has no access to. Falls back to a
      // bypass slice on error so the screen never breaks on a server hiccup.
      const [docs, aclSlice] = await Promise.all([
        listDocuments(target.id),
        getMyProjectAccess(target.id).catch(() => null),
      ]);
      setDocuments(docs);
      setAcl(aclSlice);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to load documents';
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

  const cdeCounts = useMemo(() => {
    const counts: Record<string, number> = { ALL: documents.length };
    for (const s of CDE_STATES) counts[s] = 0;
    for (const d of documents) {
      if (counts[d.cdeStatus] !== undefined) counts[d.cdeStatus]++;
    }
    return counts;
  }, [documents]);

  const filtered = useMemo(() => {
    let list = documents;
    if (cdeFilter !== 'ALL') {
      list = list.filter((d) => d.cdeStatus === cdeFilter);
    }
    if (search.trim()) {
      const q = search.toLowerCase();
      list = list.filter(
        (d) =>
          d.fileName.toLowerCase().includes(q) ||
          d.documentType.toLowerCase().includes(q) ||
          d.description.toLowerCase().includes(q) ||
          d.originator.toLowerCase().includes(q) ||
          d.revision.toLowerCase().includes(q),
      );
    }
    return list;
  }, [documents, cdeFilter, search]);

  /**
   * Phase 96 — CDE transition with ISO 19650 approval routing.
   *
   * Gated transitions (WIP→SHARED, SHARED→PUBLISHED) route through the
   * approval workflow endpoint, creating a pending approval record that
   * the designated approver (C/K role) must sign off. Non-gated transitions
   * (e.g. SHARED→WIP rework, PUBLISHED→ARCHIVE retention) call transitionCDE
   * directly because they don't release new information to the CDE.
   */
  async function handleTransition(doc: DocumentRecord, newStatus: CDEStatus) {
    if (!activeProject) return;
    setTransitioning(true);
    try {
      if (requiresApproval(doc.cdeStatus, newStatus)) {
        // Fire the approval request — does NOT actually move the CDE state;
        // the approver's decideDocumentApproval call does that server-side.
        await requestDocumentApproval(activeProject.id, doc.id, newStatus);
        Alert.alert(
          'Approval requested',
          `CDE transition to ${newStatus} submitted for approval per ISO 19650-2 §5.6. You will be notified when it is approved or rejected.`,
        );
        // Refresh so the "approval pending" badge appears if the server renders it
        await loadData(activeProject.id);
      } else {
        const updated = await transitionCDE(activeProject.id, doc.id, newStatus);
        setDocuments((prev) => prev.map((d) => (d.id === updated.id ? updated : d)));
        setSelectedDoc(updated);
      }
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Transition failed';
      // 403 on a gated transition means the user tried to bypass approval
      if (msg.includes('HTTP 403')) {
        Alert.alert(
          'Approval required',
          'This transition needs BIM Coordinator sign-off. The request has been sent — check back when it is approved.',
        );
      } else {
        Alert.alert('CDE Transition Failed', msg);
      }
    } finally {
      setTransitioning(false);
    }
  }

  /**
   * Approver path — called when an approver receives a push notification or
   * opens the documents list and sees pending approvals. The `approvalId`
   * comes from the notification payload or the (separate, future) approvals
   * inbox. Here we expose it via a confirm dialog at document-detail level
   * so a coordinator who just pulled-to-refresh the list and sees their own
   * pending approval can sign off without leaving the screen.
   */
  async function handleApprovalDecision(
    doc: DocumentRecord,
    approvalId: string,
    decision: 'APPROVED' | 'REJECTED',
    comment?: string,
  ) {
    if (!activeProject) return;
    setTransitioning(true);
    try {
      await decideDocumentApproval(activeProject.id, doc.id, approvalId, decision, comment);
      await loadData(activeProject.id);
      Alert.alert(
        decision === 'APPROVED' ? 'Approved' : 'Rejected',
        decision === 'APPROVED'
          ? 'Document has been moved to the next CDE state.'
          : 'Document remains at current state. Originator has been notified.',
      );
    } catch (err) {
      Alert.alert('Decision failed', err instanceof Error ? err.message : String(err));
    } finally {
      setTransitioning(false);
    }
  }

  if (loading) {
    return (
      <View style={styles.center}>
        <ActivityIndicator size="large" color={theme.colors.accent} />
        <Text style={styles.loadingText}>Loading documents...</Text>
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
        <Text style={styles.emptyTitle}>No Projects</Text>
        <Text style={styles.emptyText}>Create a project in the Planscape web portal.</Text>
      </View>
    );
  }

  return (
    <View style={styles.root}>
      {/* Project selector */}
      {projects.length > 1 && (
        <FlatList
          horizontal
          showsHorizontalScrollIndicator={false}
          data={projects}
          keyExtractor={(p) => p.id}
          style={styles.projectBar}
          contentContainerStyle={styles.projectBarContent}
          renderItem={({ item: p }) => (
            <TouchableOpacity
              style={[styles.projectChip, p.id === activeProject.id && styles.projectChipActive]}
              onPress={() => { setLoading(true); loadData(p.id); }}
            >
              <Text style={[styles.projectChipText, p.id === activeProject.id && styles.projectChipTextActive]}>
                {p.code || p.name}
              </Text>
            </TouchableOpacity>
          )}
        />
      )}

      {/* CDE status filter strip — Phase 177 hides chips the user can't access */}
      <FlatList
        horizontal
        showsHorizontalScrollIndicator={false}
        data={(['ALL', ...CDE_STATES] as CDEFilter[]).filter((s) => {
          if (s === 'ALL') return true;
          if (!acl || acl.bypassesAcl) return true;
          if (acl.allowedCdeStates.length === 0) return true; // null = all
          return acl.allowedCdeStates.includes(s);
        })}
        keyExtractor={(s) => s}
        style={styles.filterStrip}
        contentContainerStyle={styles.filterStripContent}
        renderItem={({ item: status }) => {
          const isActive = cdeFilter === status;
          const color = status === 'ALL' ? theme.colors.text : getCDEColor(status);
          return (
            <TouchableOpacity
              style={[
                styles.cdeChip,
                isActive && { backgroundColor: color, borderColor: color },
              ]}
              onPress={() => setCdeFilter(status)}
            >
              {status !== 'ALL' && (
                <View style={[styles.cdeDot, { backgroundColor: isActive ? '#FFF' : color }]} />
              )}
              <Text style={[styles.cdeChipText, isActive && styles.cdeChipTextActive]}>
                {status}
              </Text>
              <Text style={[styles.cdeChipCount, isActive && styles.cdeChipCountActive]}>
                {cdeCounts[status] ?? 0}
              </Text>
            </TouchableOpacity>
          );
        }}
      />

      {/* Search */}
      <View style={styles.searchRow}>
        <TextInput
          style={styles.searchInput}
          placeholder="Search documents..."
          placeholderTextColor={theme.colors.disabled}
          value={search}
          onChangeText={setSearch}
          autoCapitalize="none"
        />
        {search.length > 0 && (
          <TouchableOpacity onPress={() => setSearch('')} style={styles.clearBtn}>
            <Text style={styles.clearBtnText}>X</Text>
          </TouchableOpacity>
        )}
      </View>

      {/* Document list */}
      <FlatList
        data={filtered}
        keyExtractor={(d) => d.id}
        refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={theme.colors.accent} />}
        contentContainerStyle={filtered.length === 0 ? styles.emptyList : styles.listContent}
        ListEmptyComponent={
          <View style={styles.center}>
            <Text style={styles.emptyTitle}>No Documents</Text>
            <Text style={styles.emptyText}>
              {search || cdeFilter !== 'ALL' ? 'No documents match your filters.' : 'No documents in this project yet.'}
            </Text>
          </View>
        }
        renderItem={({ item: doc }) => (
          <DocumentCard doc={doc} onPress={() => setSelectedDoc(doc)} />
        )}
      />

      {/* Detail modal */}
      {selectedDoc && (
        <DocumentDetailModal
          doc={selectedDoc}
          transitioning={transitioning}
          onTransition={handleTransition}
          onClose={() => setSelectedDoc(null)}
        />
      )}
    </View>
  );
}

/* ── Document Card ── */

function DocumentCard({ doc, onPress }: { doc: DocumentRecord; onPress: () => void }) {
  const cdeColor = getCDEColor(doc.cdeStatus);
  return (
    <TouchableOpacity style={styles.card} onPress={onPress} activeOpacity={0.7}>
      <View style={[styles.cardCdeBadge, { backgroundColor: cdeColor }]}>
        <Text style={styles.cardCdeBadgeText}>{doc.cdeStatus}</Text>
      </View>
      <View style={styles.cardBody}>
        <Text style={styles.cardFileName} numberOfLines={1}>{doc.fileName}</Text>
        <View style={styles.cardMeta}>
          {doc.documentType ? (
            <View style={styles.typeBadge}>
              <Text style={styles.typeBadgeText}>{doc.documentType}</Text>
            </View>
          ) : null}
          {doc.suitabilityCode ? (
            <Text style={styles.cardSuitability}>{doc.suitabilityCode}</Text>
          ) : null}
          {doc.revision ? (
            <Text style={styles.cardRevision}>Rev {doc.revision}</Text>
          ) : null}
        </View>
        {doc.description ? (
          <Text style={styles.cardDescription} numberOfLines={2}>{doc.description}</Text>
        ) : null}
        <View style={styles.cardFooter}>
          <Text style={styles.cardOriginator}>{doc.originator || 'Unknown'}</Text>
          <Text style={styles.cardDate}>{formatDate(doc.updatedAt || doc.createdAt)}</Text>
        </View>
      </View>
    </TouchableOpacity>
  );
}

/* ── Detail Modal ── */

function DocumentDetailModal({
  doc,
  transitioning,
  onTransition,
  onClose,
}: {
  doc: DocumentRecord;
  transitioning: boolean;
  onTransition: (doc: DocumentRecord, status: CDEStatus) => void;
  onClose: () => void;
}) {
  const cdeColor = getCDEColor(doc.cdeStatus);
  const validNext = VALID_TRANSITIONS[doc.cdeStatus] ?? [];
  const suitLabel = doc.suitabilityCode ? SUITABILITY_LABELS[doc.suitabilityCode] : null;

  return (
    <Modal visible animationType="slide" transparent>
      <View style={styles.modalOverlay}>
        <View style={styles.modalContent}>
          {/* Header */}
          <View style={styles.modalHeader}>
            <View style={[styles.modalCdeBadge, { backgroundColor: cdeColor }]}>
              <Text style={styles.modalCdeBadgeText}>{doc.cdeStatus}</Text>
            </View>
            <TouchableOpacity onPress={onClose} style={styles.modalClose}>
              <Text style={styles.modalCloseText}>X</Text>
            </TouchableOpacity>
          </View>

          {/* File name */}
          <Text style={styles.modalFileName}>{doc.fileName}</Text>

          {/* Description */}
          {doc.description ? (
            <Text style={styles.modalDescription}>{doc.description}</Text>
          ) : null}

          {/* Detail grid */}
          <View style={styles.detailGrid}>
            <DetailField label="Document Type" value={doc.documentType || '—'} />
            <DetailField label="Suitability" value={suitLabel ? `${doc.suitabilityCode} — ${suitLabel}` : (doc.suitabilityCode || '—')} />
            <DetailField label="Revision" value={doc.revision || '—'} />
            <DetailField label="Originator" value={doc.originator || '—'} />
            <DetailField label="Created" value={formatDate(doc.createdAt)} />
            <DetailField label="Updated" value={formatDate(doc.updatedAt)} />
          </View>

          {/* CDE State Machine — Transition Buttons */}
          {validNext.length > 0 && (
            <View style={styles.transitionSection}>
              <Text style={styles.transitionTitle}>CDE Transition</Text>
              <View style={styles.transitionRow}>
                {validNext.map((next) => {
                  const nextColor = getCDEColor(next);
                  const gated = requiresApproval(doc.cdeStatus, next);
                  return (
                    <TouchableOpacity
                      key={next}
                      style={[styles.transitionBtn, { backgroundColor: nextColor }]}
                      onPress={() => onTransition(doc, next)}
                      disabled={transitioning}
                    >
                      {transitioning ? (
                        <ActivityIndicator size="small" color="#FFF" />
                      ) : (
                        <Text style={styles.transitionBtnText}>
                          {gated ? `Request approval → ${next}` : `Move to ${next}`}
                        </Text>
                      )}
                    </TouchableOpacity>
                  );
                })}
              </View>
              <Text style={styles.transitionHint}>
                {doc.cdeStatus} {'\u2192'} {validNext.join(' / ')}
              </Text>
            </View>
          )}

          {validNext.length === 0 && (
            <View style={styles.transitionSection}>
              <Text style={styles.transitionHint}>This document is in its final CDE state.</Text>
            </View>
          )}
        </View>
      </View>
    </Modal>
  );
}

/* ── Helpers ── */

function DetailField({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.detailField}>
      <Text style={styles.detailLabel}>{label}</Text>
      <Text style={styles.detailValue}>{value}</Text>
    </View>
  );
}

function formatDate(iso: string | undefined): string {
  if (!iso) return '—';
  try {
    const d = new Date(iso);
    return d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  } catch (e) { crashReporter.warn('documents.tsx:407', { e: String(e) });
    return iso;
  }
}

/* ── Styles ── */

const styles = StyleSheet.create({
  root: {
    flex: 1,
    backgroundColor: theme.colors.background,
  },
  center: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    padding: theme.spacing.lg,
    backgroundColor: theme.colors.background,
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
    color: '#FFF',
    fontSize: theme.fontSize.md,
    fontWeight: '600',
  },
  emptyTitle: {
    fontSize: theme.fontSize.xl,
    fontWeight: '600',
    color: theme.colors.text,
    marginBottom: theme.spacing.xs,
  },
  emptyText: {
    fontSize: theme.fontSize.md,
    color: theme.colors.textSecondary,
    textAlign: 'center',
  },

  // Project bar
  projectBar: { flexGrow: 0 },
  projectBarContent: { paddingHorizontal: theme.spacing.md, paddingTop: theme.spacing.md },
  projectChip: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.lg,
    paddingHorizontal: theme.spacing.md,
    paddingVertical: theme.spacing.xs + 2,
    marginRight: theme.spacing.sm,
    borderWidth: 1,
    borderColor: theme.colors.border,
  },
  projectChipActive: {
    backgroundColor: theme.colors.primary,
    borderColor: theme.colors.primary,
  },
  projectChipText: {
    fontSize: theme.fontSize.sm,
    fontWeight: '600',
    color: theme.colors.text,
  },
  projectChipTextActive: { color: '#FFF' },

  // CDE filter strip
  filterStrip: { flexGrow: 0 },
  filterStripContent: { paddingHorizontal: theme.spacing.md, paddingVertical: theme.spacing.sm },
  cdeChip: {
    flexDirection: 'row',
    alignItems: 'center',
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.lg,
    paddingHorizontal: theme.spacing.sm + 4,
    paddingVertical: theme.spacing.xs + 2,
    marginRight: theme.spacing.sm,
    borderWidth: 1,
    borderColor: theme.colors.border,
  },
  cdeDot: {
    width: 8,
    height: 8,
    borderRadius: 4,
    marginRight: 6,
  },
  cdeChipText: {
    fontSize: theme.fontSize.sm,
    fontWeight: '600',
    color: theme.colors.text,
  },
  cdeChipTextActive: { color: '#FFF' },
  cdeChipCount: {
    fontSize: theme.fontSize.xs,
    fontWeight: '700',
    color: theme.colors.textSecondary,
    marginLeft: 6,
  },
  cdeChipCountActive: { color: 'rgba(255,255,255,0.8)' },

  // Search
  searchRow: {
    flexDirection: 'row',
    alignItems: 'center',
    marginHorizontal: theme.spacing.md,
    marginBottom: theme.spacing.sm,
  },
  searchInput: {
    flex: 1,
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    borderWidth: 1,
    borderColor: theme.colors.border,
    paddingHorizontal: theme.spacing.md,
    paddingVertical: theme.spacing.sm,
    fontSize: theme.fontSize.md,
    color: theme.colors.text,
  },
  clearBtn: {
    marginLeft: theme.spacing.sm,
    width: 32,
    height: 32,
    borderRadius: 16,
    backgroundColor: theme.colors.border,
    alignItems: 'center',
    justifyContent: 'center',
  },
  clearBtnText: {
    fontSize: theme.fontSize.sm,
    fontWeight: '700',
    color: theme.colors.textSecondary,
  },

  // List
  listContent: {
    paddingHorizontal: theme.spacing.md,
    paddingBottom: theme.spacing.xl,
  },
  emptyList: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    padding: theme.spacing.lg,
  },

  // Document card
  card: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.lg,
    marginBottom: theme.spacing.sm,
    overflow: 'hidden',
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.06,
    shadowRadius: 4,
    elevation: 2,
  },
  cardCdeBadge: {
    paddingVertical: 3,
    paddingHorizontal: theme.spacing.sm,
    alignSelf: 'flex-start',
    borderBottomRightRadius: theme.borderRadius.sm,
  },
  cardCdeBadgeText: {
    fontSize: theme.fontSize.xs,
    fontWeight: '700',
    color: '#FFF',
    letterSpacing: 0.5,
  },
  cardBody: {
    padding: theme.spacing.md,
    paddingTop: theme.spacing.sm,
  },
  cardFileName: {
    fontSize: theme.fontSize.md,
    fontWeight: '600',
    color: theme.colors.text,
    marginBottom: theme.spacing.xs,
  },
  cardMeta: {
    flexDirection: 'row',
    alignItems: 'center',
    flexWrap: 'wrap',
    gap: 6,
    marginBottom: theme.spacing.xs,
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
  cardSuitability: {
    fontSize: theme.fontSize.xs,
    fontWeight: '600',
    color: theme.colors.accent,
  },
  cardRevision: {
    fontSize: theme.fontSize.xs,
    fontWeight: '600',
    color: theme.colors.textSecondary,
  },
  cardDescription: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.textSecondary,
    marginBottom: theme.spacing.xs,
  },
  cardFooter: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  cardOriginator: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
  },
  cardDate: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
  },

  // Modal
  modalOverlay: {
    flex: 1,
    backgroundColor: 'rgba(0,0,0,0.5)',
    justifyContent: 'flex-end',
  },
  modalContent: {
    backgroundColor: theme.colors.surface,
    borderTopLeftRadius: theme.borderRadius.xl,
    borderTopRightRadius: theme.borderRadius.xl,
    padding: theme.spacing.lg,
    maxHeight: '80%',
  },
  modalHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: theme.spacing.md,
  },
  modalCdeBadge: {
    paddingVertical: 4,
    paddingHorizontal: theme.spacing.md,
    borderRadius: theme.borderRadius.sm,
  },
  modalCdeBadgeText: {
    fontSize: theme.fontSize.sm,
    fontWeight: '700',
    color: '#FFF',
    letterSpacing: 0.5,
  },
  modalClose: {
    width: 32,
    height: 32,
    borderRadius: 16,
    backgroundColor: theme.colors.border,
    alignItems: 'center',
    justifyContent: 'center',
  },
  modalCloseText: {
    fontSize: theme.fontSize.md,
    fontWeight: '700',
    color: theme.colors.textSecondary,
  },
  modalFileName: {
    fontSize: theme.fontSize.xl,
    fontWeight: '700',
    color: theme.colors.text,
    marginBottom: theme.spacing.sm,
  },
  modalDescription: {
    fontSize: theme.fontSize.md,
    color: theme.colors.textSecondary,
    marginBottom: theme.spacing.md,
  },

  // Detail grid
  detailGrid: {
    marginBottom: theme.spacing.md,
  },
  detailField: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'flex-start',
    paddingVertical: theme.spacing.sm,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: theme.colors.border,
  },
  detailLabel: {
    fontSize: theme.fontSize.sm,
    fontWeight: '600',
    color: theme.colors.textSecondary,
    flex: 1,
  },
  detailValue: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.text,
    flex: 2,
    textAlign: 'right',
  },

  // Transition section
  transitionSection: {
    marginTop: theme.spacing.sm,
    paddingTop: theme.spacing.md,
    borderTopWidth: 1,
    borderTopColor: theme.colors.border,
  },
  transitionTitle: {
    fontSize: theme.fontSize.md,
    fontWeight: '700',
    color: theme.colors.text,
    marginBottom: theme.spacing.sm,
  },
  transitionRow: {
    flexDirection: 'row',
    gap: theme.spacing.sm,
    marginBottom: theme.spacing.xs,
  },
  transitionBtn: {
    flex: 1,
    borderRadius: theme.borderRadius.md,
    paddingVertical: theme.spacing.sm + 2,
    alignItems: 'center',
  },
  transitionBtnText: {
    fontSize: theme.fontSize.sm,
    fontWeight: '700',
    color: '#FFF',
  },
  transitionHint: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
    textAlign: 'center',
    marginTop: theme.spacing.xs,
  },
});
