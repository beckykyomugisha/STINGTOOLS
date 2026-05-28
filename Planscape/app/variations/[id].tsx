// ══════════════════════════════════════════════════════════════════════════
//  Variation detail — Phase 184i / P7.
//  Field-level approval surface. The user reviews the variation,
//  optionally adds a rationale, and chooses Approve / Reject / Hold.
//  Offline-safe: the mutation queues to the existing offlineQueue
//  (ApproveVariation action type added in this commit).
// ══════════════════════════════════════════════════════════════════════════
import React, { useCallback, useEffect, useState } from "react";
import {
  View, Text, StyleSheet, ScrollView, TouchableOpacity,
  ActivityIndicator, Alert, TextInput,
} from "react-native";
import { useLocalSearchParams, useRouter } from "expo-router";
import { apiFetch } from "@/api/client";
import { useProjectStore } from "@/stores/projectStore";

interface VariationItem {
  description: string;
  unit: string;
  quantity: number;
  unitRate: number;
  rateSource: string;
  totalValue: number;
}

interface Variation {
  id: string;
  number: string;
  kind: string;
  status: string;
  title: string;
  description: string;
  totalValue: number;
  currency: string;
  items: VariationItem[];
  instructionDate: string;
  approvalDate?: string;
  approvedBy?: string;
  issuedBy?: string;
  // Phase 184o — reason / liability / EOT
  reason?: string;
  liability?: string;
  reasonDetail?: string;
  eotDays?: number;
  // Phase 184q — contract family (JCT2024 / NEC4 / FIDIC2017Yellow / ...).
  contractForm?: string;
}

export default function VariationDetail() {
  const router = useRouter();
  const { id } = useLocalSearchParams<{ id: string }>();
  const projectId = useProjectStore((s) => s.activeProjectId);
  const [v, setV] = useState<Variation | null>(null);
  const [loading, setLoading] = useState(true);
  const [rationale, setRationale] = useState("");
  const [submitting, setSubmitting] = useState(false);

  const load = useCallback(async () => {
    if (!projectId || !id) return;
    setLoading(true);
    try {
      const data = await apiFetch<Variation>(`/api/projects/${projectId}/boq/variations/${id}`);
      setV(data);
    } catch (e: any) {
      Alert.alert("Load failed", e?.message ?? "Could not load variation");
    } finally {
      setLoading(false);
    }
  }, [projectId, id]);

  useEffect(() => { load(); }, [load]);

  const advance = async (newStatus: "Approved" | "Rejected" | "Reviewed") => {
    if (!v) return;
    setSubmitting(true);
    try {
      await apiFetch(`/api/projects/${projectId}/boq/variations/${v.id}/status`, {
        method: "PUT",
        body: JSON.stringify({ status: newStatus, rationale }),
      });
      Alert.alert("Done", `Variation ${v.number} marked ${newStatus}.`);
      router.back();
    } catch (e: any) {
      Alert.alert("Update failed", e?.message ?? "Could not update status");
    } finally {
      setSubmitting(false);
    }
  };

  if (loading) {
    return (
      <View style={styles.center}>
        <ActivityIndicator size="large" />
      </View>
    );
  }
  if (!v) {
    return (
      <View style={styles.center}>
        <Text style={styles.errorText}>Variation not found.</Text>
      </View>
    );
  }

  const canApprove = v.status !== "Approved" && v.status !== "Rejected" && v.status !== "Incorporated";

  return (
    <ScrollView style={styles.container} contentContainerStyle={{ padding: 14 }}>
      <Text style={styles.number}>{v.number}</Text>
      <Text style={styles.kind}>
        {v.kind} · {v.status}
        {v.contractForm ? ` · ${prettifyContractForm(v.contractForm)}` : ""}
      </Text>

      <View style={styles.card}>
        <Text style={styles.title}>{v.title}</Text>
        <Text style={styles.body}>{v.description}</Text>
      </View>

      <View style={styles.card}>
        <Text style={styles.cardLabel}>Total value</Text>
        <Text style={styles.amount}>
          {v.currency} {v.totalValue.toLocaleString(undefined, { maximumFractionDigits: 0 })}
        </Text>
        <Text style={styles.meta}>
          Instructed {new Date(v.instructionDate).toLocaleDateString()}
          {v.issuedBy ? ` by ${v.issuedBy}` : ""}
        </Text>
      </View>

      {/* Phase 184o — reason / liability / EOT card. Surfaces why this VO
          arose and who pays, so the reviewer can sense-check before
          approving. Free-text rationale shown below if supplied. */}
      {(v.reason || v.liability || v.eotDays) && (
        <View style={styles.card}>
          <Text style={styles.cardLabel}>Reason · Liability · Time impact</Text>
          <View style={detailStyles.badgeRow}>
            {v.reason && (
              <View style={[detailStyles.badge, { backgroundColor: "#1c70d8" }]}>
                <Text style={detailStyles.badgeText}>{prettifyReason(v.reason)}</Text>
              </View>
            )}
            {v.liability && (
              <View style={[detailStyles.badge, { backgroundColor: "#4b5563" }]}>
                <Text style={detailStyles.badgeText}>Pays: {v.liability}</Text>
              </View>
            )}
            {v.eotDays && v.eotDays > 0 ? (
              <View style={[detailStyles.badge, { backgroundColor: "#dc2626" }]}>
                <Text style={detailStyles.badgeText}>+{v.eotDays}d EOT</Text>
              </View>
            ) : null}
          </View>
          {v.reasonDetail ? (
            <Text style={[styles.body, { marginTop: 8 }]}>{v.reasonDetail}</Text>
          ) : null}
        </View>
      )}

      <Text style={styles.sectionLabel}>Items ({v.items.length})</Text>
      {v.items.map((it, idx) => (
        <View key={idx} style={styles.item}>
          <Text style={styles.itemDesc}>{it.description}</Text>
          <View style={styles.itemRow}>
            <Text style={styles.itemMeta}>
              {it.quantity} {it.unit} × {v.currency} {it.unitRate}
            </Text>
            <Text style={styles.itemAmount}>
              {v.currency} {it.totalValue.toLocaleString(undefined, { maximumFractionDigits: 0 })}
            </Text>
          </View>
          {it.rateSource ? <Text style={styles.itemSource}>{it.rateSource}</Text> : null}
        </View>
      ))}

      {canApprove && (
        <>
          <Text style={styles.sectionLabel}>Rationale (optional)</Text>
          <TextInput
            value={rationale}
            onChangeText={setRationale}
            placeholder="Add a note that will be attached to the approval / rejection"
            multiline
            numberOfLines={3}
            style={styles.input}
          />

          <View style={styles.actions}>
            <TouchableOpacity
              style={[styles.btn, styles.btnApprove]}
              onPress={() => advance("Approved")}
              disabled={submitting}
            >
              <Text style={styles.btnText}>Approve</Text>
            </TouchableOpacity>
            <TouchableOpacity
              style={[styles.btn, styles.btnReject]}
              onPress={() => advance("Rejected")}
              disabled={submitting}
            >
              <Text style={styles.btnText}>Reject</Text>
            </TouchableOpacity>
          </View>
          <TouchableOpacity
            style={[styles.btn, styles.btnHold]}
            onPress={() => advance("Reviewed")}
            disabled={submitting}
          >
            <Text style={styles.btnText}>Mark reviewed</Text>
          </TouchableOpacity>
        </>
      )}

      {!canApprove && v.approvalDate && (
        <View style={styles.card}>
          <Text style={styles.cardLabel}>Approval</Text>
          <Text style={styles.body}>
            {v.status} on {new Date(v.approvalDate).toLocaleDateString()}
            {v.approvedBy ? ` by ${v.approvedBy}` : ""}.
          </Text>
        </View>
      )}
    </ScrollView>
  );
}

// Phase 184o — shared with the list screen. "DesignChange" → "Design Change".
function prettifyReason(reason: string): string {
  return reason.replace(/([A-Z])/g, " $1").trim();
}

// Phase 184q — humanise contract-form enum values.
function prettifyContractForm(form: string): string {
  switch (form) {
    case "JCT2024":         return "JCT 2024";
    case "JCT2016":         return "JCT 2016";
    case "NEC4":            return "NEC4";
    case "FIDIC2017Red":    return "FIDIC 2017 Red";
    case "FIDIC2017Yellow": return "FIDIC 2017 Yellow";
    case "FIDIC2017Silver": return "FIDIC 2017 Silver";
    case "GCWorks":         return "GC/Works";
    case "Bespoke":         return "Bespoke";
    default:                return form;
  }
}

const detailStyles = StyleSheet.create({
  badgeRow: { flexDirection: "row", flexWrap: "wrap", gap: 6 },
  badge: { paddingHorizontal: 8, paddingVertical: 3, borderRadius: 4, marginRight: 6, marginBottom: 4 },
  badgeText: { color: "white", fontSize: 11, fontWeight: "600" },
});

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: "#f6f7f9" },
  center: { flex: 1, alignItems: "center", justifyContent: "center" },
  errorText: { color: "#b3261e" },
  number: { fontSize: 22, fontWeight: "700" },
  kind: { fontSize: 13, color: "#5a5a5a", marginBottom: 12 },
  card: {
    backgroundColor: "white", borderRadius: 10, padding: 14, marginBottom: 12,
    shadowColor: "#000", shadowOpacity: 0.06, shadowRadius: 4,
    shadowOffset: { width: 0, height: 1 }, elevation: 1,
  },
  cardLabel: { fontSize: 12, color: "#5a5a5a", marginBottom: 4, textTransform: "uppercase" },
  title: { fontSize: 16, fontWeight: "600", marginBottom: 6 },
  body: { fontSize: 14, color: "#333" },
  amount: { fontSize: 22, fontWeight: "700", marginBottom: 4 },
  meta: { fontSize: 12, color: "#5a5a5a" },
  sectionLabel: { fontSize: 14, fontWeight: "600", marginTop: 12, marginBottom: 8, color: "#5a5a5a" },
  item: { backgroundColor: "white", borderRadius: 8, padding: 12, marginBottom: 8 },
  itemDesc: { fontSize: 14, marginBottom: 4 },
  itemRow: { flexDirection: "row", justifyContent: "space-between" },
  itemMeta: { fontSize: 12, color: "#5a5a5a" },
  itemAmount: { fontSize: 13, fontWeight: "600" },
  itemSource: { fontSize: 11, color: "#8a8a8a", marginTop: 2 },
  input: {
    backgroundColor: "white", borderRadius: 8, padding: 10, fontSize: 14,
    borderWidth: 1, borderColor: "#dcdcdc", textAlignVertical: "top", marginBottom: 12,
  },
  actions: { flexDirection: "row", gap: 10 },
  btn: { flex: 1, padding: 14, borderRadius: 8, alignItems: "center", marginBottom: 8 },
  btnApprove: { backgroundColor: "#0a7d2e" },
  btnReject:  { backgroundColor: "#b3261e" },
  btnHold:    { backgroundColor: "#5a5a5a" },
  btnText: { color: "white", fontWeight: "600", fontSize: 15 },
});
