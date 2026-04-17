// FLEX-13 — Dynamic renderer for a single custom field.
//
// Placement: decision 4.4 = (b) a collapsible "More fields" section below the
// standard issue form. See <CustomFieldsSection /> below for the wrapper.

import { useState } from "react";
import {
  View,
  Text,
  TextInput,
  TouchableOpacity,
  Switch,
  ScrollView,
  Platform,
} from "react-native";
import type { CustomFieldSchema, CustomFieldValues } from "@/types/customFields";
import { parseOptions } from "@/types/customFields";

interface Props {
  schema: CustomFieldSchema;
  value: unknown;
  onChange: (value: unknown) => void;
}

export function CustomFieldInput({ schema, value, onChange }: Props) {
  return (
    <View style={{ marginBottom: 16 }}>
      <Text style={{ fontSize: 12, color: "#555", marginBottom: 4, textTransform: "uppercase", letterSpacing: 0.5 }}>
        {schema.label}
        {schema.required ? " *" : ""}
      </Text>
      {renderControl(schema, value, onChange)}
      {schema.helpText && (
        <Text style={{ fontSize: 11, color: "#888", marginTop: 4 }}>{schema.helpText}</Text>
      )}
    </View>
  );
}

function renderControl(schema: CustomFieldSchema, value: unknown, onChange: (v: unknown) => void) {
  switch (schema.fieldType) {
    case "Text":
      return (
        <TextInput
          value={asString(value)}
          onChangeText={onChange}
          style={baseInputStyle}
        />
      );
    case "TextArea":
      return (
        <TextInput
          value={asString(value)}
          onChangeText={onChange}
          multiline
          numberOfLines={4}
          textAlignVertical="top"
          style={{ ...baseInputStyle, height: 88 }}
        />
      );
    case "Number":
      return (
        <TextInput
          value={asString(value)}
          onChangeText={(t) => onChange(t === "" ? null : Number(t))}
          keyboardType="decimal-pad"
          style={baseInputStyle}
        />
      );
    case "Date":
      // Lightweight text input — swap for @react-native-community/datetimepicker
      // on the admin screen when required. Keeps this component dependency-free.
      return (
        <TextInput
          value={asString(value)}
          onChangeText={onChange}
          placeholder={Platform.OS === "ios" ? "YYYY-MM-DD" : "YYYY-MM-DD"}
          style={baseInputStyle}
        />
      );
    case "Boolean":
      return (
        <Switch value={!!value} onValueChange={onChange} />
      );
    case "Dropdown":
      return <SelectControl schema={schema} value={value} onChange={onChange} multi={false} />;
    case "MultiSelect":
      return <SelectControl schema={schema} value={value} onChange={onChange} multi />;
    case "UserPicker":
      // v1 stub — prompts for email. Admin screen will wire an in-app picker later.
      return (
        <TextInput
          value={asString(value)}
          onChangeText={onChange}
          placeholder="user@example.com"
          autoCapitalize="none"
          keyboardType="email-address"
          style={baseInputStyle}
        />
      );
    case "ElementReference":
      // v1 stub — ISO 19650 tag text. Scanner flow can pre-populate via OCR.
      return (
        <TextInput
          value={asString(value)}
          onChangeText={onChange}
          placeholder="M-BLD1-Z01-L01-HVAC-SUP-AHU-0001"
          autoCapitalize="characters"
          style={{ ...baseInputStyle, fontFamily: "monospace" }}
        />
      );
    default:
      return <Text style={{ color: "#999" }}>Unsupported type: {schema.fieldType}</Text>;
  }
}

function SelectControl({
  schema, value, onChange, multi,
}: {
  schema: CustomFieldSchema;
  value: unknown;
  onChange: (v: unknown) => void;
  multi: boolean;
}) {
  const options = parseOptions(schema.optionsJson);
  const current: string[] = multi
    ? Array.isArray(value) ? value.map(String) : []
    : value == null ? [] : [String(value)];

  function toggle(v: string) {
    if (multi) {
      onChange(current.includes(v) ? current.filter((x) => x !== v) : [...current, v]);
    } else {
      onChange(current[0] === v ? null : v);
    }
  }

  return (
    <ScrollView horizontal showsHorizontalScrollIndicator={false}>
      <View style={{ flexDirection: "row", gap: 6 }}>
        {options.map((opt) => {
          const selected = current.includes(opt.value);
          return (
            <TouchableOpacity
              key={opt.value}
              onPress={() => toggle(opt.value)}
              style={{
                paddingHorizontal: 12,
                paddingVertical: 8,
                borderRadius: 16,
                backgroundColor: selected ? "#E8912D" : "#f0f0f0",
              }}
            >
              <Text style={{ color: selected ? "#fff" : "#333", fontSize: 13, fontWeight: "500" }}>
                {opt.label}
              </Text>
            </TouchableOpacity>
          );
        })}
      </View>
    </ScrollView>
  );
}

// ── Public: collapsible wrapper ─────────────────────────────────────────

export function CustomFieldsSection({
  schemas,
  values,
  onChange,
  initiallyOpen = false,
}: {
  schemas: CustomFieldSchema[];
  values: CustomFieldValues;
  onChange: (v: CustomFieldValues) => void;
  initiallyOpen?: boolean;
}) {
  const [open, setOpen] = useState(initiallyOpen);
  if (schemas.length === 0) return null;

  return (
    <View style={{ marginTop: 16 }}>
      <TouchableOpacity
        onPress={() => setOpen(!open)}
        style={{ flexDirection: "row", alignItems: "center", paddingVertical: 8 }}
      >
        <Text style={{ fontSize: 14, fontWeight: "600", color: "#333" }}>
          {open ? "▼" : "▶"} More fields ({schemas.length})
        </Text>
      </TouchableOpacity>
      {open && (
        <View style={{ paddingTop: 12 }}>
          {schemas.map((s) => (
            <CustomFieldInput
              key={s.id}
              schema={s}
              value={values[s.key]}
              onChange={(v) => onChange({ ...values, [s.key]: v })}
            />
          ))}
        </View>
      )}
    </View>
  );
}

// ── Shared styles/helpers ───────────────────────────────────────────────

const baseInputStyle = {
  borderWidth: 1,
  borderColor: "#ddd",
  borderRadius: 6,
  padding: 10,
  fontSize: 15,
};

function asString(value: unknown): string {
  if (value == null) return "";
  if (typeof value === "string") return value;
  if (typeof value === "number") return String(value);
  if (typeof value === "boolean") return value ? "true" : "false";
  try { return JSON.stringify(value); } catch { return String(value); }
}
