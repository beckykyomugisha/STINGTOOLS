// FLEX-15-OCR — On-device text recognition.
//
// Platform strategy (no new runtime dependency yet):
//   iOS     → Vision framework via a Native Module (to be wired when the
//             custom iOS module is added to the project). Until then we
//             invoke a `TextRecognizer` bridge if present; otherwise no-op.
//   Android → ML Kit Text Recognition via `@react-native-ml-kit/text-recognition`.
//             Same bridge pattern — module is optional.
//
// When neither is available (Expo Go, first-run before native module ships)
// the service cleanly falls back to "no result" and the UI keeps the manual
// entry path. This keeps the feature shippable piece-by-piece without
// breaking existing builds.
//
// To enable real OCR later, drop in one of:
//   npm install @react-native-ml-kit/text-recognition
//   OR
//   npm install vision-camera-text-recognition
// and the detectText() call below will start returning real results.

import { NativeModules, Platform } from "react-native";

export interface OcrResult {
  text: string;
  confidence: number;           // 0–1
  blocks: OcrBlock[];
  processedAt: number;
  source: "native-ios" | "native-android" | "unavailable";
}

export interface OcrBlock {
  text: string;
  confidence: number;
  bounds?: { x: number; y: number; width: number; height: number };
}

export interface OcrExtraction {
  isoTag?: string;
  serialNumber?: string;
  manufacturerModel?: string;
  drawingNumber?: string;
  raw: OcrResult;
  /** 0–1 — the minimum confidence across the fields we extracted. */
  extractionConfidence: number;
}

// Threshold used by the "did you mean?" prompt. Matches decision 1.5 = (b) 90 %.
export const AUTO_APPLY_CONFIDENCE = 0.9;

// ── Native-module discovery ─────────────────────────────────────────────

type BridgeResult = { text?: string; confidence?: number; blocks?: OcrBlock[] };
function findBridge(): { recognize: (uri: string) => Promise<BridgeResult> } | null {
  // iOS: PlanscapeVisionOcr (custom module shipped with the iOS app).
  // Android: MLKitTextRecognition (provided by @react-native-ml-kit/text-recognition).
  const ios     = (NativeModules as any)?.PlanscapeVisionOcr;
  const android = (NativeModules as any)?.MLKitTextRecognition;
  const chosen  = Platform.OS === "ios" ? ios : android;
  if (chosen && typeof chosen.recognize === "function") {
    return { recognize: chosen.recognize.bind(chosen) };
  }
  return null;
}

// ── Public API ──────────────────────────────────────────────────────────

/**
 * Run text recognition on a local image URI. Returns an empty result when
 * no native module is wired — callers should check `source === "unavailable"`
 * and skip the OCR-dependent code path.
 */
export async function detectText(imageUri: string): Promise<OcrResult> {
  const bridge = findBridge();
  if (!bridge) {
    return {
      text: "",
      confidence: 0,
      blocks: [],
      processedAt: Date.now(),
      source: "unavailable",
    };
  }
  try {
    const raw = await bridge.recognize(imageUri);
    return {
      text: raw.text ?? "",
      confidence: clamp01(raw.confidence ?? 0),
      blocks: (raw.blocks ?? []).map(b => ({
        text: b.text,
        confidence: clamp01(b.confidence ?? 0),
        bounds: b.bounds,
      })),
      processedAt: Date.now(),
      source: Platform.OS === "ios" ? "native-ios" : "native-android",
    };
  } catch (err) {
    console.warn("[ocr] native recogniser failed:", err);
    return {
      text: "",
      confidence: 0,
      blocks: [],
      processedAt: Date.now(),
      source: "unavailable",
    };
  }
}

/**
 * Extract structured fields from an OCR result. Runs deterministic regex
 * first (ISO 19650 tag, drawing number), then falls back to heuristics for
 * serial numbers and manufacturer plates. Callers can inspect
 * `extractionConfidence` and decide whether to auto-apply (>= 0.9) or show
 * the confirmation modal.
 */
export function extract(result: OcrResult): OcrExtraction {
  const text = result.text ?? "";
  const isoTag = matchIsoTag(text);
  const drawingNumber = matchDrawingNumber(text);
  const serialNumber = matchSerial(text);
  const manufacturerModel = matchManufacturerModel(text);

  // Lowest per-field confidence wins. Confidence weighted: ISO tag match is
  // only valid when the regex is fully satisfied; heuristics cap at 0.75.
  const fieldConfidences: number[] = [];
  if (isoTag)              fieldConfidences.push(result.confidence);
  if (drawingNumber)       fieldConfidences.push(Math.min(result.confidence, 0.85));
  if (serialNumber)        fieldConfidences.push(Math.min(result.confidence, 0.75));
  if (manufacturerModel)   fieldConfidences.push(Math.min(result.confidence, 0.7));

  const extractionConfidence = fieldConfidences.length === 0
    ? 0
    : Math.min(...fieldConfidences);

  return {
    isoTag,
    drawingNumber,
    serialNumber,
    manufacturerModel,
    raw: result,
    extractionConfidence,
  };
}

export async function detectAndExtract(imageUri: string): Promise<OcrExtraction> {
  const raw = await detectText(imageUri);
  return extract(raw);
}

// ── Regex / heuristic helpers ───────────────────────────────────────────

/**
 * ISO 19650 tag — 8 segments separated by the configured separator (default '-').
 *   DISC   1–2 letters (M, E, P, A, S, FP, LV)
 *   LOC    3–6 alphanumerics (BLD1, EXT, XX, ROOF)
 *   ZONE   3 chars (Z01, Z02, ZZ, XX)
 *   LVL    2–4 chars (L01, GF, B1, RF, XX)
 *   SYS    3–5 letters (HVAC, DCW, LV, SAN)
 *   FUNC   3–5 letters (SUP, HTG, PWR)
 *   PROD   2–5 letters (AHU, DB, DR)
 *   SEQ    3–4 digits
 *
 * OCR frequently misreads 'O' for '0' and 'I' for '1' in the SEQ segment,
 * so we accept both and normalise the result.
 */
function matchIsoTag(text: string): string | undefined {
  const pattern = /\b([A-Z]{1,2})-([A-Z0-9]{2,6})-([A-Z0-9]{2,4})-([A-Z0-9]{2,4})-([A-Z]{2,5})-([A-Z]{2,5})-([A-Z]{2,5})-([A-Z0-9OI]{3,4})\b/i;
  const m = text.match(pattern);
  if (!m) return undefined;
  // Normalise SEQ — swap common OCR confusions.
  const seq = m[8].replace(/O/gi, "0").replace(/I/g, "1");
  return [m[1], m[2], m[3], m[4], m[5], m[6], m[7], seq].map(s => s.toUpperCase()).join("-");
}

/** Drawing number — usually "AA-NN-NNNN" on title blocks. */
function matchDrawingNumber(text: string): string | undefined {
  const m = text.match(/\b([A-Z]{1,3})[- ]?(\d{2,4})[- ]?(\d{3,5})\b/);
  return m ? `${m[1]}-${m[2]}-${m[3]}`.toUpperCase() : undefined;
}

/** Serial — 6+ alnum with at least one digit, between separators/whitespace. */
function matchSerial(text: string): string | undefined {
  const candidates = text.match(/\b[A-Z0-9]{6,}\b/g) ?? [];
  return candidates.find(c => /\d/.test(c) && !/^[A-Z]+$/.test(c));
}

/**
 * Manufacturer + model is usually on consecutive lines; grab the longest
 * short line near "MODEL" / "TYPE" / "MFR". Rough but useful for pre-filling.
 */
function matchManufacturerModel(text: string): string | undefined {
  const lines = text.split(/\r?\n/).map(l => l.trim()).filter(Boolean);
  const idx = lines.findIndex(l => /\b(model|type|mfr|manufacturer)\b/i.test(l));
  if (idx === -1) return undefined;
  const next = lines[idx + 1];
  if (next && next.length <= 40) return next;
  // Sometimes "MODEL: XYZ-1234" on the same line.
  const inline = lines[idx].match(/:(.+)$/);
  return inline ? inline[1].trim() : undefined;
}

function clamp01(n: number): number {
  if (!Number.isFinite(n)) return 0;
  return n < 0 ? 0 : n > 1 ? 1 : n;
}
