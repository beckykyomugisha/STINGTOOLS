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
import { router } from 'expo-router';
import { theme, getCDEColor } from '@/utils/theme';
import { listProjects, listDocuments, transitionCDE, requestDocumentApproval, decideDocumentApproval, getMyProjectAccess, type MyProjectAccess, type ListDocumentsFilters } from '@/api/endpoints';
import type { DocumentRecord, Project, CDEStatus } from '@/types/api';
import { crashReporter } from '@/services/crashReporter';
import { describeTransitionFailure } from '@/utils/cdeTransitionMessage';
import { useAuthStore } from '@/stores/authStore';

const CDE_STATES: CDEStatus[] = ['WIP', 'SHARED', 'PUBLISHED', 'ARCHIVE'];

/**
 * #633 — the CDE state machine is SERVED now (doc.allowedTransitions), not
 * re-derived here. Two local tables used to hold a copy of it and both had
 * drifted, in opposite directions:
 *
 *   VALID_TRANSITIONS said PUBLISHED -> [ARCHIVE]. The server also allows
 *   SUPERSEDED and WITHDRAWN, and SHARED -> WITHDRAWN. Three legal moves the
 *   user simply could not see.
 *
 *   TRANSITIONS_REQUIRING_APPROVAL said {WIP->SHARED, SHARED->PUBLISHED}. The
 *   server says {SHARED->PUBLISHED, PUBLISHED->SUPERSEDED}. So WIP->SHARED was
 *   sent through an approval workflow the server does not require, and
 *   PUBLISHED->SUPERSEDED went straight at the transition endpoint, which
 *   refuses it for want of an approval record.
 *
 * Both tables are gone. What remains is the FALLBACK for a server that does
 * not send the field yet — see transitionsFor() below.
 */
const LEGACY_FALLBACK_TRANSITIONS: Record<string, CDEStatus[]> = {
  WIP: ['SHARED'],
  SHARED: ['WIP', 'PUBLISHED'],
  PUBLISHED: ['ARCHIVE'],
  ARCHIVE: [],
};

/**
 * The document's transitions, and where they came from.
 *
 * THREE STATES, NOT TWO — the same contract capabilities.ts uses (#634).
 *
 *   served  the server computed them; trust them completely, including
 *           requiresApproval, which decides WHICH ENDPOINT the button calls
 *   legacy  the field is absent — an older server. UNKNOWN, not "none".
 *           Offer the old local set so the screen still works, but treat
 *           approval as unknown and let the server answer: it enforces the
 *           gate either way and says so when it refuses.
 *
 * An empty served array is NOT the legacy case. It means the state machine was
 * consulted and this is a terminal state (ARCHIVE, SUPERSEDED, WITHDRAWN) —
 * a real answer, and the screen renders "no further transitions" for it.
 */
function transitionsFor(doc: DocumentRecord):
  { source: 'served' | 'legacy'; options: { to: string; requiresApproval: boolean | null }[] } {
  if (Array.isArray(doc.allowedTransitions)) {
    return {
      source: 'served',
      options: doc.allowedTransitions.map((t) => ({
        to: t.to,
        requiresApproval: t.requiresApproval,
      })),
    };
  }
  return {
    source: 'legacy',
    options: (LEGACY_FALLBACK_TRANSITIONS[doc.cdeStatus] ?? []).map((to) => ({
      to,
      // null = we do not know. NOT false — claiming "no approval needed" would
      // send PUBLISHED->SUPERSEDED at the wrong endpoint, which is exactly the
      // bug the old hardcoded table caused.
      requiresApproval: null,
    })),
  };
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

  const loadData = useCallback(async (projectId?: string, docFilters?: ListDocumentsFilters) => {
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
        listDocuments(target.id, docFilters),
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
    // FIX-15: pass the current search query to the server on refresh too.
    loadData(activeProject?.id, search.trim() ? { search: search.trim() } : undefined);
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
  /**
   * `needsApproval` comes from the server (doc.allowedTransitions) — or is null
   * when this server does not send the field yet. Null takes the DIRECT path
   * deliberately: the server enforces the approval gate itself and refuses with
   * a reason, so an unknown becomes a message the user can act on. Guessing
   * "yes" instead would file an approval request for a transition that never
   * needed one, and the user would wait for a decision nobody was asked to make.
   */
  async function handleTransition(
    doc: DocumentRecord, newStatus: CDEStatus, needsApproval: boolean | null,
  ) {
    if (!activeProject) return;
    setTransitioning(true);

    // #624 — the two branches below are separate outcomes and must report
    // separately. They used to share one `catch` whose 403 arm said "The
    // request has been sent", which was untrue on BOTH paths: on the direct
    // path no approval request was ever attempted, and on the approval path
    // the request is precisely what was refused. Either way nothing is
    // pending, and the user was told to wait for an approval that would never
    // arrive. Keep the attempts — and the messages — apart.
    try {
      if (needsApproval === true) {
        // Fire the approval request — does NOT actually move the CDE state;
        // the approver's decideDocumentApproval call does that server-side.
        try {
          await requestDocumentApproval(activeProject.id, doc.id, newStatus);
        } catch (err: unknown) {
          const failure = describeTransitionFailure(err, 'approval-request');
          Alert.alert(failure.title, failure.body);
          return;
        }
        Alert.alert(
          'Approval requested',
          `CDE transition to ${newStatus} submitted for approval per ISO 19650-2 §5.6. You will be notified when it is approved or rejected.`,
        );
        // Refresh so the "approval pending" badge appears if the server renders it.
        // A failure here is a refresh failure, not a transition failure — the
        // request above did land — so it must not be reported as either.
        try {
          await loadData(activeProject.id);
        } catch (refreshErr: unknown) {
          crashReporter.warn('documents.handleTransition: refresh after approval request failed', {
            e: String(refreshErr),
          });
        }
      } else {
        let updated: DocumentRecord;
        try {
          updated = await transitionCDE(activeProject.id, doc.id, newStatus);
        } catch (err: unknown) {
          const failure = describeTransitionFailure(err, 'transition');
          Alert.alert(failure.title, failure.body);
          return;
        }
        setDocuments((prev) => prev.map((d) => (d.id === updated.id ? updated : d)));
        setSelectedDoc(updated);
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

      {/* Search — FIX-15: value also forwarded to server as ?search= param */}
      <View style={styles.searchRow}>
        <TextInput
          style={styles.searchInput}
          placeholder="Search documents..."
          placeholderTextColor={theme.colors.disabled}
          value={search}
          onChangeText={(v) => {
            setSearch(v);
            // Pass the query to the server so large corpora can be filtered
            // server-side rather than loading all documents first.
            loadData(activeProject?.id, v.trim() ? { search: v.trim() } : undefined);
          }}
          autoCapitalize="none"
        />
        {search.length > 0 && (
          <TouchableOpacity
            onPress={() => {
              setSearch('');
              loadData(activeProject?.id);
            }}
            style={styles.clearBtn}
          >
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
          projectId={activeProject?.id}
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
  projectId,
  transitioning,
  onTransition,
  onClose,
}: {
  doc: DocumentRecord;
  projectId?: string;
  transitioning: boolean;
  onTransition: (doc: DocumentRecord, status: CDEStatus, needsApproval: boolean | null) => void;
  onClose: () => void;
}) {
  const cdeColor = getCDEColor(doc.cdeStatus);
  const { source: transitionSource, options: nextOptions } = transitionsFor(doc);
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
          {nextOptions.length > 0 && (
            <View style={styles.transitionSection}>
              <Text style={styles.transitionTitle}>CDE Transition</Text>
              <View style={styles.transitionRow}>
                {nextOptions.map((opt) => {
                  const next = opt.to as CDEStatus;
                  const nextColor = getCDEColor(next);
                  // true -> approval route. false -> direct. null -> unknown
                  // (legacy server): label it plainly rather than asserting
                  // either, and let the server's own refusal decide.
                  const gated = opt.requiresApproval === true;
                  return (
                    <TouchableOpacity
                      key={next}
                      style={[styles.transitionBtn, { backgroundColor: nextColor }]}
                      onPress={() => onTransition(doc, next, opt.requiresApproval)}
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
                {doc.cdeStatus} {'\u2192'} {nextOptions.map((o) => o.to).join(' / ')}
              </Text>
              {transitionSource === 'legacy' && (
                // Say that this list is the app's older built-in guess, not the
                // server's answer. Without it the screen looks equally
                // authoritative either way, and missing transitions read as
                // rules rather than as a gap.
                <Text style={styles.transitionHint}>
                  This server does not publish its CDE state machine — showing
                  this app&apos;s built-in list. Some transitions may be missing,
                  and the server decides whether approval is required.
                </Text>
              )}
            </View>
          )}

          {nextOptions.length === 0 && (
            <View style={styles.transitionSection}>
              <Text style={styles.transitionHint}>This document is in its final CDE state.</Text>
            </View>
          )}

          {/* T3-15 — 2D markup. Currently restricted to PDFs because the
              shipped renderer (react-native-pdf) won't open .docx / .dwg.
              Image documents could be added in v2 by branching the route
              on contentType. */}
          {projectId && doc.fileName?.toLowerCase().endsWith('.pdf') ? (
            <View style={styles.transitionSection}>
              <TouchableOpacity
                style={[styles.transitionBtn, { backgroundColor: theme.colors.accent }]}
                onPress={() => {
                  onClose();
                  router.push({
                    pathname: '/documents/markup',
                    params: { projectId, documentId: doc.id, fileName: doc.fileName },
                  });
                }}
              >
                <Text style={styles.transitionBtnText}>📝 Open Markup</Text>
              </TouchableOpacity>
              <Text style={styles.transitionHint}>
                Add pen / arrow / text / circle annotations to the drawing.
              </Text>
            </View>
          ) : null}
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
