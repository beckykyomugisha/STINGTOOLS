// MODEL-VIEWER — single-model viewer screen.
//
// Wraps <ModelViewer /> and wires:
//   - GET /models/{id}          → metadata
//   - GET /models/{id}/file     → geometry URL (passed to the WebView)
//   - GET /models/{id}/element-map (optional sidecar)
//   - SignalR: listens for IssueCreated to refresh pins
//   - Long-press in viewer → opens issue-create modal prefilled with element GUID

import { useEffect, useRef, useState } from "react";
import { View, Text, Alert, StyleSheet } from "react-native";
import { useLocalSearchParams, useRouter, Stack } from "expo-router";
import {
  ModelViewer,
  ModelViewerHandle,
  type PickEvent,
  type PlaceIssueEvent,
} from "@/components/ModelViewer";
import { useProjectStore } from "@/stores/projectStore";
import { getModel, modelFileUrl, fetchElementMap } from "@/api/models";
import { listIssues } from "@/api/endpoints";
import { getToken } from "@/api/client";
import type { ModelMeta, ElementMap, ModelPin } from "@/types/models";

export default function ModelViewerScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const router = useRouter();
  const projectId = useProjectStore((s) => s.activeProjectId);
  const viewerRef = useRef<ModelViewerHandle>(null);

  const [meta, setMeta] = useState<ModelMeta | null>(null);
  const [modelUrl, setModelUrl] = useState<string | undefined>();
  const [elementMap, setElementMap] = useState<ElementMap | undefined>();
  const [pins, setPins] = useState<ModelPin[]>([]);
  const [error, setError] = useState<string | null>(null);

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

  function onPick(e: PickEvent) {
    if (!e.meta) return;
    const tag = e.meta.tag ?? e.name ?? e.guid.slice(0, 8);
    Alert.alert(tag, [
      e.meta.category && `Category: ${e.meta.category}`,
      e.meta.discipline && `Discipline: ${e.meta.discipline}`,
      e.meta.level && `Level: ${e.meta.level}`,
    ].filter(Boolean).join("\n") || "No metadata available.");
  }

  function onPlaceIssue(e: PlaceIssueEvent) {
    if (!projectId || !id) return;
    router.push({
      pathname: "/issues/new",
      params: {
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
    router.push(`/issues/${e.issueId}`);
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
          onPick={onPick}
          onPlaceIssue={onPlaceIssue}
          onPinTap={onPinTap}
          onMeasure={(m) => console.log("[viewer] distance", m.distance)}
          onError={setError}
        />
      </View>
    </>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1, backgroundColor: "#1a1a1a" },
  center: { flex: 1, alignItems: "center", justifyContent: "center", padding: 40 },
  errorTitle: { fontSize: 18, fontWeight: "700", marginBottom: 8, color: "#d32f2f" },
  errorBody: { color: "#666", textAlign: "center" },
});
