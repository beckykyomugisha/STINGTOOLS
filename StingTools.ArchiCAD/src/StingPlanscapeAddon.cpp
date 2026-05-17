// StingTools ArchiCAD Add-On — Main entry point
// Built against Graphisoft ArchiCAD API (GDL Add-On SDK, C++17)
// Mirrors the StingTools Revit plugin for ISO 19650 coordination workflows.
//
// Build requirements:
//   - ArchiCAD API headers (from Graphisoft Developer portal — free registration)
//   - ArchiCAD 22–28 SDK (matching the target ArchiCAD version)
//   - Windows: MSVC 2022, x64. macOS: Xcode 15, arm64 + x86_64 universal.
//
// This file is a scaffold. Replace TODO-VERIFY-API markers before shipping.

#include "ACAPinc.h"          // ArchiCAD Add-On API entry point
#include "APIEnvir.h"
#include "ACAPinc.h"
#include "DG.h"               // Dialog Manager

#include "../include/PlanscapeClient.hpp"

#include <string>
#include <vector>
#include <memory>

// ── Global state ──────────────────────────────────────────────────────────────

static std::unique_ptr<Planscape::PlanscapeClient> gClient;

// ── Menu command IDs ─────────────────────────────────────────────────────────

enum MenuCommandId {
    Cmd_Login             = 1,
    Cmd_SyncIfc           = 2,
    Cmd_SyncTags          = 3,
    Cmd_ExportBcf         = 4,
    Cmd_ImportBcf         = 5,
    Cmd_ComplianceDash    = 6,
    Cmd_Settings          = 7,
};

// ── Helper: collect elements and build SyncElement list ──────────────────────

static std::vector<Planscape::SyncElement> CollectElements()
{
    std::vector<Planscape::SyncElement> out;

    // TODO-VERIFY-API: ACAPI_Element_GetDefaults / ACAPI_Element_Get
    // Walk all elements, read user-defined properties from "Planscape_Asset"
    // property set (defined in ArchiCAD Options → Element Attributes → Properties).
    // Map AC_Pset_* native properties via STING_IFC_PSET_MAPPING.json rules.

    // Skeleton iteration:
    // ACAPI_Element_Filter(apiElemType_Wall, APIFilt_InMyWorkspace, ...);
    // for each element: read IfcGlobalId, read properties, build SyncElement.

    return out;
}

// ── Menu command handler ──────────────────────────────────────────────────────

static GSErrCode MenuCommandHandler(const API_MenuParams* menuParams)
{
    if (!menuParams) return APIERR_BADPARS;

    switch (menuParams->itemIndex) {

    case Cmd_Login: {
        // TODO: show login dialog (DG::Dialog subclass) collecting email + password
        // gClient->Login(email, password);
        DGAlert(DG_INFORMATION, "Planscape", "Login dialog — to be implemented.", nullptr, "OK");
        break;
    }

    case Cmd_SyncIfc: {
        // Export IFC from ArchiCAD and upload to Planscape.
        // TODO-VERIFY-API: ACAPI_Command_Call for IFC export, or
        // use the Publisher workflow via GS::UniString filePath = ...
        if (!gClient || !gClient->IsAuthenticated()) {
            DGAlert(DG_ERROR, "Planscape", "Please log in first.", nullptr, "OK");
            return NoError;
        }
        // std::string modelId = gClient->UploadIfc(projectId, ifcPath, progressCb);
        DGAlert(DG_INFORMATION, "Planscape", "IFC sync — to be implemented.", nullptr, "OK");
        break;
    }

    case Cmd_SyncTags: {
        // Read STING properties from elements and push to Planscape tag sync endpoint.
        if (!gClient || !gClient->IsAuthenticated()) {
            DGAlert(DG_ERROR, "Planscape", "Please log in first.", nullptr, "OK");
            return NoError;
        }
        auto elements = CollectElements();
        // gClient->SyncTags(projectId, elements);
        DGAlert(DG_INFORMATION, "Planscape",
            GS::UniString::Printf("Collected %d elements — sync to be implemented.", (int)elements.size()),
            nullptr, "OK");
        break;
    }

    case Cmd_ExportBcf: {
        // Download open issues from Planscape as BCF zip, write to temp file,
        // then call ACAPI_Interoperability_ImportBCF (ArchiCAD 26+) or prompt
        // user to import manually via File → Interoperability → BCF Manager.
        if (!gClient || !gClient->IsAuthenticated()) {
            DGAlert(DG_ERROR, "Planscape", "Please log in first.", nullptr, "OK");
            return NoError;
        }
        // auto bytes = gClient->ExportBcf(projectId, "OPEN");
        // write bytes to temp .bcfzip, prompt user to import
        DGAlert(DG_INFORMATION, "Planscape", "BCF export — to be implemented.", nullptr, "OK");
        break;
    }

    case Cmd_ImportBcf: {
        // Read the .bcfzip the user has exported from ArchiCAD BCF Manager
        // (after resolving issues) and push it back to Planscape.
        // TODO: file picker → read bytes → gClient->ImportBcf(projectId, bytes)
        DGAlert(DG_INFORMATION, "Planscape", "BCF import — to be implemented.", nullptr, "OK");
        break;
    }

    case Cmd_ComplianceDash: {
        // Show compliance summary pulled from Planscape in a modeless dialog.
        DGAlert(DG_INFORMATION, "Planscape", "Compliance dashboard — to be implemented.", nullptr, "OK");
        break;
    }

    case Cmd_Settings: {
        // Show settings dialog: server URL, project selection, auto-sync interval.
        DGAlert(DG_INFORMATION, "Planscape", "Settings — to be implemented.", nullptr, "OK");
        break;
    }

    default:
        break;
    }
    return NoError;
}

// ── Add-On entry points ───────────────────────────────────────────────────────

API_AddonType __ACENV_CALL CheckEnvironment(API_EnvirParams* envirParams)
{
    if (envirParams->serverInfo.serverApplication != APIAppl_ArchiCADID)
        return APIAddon_DontRegister;

    // Require ArchiCAD 22 or later (BCF Manager available from v22).
    if (envirParams->serverInfo.mainVersion < 22)
        return APIAddon_DontRegister;

    RSGetIndString(&envirParams->addOnInfo.name,        32000, 1, ACAPI_GetOwnResModule());
    RSGetIndString(&envirParams->addOnInfo.description, 32000, 2, ACAPI_GetOwnResModule());

    return APIAddon_Normal;
}

GSErrCode __ACENV_CALL RegisterInterface()
{
    // Register "Planscape" menu under the ArchiCAD menu bar.
    // TODO-VERIFY-API: exact signature may differ between SDK versions.
    return ACAPI_Register_Menu(32500, 32600, MenuCode_UserDef, MenuFlag_Default);
}

GSErrCode __ACENV_CALL Initialize()
{
    gClient = std::make_unique<Planscape::PlanscapeClient>("https://api.planscape.app");

    GSErrCode err = ACAPI_Install_MenuHandler(32500, MenuCommandHandler);
    if (err != NoError) return err;

    // TODO: register project-open observer to auto-trigger IFC sync if configured.
    // ACAPI_Notify_RegisterEventHandler(APINotify_Open, ...);

    return NoError;
}

GSErrCode __ACENV_CALL FreeData()
{
    gClient.reset();
    return NoError;
}
