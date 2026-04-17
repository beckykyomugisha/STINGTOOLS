// FLEX-15-OCR — "Did you mean?" confirmation UI for low-confidence OCR extractions.
//
// Shown when OCR returns a result below AUTO_APPLY_CONFIDENCE (0.9). Users can
// edit each field before applying, or dismiss entirely and keep their manual
// entry.

import { useState } from "react";
import { Modal, View, Text, TextInput, TouchableOpacity, ScrollView } from "react-native";
import type { OcrExtraction } from "@/services/ocr";
import { t } from "@/i18n";

interface Props {
  visible: boolean;
  extraction: OcrExtraction | null;
  onApply: (fields: Partial<OcrFields>) => void;
  onCancel: () => void;
}

export interface OcrFields {
  isoTag?: string;
  drawingNumber?: string;
  serialNumber?: string;
  manufacturerModel?: string;
}

export function OcrConfirmModal({ visible, extraction, onApply, onCancel }: Props) {
  const [fields, setFields] = useState<OcrFields>({});

  // Re-seed fields each time a new extraction comes in.
  if (extraction && Object.keys(fields).length === 0) {
    setFields({
      isoTag: extraction.isoTag,
      drawingNumber: extraction.drawingNumber,
      serialNumber: extraction.serialNumber,
      manufacturerModel: extraction.manufacturerModel,
    });
  }

  if (!extraction) return null;

  const confidencePct = Math.round(extraction.extractionConfidence * 100);

  return (
    <Modal visible={visible} transparent animationType="slide" onRequestClose={onCancel}>
      <View style={{ flex: 1, justifyContent: "flex-end", backgroundColor: "rgba(0,0,0,0.5)" }}>
        <View style={{ backgroundColor: "#fff", borderTopLeftRadius: 16, borderTopRightRadius: 16, padding: 20, maxHeight: "80%" }}>
          <ScrollView>
            <Text style={{ fontSize: 18, fontWeight: "700", marginBottom: 4 }}>
              {t("common.edit")} — OCR ({confidencePct}%)
            </Text>
            <Text style={{ color: "#666", marginBottom: 20, fontSize: 13 }}>
              {confidencePct < 70
                ? "Confidence is low — please check each field before applying."
                : "Review and adjust the auto-detected fields below."}
            </Text>

            <Field label="ISO 19650 tag"       value={fields.isoTag}            onChange={v => setFields({ ...fields, isoTag: v })} />
            <Field label="Drawing number"      value={fields.drawingNumber}     onChange={v => setFields({ ...fields, drawingNumber: v })} />
            <Field label="Serial number"       value={fields.serialNumber}      onChange={v => setFields({ ...fields, serialNumber: v })} />
            <Field label="Manufacturer / model" value={fields.manufacturerModel} onChange={v => setFields({ ...fields, manufacturerModel: v })} />

            <View style={{ flexDirection: "row", marginTop: 20, gap: 12 }}>
              <TouchableOpacity
                onPress={onCancel}
                style={{ flex: 1, padding: 14, borderRadius: 8, backgroundColor: "#eee", alignItems: "center" }}
              >
                <Text style={{ fontWeight: "600" }}>{t("common.cancel")}</Text>
              </TouchableOpacity>
              <TouchableOpacity
                onPress={() => onApply(fields)}
                style={{ flex: 1, padding: 14, borderRadius: 8, backgroundColor: "#E8912D", alignItems: "center" }}
              >
                <Text style={{ fontWeight: "700", color: "#fff" }}>{t("common.save")}</Text>
              </TouchableOpacity>
            </View>
          </ScrollView>
        </View>
      </View>
    </Modal>
  );
}

function Field({ label, value, onChange }: { label: string; value?: string; onChange: (v: string) => void }) {
  return (
    <View style={{ marginBottom: 16 }}>
      <Text style={{ fontSize: 12, color: "#555", marginBottom: 4, textTransform: "uppercase", letterSpacing: 0.5 }}>{label}</Text>
      <TextInput
        value={value ?? ""}
        onChangeText={onChange}
        autoCapitalize="characters"
        placeholder="—"
        style={{
          borderWidth: 1,
          borderColor: "#ddd",
          borderRadius: 6,
          padding: 10,
          fontSize: 15,
          fontFamily: "monospace",
        }}
      />
    </View>
  );
}
