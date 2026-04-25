# On-device OCR (FLEX-15-OCR)

Zero-dependency scaffold — real text recognition activates as soon as a native
module is added to the iOS / Android projects. Until then `detectText()`
returns a `source: "unavailable"` result and the UI keeps the manual-entry
path.

## Engine choice (decision 1.1 = b)

Apple Vision (iOS) + ML Kit Text Recognition (Android) — free, on-device,
offline, no PII leaves the device. ~90% accuracy on printed plant labels.

## Wiring real recognition

### iOS

1. Add a Swift native module `PlanscapeVisionOcr` exposing:
   ```swift
   @objc(PlanscapeVisionOcr)
   class PlanscapeVisionOcr: NSObject {
     @objc func recognize(_ uri: NSString, resolve: RCTPromiseResolveBlock, reject: RCTPromiseRejectBlock) {
       // use VNRecognizeTextRequest, return { text, confidence, blocks }
     }
   }
   ```
2. `pod install` after adding the module to Podfile.
3. Rebuild the iOS shell. `ocr.ts` picks it up automatically via
   `NativeModules.PlanscapeVisionOcr`.

### Android

1. `npm install @react-native-ml-kit/text-recognition`
2. `cd android && ./gradlew clean`
3. Rebuild. `ocr.ts` picks up `NativeModules.MLKitTextRecognition`.

### Verifying

```ts
import { detectText } from "@/services/ocr";
const r = await detectText("file:///path/to/photo.jpg");
console.log(r.source, r.confidence, r.text);
// source: "unavailable"  → native module not wired yet
// source: "native-ios" / "native-android"  → real result
```

## Trigger points (decision 1.4)

Implemented:
- **Issue-create attachment upload** — `OcrConfirmModal` pops when confidence
  falls below 90 %. Above 90 %, fields auto-apply silently.

Follow-up (same service, no additional scaffolding needed):
- Scanner tab lookup — already wired to QR; add a "Scan label" button that
  calls `detectAndExtract` on the captured frame.
- Document upload — call `detectAndExtract` server-side via the rendered PDF
  thumbnail; not implemented in v1.

## Confidence handling (decision 1.5 = b, 90 %)

- `extract()` returns an `extractionConfidence` 0–1.
- `AUTO_APPLY_CONFIDENCE = 0.9` is the gate. Callers:
  ```ts
  const result = await detectAndExtract(uri);
  if (result.raw.source === "unavailable") return; // no-op, keep manual path
  if (result.extractionConfidence >= AUTO_APPLY_CONFIDENCE) {
    applyFields(result);
  } else {
    // show OcrConfirmModal
  }
  ```

## What it extracts (decision 1.3)

- ISO 19650 tag strings (strong regex with OCR-confusion fix-ups for
  `O↔0`, `I↔1` in SEQ segment)
- Drawing numbers (title block format)
- Serial numbers (heuristic: alphanumeric with digits)
- Manufacturer/model (nearby-line heuristic)

## Adding fields

1. Write a `matchX` helper in `ocr.ts`.
2. Add it to `extract()` and the `OcrExtraction` interface.
3. Add a `<Field>` row in `OcrConfirmModal.tsx`.

## Privacy

All recognition runs on the device. No image is uploaded for OCR (attachments
still upload to MinIO/S3 for storage — OCR happens before, not on the server).
