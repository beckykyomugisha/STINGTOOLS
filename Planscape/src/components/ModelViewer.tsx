// MODEL-VIEWER — WebView-hosted three.js viewer + RN bridge.
//
// Hosts `assets/viewer/viewer.html` in a WebView, posts commands to load the
// model + element map, and emits typed events back to RN (`onPick`,
// `onPlaceIssue`, `onMeasure`, `onPinTap`). Zero native-module dependencies —
// ships in Expo Go as soon as `react-native-webview` is installed.
//
// Usage:
//
//   <ModelViewer
//     modelUrl={url}
//     elementMap={map}
//     pins={pins}
//     onPick={(e)        => console.log("picked", e.guid)}
//     onPlaceIssue={(e)  => openIssueModal(e)}
//     onPinTap={(e)      => navigate(`/issues/${e.issueId}`)}
//     onMeasure={(e)     => console.log("distance", e.distance)}
//   />

import React, { useEffect, useRef, useState } from "react";
import { StyleSheet, View, ActivityIndicator, Text, Platform } from "react-native";
import { WebView, WebViewMessageEvent } from "react-native-webview";
import { Asset } from "expo-asset";
import type { ElementMap, ModelPin, ViewerTool } from "@/types/models";

export interface ModelViewerHandle {
  setTool: (tool: ViewerTool) => void;
  fit: () => void;
  clearMeasure: () => void;
  clearHighlight: () => void;
  setDisciplineVisible: (discipline: string, visible: boolean) => void;
  setPins: (pins: ModelPin[]) => void;
  addPin: (pin: ModelPin) => void;
  clearPins: () => void;
  setBackground: (color: number) => void;
  // Federation: load multiple GLBs at once (one per discipline).
  loadFederation: (sources: Array<{ url: string; label?: string; discipline?: string }>) => void;
  // Per-model visibility toggle for federated scenes.
  setModelVisible: (label: string, visible: boolean) => void;
  // Oblique section: arbitrary normal direction + 0..1 offset across bounds.
  setSectionPlane: (opts: { enabled: boolean; normal?: [number, number, number]; offset?: number }) => void;
  // Multi-plane section: add / update / remove individual clipping planes.
  addSectionPlaneAxis: (axis: 'x' | 'y' | 'z' | 'free', offset?: number) => void;
  updateSectionPlane: (id: number, offset: number) => void;
  removeSectionPlane: (id: number) => void;
  clearSectionPlanes: () => void;
  // Section box: 6-plane AABB clip with optional inset (0=tight bounds).
  setSectionBox: (opts?: { inset?: number }) => void;
  updateSectionBoxFace: (faceIndex: number, offsetMm: number) => void;
  clearSectionBox: () => void;
  // Exploded view: factor 0=collapsed, 1=fully exploded.
  setExplodeFactor: (factor: number) => void;
  // Markup canvas overlay (draw/arrow/text/rect; null to exit markup mode).
  startMarkup: (mode: 'draw' | 'arrow' | 'text' | 'rect' | null) => void;
  clearMarkup: () => void;
  // First-person walkthrough.
  setWalkthrough: (enabled: boolean) => void;
  // Polygon area measurement.
  startArea: () => void;
  addAreaPoint: (point: [number, number, number]) => void;
  finishArea: () => void;
  // Volume of the currently highlighted element's AABB.
  measureSelectionVolume: () => void;
  // Select a mesh by its elementGuid, highlight it, and zoom the camera in.
  selectAndZoom: (guid: string) => void;
  // Per-model opacity (federation overlay fade, 0..1).
  setModelOpacity: (label: string, opacity: number) => void;
  // Solo a single federation model (hide all others). Pass null to clear.
  setModelSolo: (label: string | null) => void;
  // Isolate a set of element GUIDs (hide everything else). Empty/null clears.
  setIsolatedGuids: (guids: string[] | null) => void;
  clearIsolation: () => void;
  // Snap camera to an orthogonal or iso preset.
  setCameraPreset: (preset: 'top' | 'bottom' | 'front' | 'back' | 'left' | 'right' | 'iso' | 'home') => void;
  // Camera bookmark slots (1..4) — save current position, jump to it later.
  saveCameraBookmark: (slot: number) => void;
  restoreCameraBookmark: (slot: number) => void;
  // Render mode — applies to whole modelRoot.
  setRenderMode: (mode: 'shaded' | 'wireframe' | 'xray' | 'ghost') => void;
  // Edge silhouette overlay (wireframe-on-shaded depth perception).
  setEdgeOverlay: (enabled: boolean) => void;
  // Section caps — fill cut surfaces of active section planes.
  setSectionCaps: (enabled: boolean) => void;
  // Stream cursor-XYZ on pointermove (heavy — enable only while UI consumes).
  setCoordReadout: (enabled: boolean) => void;
  // Multi-segment path measurement (cumulative distance).
  startCumulativeMeasure: () => void;
  addCumulativePoint: (point: [number, number, number]) => void;
  finishCumulativeMeasure: () => void;
  clearCumulativeMeasure: () => void;
  // Full view-state snapshot — captures camera + sections + visibility + render mode.
  // The result arrives back via the `onViewState` callback.
  captureViewState: () => void;
  restoreViewState: (state: object) => void;
}

interface ModelViewerProps {
  /**
   * URL the WebView should fetch to load the glTF/GLB. Include an auth
   * token in the URL if your server endpoint requires it — WebView doesn't
   * forward the app's Authorization header automatically.
   */
  modelUrl?: string;
  /** Optional element metadata keyed by GUID. */
  elementMap?: ElementMap;
  /** Pins to show at XYZ positions in model space. */
  pins?: ModelPin[];
  /** Initial tool. Default "pick". */
  initialTool?: ViewerTool;

  onReady?: () => void;
  onLoaded?: (meta: { elementCount: number; bounds: number[] }) => void;
  onPick?: (e: PickEvent) => void;
  onPlaceIssue?: (e: PlaceIssueEvent) => void;
  onPinTap?: (e: { issueId: string; priority?: string }) => void;
  onMeasure?: (e: { distance: number; points: number[][] }) => void;
  onMeasureArea?: (e: { area: number; points: number[][] }) => void;
  onMeasureVolume?: (e: { volume: number; size: number[]; bounds: number[] }) => void;
  onMeasureAngle?: (e: { angle: number; vertex: number[]; a: number[]; b: number[] }) => void;
  onWalkthrough?: (e: { active: boolean }) => void;
  onLodChanged?: (e: { level: number; avgFps: number }) => void;
  onToolChanged?: (tool: ViewerTool) => void;
  onSectionPlanesChanged?: (e: { count: number; planes: Array<{ id: number; axis: string; offset: number }> }) => void;
  onSectionBoxSet?: (e: { faces: number[] }) => void;
  onExplodeFactor?: (e: { factor: number }) => void;
  onMarkupUpdated?: (e: { count: number }) => void;
  onMarkupCleared?: () => void;
  onCoord?: (e: { hit: boolean; point?: [number, number, number]; off?: boolean }) => void;
  onMeasurePath?: (e: { segment: number; total: number; points: number[][] }) => void;
  onMeasurePathFinal?: (e: { total: number; points: number[][] }) => void;
  onCameraPreset?: (e: { preset: string }) => void;
  onBookmarkSaved?: (e: { slot: number; slots: number[] }) => void;
  onBookmarkRestored?: (e: { slot: number }) => void;
  onRenderMode?: (e: { mode: string }) => void;
  onViewState?: (state: object) => void;
  onViewStateRestored?: (e: { keys: string[] }) => void;
  onError?: (err: string) => void;
}

export interface PickEvent {
  guid: string;
  point: [number, number, number];
  name?: string;
  meta?: ElementMap[string] | null;
}
export interface PlaceIssueEvent {
  guid: string;
  point: [number, number, number];
  meta?: ElementMap[string] | null;
}

const VIEWER_ASSET = require("../../assets/viewer/viewer.html");

export const ModelViewer = React.forwardRef<ModelViewerHandle, ModelViewerProps>(
  function ModelViewer(props, ref) {
    const webRef = useRef<WebView>(null);
    const [localUri, setLocalUri] = useState<string | null>(null);
    const [isReady, setIsReady] = useState(false);

    // Resolve the bundled viewer.html to a file:// URI the WebView can load.
    useEffect(() => {
      let mounted = true;
      (async () => {
        const asset = Asset.fromModule(VIEWER_ASSET);
        await asset.downloadAsync();
        if (mounted) setLocalUri(asset.localUri ?? asset.uri);
      })();
      return () => { mounted = false; };
    }, []);

    // Drain queued commands once the viewer says it's ready.
    useEffect(() => {
      if (!isReady || !webRef.current) return;
      if (props.modelUrl) {
        send({ type: "load", payload: { url: props.modelUrl } });
      }
      if (props.elementMap) {
        send({ type: "elementMap", payload: { map: props.elementMap } });
      }
      if (props.pins) {
        send({ type: "setPins", payload: props.pins });
      }
      if (props.initialTool) {
        send({ type: "setTool", payload: { tool: props.initialTool } });
      }
    }, [isReady, props.modelUrl]);

    // Keep pins + element map in sync when they change post-ready.
    useEffect(() => {
      if (!isReady) return;
      if (props.elementMap) send({ type: "elementMap", payload: { map: props.elementMap } });
    }, [isReady, props.elementMap]);
    useEffect(() => {
      if (!isReady) return;
      if (props.pins) send({ type: "setPins", payload: props.pins });
    }, [isReady, props.pins]);

    function send(cmd: { type: string; payload?: unknown }) {
      const json = JSON.stringify(cmd).replace(/\\/g, "\\\\").replace(/'/g, "\\'");
      // Both platforms — iOS WebView listens on window, Android on document.
      const js = `
        (function() {
          var msg = '${json}';
          try { window.dispatchEvent(new MessageEvent('message', { data: msg })); } catch(e) {}
          try { document.dispatchEvent(new MessageEvent('message', { data: msg })); } catch(e) {}
        })();
        true;
      `;
      webRef.current?.injectJavaScript(js);
    }

    React.useImperativeHandle(ref, () => ({
      setTool: (tool) => send({ type: "setTool", payload: { tool } }),
      fit: () => send({ type: "fit" }),
      clearMeasure: () => send({ type: "clearMeasure" }),
      clearHighlight: () => send({ type: "clearHighlight" }),
      setDisciplineVisible: (discipline, visible) =>
        send({ type: "setDiscipline", payload: { discipline, visible } }),
      setPins: (pins) => send({ type: "setPins", payload: pins }),
      addPin: (pin) => send({ type: "addPin", payload: pin }),
      clearPins: () => send({ type: "clearPins" }),
      setBackground: (color) => send({ type: "setBackground", payload: { color } }),
      loadFederation: (sources) => send({ type: "loadFederation", payload: { sources } }),
      setModelVisible: (label, visible) =>
        send({ type: "setModelVisible", payload: { label, visible } }),
      setSectionPlane: (opts) => send({ type: "setSectionPlane", payload: opts }),
      addSectionPlaneAxis: (axis, offset) =>
        send({ type: "addSectionPlaneAxis", payload: { axis, offset } }),
      updateSectionPlane: (id, offset) =>
        send({ type: "updateSectionPlane", payload: { id, offset } }),
      removeSectionPlane: (id) =>
        send({ type: "removeSectionPlane", payload: { id } }),
      clearSectionPlanes: () => send({ type: "clearSectionPlanes" }),
      setSectionBox: (opts) => send({ type: "setSectionBox", payload: opts ?? {} }),
      updateSectionBoxFace: (faceIndex, offsetMm) =>
        send({ type: "updateSectionBoxFace", payload: { faceIndex, offsetMm } }),
      clearSectionBox: () => send({ type: "clearSectionBox" }),
      setExplodeFactor: (factor) =>
        send({ type: "setExplodeFactor", payload: { factor } }),
      startMarkup: (mode) => send({ type: "startMarkup", payload: { mode } }),
      clearMarkup: () => send({ type: "clearMarkup" }),
      setWalkthrough: (enabled) => send({ type: "setWalkthrough", payload: { enabled } }),
      startArea: () => send({ type: "startArea" }),
      addAreaPoint: (point) => send({ type: "addAreaPoint", payload: { point } }),
      finishArea: () => send({ type: "finishArea" }),
      measureSelectionVolume: () => send({ type: "measureVolume" }),
      selectAndZoom: (guid) => send({ type: "selectAndZoom", payload: { guid } }),
      setModelOpacity: (label, opacity) =>
        send({ type: "setModelOpacity", payload: { label, opacity } }),
      setModelSolo: (label) => send({ type: "setModelSolo", payload: { label } }),
      setIsolatedGuids: (guids) =>
        send({ type: "setIsolatedGuids", payload: { guids } }),
      clearIsolation: () => send({ type: "clearIsolation" }),
      setCameraPreset: (preset) =>
        send({ type: "setCameraPreset", payload: { preset } }),
      saveCameraBookmark: (slot) =>
        send({ type: "saveCameraBookmark", payload: { slot } }),
      restoreCameraBookmark: (slot) =>
        send({ type: "restoreCameraBookmark", payload: { slot } }),
      setRenderMode: (mode) => send({ type: "setRenderMode", payload: { mode } }),
      setEdgeOverlay: (enabled) =>
        send({ type: "setEdgeOverlay", payload: { enabled } }),
      setSectionCaps: (enabled) =>
        send({ type: "setSectionCaps", payload: { enabled } }),
      setCoordReadout: (enabled) =>
        send({ type: "setCoordReadout", payload: { enabled } }),
      startCumulativeMeasure: () => send({ type: "startCumulativeMeasure" }),
      addCumulativePoint: (point) =>
        send({ type: "addCumulativePoint", payload: { point } }),
      finishCumulativeMeasure: () => send({ type: "finishCumulativeMeasure" }),
      clearCumulativeMeasure: () => send({ type: "clearCumulativeMeasure" }),
      captureViewState: () => send({ type: "captureViewState" }),
      restoreViewState: (state) => send({ type: "restoreViewState", payload: state }),
    }));

    function onMessage(ev: WebViewMessageEvent) {
      let msg: { type: string; payload?: any };
      try { msg = JSON.parse(ev.nativeEvent.data); } catch { return; }
      switch (msg.type) {
        case "ready":        setIsReady(true); props.onReady?.(); break;
        case "loaded":       props.onLoaded?.(msg.payload); break;
        case "pick":         props.onPick?.(msg.payload); break;
        case "placeIssue":   props.onPlaceIssue?.(msg.payload); break;
        case "pinTap":       props.onPinTap?.(msg.payload); break;
        case "measure":              props.onMeasure?.(msg.payload); break;
        case "measureArea":          props.onMeasureArea?.(msg.payload); break;
        case "measureVolume":        props.onMeasureVolume?.(msg.payload); break;
        case "measureAngle":         props.onMeasureAngle?.(msg.payload); break;
        case "walkthrough":          props.onWalkthrough?.(msg.payload); break;
        case "lodChanged":           props.onLodChanged?.(msg.payload); break;
        case "toolChanged":          props.onToolChanged?.(msg.payload?.tool); break;
        case "sectionPlanesChanged": props.onSectionPlanesChanged?.(msg.payload); break;
        case "sectionBoxSet":        props.onSectionBoxSet?.(msg.payload); break;
        case "explodeFactor":        props.onExplodeFactor?.(msg.payload); break;
        case "markupUpdated":        props.onMarkupUpdated?.(msg.payload); break;
        case "markupCleared":        props.onMarkupCleared?.(); break;
        case "coord":                props.onCoord?.(msg.payload); break;
        case "measurePath":          props.onMeasurePath?.(msg.payload); break;
        case "measurePathFinal":     props.onMeasurePathFinal?.(msg.payload); break;
        case "cameraPreset":         props.onCameraPreset?.(msg.payload); break;
        case "bookmarkSaved":        props.onBookmarkSaved?.(msg.payload); break;
        case "bookmarkRestored":     props.onBookmarkRestored?.(msg.payload); break;
        case "renderMode":           props.onRenderMode?.(msg.payload); break;
        case "viewState":            props.onViewState?.(msg.payload); break;
        case "viewStateRestored":    props.onViewStateRestored?.(msg.payload); break;
      }
    }

    if (!localUri) {
      return (
        <View style={styles.center}>
          <ActivityIndicator />
          <Text style={styles.hint}>Preparing viewer…</Text>
        </View>
      );
    }

    return (
      <View style={styles.flex}>
        <WebView
          ref={webRef}
          source={{ uri: localUri }}
          // M12 — narrow originWhitelist from "*" to the schemes we actually
          // load (file:// for the bundled viewer.html, https://localhost
          // for dev). The previous wildcard let any model file containing
          // an embedded URL drive a redirect to an arbitrary origin and
          // exfiltrate session data via the bridged onMessage channel.
          originWhitelist={["file://*", "https://localhost*"]}
          // M12 — disable third-party content (fonts, images, scripts from
          // remote origins). The bundled viewer is fully self-contained;
          // any uploaded BIM file that tries to fetch external resources
          // is denied at the network layer.
          thirdPartyCookiesEnabled={false}
          // Keep file-URL access for the bundled viewer.html → its sibling
          // JS / CSS / textures, but document the trade-off. If we ever
          // serve the viewer over HTTPS, both flags below should drop.
          allowFileAccessFromFileURLs
          allowUniversalAccessFromFileURLs
          allowsInlineMediaPlayback
          mediaPlaybackRequiresUserAction={false}
          javaScriptEnabled
          domStorageEnabled
          onMessage={onMessage}
          onError={(e) => props.onError?.(e.nativeEvent.description)}
          // iOS has its own inertia that fights with OrbitControls.
          scrollEnabled={false}
          bounces={false}
          // Keep touch events coming even while RN list views scroll.
          nestedScrollEnabled
          style={styles.flex}
        />
      </View>
    );
  }
);

const styles = StyleSheet.create({
  flex: { flex: 1, backgroundColor: "#1a1a1a" },
  center: { flex: 1, alignItems: "center", justifyContent: "center", backgroundColor: "#1a1a1a" },
  hint: { color: "#ddd", marginTop: 8, fontSize: 13 },
});
