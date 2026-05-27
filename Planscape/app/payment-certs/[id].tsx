// ══════════════════════════════════════════════════════════════════════════
//  Payment cert detail — Phase 184i / P7.
//  Contractor sign-off surface. Signature capture is a typed-name +
//  date stamp in this MVP — react-native-signature-canvas integration
//  is a follow-on commit.
// ══════════════════════════════════════════════════════════════════════════
import React, { useCallback, useEffect, useRef, useState } from "react";
import {
  View, Text, StyleSheet, ScrollView, TouchableOpacity,
  ActivityIndicator, Alert, TextInput, Modal,
} from "react-native";
import { useLocalSearchParams, useRouter } from "expo-router";
import { apiFetch } from "@/api/client";
import { useAuthStore } from "@/stores/authStore";
// Phase 184k caveat #4 — signature pad via react-native-signature-canvas
// (built on react-native-webview which is already a dep). The signature is
// captured as a base64 PNG and uploaded as an attachment via the
// payment-cert sign endpoint.
import SignatureScreen, { SignatureViewRef } from "react-native-signature-canvas";

interface SovLine {
  section: string;
  description: string;
  contractValue: number;
  percentComplete: number;
  previouslyCertified: number;
  materialsOnSite: number;
  grossThisCert: number;
}

interface PaymentCert {
  id: string;
  certNumber: number;
  contractRef: string;
  form: string;
  status: string;
  currency: string;
  contractorName?: string;
  employerName?: string;
  projectName?: string;
  grossValuation: number;
  effectiveRetentionPercent: number;
  retentionAmount: number;
  otherDeductions: number;
  netThisCert: number;
  vatAmount: number;
  vatPercent: number;
  totalPayable: number;
  valuationDate: string;
  lines: SovLine[];
  signedByContractor?: string;
  contractorSignedDate?: string;
  signedByEmployer?: string;
  employerSignedDate?: string;
}

export default function PaymentCertDetail() {
  const router = useRouter();
  const { id } = useLocalSearchParams<{ id: string }>();
  const projectId = useAuthStore((s) => s.activeProjectId);
  const [c, setC] = useState<PaymentCert | null>(null);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [signerName, setSignerName] = useState("");
  const [disputeNote, setDisputeNote] = useState("");
  const [showSignaturePad, setShowSignaturePad] = useState(false);
  const [pendingAction, setPendingAction] = useState<"agree" | "dispute" | null>(null);
  const [signaturePngBase64, setSignaturePngBase64] = useState<string | null>(null);
  const sigRef = useRef<SignatureViewRef>(null);

  const load = useCallback(async () => {
    if (!projectId || !id) return;
    setLoading(true);
    try {
      const data = await apiFetch<PaymentCert>(`/api/projects/${projectId}/boq/payment-certs/${id}`);
      setC(data);
    } catch (e: any) {
      Alert.alert("Load failed", e?.message ?? "Could not load certificate");
    } finally {
      setLoading(false);
    }
  }, [projectId, id]);

  useEffect(() => { load(); }, [load]);

  const sign = async (kind: "agree" | "dispute") => {
    if (!c) return;
    if (!signerName.trim()) {
      Alert.alert("Signature required", "Type your full name first.");
      return;
    }
    // Open the signature pad; actual submit happens in submitSignature.
    setPendingAction(kind);
    setShowSignaturePad(true);
  };

  // Called by SignatureScreen when the user taps "Confirm" inside the pad.
  const onSignatureCaptured = (base64Png: string) => {
    setSignaturePngBase64(base64Png);
    setShowSignaturePad(false);
    submitSignature(base64Png);
  };

  const onSignatureCancelled = () => {
    setShowSignaturePad(false);
    setPendingAction(null);
  };

  const submitSignature = async (signaturePng: string) => {
    if (!c || !pendingAction) return;
    setSubmitting(true);
    try {
      // signaturePng is a data URI: "data:image/png;base64,iVBOR..."
      // Strip the prefix; server attaches it as a DocumentRecord image.
      const base64Payload = signaturePng.includes(",")
        ? signaturePng.split(",")[1]
        : signaturePng;
      await apiFetch(`/api/projects/${projectId}/boq/payment-certs/${c.id}/sign`, {
        method: "PUT",
        body: JSON.stringify({
          action: pendingAction,
          signerName: signerName.trim(),
          rationale: pendingAction === "dispute" ? disputeNote.trim() : "",
          signaturePngBase64: base64Payload,
        }),
      });
      Alert.alert("Done", `Certificate ${c.certNumber} ${pendingAction === "agree" ? "agreed" : "disputed"}.`);
      router.back();
    } catch (e: any) {
      Alert.alert("Sign failed", e?.message ?? "Could not submit signature");
    } finally {
      setSubmitting(false);
      setPendingAction(null);
    }
  };

  if (loading) {
    return <View style={styles.center}><ActivityIndicator size="large" /></View>;
  }
  if (!c) {
    return <View style={styles.center}><Text>Certificate not found.</Text></View>;
  }

  const canSign = c.status === "Issued";

  return (
    <ScrollView style={styles.container} contentContainerStyle={{ padding: 14 }}>
      <Text style={styles.number}>Cert #{c.certNumber}</Text>
      <Text style={styles.meta}>{c.contractRef} · {c.form} · {c.status}</Text>

      <View style={styles.bigAmount}>
        <Text style={styles.amountLabel}>Total payable</Text>
        <Text style={styles.amount}>
          {c.currency} {c.totalPayable.toLocaleString(undefined, { maximumFractionDigits: 2 })}
        </Text>
        <Text style={styles.meta}>
          Valuation date {new Date(c.valuationDate).toLocaleDateString()}
        </Text>
      </View>

      <View style={styles.breakdown}>
        <Row label="Gross valuation" value={c.grossValuation} currency={c.currency} />
        <Row label={`Retention (${c.effectiveRetentionPercent}%)`} value={-c.retentionAmount} currency={c.currency} />
        <Row label="Other deductions" value={-c.otherDeductions} currency={c.currency} />
        <Row label="Net this cert" value={c.netThisCert} currency={c.currency} bold />
        <Row label={`VAT (${c.vatPercent}%)`} value={c.vatAmount} currency={c.currency} />
        <Row label="Payable" value={c.totalPayable} currency={c.currency} bold large />
      </View>

      <Text style={styles.sectionLabel}>Schedule of values ({c.lines.length} lines)</Text>
      {c.lines.map((l, i) => (
        <View key={i} style={styles.line}>
          <View style={styles.lineHead}>
            <Text style={styles.lineSection}>{l.section}</Text>
            <Text style={styles.linePct}>{l.percentComplete.toFixed(1)}%</Text>
          </View>
          <Text style={styles.lineDesc} numberOfLines={2}>{l.description}</Text>
          <Text style={styles.lineAmount}>
            Gross this cert: {c.currency} {l.grossThisCert.toLocaleString(undefined, { maximumFractionDigits: 0 })}
          </Text>
        </View>
      ))}

      {canSign && (
        <View style={styles.signCard}>
          <Text style={styles.sectionLabel}>Sign / dispute</Text>
          <TextInput
            value={signerName}
            onChangeText={setSignerName}
            placeholder="Your full name (acts as signature)"
            style={styles.input}
          />
          <TextInput
            value={disputeNote}
            onChangeText={setDisputeNote}
            placeholder="If disputing, add a brief reason"
            multiline
            numberOfLines={3}
            style={styles.input}
          />
          <View style={styles.actions}>
            <TouchableOpacity
              style={[styles.btn, styles.btnAgree]}
              onPress={() => sign("agree")}
              disabled={submitting}
            >
              <Text style={styles.btnText}>Agree</Text>
            </TouchableOpacity>
            <TouchableOpacity
              style={[styles.btn, styles.btnDispute]}
              onPress={() => sign("dispute")}
              disabled={submitting}
            >
              <Text style={styles.btnText}>Dispute</Text>
            </TouchableOpacity>
          </View>
        </View>
      )}

      {!canSign && (c.contractorSignedDate || c.employerSignedDate) && (
        <View style={styles.signCard}>
          <Text style={styles.sectionLabel}>Signed</Text>
          {c.employerSignedDate && (
            <Text style={styles.meta}>
              Employer: {c.signedByEmployer ?? ""} on {new Date(c.employerSignedDate).toLocaleDateString()}
            </Text>
          )}
          {c.contractorSignedDate && (
            <Text style={styles.meta}>
              Contractor: {c.signedByContractor ?? ""} on {new Date(c.contractorSignedDate).toLocaleDateString()}
            </Text>
          )}
        </View>
      )}

      {/*
        Signature pad modal — opens on Agree / Dispute. SignatureScreen
        renders an HTML canvas inside a WebView and returns the captured
        signature as a base64 PNG data-URI when the user taps OK.
        Cancellation closes the modal without submitting.
      */}
      <Modal
        visible={showSignaturePad}
        animationType="slide"
        transparent={false}
        onRequestClose={onSignatureCancelled}
      >
        <View style={styles.modalContainer}>
          <View style={styles.modalHeader}>
            <Text style={styles.modalTitle}>
              Sign {pendingAction === "agree" ? "(Agree)" : "(Dispute)"}
            </Text>
            <Text style={styles.modalSubtitle}>
              {signerName} — Cert #{c.certNumber}
            </Text>
          </View>
          <SignatureScreen
            ref={sigRef}
            onOK={onSignatureCaptured}
            onEmpty={() => Alert.alert("Empty signature",
              "Please sign with your finger or stylus before confirming.")}
            descriptionText="Sign with your finger or stylus, then tap Confirm."
            confirmText="Confirm"
            clearText="Clear"
            imageType="image/png"
            webStyle={signatureWebStyle}
          />
          <TouchableOpacity style={styles.cancelBtn} onPress={onSignatureCancelled}>
            <Text style={styles.cancelBtnText}>Cancel</Text>
          </TouchableOpacity>
        </View>
      </Modal>
    </ScrollView>
  );
}

// Inline CSS injected into the SignatureScreen WebView. Keeps the look
// consistent with the rest of the cert UI.
const signatureWebStyle = `
  .m-signature-pad { box-shadow: none; border: none; }
  .m-signature-pad--body { border: 1px dashed #c7c7c7; border-radius: 8px; }
  .m-signature-pad--footer .description { color: #5a5a5a; font-size: 13px; }
  .m-signature-pad--footer .button.clear { background-color: #b3261e; color: white; }
  .m-signature-pad--footer .button.save { background-color: #0a7d2e; color: white; }
`;

function Row({ label, value, currency, bold, large }:
  { label: string; value: number; currency: string; bold?: boolean; large?: boolean }) {
  return (
    <View style={localStyles.row}>
      <Text style={[localStyles.rowLabel, bold && localStyles.bold]}>{label}</Text>
      <Text style={[
        localStyles.rowAmount,
        bold && localStyles.bold,
        large && localStyles.large
      ]}>
        {currency} {value.toLocaleString(undefined, { maximumFractionDigits: 2 })}
      </Text>
    </View>
  );
}

const localStyles = StyleSheet.create({
  row: { flexDirection: "row", justifyContent: "space-between", paddingVertical: 6 },
  rowLabel: { fontSize: 14, color: "#333" },
  rowAmount: { fontSize: 14 },
  bold: { fontWeight: "700" },
  large: { fontSize: 18 },
});

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: "#f6f7f9" },
  center: { flex: 1, alignItems: "center", justifyContent: "center" },
  number: { fontSize: 22, fontWeight: "700" },
  meta: { fontSize: 13, color: "#5a5a5a", marginBottom: 4 },
  bigAmount: {
    backgroundColor: "white", borderRadius: 12, padding: 18, alignItems: "center",
    marginVertical: 12, shadowColor: "#000", shadowOpacity: 0.06, shadowRadius: 4,
    shadowOffset: { width: 0, height: 1 }, elevation: 1,
  },
  amountLabel: { fontSize: 12, color: "#5a5a5a", textTransform: "uppercase" },
  amount: { fontSize: 28, fontWeight: "700", marginVertical: 4 },
  breakdown: { backgroundColor: "white", borderRadius: 10, padding: 14, marginBottom: 12 },
  sectionLabel: { fontSize: 14, fontWeight: "600", marginTop: 8, marginBottom: 8, color: "#5a5a5a" },
  line: { backgroundColor: "white", borderRadius: 8, padding: 12, marginBottom: 8 },
  lineHead: { flexDirection: "row", justifyContent: "space-between" },
  lineSection: { fontSize: 13, fontWeight: "600" },
  linePct: { fontSize: 13, color: "#0a7d2e", fontWeight: "600" },
  lineDesc: { fontSize: 12, color: "#5a5a5a", marginVertical: 4 },
  lineAmount: { fontSize: 12 },
  signCard: { backgroundColor: "white", borderRadius: 10, padding: 14, marginTop: 12 },
  input: {
    backgroundColor: "#f6f7f9", borderRadius: 8, padding: 10, fontSize: 14,
    borderWidth: 1, borderColor: "#dcdcdc", marginBottom: 10, textAlignVertical: "top",
  },
  actions: { flexDirection: "row", gap: 10 },
  btn: { flex: 1, padding: 14, borderRadius: 8, alignItems: "center" },
  btnAgree:   { backgroundColor: "#0a7d2e" },
  btnDispute: { backgroundColor: "#b3261e" },
  btnText: { color: "white", fontWeight: "600", fontSize: 15 },

  // Signature pad modal
  modalContainer: { flex: 1, backgroundColor: "white" },
  modalHeader: { padding: 14, borderBottomWidth: 1, borderBottomColor: "#e0e0e0" },
  modalTitle: { fontSize: 18, fontWeight: "700" },
  modalSubtitle: { fontSize: 13, color: "#5a5a5a", marginTop: 2 },
  cancelBtn: {
    padding: 14, alignItems: "center", borderTopWidth: 1, borderTopColor: "#e0e0e0",
  },
  cancelBtnText: { color: "#5a5a5a", fontSize: 15 },
});
