import { useState, useEffect, useCallback } from 'react';
import {
  View,
  Text,
  StyleSheet,
  TextInput,
  TouchableOpacity,
  FlatList,
  ActivityIndicator,
  Alert,
  ScrollView,
  KeyboardAvoidingView,
  Platform,
  Modal,
} from 'react-native';
import { theme, getRAGColor } from '@/utils/theme';
import { listProjects, lookupElement } from '@/api/endpoints';
import type { Project, TaggedElement } from '@/types/api';
import { CameraView, useCameraPermissions, BarcodeScanningResult } from 'expo-camera';
import { parseQr } from '@/services/qrParser';
import { crashReporter } from '@/services/crashReporter';

interface ScanHistoryEntry {
  query: string;
  resultCount: number;
  timestamp: string;
}

const DISCIPLINE_LABELS: Record<string, string> = {
  M: 'Mechanical',
  E: 'Electrical',
  P: 'Plumbing',
  A: 'Architectural',
  S: 'Structural',
  FP: 'Fire Protection',
  LV: 'Low Voltage',
  G: 'General',
};

export default function ScannerScreen() {
  const [projects, setProjects] = useState<Project[]>([]);
  const [activeProject, setActiveProject] = useState<Project | null>(null);
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<TaggedElement[]>([]);
  const [selectedElement, setSelectedElement] = useState<TaggedElement | null>(null);
  const [loading, setLoading] = useState(false);
  const [searching, setSearching] = useState(false);
  const [history, setHistory] = useState<ScanHistoryEntry[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [scanning, setScanning] = useState(false);
  const [permission, requestPermission] = useCameraPermissions();

  useEffect(() => {
    loadProjects();
  }, []);

  async function openScanner() {
    if (!permission?.granted) {
      const res = await requestPermission();
      if (!res.granted) {
        Alert.alert(
          'Camera permission required',
          'Enable camera access in Settings to scan QR codes on site.',
        );
        return;
      }
    }
    setScanning(true);
  }

  async function onBarcodeScanned(result: BarcodeScanningResult) {
    if (!scanning) return;
    setScanning(false);
    const parsed = parseQr(result.data);
    if (parsed.type === 'unknown' || !parsed.id) {
      // Drop the raw payload into the search box so users can refine
      setQuery(result.data);
      Alert.alert('Unrecognised code', `Scanned: ${result.data}`);
      return;
    }
    // Treat element/issue/document QR payloads as element tag lookup
    setQuery(parsed.id);
    if (activeProject) {
      setSearching(true);
      setError(null);
      try {
        const elements = await lookupElement(activeProject.id, parsed.id);
        setResults(elements);
        setHistory(prev => [
          { query: parsed.id!, resultCount: elements.length, timestamp: new Date().toISOString() },
          ...prev.slice(0, 19),
        ]);
        if (elements.length === 0) {
          Alert.alert('No match', `Scanned ${parsed.id} — no element in this project.`);
        }
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Lookup failed');
      } finally {
        setSearching(false);
      }
    }
  }

  async function loadProjects() {
    setLoading(true);
    try {
      const list = await listProjects();
      setProjects(list);
      if (list.length > 0) setActiveProject(list[0]);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to load projects';
      setError(msg);
    } finally {
      setLoading(false);
    }
  }

  const handleSearch = useCallback(async () => {
    const trimmed = query.trim();
    if (!trimmed || !activeProject) return;

    setSearching(true);
    setError(null);
    setSelectedElement(null);
    try {
      const elements = await lookupElement(activeProject.id, trimmed);
      setResults(elements);
      setHistory((prev) => [
        { query: trimmed, resultCount: elements.length, timestamp: new Date().toISOString() },
        ...prev.slice(0, 19),
      ]);
      if (elements.length === 0) {
        Alert.alert('No Results', `No elements found matching "${trimmed}"`);
      }
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Lookup failed';
      setError(msg);
    } finally {
      setSearching(false);
    }
  }, [query, activeProject]);

  function handleHistoryTap(entry: ScanHistoryEntry) {
    setQuery(entry.query);
  }

  if (loading) {
    return (
      <View style={styles.center}>
        <ActivityIndicator size="large" color={theme.colors.accent} />
        <Text style={styles.loadingText}>Loading...</Text>
      </View>
    );
  }

  if (!activeProject) {
    return (
      <View style={styles.center}>
        <Text style={styles.emptyTitle}>No Projects</Text>
        <Text style={styles.emptySubtext}>Create a project to start scanning assets.</Text>
      </View>
    );
  }

  return (
    <KeyboardAvoidingView
      style={styles.root}
      behavior={Platform.OS === 'ios' ? 'padding' : undefined}
    >
      <ScrollView
        style={styles.root}
        contentContainerStyle={styles.scroll}
        keyboardShouldPersistTaps="handled"
      >
        {/* Project selector */}
        {projects.length > 1 && (
          <ScrollView horizontal showsHorizontalScrollIndicator={false} style={styles.projectBar}>
            {projects.map((p) => (
              <TouchableOpacity
                key={p.id}
                style={[styles.projectChip, p.id === activeProject.id && styles.projectChipActive]}
                onPress={() => {
                  setActiveProject(p);
                  setResults([]);
                  setSelectedElement(null);
                }}
              >
                <Text style={[styles.projectChipText, p.id === activeProject.id && styles.projectChipTextActive]}>
                  {p.code || p.name}
                </Text>
              </TouchableOpacity>
            ))}
          </ScrollView>
        )}

        {/* Scan header */}
        <View style={styles.headerCard}>
          <Text style={styles.headerTitle}>Asset Lookup</Text>
          <Text style={styles.headerSubtitle}>
            Enter an ISO 19650 tag, asset code, or scan a QR code
          </Text>
        </View>

        {/* Search input */}
        <View style={styles.searchRow}>
          <TextInput
            style={styles.searchInput}
            placeholder="e.g. M-BLD1-Z01-L02-HVAC-SUP-AHU-0003"
            placeholderTextColor={theme.colors.disabled}
            value={query}
            onChangeText={setQuery}
            onSubmitEditing={handleSearch}
            returnKeyType="search"
            autoCapitalize="characters"
            autoCorrect={false}
          />
          <TouchableOpacity
            style={[styles.searchButton, (!query.trim() || searching) && styles.searchButtonDisabled]}
            onPress={handleSearch}
            disabled={!query.trim() || searching}
          >
            {searching ? (
              <ActivityIndicator size="small" color={theme.colors.surface} />
            ) : (
              <Text style={styles.searchButtonText}>Search</Text>
            )}
          </TouchableOpacity>
        </View>

        {/* QR scan button (MOB-01: live expo-camera) */}
        <TouchableOpacity
          style={styles.qrButton}
          onPress={openScanner}
          accessibilityRole="button"
          accessibilityLabel="Open QR code scanner"
        >
          <Text style={styles.qrIcon}>[ ]</Text>
          <Text style={styles.qrButtonText}>Scan QR Code</Text>
          <Text style={styles.qrHint}>
            {permission?.granted ? 'Tap to open camera' : 'Camera access required'}
          </Text>
        </TouchableOpacity>

        {/* Live camera scanner modal */}
        <Modal visible={scanning} animationType="slide" onRequestClose={() => setScanning(false)}>
          <View style={{ flex: 1, backgroundColor: '#000' }}>
            <CameraView
              style={{ flex: 1 }}
              barcodeScannerSettings={{ barcodeTypes: ['qr', 'pdf417', 'code128', 'code39', 'ean13'] }}
              onBarcodeScanned={onBarcodeScanned}
            />
            <View style={{
              position: 'absolute', bottom: 0, left: 0, right: 0,
              padding: 24, alignItems: 'center', backgroundColor: 'rgba(0,0,0,0.6)',
            }}>
              <Text style={{ color: '#fff', marginBottom: 12, fontSize: 14 }}>
                Point at an asset QR or barcode
              </Text>
              <TouchableOpacity
                style={{
                  backgroundColor: theme.colors.accent,
                  paddingHorizontal: 24, paddingVertical: 10, borderRadius: 8,
                }}
                onPress={() => setScanning(false)}
                accessibilityRole="button"
                accessibilityLabel="Cancel scanning"
              >
                <Text style={{ color: '#fff', fontWeight: '600' }}>Cancel</Text>
              </TouchableOpacity>
            </View>
          </View>
        </Modal>

        {error && (
          <View style={styles.errorBanner}>
            <Text style={styles.errorText}>{error}</Text>
          </View>
        )}

        {/* Selected element detail */}
        {selectedElement && (
          <ElementDetail element={selectedElement} onClose={() => setSelectedElement(null)} />
        )}

        {/* Search results */}
        {results.length > 0 && !selectedElement && (
          <View style={styles.sectionCard}>
            <Text style={styles.sectionTitle}>Results ({results.length})</Text>
            {results.map((el) => (
              <TouchableOpacity
                key={el.id}
                style={styles.resultRow}
                onPress={() => setSelectedElement(el)}
              >
                <View style={[styles.discDot, { backgroundColor: getDisciplineColor(el.discipline) }]} />
                <View style={styles.resultContent}>
                  <Text style={styles.resultTag} numberOfLines={1}>{el.assTag1}</Text>
                  <Text style={styles.resultMeta}>
                    {el.categoryName} {el.familyName ? `\u00B7 ${el.familyName}` : ''}
                    {el.roomName ? ` \u00B7 ${el.roomName}` : ''}
                  </Text>
                </View>
                <Text style={styles.resultArrow}>&gt;</Text>
              </TouchableOpacity>
            ))}
          </View>
        )}

        {/* Scan history */}
        {history.length > 0 && !selectedElement && results.length === 0 && (
          <View style={styles.sectionCard}>
            <Text style={styles.sectionTitle}>Recent Lookups</Text>
            {history.map((entry, idx) => (
              <TouchableOpacity
                key={`${entry.timestamp}-${idx}`}
                style={styles.historyRow}
                onPress={() => handleHistoryTap(entry)}
              >
                <View style={styles.historyContent}>
                  <Text style={styles.historyQuery}>{entry.query}</Text>
                  <Text style={styles.historyMeta}>
                    {entry.resultCount} result{entry.resultCount !== 1 ? 's' : ''} \u00B7{' '}
                    {formatTime(entry.timestamp)}
                  </Text>
                </View>
              </TouchableOpacity>
            ))}
          </View>
        )}

        {/* Quick reference */}
        {results.length === 0 && !selectedElement && history.length === 0 && (
          <View style={styles.sectionCard}>
            <Text style={styles.sectionTitle}>Tag Format Reference</Text>
            <Text style={styles.refText}>ISO 19650 asset tags follow the 8-segment format:</Text>
            <View style={styles.refFormat}>
              <Text style={styles.refFormatText}>DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ</Text>
            </View>
            <View style={styles.refGrid}>
              <RefItem label="DISC" example="M, E, P, A" desc="Discipline" />
              <RefItem label="LOC" example="BLD1, EXT" desc="Location" />
              <RefItem label="ZONE" example="Z01, Z02" desc="Zone" />
              <RefItem label="LVL" example="L01, GF, B1" desc="Level" />
              <RefItem label="SYS" example="HVAC, DCW" desc="System" />
              <RefItem label="FUNC" example="SUP, HTG" desc="Function" />
              <RefItem label="PROD" example="AHU, DB" desc="Product" />
              <RefItem label="SEQ" example="0001" desc="Sequence" />
            </View>
            <Text style={styles.refHint}>
              You can search by full tag, partial tag, product code, or sequence number.
            </Text>
          </View>
        )}
      </ScrollView>
    </KeyboardAvoidingView>
  );
}

function ElementDetail({ element, onClose }: { element: TaggedElement; onClose: () => void }) {
  const discLabel = DISCIPLINE_LABELS[element.discipline] || element.discipline;

  return (
    <View style={styles.detailCard}>
      <View style={styles.detailHeader}>
        <View style={styles.detailHeaderLeft}>
          <View style={[styles.detailDiscBadge, { backgroundColor: getDisciplineColor(element.discipline) }]}>
            <Text style={styles.detailDiscText}>{element.discipline}</Text>
          </View>
          <Text style={styles.detailDiscLabel}>{discLabel}</Text>
        </View>
        <TouchableOpacity onPress={onClose} style={styles.closeButton}>
          <Text style={styles.closeButtonText}>Close</Text>
        </TouchableOpacity>
      </View>

      {/* Tag */}
      <View style={styles.tagBanner}>
        <Text style={styles.tagBannerText}>{element.assTag1}</Text>
      </View>

      {/* Token breakdown */}
      <Text style={styles.detailSectionTitle}>Token Breakdown</Text>
      <View style={styles.tokenGrid}>
        <TokenCell label="DISC" value={element.discipline} />
        <TokenCell label="LOC" value={element.location} />
        <TokenCell label="ZONE" value={element.zone} />
        <TokenCell label="LVL" value={element.level} />
        <TokenCell label="SYS" value={element.systemType} />
        <TokenCell label="FUNC" value={element.function} />
        <TokenCell label="PROD" value={element.productCode} />
        <TokenCell label="SEQ" value={element.sequenceNumber} />
      </View>

      {/* Identity */}
      <Text style={styles.detailSectionTitle}>Identity</Text>
      <DetailField label="Category" value={element.categoryName} />
      <DetailField label="Family" value={element.familyName} />
      <DetailField label="Type" value={element.typeName} />
      <DetailField label="Status" value={element.status} />
      <DetailField label="Revision" value={element.revision} />

      {/* Spatial */}
      {(element.roomName || element.gridRef) && (
        <>
          <Text style={styles.detailSectionTitle}>Spatial</Text>
          {element.roomName ? <DetailField label="Room" value={element.roomName} /> : null}
          {element.gridRef ? <DetailField label="Grid Ref" value={element.gridRef} /> : null}
        </>
      )}

      {/* TAG7 summary */}
      {element.tag7Summary ? (
        <>
          <Text style={styles.detailSectionTitle}>Description</Text>
          <Text style={styles.tag7Text}>{element.tag7Summary}</Text>
        </>
      ) : null}

      <Text style={styles.syncedAt}>
        Last synced: {formatDate(element.syncedAt)}
      </Text>
    </View>
  );
}

function TokenCell({ label, value }: { label: string; value: string }) {
  const isPlaceholder = !value || value === 'XX' || value === 'ZZ' || value === 'GEN' || value === '0000';
  return (
    <View style={styles.tokenCell}>
      <Text style={styles.tokenLabel}>{label}</Text>
      <Text style={[styles.tokenValue, isPlaceholder && styles.tokenPlaceholder]}>
        {value || '--'}
      </Text>
    </View>
  );
}

function DetailField({ label, value }: { label: string; value: string }) {
  if (!value) return null;
  return (
    <View style={styles.detailFieldRow}>
      <Text style={styles.detailFieldLabel}>{label}</Text>
      <Text style={styles.detailFieldValue}>{value}</Text>
    </View>
  );
}

function RefItem({ label, example, desc }: { label: string; example: string; desc: string }) {
  return (
    <View style={styles.refItem}>
      <Text style={styles.refLabel}>{label}</Text>
      <Text style={styles.refDesc}>{desc}</Text>
      <Text style={styles.refExample}>{example}</Text>
    </View>
  );
}

function getDisciplineColor(disc: string): string {
  switch (disc) {
    case 'M': return '#2196F3';
    case 'E': return '#FFC107';
    case 'P': return '#4CAF50';
    case 'A': return '#9E9E9E';
    case 'S': return '#F44336';
    case 'FP': return '#FF5722';
    case 'LV': return '#9C27B0';
    case 'G': return '#795548';
    default: return theme.colors.disabled;
  }
}

function formatTime(iso: string): string {
  try {
    const d = new Date(iso);
    return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  } catch (e) { crashReporter.warn('scanner.tsx:475', { e: String(e) });
    return '';
  }
}

function formatDate(iso: string): string {
  try {
    const d = new Date(iso);
    return d.toLocaleDateString() + ' ' + d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  } catch (e) { crashReporter.warn('scanner.tsx:484', { e: String(e) });
    return iso;
  }
}

const styles = StyleSheet.create({
  root: {
    flex: 1,
    backgroundColor: theme.colors.background,
  },
  scroll: {
    padding: theme.spacing.md,
    paddingBottom: theme.spacing.xl,
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
  emptyTitle: {
    fontSize: theme.fontSize.xl,
    fontWeight: '600',
    color: theme.colors.text,
    marginBottom: theme.spacing.sm,
  },
  emptySubtext: {
    fontSize: theme.fontSize.md,
    color: theme.colors.textSecondary,
    textAlign: 'center',
  },

  // Project bar
  projectBar: {
    marginBottom: theme.spacing.md,
    flexGrow: 0,
  },
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
  projectChipTextActive: {
    color: theme.colors.surface,
  },

  // Header
  headerCard: {
    backgroundColor: theme.colors.primary,
    borderRadius: theme.borderRadius.xl,
    padding: theme.spacing.lg,
    marginBottom: theme.spacing.md,
  },
  headerTitle: {
    fontSize: theme.fontSize.xxl,
    fontWeight: '700',
    color: theme.colors.surface,
  },
  headerSubtitle: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.surface,
    opacity: 0.8,
    marginTop: theme.spacing.xs,
  },

  // Search
  searchRow: {
    flexDirection: 'row',
    marginBottom: theme.spacing.md,
    gap: theme.spacing.sm,
  },
  searchInput: {
    flex: 1,
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.md,
    borderWidth: 1,
    borderColor: theme.colors.border,
    paddingHorizontal: theme.spacing.md,
    paddingVertical: theme.spacing.sm + 2,
    fontSize: theme.fontSize.md,
    color: theme.colors.text,
    fontFamily: Platform.OS === 'ios' ? 'Menlo' : 'monospace',
  },
  searchButton: {
    backgroundColor: theme.colors.accent,
    borderRadius: theme.borderRadius.md,
    paddingHorizontal: theme.spacing.lg,
    justifyContent: 'center',
    alignItems: 'center',
  },
  searchButtonDisabled: {
    opacity: 0.5,
  },
  searchButtonText: {
    color: theme.colors.surface,
    fontSize: theme.fontSize.md,
    fontWeight: '600',
  },

  // QR button
  qrButton: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.lg,
    borderWidth: 1,
    borderColor: theme.colors.border,
    borderStyle: 'dashed',
    padding: theme.spacing.lg,
    alignItems: 'center',
    marginBottom: theme.spacing.md,
  },
  qrIcon: {
    fontSize: 36,
    color: theme.colors.primary,
    fontWeight: '300',
    marginBottom: theme.spacing.sm,
  },
  qrButtonText: {
    fontSize: theme.fontSize.lg,
    fontWeight: '600',
    color: theme.colors.primary,
  },
  qrHint: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
    marginTop: theme.spacing.xs,
  },

  // Error
  errorBanner: {
    backgroundColor: '#FFEBEE',
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.sm,
    marginBottom: theme.spacing.md,
  },
  errorText: {
    color: theme.colors.danger,
    fontSize: theme.fontSize.sm,
    textAlign: 'center',
  },

  // Section card
  sectionCard: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.lg,
    padding: theme.spacing.md,
    marginBottom: theme.spacing.md,
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 1 },
    shadowOpacity: 0.06,
    shadowRadius: 4,
    elevation: 2,
  },
  sectionTitle: {
    fontSize: theme.fontSize.lg,
    fontWeight: '600',
    color: theme.colors.text,
    marginBottom: theme.spacing.sm,
  },

  // Results
  resultRow: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingVertical: theme.spacing.sm,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: theme.colors.border,
  },
  discDot: {
    width: 10,
    height: 10,
    borderRadius: 5,
    marginRight: theme.spacing.sm,
  },
  resultContent: {
    flex: 1,
  },
  resultTag: {
    fontSize: theme.fontSize.md,
    fontWeight: '600',
    color: theme.colors.text,
    fontFamily: Platform.OS === 'ios' ? 'Menlo' : 'monospace',
  },
  resultMeta: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
    marginTop: 2,
  },
  resultArrow: {
    fontSize: theme.fontSize.lg,
    color: theme.colors.disabled,
    marginLeft: theme.spacing.sm,
  },

  // History
  historyRow: {
    paddingVertical: theme.spacing.sm,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: theme.colors.border,
  },
  historyContent: {},
  historyQuery: {
    fontSize: theme.fontSize.md,
    fontWeight: '500',
    color: theme.colors.primary,
    fontFamily: Platform.OS === 'ios' ? 'Menlo' : 'monospace',
  },
  historyMeta: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
    marginTop: 2,
  },

  // Element detail
  detailCard: {
    backgroundColor: theme.colors.surface,
    borderRadius: theme.borderRadius.xl,
    padding: theme.spacing.md,
    marginBottom: theme.spacing.md,
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 2 },
    shadowOpacity: 0.1,
    shadowRadius: 8,
    elevation: 4,
  },
  detailHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: theme.spacing.md,
  },
  detailHeaderLeft: {
    flexDirection: 'row',
    alignItems: 'center',
  },
  detailDiscBadge: {
    borderRadius: theme.borderRadius.sm,
    paddingHorizontal: theme.spacing.sm,
    paddingVertical: theme.spacing.xs,
    marginRight: theme.spacing.sm,
  },
  detailDiscText: {
    color: theme.colors.surface,
    fontSize: theme.fontSize.sm,
    fontWeight: '700',
  },
  detailDiscLabel: {
    fontSize: theme.fontSize.md,
    fontWeight: '600',
    color: theme.colors.text,
  },
  closeButton: {
    paddingHorizontal: theme.spacing.md,
    paddingVertical: theme.spacing.xs,
  },
  closeButtonText: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.accent,
    fontWeight: '600',
  },

  // Tag banner
  tagBanner: {
    backgroundColor: theme.colors.primary,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.md,
    alignItems: 'center',
    marginBottom: theme.spacing.md,
  },
  tagBannerText: {
    color: theme.colors.surface,
    fontSize: theme.fontSize.lg,
    fontWeight: '700',
    fontFamily: Platform.OS === 'ios' ? 'Menlo' : 'monospace',
    letterSpacing: 1,
  },

  // Token grid
  tokenGrid: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: theme.spacing.sm,
    marginBottom: theme.spacing.md,
  },
  tokenCell: {
    width: '22%',
    backgroundColor: theme.colors.background,
    borderRadius: theme.borderRadius.sm,
    padding: theme.spacing.xs + 2,
    alignItems: 'center',
  },
  tokenLabel: {
    fontSize: theme.fontSize.xs,
    fontWeight: '700',
    color: theme.colors.textSecondary,
    marginBottom: 2,
  },
  tokenValue: {
    fontSize: theme.fontSize.sm,
    fontWeight: '600',
    color: theme.colors.text,
    fontFamily: Platform.OS === 'ios' ? 'Menlo' : 'monospace',
  },
  tokenPlaceholder: {
    color: theme.colors.danger,
  },

  // Detail section title
  detailSectionTitle: {
    fontSize: theme.fontSize.sm,
    fontWeight: '700',
    color: theme.colors.textSecondary,
    textTransform: 'uppercase',
    letterSpacing: 1,
    marginTop: theme.spacing.sm,
    marginBottom: theme.spacing.xs,
  },

  // Detail fields
  detailFieldRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    paddingVertical: theme.spacing.xs + 2,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: theme.colors.border,
  },
  detailFieldLabel: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.textSecondary,
    fontWeight: '500',
  },
  detailFieldValue: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.text,
    fontWeight: '600',
    maxWidth: '60%',
    textAlign: 'right',
  },

  // TAG7 summary
  tag7Text: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.text,
    lineHeight: 20,
    marginTop: theme.spacing.xs,
  },

  syncedAt: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.disabled,
    textAlign: 'center',
    marginTop: theme.spacing.md,
  },

  // Reference card
  refText: {
    fontSize: theme.fontSize.sm,
    color: theme.colors.textSecondary,
    marginBottom: theme.spacing.sm,
  },
  refFormat: {
    backgroundColor: theme.colors.primary,
    borderRadius: theme.borderRadius.md,
    padding: theme.spacing.sm + 2,
    alignItems: 'center',
    marginBottom: theme.spacing.md,
  },
  refFormatText: {
    color: theme.colors.surface,
    fontSize: theme.fontSize.sm,
    fontWeight: '700',
    fontFamily: Platform.OS === 'ios' ? 'Menlo' : 'monospace',
    letterSpacing: 0.5,
  },
  refGrid: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: theme.spacing.sm,
    marginBottom: theme.spacing.sm,
  },
  refItem: {
    width: '47%',
    backgroundColor: theme.colors.background,
    borderRadius: theme.borderRadius.sm,
    padding: theme.spacing.xs + 2,
  },
  refLabel: {
    fontSize: theme.fontSize.xs,
    fontWeight: '700',
    color: theme.colors.primary,
  },
  refDesc: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
  },
  refExample: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.text,
    fontFamily: Platform.OS === 'ios' ? 'Menlo' : 'monospace',
    marginTop: 2,
  },
  refHint: {
    fontSize: theme.fontSize.xs,
    color: theme.colors.textSecondary,
    fontStyle: 'italic',
  },
});
