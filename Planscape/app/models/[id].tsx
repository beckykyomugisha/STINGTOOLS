// MODEL-VIEWER — single-model viewer screen.
//
// Wraps <ModelViewer /> and wires:
//   - GET /models/{id}          → metadata
//   - GET /models/{id}/file     → geometry URL (passed to the WebView)
//   - GET /models/{id}/element-map (optional sidecar)
//   - SignalR: listens for IssueCreated to refresh pins
//   - Long-press in viewer → opens issue-create modal prefilled with element GUID

import { useEffect, useRef, useState } from "react";
import { View, Text, Alert, StyleSheet, TouchableOpacity } from "react-native";
import { useLocalSearchParams, useRouter, Stack } from "expo-router";
import {
  ModelViewer,
  ModelViewerHandle,
  type PickEvent,
  type PlaceIssueEvent,
  type BcfViewpoint,
} from "@/components/ModelViewer";
import { useProjectStore } from "@/stores/projectStore";
import { getModel, modelFileUrl, fetchElementMap, fetchHeatmap } from "@/api/models";
import { listIssues } from "@/api/endpoints";
import { getToken } from "@/api/client";
import type { ModelMeta, ElementMap, ModelPin } from "@/types/models";

export default function ModelViewerScreen() {
  const { id, highlightElement, issueId } = useLocalSearchParams<{
    id: string;
    highlightElement?: string;
    issueId?: string;
  }>();
  const router = useRouter();
  const projectId = useProjectStore((s) => s.activeProjectId);
  const viewerRef = useRef<ModelViewerHandle>(null);

  const [meta, setMeta] = useState<ModelMeta | null>(null);
  const [modelUrl, setModelUrl] = useState<string | undefined>();
  const [elementMap, setElementMap] = useState<ElementMap | undefined>();
  const [pins, setPins] = useState<ModelPin[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [walkActive, setWalkActive] = useState(false);
  const [sectionEnabled, setSectionEnabled] = useState(false);
  const [heatmapActive, setHeatmapActive] = useState(false);

  useEffect(() => {
    if (!projectId || !id) return;
    (async () => {
      try {
        const [m, token] = await Promise.all([
          getModel(projectId, id),
          getToken(),
        ]);
        setMeta(m);

        // WebView doesn't forward Authorization automatically — append the
        // token as a query parameter so the viewer can fetch directly.
        const base = await modelFileUrl(projectId, id);
        setModelUrl(token ? `${base}?access_token=${encodeURIComponent(token)}` : base);

        if (m.hasElementMap) {
          try {
            const map = await fetchElementMap(projectId, id) as ElementMap;
            setElementMap(map);
          } catch (err) {
            console.warn("[viewer] element map fetch failed", err);
          }
        }

        // Load existing issue pins for this model.
        try {
          const issues = await listIssues(projectId);
          const modelPins: ModelPin[] = issues
            .filter((i) => i.modelId === id && i.modelX != null && i.modelY != null && i.modelZ != null)
            .map((i) => ({
              id: i.id,
              x: i.modelX!,
              y: i.modelY!,
              z: i.modelZ!,
              priority: (i.priority as ModelPin["priority"]) ?? "MEDIUM",
            }));
          setPins(modelPins);
        } catch (err) {
          console.warn("[viewer] issues fetch failed", err);
        }
      } catch (err) {
        setError(String(err));
      }
    })();
  }, [projectId, id]);

  async function toggleHeatmap() {
    if (!projectId) return;
    if (heatmapActive) {
      viewerRef.current?.clearHeatmap();
      setHeatmapActive(false);
      return;
    }
    try {
      const data = await fetchHeatmap(projectId);
      viewerRef.current?.setHeatmap(data.elements, "rag");
      setHeatmapActive(true);
    } catch (err) {
      Alert.alert("Heatmap", "Could not load compliance data.");
    }
  }

  function onPick(e: PickEvent) {
    const tag = e.meta?.tag ?? e.name ?? e.guid.slice(0, 8);
    const lines = [
      e.meta?.category   && `Category: ${e.meta.category}`,
      e.meta?.discipline && `Discipline: ${e.meta.discipline}`,
      e.meta?.level      && `Level: ${e.meta.level}`,
      e.meta?.system     && `System: ${e.meta.system}`,
      e.meta?.status     && `Status: ${e.meta.status}`,
      `GUID: ${e.guid.slice(0, 8)}…`,
    ].filter(Boolean).join("\n");
    Alert.alert(tag, lines || "No metadata available.");
  }

  function onPlaceIssue(e: PlaceIssueEvent) {
    if (!projectId || !id) return;
    // Phase 163 — pre-Phase-163 this pushed to "/issues/new", a route that
    // doesn't exist. Now routes through the (tabs)/issues creation modal
    // via ?fromViewer=1, which reads the anchor params and pre-fills the
    // new-issue form (see app/(tabs)/issues.tsx Phase 163 deep-link effect).
    router.push({
      pathname: "/(tabs)/issues",
      params: {
        fromViewer: "1",
        modelId: id,
        modelElementGuid: e.guid,
        modelX: String(e.point[0]),
        modelY: String(e.point[1]),
        modelZ: String(e.point[2]),
        tag: e.meta?.tag ?? "",
        category: e.meta?.category ?? "",
        discipline: e.meta?.discipline ?? "",
      },
    });
  }

  function onPinTap(e: { issueId: string }) {
    // Phase 163 — pin tap routes through the (tabs)/issue-detail screen
    // (which is what every other issue link uses) instead of "/issues/<id>"
    // which only the legacy app/issues/[id].tsx legacy detail screen
    // handles. Keeps deep-link conventions consistent across the app.
    router.push(`/(tabs)/issue-detail?id=${e.issueId}`);
  }

  if (error) {
    return (
      <View style={styles.center}>
        <Text style={styles.errorTitle}>Couldn't load the model</Text>
        <Text style={styles.errorBody}>{error}</Text>
      </View>
    );
  }

  return (
    <>
      <Stack.Screen options={{ title: meta?.name ?? "Model", headerBackTitle: "Models" }} />
      <View style={styles.flex}>
        <ModelViewer
          ref={viewerRef}
          modelUrl={modelUrl}
          elementMap={elementMap}
          pins={pins}
          onReady={() => {}}
          onPick={onPick}
          onPlaceIssue={onPlaceIssue}
          onPinTap={onPinTap}
          onMeasure={(m) => console.log("[viewer] distance", m.distance)}
          onMeasureArea={(m) => Alert.alert("Area", `${(m.area / 1_000_000).toFixed(2)} m²`)}
          onMeasureVolume={(m) => Alert.alert("Volume", `${(m.volume / 1_000_000_000).toFixed(3)} m³`)}
          onWalkthrough={(e) => setWalkActive(e.active)}
          onError={setError}
        />
        <ExtraToolbar viewerRef={viewerRef} walkActive={walkActive}
          sectionEnabled={sectionEnabled} setSectionEnabled={setSectionEnabled}
          heatmapActive={heatmapActive} onToggleHeatmap={toggleHeatmap} />
      </View>
    </>
  );
}

function ExtraToolbar({ viewerRef, walkActive, sectionEnabled, setSectionEnabled,
  heatmapActive, onToggleHeatmap }: {
  viewerRef: React.RefObject<ModelViewerHandle>;
  walkActive: boolean;
  sectionEnabled: boolean;
  setSectionEnabled: (v: boolean) => void;
  heatmapActive: boolean;
  onToggleHeatmap: () => void;
}) {
  const [areaActive, setAreaActive] = useState(false);
  const cell = (label: string, active: boolean, onPress: () => void) => (
    <TouchableOpacity onPress={onPress} style={[styles.tb, active && styles.tbActive]}>
      <Text style={styles.tbLabel}>{label}</Text>
    </TouchableOpacity>
  );
  return (
    <View style={styles.toolbar}>
      {cell("Walk", walkActive, () => viewerRef.current?.setWalkthrough(!walkActive))}
      {cell("Section", sectionEnabled, () => {
        const next = !sectionEnabled;
        setSectionEnabled(next);
        viewerRef.current?.setSectionPlane({ enabled: next, normal: [0, -1, 0], offset: 0.5 });
      })}
      {cell(areaActive ? "Done" : "Area", areaActive, () => {
        if (areaActive) { viewerRef.current?.finishArea(); setAreaActive(false); }
        else            { viewerRef.current?.startArea();  setAreaActive(true); }
      })}
      {cell("Vol", false, () => viewerRef.current?.measureSelectionVolume())}
      {cell(heatmapActive ? "RAG ✓" : "RAG", heatmapActive, onToggleHeatmap)}
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1, backgroundColor: "#1a1a1a" },
  center: { flex: 1, alignItems: "center", justifyContent: "center", padding: 40 },
  errorTitle: { fontSize: 18, fontWeight: "700", marginBottom: 8, color: "#d32f2f" },
  errorBody: { color: "#666", textAlign: "center" },
  toolbar: {
    position: "absolute", top: 12, right: 12, flexDirection: "row", gap: 6,
    backgroundColor: "rgba(0,0,0,0.55)", borderRadius: 18, padding: 4,
  },
  tb: { paddingHorizontal: 10, paddingVertical: 6, borderRadius: 14 },
  tbActive: { backgroundColor: "#E8912D" },
  tbLabel: { color: "#fff", fontSize: 12, fontWeight: "600" },
});
