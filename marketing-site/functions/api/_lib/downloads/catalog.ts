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

export type ToolStatus = "available" | "beta" | "in-development";

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
        sizeMb: 88,
        releasedAt: "2026-07-05",
        notes:
          "One package covers all three Revit versions — the installer puts the files in the right place. Includes an install guide and an uninstaller.",
        objectKey: "sting-tools/2026-07-05/StingTools_Deploy_20260705.zip",
        sha256: null,
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
    platform: "Windows · macOS · ArchiCAD",
    requiresProduct: null,
    versions: [
      {
        version: "beta",
        notes:
          "In testing with a small group. Tell us about your ArchiCAD setup and we will include you.",
        objectKey: null,
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
