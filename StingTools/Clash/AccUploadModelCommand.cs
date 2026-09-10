// AccUploadModelCommand.cs — the REAL ACC upload, as a dispatchable command.
//
// Why this exists: ACCPublish builds a LOCAL ACC-ready ZIP and tells the operator to
// upload it by hand. That is honest, but it left the actual uploader — AccModelUpload,
// which does the full APS Data Management dance (storage object -> signed-S3 PUT ->
// item/version) — reachable from exactly one button inside the BIM Coordination Center
// and from no command tag at all. This exposes it on the dock panel and to
// WorkflowEngine.ResolveCommand, mirroring BIMCoordinationCenter.BuildAccDetail's
// "Upload Model to ACC" button.
//
// Deliberately NOT added to any KUT workflow. A workflow step cannot answer "upload
// which file?", and guessing (the model path, or silently the active document) would
// put an unintended file in an issued CDE container. The file choice stays with a human.
//
// Read-only with respect to the Revit model (no Transaction). Network I/O only.
// Credentials come from %APPDATA%\Planscape\acc_credentials.json (AccIssueSync).

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.V6;

namespace StingTools.Core.Clash
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AccUploadModelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var creds = AccIssueSync.LoadCredentials();
            if (string.IsNullOrEmpty(creds.ClientId) || string.IsNullOrEmpty(creds.RefreshToken) ||
                string.IsNullOrEmpty(creds.ProjectId))
            {
                TaskDialog.Show("ACC — Upload Model",
                    "ACC credentials are not configured.\n\n" +
                    "Create %APPDATA%\\Planscape\\acc_credentials.json with at least:\n" +
                    "  ClientId, ClientSecret, RefreshToken, ProjectId\n\n" +
                    "The APS app also needs data:read, data:write and data:create scopes, " +
                    "or the upload is rejected after the file has already been staged.");
                return Result.Cancelled;
            }

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Pick a model / deliverable to upload to ACC",
                Filter = "Model & doc files (*.glb;*.ifc;*.nwc;*.nwd;*.rvt;*.pdf;*.dwg)|*.glb;*.ifc;*.nwc;*.nwd;*.rvt;*.pdf;*.dwg|All files (*.*)|*.*",
            };
            if (dlg.ShowDialog() != true) return Result.Cancelled;

            AccModelUpload.UploadResult result;
            try
            {
                result = AccModelUpload.UploadAsync(creds, dlg.FileName).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                StingLog.Error("ACC_UploadModel", ex);
                TaskDialog.Show("ACC — Upload Model", "Upload failed: " + ex.Message);
                return Result.Failed;
            }

            if (result == null || !result.Ok)
            {
                // A failed upload must not read as a completed one — the operator would
                // believe the deliverable reached the CDE container.
                string why = result?.Message ?? "the upload returned no result";
                TaskDialog.Show("ACC — Upload Model",
                    "The file was NOT uploaded to ACC.\n\n" +
                    "Reason: " + why + "\n\n" +
                    $"Project (container) used: {creds.ProjectId}\n" +
                    $"Folder URN: {(string.IsNullOrWhiteSpace(creds.FolderUrn) ? "(auto-resolved 'Project Files')" : creds.FolderUrn)}");
                StingLog.Warn($"ACC_UploadModel FAILED for '{dlg.FileName}': {why}");
                return Result.Failed;
            }

            TaskDialog.Show("ACC — Upload Model",
                result.Message + (string.IsNullOrWhiteSpace(result.ItemUrn) ? "" : "\n\nItem: " + result.ItemUrn));
            StingLog.Info($"ACC_UploadModel: uploaded '{dlg.FileName}' -> {result.ItemUrn}");
            return Result.Succeeded;
        }
    }
}
