/**
 * T3-15 — 2D plan markup viewer.
 *
 * Opens a PDF (or image) document and lets the user overlay vector
 * annotations: pen / arrow / text / circle. Shapes are persisted to the
 * server's existing DocumentMarkup entity (Planscape.Core/Entities) via
 * the MarkupsController endpoints.
 *
 * Implementation notes
 * ────────────────────
 *  • PDF rendering uses react-native-pdf which is already in package.json
 *    so we don't add a new dependency. The pdf component fills the screen
 *    and we lay an absolutely-positioned <View> "canvas" on top. The
 *    canvas captures touches via PanResponder and renders shapes with
 *    plain RN <View>s — no SVG dependency.
 *  • Shapes are stored as a normalised array (0..1 coords against the
 *    canvas bbox) so re-rendering at a different zoom / orientation is
 *    lossless. The same JSON shape catalogue is what the eventual web
 *    canvas will consume — see the `MarkupShape` interface in
 *    src/api/endpoints.ts.
 *  • The web markup viewer is intentionally NOT included here — porting
 *    a touch-based pen tool to a mouse-driven canvas is >2 days of work
 *    per the orchestrator brief and the viewer.html surface doesn't
 *    currently load PDFs at all.
 */
import { useEffect, useRef, useState, useCallback } from 'react';
import {
  View,
  Text,
  StyleSheet,
  TouchableOpacity,
  TextInput,
  Modal,
  Alert,
  PanResponder,
  Platform,
  ActivityIndicator,
} from 'react-native';
import { useLocalSearchParams, Stack, router } from 'expo-router';
import Pdf from 'react-native-pdf';
import {
  listDocumentMarkups,
  createDocumentMarkup,
  updateDocumentMarkup,
  type DocumentMarkup,
  type MarkupShape,
} from '@/api/endpoints';
import { _getBaseUrl } from '@/api/endpoints';
import { getToken } from '@/api/client';
import { theme } from '@/utils/theme';
import { crashReporter } from '@/services/crashReporter';

type Tool = 'pen' | 'arrow' | 'text' | 'circle' | 'pan';
const PALETTE = ['#ef4444', '#f59e0b', '#10b981', '#3b82f6', '#8b5cf6', '#1f2937'];

export default function MarkupScreen() {
  const { documentId, projectId, fileName } = useLocalSearchParams<{
    documentId: string; projectId: string; fileName?: string;
  }>();

  const [tool, setTool] = useState<Tool>('pen');
  const [color, setColor] = useState<string>(PALETTE[0]);
  const [shapes, setShapes] = useState<MarkupShape[]>([]);
  const [drafting, setDrafting] = useState<MarkupShape | null>(null);
  const [pdfUrl, setPdfUrl] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [existing, setExisting] = useState<DocumentMarkup | null>(null);
  const [textModalOpen, setTextModalOpen] = useState(false);
  const [pendingText, setPendingText] = useState('');
  const [pendingTextAt, setPendingTextAt] = useState<{ x: number; y: number } | null>(null);

  const canvasRef = useRef<View>(null);
  // Live size of the overlay; captured on first onLayout so we can convert
  // pageX/pageY to normalised 0..1 coords. PDF zoom changes are not yet
  // wired up — v1 keeps the PDF locked at fit-to-width.
  const layoutRef = useRef<{ x: number; y: number; w: number; h: number }>({ x: 0, y: 0, w: 1, h: 1 });

  // Load the doc URL + any existing markup on mount.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const tok = await getToken();
        // The DocumentsController exposes /content as the raw binary path.
        const url = `${_getBaseUrl()}/api/projects/${projectId}/documents/${documentId}/content`;
        if (!cancelled) setPdfUrl(`${url}?access_token=${encodeURIComponent(tok || '')}`);

        const markups = await listDocumentMarkups(projectId as string, documentId as string);
        const latest = markups && markups.length > 0 ? markups[markups.length - 1] : null;
        if (!cancelled && latest) {
          setExisting(latest);
          try { setShapes(JSON.parse(latest.shapesJson || '[]')); } catch { setShapes([]); }
        }
      } catch (e) {
        crashReporter.warn('markup.loadDoc', { e: String(e) });
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [projectId, documentId]);

  // Convert touch pageX/pageY to normalised coords. We deliberately keep
  // values in 0..1 (clamped) so the same shape json renders identically at
  // any future zoom level — the renderer multiplies by current bbox size.
  const norm = useCallback((px: number, py: number) => {
    const { x, y, w, h } = layoutRef.current;
    return {
      x: Math.max(0, Math.min(1, (px - x) / Math.max(1, w))),
      y: Math.max(0, Math.min(1, (py - y) / Math.max(1, h))),
    };
  }, []);

  // PanResponder — single source of truth for pen/arrow/circle tools.
  // For text, the first tap opens a modal asking what to write.
  const responder = useRef(
    PanResponder.create({
      onStartShouldSetPanResponder: () => tool !== 'pan',
      onMoveShouldSetPanResponder: () => tool !== 'pan',
      onPanResponderGrant: (e) => {
        if (tool === 'pan') return;
        const p = norm(e.nativeEvent.pageX, e.nativeEvent.pageY);
        if (tool === 'pen')    setDrafting({ kind: 'pen',    color, strokeWidth: 3, points: [p] });
        if (tool === 'arrow')  setDrafting({ kind: 'arrow',  color, strokeWidth: 3, from: p, to: p });
        if (tool === 'circle') setDrafting({ kind: 'circle', color, strokeWidth: 3, centre: p, radius: 0 });
        if (tool === 'text')   { setPendingTextAt(p); setTextModalOpen(true); }
      },
      onPanResponderMove: (e) => {
        if (tool === 'pan' || tool === 'text') return;
        const p = norm(e.nativeEvent.pageX, e.nativeEvent.pageY);
        setDrafting((prev) => {
          if (!prev) return null;
          if (prev.kind === 'pen')    return { ...prev, points: [...(prev.points ?? []), p] };
          if (prev.kind === 'arrow')  return { ...prev, to: p };
          if (prev.kind === 'circle') {
            const dx = p.x - (prev.centre?.x ?? 0);
            const dy = p.y - (prev.centre?.y ?? 0);
            return { ...prev, radius: Math.sqrt(dx * dx + dy * dy) };
          }
          return prev;
        });
      },
      onPanResponderRelease: () => {
        setDrafting((d) => {
          if (d && d.kind !== 'text') setShapes((prev) => [...prev, d]);
          return null;
        });
      },
    })
  ).current;

  function commitText() {
    if (pendingTextAt && pendingText.trim()) {
      setShapes((prev) => [...prev, {
        kind: 'text', color, at: pendingTextAt, text: pendingText.trim(), fontSize: 14,
      }]);
    }
    setPendingText(''); setPendingTextAt(null); setTextModalOpen(false);
  }

  function undo() { setShapes((prev) => prev.slice(0, -1)); }
  function clearAll() {
    Alert.alert('Clear markup', 'Discard every shape on this drawing?', [
      { text: 'Cancel', style: 'cancel' },
      { text: 'Clear', style: 'destructive', onPress: () => setShapes([]) },
    ]);
  }

  async function save() {
    if (saving) return;
    setSaving(true);
    try {
      const json = JSON.stringify(shapes);
      if (existing) {
        const updated = await updateDocumentMarkup(
          projectId as string, documentId as string, existing.id,
          { shapesJson: json },
        );
        setExisting(updated);
      } else {
        const created = await createDocumentMarkup(
          projectId as string, documentId as string,
          { shapesJson: json, pageNumber: 1 },
        );
        setExisting(created);
      }
      Alert.alert('Saved', `${shapes.length} shape${shapes.length === 1 ? '' : 's'} saved.`);
    } catch (e) {
      Alert.alert('Save failed', e instanceof Error ? e.message : String(e));
    } finally {
      setSaving(false);
    }
  }

  if (loading) {
    return (
      <View style={styles.center}>
        <ActivityIndicator size="large" color={theme.colors.accent} />
        <Text style={styles.loadingText}>Loading drawing…</Text>
      </View>
    );
  }

  return (
    <View style={styles.root}>
      <Stack.Screen options={{ title: fileName ? `Markup · ${fileName}` : 'Markup' }} />

      {/* PDF below, transparent overlay on top — pointerEvents on the PDF
          lets the overlay capture touches when a draw tool is active. */}
      <View style={styles.canvasFrame}>
        {pdfUrl ? (
          <Pdf
            source={{ uri: pdfUrl, cache: false }}
            style={styles.pdf}
            onError={(e) => crashReporter.warn('markup.pdfError', { e: String(e) })}
            // Prevent gesture conflicts when a draw tool is active.
            enablePaging={false}
          />
        ) : null}
        <View
          ref={canvasRef}
          style={[styles.canvas, tool === 'pan' ? styles.canvasPan : null]}
          onLayout={(ev) => {
            const { x, y, width, height } = ev.nativeEvent.layout;
            layoutRef.current = { x, y, w: width, h: height };
          }}
          {...(tool === 'pan' ? {} : responder.panHandlers)}
        >
          {[...shapes, drafting].filter(Boolean).map((s, idx) => (
            <ShapeOverlay key={idx} shape={s as MarkupShape} bbox={layoutRef.current} />
          ))}
        </View>
      </View>

      {/* Toolbar */}
      <View style={styles.toolbar}>
        {(['pen', 'arrow', 'text', 'circle', 'pan'] as Tool[]).map((t) => (
          <TouchableOpacity
            key={t}
            style={[styles.toolBtn, tool === t && styles.toolBtnActive]}
            onPress={() => setTool(t)}
            accessibilityRole="button"
            accessibilityLabel={`${t} tool`}
          >
            <Text style={[styles.toolBtnText, tool === t && styles.toolBtnTextActive]}>
              {labelFor(t)}
            </Text>
          </TouchableOpacity>
        ))}
      </View>

      {/* Colour palette */}
      <View style={styles.paletteRow}>
        {PALETTE.map((c) => (
          <TouchableOpacity
            key={c}
            style={[styles.swatch, { backgroundColor: c }, color === c && styles.swatchActive]}
            onPress={() => setColor(c)}
            accessibilityLabel={`Use ${c}`}
          />
        ))}
        <View style={{ flex: 1 }} />
        <TouchableOpacity onPress={undo} style={styles.actionBtn}>
          <Text style={styles.actionBtnText}>Undo</Text>
        </TouchableOpacity>
        <TouchableOpacity onPress={clearAll} style={styles.actionBtn}>
          <Text style={styles.actionBtnText}>Clear</Text>
        </TouchableOpacity>
        <TouchableOpacity onPress={save} disabled={saving} style={[styles.actionBtn, styles.saveBtn]}>
          <Text style={styles.saveBtnText}>{saving ? 'Saving…' : 'Save'}</Text>
        </TouchableOpacity>
      </View>

      {/* Text-tool modal */}
      <Modal visible={textModalOpen} transparent animationType="fade" onRequestClose={() => setTextModalOpen(false)}>
        <View style={styles.modalBackdrop}>
          <View style={styles.modalCard}>
            <Text style={styles.modalTitle}>Annotation text</Text>
            <TextInput
              style={styles.modalInput}
              value={pendingText}
              onChangeText={setPendingText}
              placeholder="e.g. Door swing wrong"
              placeholderTextColor={theme.colors.disabled}
              autoFocus
            />
            <View style={styles.modalRow}>
              <TouchableOpacity onPress={() => { setTextModalOpen(false); setPendingText(''); }} style={styles.modalBtn}>
                <Text>Cancel</Text>
              </TouchableOpacity>
              <TouchableOpacity onPress={commitText} style={[styles.modalBtn, styles.modalBtnPrimary]}>
                <Text style={styles.modalBtnPrimaryText}>Add</Text>
              </TouchableOpacity>
            </View>
          </View>
        </View>
      </Modal>
    </View>
  );
}

function labelFor(t: Tool) {
  if (t === 'pen')    return '✏ Pen';
  if (t === 'arrow')  return '→ Arrow';
  if (t === 'text')   return 'T Text';
  if (t === 'circle') return '○ Circle';
  return '✋ Pan';
}

/**
 * Render a single MarkupShape as plain RN <View>s so we don't pull in a
 * SVG library. We trade fidelity for footprint: pen strokes are rendered
 * as a series of small dots and arrows are a line + a triangle head.
 * Good enough for site review — the source-of-truth shape data is the
 * normalised JSON, not the on-screen pixels.
 */
function ShapeOverlay({ shape, bbox }: { shape: MarkupShape; bbox: { w: number; h: number } }) {
  const W = bbox.w, H = bbox.h;
  const w = shape.strokeWidth ?? 3;
  if (shape.kind === 'pen' && shape.points && shape.points.length > 1) {
    // Segment-by-segment line approximation.
    return (
      <>
        {shape.points.slice(1).map((p, idx) => {
          const a = shape.points![idx];
          const b = p;
          const segs = renderSegment(a.x * W, a.y * H, b.x * W, b.y * H, shape.color, w);
          return <View key={idx} style={segs} />;
        })}
      </>
    );
  }
  if (shape.kind === 'arrow' && shape.from && shape.to) {
    const ax = shape.from.x * W, ay = shape.from.y * H;
    const bx = shape.to.x   * W, by = shape.to.y   * H;
    const seg = renderSegment(ax, ay, bx, by, shape.color, w);
    // Head: simple triangle approximated as 2 rotated segments.
    const angle = Math.atan2(by - ay, bx - ax);
    const head = 14;
    const h1 = renderSegment(bx, by, bx - head * Math.cos(angle - Math.PI / 6), by - head * Math.sin(angle - Math.PI / 6), shape.color, w);
    const h2 = renderSegment(bx, by, bx - head * Math.cos(angle + Math.PI / 6), by - head * Math.sin(angle + Math.PI / 6), shape.color, w);
    return (
      <>
        <View style={seg} />
        <View style={h1} />
        <View style={h2} />
      </>
    );
  }
  if (shape.kind === 'circle' && shape.centre) {
    const r = (shape.radius ?? 0) * Math.min(W, H);
    const cx = shape.centre.x * W;
    const cy = shape.centre.y * H;
    return (
      <View style={{
        position: 'absolute', left: cx - r, top: cy - r,
        width: r * 2, height: r * 2, borderRadius: r,
        borderColor: shape.color, borderWidth: w,
      }} />
    );
  }
  if (shape.kind === 'text' && shape.at && shape.text) {
    return (
      <Text style={{
        position: 'absolute',
        left: shape.at.x * W, top: shape.at.y * H,
        color: shape.color, fontSize: shape.fontSize ?? 14, fontWeight: '700',
      }}>{shape.text}</Text>
    );
  }
  return null;
}

function renderSegment(x1: number, y1: number, x2: number, y2: number, color: string, w: number) {
  const dx = x2 - x1, dy = y2 - y1;
  const len = Math.sqrt(dx * dx + dy * dy);
  const angle = Math.atan2(dy, dx);
  return {
    position: 'absolute' as const,
    left: x1, top: y1 - w / 2,
    width: len, height: w,
    backgroundColor: color,
    transform: [
      { translateX: 0 }, { translateY: 0 },
      { rotate: `${angle}rad` },
    ],
    transformOrigin: '0% 50%' as any,
  };
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: '#000' },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: theme.colors.background },
  loadingText: { marginTop: 12, color: theme.colors.text },
  canvasFrame: { flex: 1, position: 'relative', backgroundColor: '#222' },
  pdf: { flex: 1, width: '100%', backgroundColor: '#222' },
  canvas: { ...StyleSheet.absoluteFillObject },
  canvasPan: { /* pointerEvents on parent; this is a marker for clarity */ },
  toolbar: {
    flexDirection: 'row', backgroundColor: theme.colors.surface,
    borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: theme.colors.border,
  },
  toolBtn: { flex: 1, paddingVertical: 10, alignItems: 'center' },
  toolBtnActive: { backgroundColor: theme.colors.accent + '22' },
  toolBtnText: { fontSize: 13, color: theme.colors.text },
  toolBtnTextActive: { fontWeight: '700', color: theme.colors.accent },
  paletteRow: {
    flexDirection: 'row', alignItems: 'center', padding: 10,
    backgroundColor: theme.colors.surface,
    borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: theme.colors.border,
  },
  swatch: { width: 24, height: 24, borderRadius: 12, marginRight: 6, borderWidth: 2, borderColor: 'transparent' },
  swatchActive: { borderColor: theme.colors.text },
  actionBtn: {
    paddingHorizontal: 10, paddingVertical: 6, marginLeft: 4,
    backgroundColor: theme.colors.background, borderRadius: 6,
    borderWidth: StyleSheet.hairlineWidth, borderColor: theme.colors.border,
  },
  actionBtnText: { fontSize: 12, color: theme.colors.text },
  saveBtn: { backgroundColor: theme.colors.accent, borderColor: theme.colors.accent },
  saveBtnText: { fontSize: 12, color: '#fff', fontWeight: '700' },
  modalBackdrop: {
    flex: 1, backgroundColor: 'rgba(0,0,0,0.4)',
    alignItems: 'center', justifyContent: 'center', padding: 24,
  },
  modalCard: {
    width: '100%', maxWidth: 400, backgroundColor: theme.colors.surface,
    borderRadius: 8, padding: 16,
  },
  modalTitle: { fontSize: 16, fontWeight: '700', marginBottom: 8, color: theme.colors.text },
  modalInput: {
    borderWidth: 1, borderColor: theme.colors.border, borderRadius: 6,
    padding: 10, fontSize: 14, color: theme.colors.text, marginBottom: 12,
  },
  modalRow: { flexDirection: 'row', justifyContent: 'flex-end' },
  modalBtn: { paddingHorizontal: 14, paddingVertical: 8, marginLeft: 8, borderRadius: 6 },
  modalBtnPrimary: { backgroundColor: theme.colors.accent },
  modalBtnPrimaryText: { color: '#fff', fontWeight: '700' },
});
