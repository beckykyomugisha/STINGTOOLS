// The download catalogue — one entry per distributable tool.
//
// This is deliberately data, not code: adding a tool (or a new version of an
// existing one) means adding an object here, and the API and the downloads page
// pick it up with no other changes. The same applies to tools that do not exist
// yet — declare them with status "in-development" and they appear on the page as
// "coming soon" rather than being invisible until launch day.
//
// Entitlement is by tenant subscription status, resolved in entitlementFor()
// below. It is intentionally coarse: trial and active tenants get everything.
// Per-product entitlement (STING Tools subscribers get only the plugin) would go
// here too, via requiresProduct, once the plans justify it.
//
// Releasing a build: use tools/release-download.mjs — it uploads the files to
// R2, computes sha256 + size from the bytes, and prints the entry to paste
// here, so the catalogue cannot drift from what is actually in the bucket.

export type ToolStatus = "available" | "beta" | "in-development";

// One downloadable file within a version. A cross-platform tool ships several
// (a Windows EXE zip, a platform-neutral source zip); a single-platform tool
// keeps using ToolVersion.objectKey and never declares these.
//
// Ship ZIPS ONLY: the streaming endpoint serves everything as application/zip,
// which is also kinder to browsers than a bare .exe download.
//
// Use tools/release-download.mjs to upload the files and generate this block —
// it computes the sha256 and size so they can never drift from the object.
export interface ToolArtifact {
  // URL-safe slug, unique within the version (e.g. "win64", "any"). Shown on
  // the download button and passed back as ?artifact= to select the file.
  label: string;
  // Short human label for where it runs, e.g. "Windows 64-bit" or
  // "Any OS (Python 3.11+)". Display only.
  platform?: string;
  objectKey: string;
  sizeMb?: number;
  sha256?: string | null;
}

export interface ToolVersion {
  version: string;
  // Which Revit releases this build targets. Empty for tools that aren't
  // Revit add-ins.
  hosts?: string[];
  sizeMb?: number;
  releasedAt?: string; // ISO date
  notes?: string;
  // Key of the object in the private R2 bucket. Set this and the download
  // becomes self-serve, streamed through /api/downloads/:tool/:version with an
  // entitlement check. Null means we have no file yet and the page falls back
  // to "request by email" — no code change either way.
  objectKey?: string | null;
  sha256?: string | null;
  // Multi-file alternative to objectKey for tools that ship more than one
  // build per version. When present it wins over objectKey.
  artifacts?: ToolArtifact[];
}

// Normalise the two shapes: a version's downloadable files as a single list.
// Single-file versions (objectKey) come back as one artifact with an empty
// label, which the endpoint serves without needing ?artifact= — so existing
// STING Tools links keep working unchanged.
export function resolveArtifacts(v: ToolVersion): ToolArtifact[] {
  if (v.artifacts && v.artifacts.length) return v.artifacts;
  if (v.objectKey) {
    return [
      {
        label: "",
        objectKey: v.objectKey,
        sizeMb: v.sizeMb,
        sha256: v.sha256 ?? null,
      },
    ];
  }
  return [];
}

export interface Tool {
  id: string;
  name: string;
  tagline: string;
  // What kind of thing this is, so the page can group and label sensibly.
  kind: "revit-plugin" | "connector" | "desktop" | "cloud" | "cli";
  status: ToolStatus;
  platform?: string;
  // Product a tenant must be subscribed to in order to download. null = any
  // entitled tenant. Reserved for when plans diverge.
  requiresProduct?: "sting-tools" | "planscape" | null;
  docsUrl?: string;
  versions: ToolVersion[];
}

export const DOWNLOAD_CATALOG: Tool[] = [
  {
    id: "sting-tools",
    name: "STING Tools",
    tagline:
      "The Revit plugin — tagging, drawing production, MEP sizing and coordination. Runs on your workstation; no internet connection needed.",
    kind: "revit-plugin",
    status: "available",
    platform: "Windows · Revit 2025, 2026, 2027",
    requiresProduct: null,
    docsUrl: "/guides/revit-plugin-setup.html",
    versions: [
      {
        version: "2026-07-05",
        hosts: ["Revit 2025", "Revit 2026", "Revit 2027"],
        sizeMb: 89,
        releasedAt: "2026-07-05",
        notes:
          "One package covers all three Revit versions — the installer puts the files in the right place. Includes an install guide and an uninstaller.",
        objectKey: "sting-tools/2026-07-05/StingTools_Deploy_20260705.zip",
        // Hashed from the canonical bucket object (wrangler r2 object get →
        // sha256), 2026-07-19.
        sha256:
          "9ed1036ad08c12653e15f1501cd282f773a6edf06523f770af1565697f91b00c",
      },
    ],
  },
  {
    id: "sting-bridge",
    name: "StingBridge",
    tagline:
      "Connects ArchiCAD models to Planscape, and watches a folder for IFC files to bring in automatically.",
    kind: "connector",
    status: "beta",
    platform: "Windows · macOS · ArchiCAD (and any IFC-exporting tool)",
    requiresProduct: null,
    docsUrl: "/guides/stingbridge-setup.html",
    versions: [
      {
        version: "0.1.0-beta.2",
        releasedAt: "2026-07-20",
        notes:
          "Adds sequence numbers to ArchiCAD tags so they match Revit's 8-segment format, and files processed IFCs into done/ and failed/ folders so you can see at a glance what is still outstanding. Sign in with an access token instead of a password — the only option if you signed up on planscape.build.",
        artifacts: [
          {
            label: "win64",
            platform: "Windows 64-bit",
            objectKey: "sting-bridge/0.1.0-beta.2/StingBridge_0.1.0-beta.2_win64.zip",
            sizeMb: 57,
            sha256: "1b7eada10cff762e4c0a74503fbc7321301117766f6882a5d0da91aa15bc81ca",
          },
          {
            label: "any",
            platform: "Any OS (Python 3.11+)",
            objectKey: "sting-bridge/0.1.0-beta.2/StingBridge_0.1.0-beta.2_any.zip",
            sizeMb: 1,
            sha256: "7353a062fdec8057942bb98c98320f79e3b1efecc91ac55f0aeaa931aaeb572c",
          },
        ],
      },
      {
        version: "0.1.0-beta.1",
        releasedAt: "2026-07-19",
        notes:
          "First public beta. The IFC drop-folder and single-file workflows are verified end-to-end; live ArchiCAD JSON-API sync ships but is still maturing — tell us how it goes.",
        artifacts: [
          {
            label: "win64",
            platform: "Windows 64-bit",
            objectKey: "sting-bridge/0.1.0-beta.1/StingBridge_0.1.0-beta.1_win64.zip",
            sizeMb: 51,
            sha256: "976401ecce06c9c3231c1d92477c1fe3185ba167232ee3bf8ce4a857e0bc6d26",
          },
          {
            label: "any",
            platform: "Any OS (Python 3.11+)",
            objectKey: "sting-bridge/0.1.0-beta.1/StingBridge_0.1.0-beta.1_any.zip",
            sizeMb: 1,
            sha256: "22f1ed6805f79e5fa2b64dc91a6b0a8c5a7475dd640dd68cbf48fc34ac425cc2",
          },
        ],
      },
    ],
  },
  {
    id: "planscape-cloud",
    name: "Planscape cloud",
    tagline:
      "Shared project workspace, document register and issue tracking, with the mobile site app.",
    kind: "cloud",
    status: "in-development",
    platform: "Web · iOS · Android",
    requiresProduct: null,
    versions: [],
  },
];

export type Entitlement = "allowed" | "locked" | "unavailable";

export interface EntitlementResult {
  entitlement: Entitlement;
  reason: string;
}

// Coarse by design: what a tenant may download is decided by whether their
// subscription is live, not by which plan they are on. A tool that is not yet
// released is "unavailable" to everyone regardless of subscription — being a
// paying customer does not conjure software that does not exist.
export function entitlementFor(
  tool: Tool,
  subscriptionStatus: string | null | undefined
): EntitlementResult {
  if (tool.status === "in-development") {
    return {
      entitlement: "unavailable",
      reason: "Still in development — not available to download yet.",
    };
  }

  switch (subscriptionStatus) {
    case "trial":
      return { entitlement: "allowed", reason: "Included in your trial." };
    case "active":
      return { entitlement: "allowed", reason: "Included in your plan." };
    case "past_due":
      // Don't cut off access the moment a payment bounces — dunning may still
      // recover it, and locking a working tool over a card problem is a good
      // way to lose a customer who intended to pay.
      return {
        entitlement: "allowed",
        reason: "Your last payment did not go through — please update it to keep access.",
      };
    case "read_only":
      return {
        entitlement: "locked",
        reason: "Your trial has ended. Choose a plan to download.",
      };
    case "cancelled":
      return {
        entitlement: "locked",
        reason: "Your subscription has ended. Choose a plan to download again.",
      };
    default:
      return { entitlement: "locked", reason: "Choose a plan to download." };
  }
}
