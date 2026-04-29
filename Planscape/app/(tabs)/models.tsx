// MODEL-VIEWER — list of 3D models for the current project.

import { useEffect, useState } from "react";
import {
  View,
  Text,
  FlatList,
  TouchableOpacity,
  RefreshControl,
  Image,
  ActivityIndicator,
  StyleSheet,
} from "react-native";
import { useRouter } from "expo-router";
import { listModels, modelThumbnailUrl } from "@/api/models";
import { useProjectStore } from "@/stores/projectStore";
import type { ModelMeta } from "@/types/models";
import { theme } from "@/utils/theme";
import { t } from "@/i18n";

export default function ModelsList() {
  const router = useRouter();
  const projectId = useProjectStore((s) => s.activeProjectId);
  const [models, setModels] = useState<ModelMeta[]>([]);
  const [refreshing, setRefreshing] = useState(false);
  const [loading, setLoading] = useState(true);

  async function load() {
    if (!projectId) return;
    try {
      const rows = await listModels(projectId);
      setModels(rows);
    } catch (err) {
      console.warn("[models] list failed:", err);
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }

  useEffect(() => { load(); }, [projectId]);

  if (!projectId) {
    return (
      <View style={styles.empty}>
        <Text style={styles.emptyText}>Select a project first.</Text>
      </View>
    );
  }
  if (loading) {
    return <View style={styles.empty}><ActivityIndicator /></View>;
  }
  if (models.length === 0) {
    return (
      <View style={styles.empty}>
        <Text style={styles.emptyTitle}>No models yet</Text>
        <Text style={styles.emptyText}>
          Publish a glTF/GLB from the Revit plugin ("Publish Model to Planscape")
          or upload one from the web dashboard.
        </Text>
      </View>
    );
  }

  return (
    <FlatList
      data={models}
      keyExtractor={(m) => m.id}
      refreshControl={
        <RefreshControl refreshing={refreshing} onRefresh={() => { setRefreshing(true); load(); }} />
      }
      renderItem={({ item }) => (
        <ModelRow
          model={item}
          projectId={projectId}
          onPress={() => router.push(`/models/${item.id}`)}
        />
      )}
      ItemSeparatorComponent={() => <View style={styles.sep} />}
    />
  );
}

function ModelRow({
  model, projectId, onPress,
}: { model: ModelMeta; projectId: string; onPress: () => void }) {
  const [thumb, setThumb] = useState<string | null>(null);

  useEffect(() => {
    if (!model.hasThumbnail) return;
    modelThumbnailUrl(projectId, model.id).then(setThumb);
  }, [model.hasThumbnail, model.id, projectId]);

  return (
    <TouchableOpacity onPress={onPress} style={styles.row}>
      <View style={styles.thumb}>
        {thumb ? (
          <Image source={{ uri: thumb }} style={styles.thumbImg} />
        ) : (
          <Text style={styles.thumbPlaceholder}>
            {model.format === "Glb" || model.format === "Gltf" ? "🧊" : "📦"}
          </Text>
        )}
      </View>
      <View style={styles.body}>
        <Text style={styles.title} numberOfLines={1}>{model.name}</Text>
        <Text style={styles.subtitle} numberOfLines={1}>
          {model.format} · {formatBytes(model.fileSizeBytes)}
          {model.discipline ? ` · ${model.discipline}` : ""}
          {model.revision ? ` · ${model.revision}` : ""}
        </Text>
        <Text style={styles.meta} numberOfLines={1}>
          by {model.uploadedBy || "—"} · {formatDate(model.uploadedAt)}
        </Text>
      </View>
    </TouchableOpacity>
  );
}

function formatBytes(n: number): string {
  if (n < 1024) return `${n} B`;
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(0)} KB`;
  return `${(n / 1024 / 1024).toFixed(1)} MB`;
}
function formatDate(iso: string): string {
  try {
    const d = new Date(iso);
    return d.toLocaleDateString();
  } catch { return iso; }
}

const styles = StyleSheet.create({
  empty: { flex: 1, justifyContent: "center", alignItems: "center", padding: 40 },
  emptyTitle: { fontSize: 18, fontWeight: "600", marginBottom: 8, color: "#333" },
  emptyText: { color: "#666", textAlign: "center", fontSize: 14, lineHeight: 20 },
  row: { flexDirection: "row", padding: 12, alignItems: "center", backgroundColor: "#fff" },
  thumb: { width: 72, height: 72, borderRadius: 8, backgroundColor: "#eee", alignItems: "center", justifyContent: "center", overflow: "hidden" },
  thumbImg: { width: "100%", height: "100%" },
  thumbPlaceholder: { fontSize: 32 },
  body: { flex: 1, marginLeft: 12 },
  title: { fontSize: 15, fontWeight: "600", color: "#222" },
  subtitle: { fontSize: 12, color: "#666", marginTop: 2 },
  meta: { fontSize: 11, color: "#999", marginTop: 4 },
  sep: { height: 1, backgroundColor: "#eee" },
});
