#pragma once
// StingTools ArchiCAD Add-On — Planscape cloud client
// Mirrors the SyncClient in Planscape.PluginSync for the Revit plugin.
// Uses WinHTTP on Windows, libcurl on macOS.

#include <string>
#include <vector>
#include <functional>

namespace Planscape {

struct SyncElement {
    std::string ifcGuid;        // IfcGlobalId from ArchiCAD element
    std::string discipline;     // DISC token
    std::string location;       // LOC token
    std::string zone;           // ZONE token
    std::string level;          // LVL token
    std::string systemType;     // SYS token
    std::string productCode;    // PROD token
    std::string sequenceNumber; // SEQ token
    std::string fullTag;        // assembled ISO 19650 tag
    std::string status;         // renovation status → NEW/EXISTING/DEMOLISHED
    std::string ifcType;        // e.g. "IfcWall", "IfcBeam"
};

struct BcfTopic {
    std::string guid;
    std::string title;
    std::string description;
    std::string priority;   // CRITICAL/HIGH/MEDIUM/LOW
    std::string status;     // OPEN/IN_PROGRESS/RESOLVED/CLOSED
    std::string type;       // RFI/NCR/SI/CLASH
    std::string assignee;
    std::string ifcGuid;    // referenced element IfcGlobalId
    double cameraX = 0, cameraY = 0, cameraZ = 10;
};

struct AuthToken {
    std::string accessToken;
    std::string refreshToken;
    long long   expiresAt = 0; // unix timestamp
};

// Callback types for async operations
using ProgressCallback = std::function<void(int percent, const std::string& message)>;
using ErrorCallback    = std::function<void(int httpStatus, const std::string& error)>;

class PlanscapeClient {
public:
    explicit PlanscapeClient(const std::string& baseUrl = "https://api.planscape.app");

    // ── Authentication ────────────────────────────────────────────────────
    bool Login(const std::string& email, const std::string& password);
    bool RefreshIfNeeded();
    bool IsAuthenticated() const;
    void Logout();

    // ── Project ───────────────────────────────────────────────────────────
    // Returns projectId GUID string, or empty on failure.
    std::string GetOrCreateProject(const std::string& projectName);

    // ── IFC upload ────────────────────────────────────────────────────────
    // Uploads the IFC file at ifcPath to Planscape as a new model version.
    // Returns model record ID, or empty on failure.
    std::string UploadIfc(
        const std::string& projectId,
        const std::string& ifcPath,
        ProgressCallback   onProgress = nullptr);

    // ── Tag sync ──────────────────────────────────────────────────────────
    // Pushes STING coordination tags for the given elements to Planscape.
    // Called after the Add-On has resolved tag tokens from element properties.
    bool SyncTags(
        const std::string&             projectId,
        const std::vector<SyncElement>& elements,
        ProgressCallback               onProgress = nullptr);

    // ── Issue / BCF ───────────────────────────────────────────────────────
    // Downloads open issues for the project as a BCF 2.1 zip.
    // Caller writes the bytes to disk; ArchiCAD imports via BCF Manager.
    std::vector<uint8_t> ExportBcf(
        const std::string& projectId,
        const std::string& statusFilter = "OPEN");

    // Pushes BCF topics resolved in ArchiCAD back to Planscape.
    bool ImportBcf(
        const std::string&           projectId,
        const std::vector<uint8_t>&  bcfZipBytes);

    // ── Compliance ────────────────────────────────────────────────────────
    // Posts a compliance snapshot (% tagged, by discipline) to Planscape.
    bool PostComplianceSnapshot(
        const std::string& projectId,
        int                totalElements,
        int                taggedElements,
        const std::string& disciplineBreakdownJson);

private:
    std::string  m_baseUrl;
    AuthToken    m_auth;
    std::string  m_tenantId;

    std::string  HttpGet (const std::string& path);
    std::string  HttpPost(const std::string& path, const std::string& body);
    std::string  HttpPostMultipart(const std::string& path, const std::string& filePath, ProgressCallback cb);
    void         SetAuthHeader(/* platform-specific request handle */);
};

} // namespace Planscape
