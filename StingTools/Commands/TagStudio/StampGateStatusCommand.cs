// ============================================================================
// StampGateStatusCommand.cs — compute + stamp universal-tag status gates.
//
//   *** Phase 195 — Universal Tag pivot, Task 2 ***
//
// The universal tag carries two status BADGES (left = data-completeness gate,
// right = QA / sign-off gate) built into the master by hand (see
// UNIVERSAL_TAG_LABEL_BUILD_SHEET.md STEP 4). Each badge shows green / amber /
// red by swapping overlaid pre-coloured glyphs whose Visible property is driven
// by  and(TAG_WARN_VISIBLE_BOOL, STING_GATE_x_STATUS_INT = n).
//
// This command is the PLUGIN side of that scheme: it computes each element's two
// gate statuses via ComplianceScan.ComputeElementGates (which reuses
// ISO19650Validator) and stamps the two instance INTEGER params
// STING_GATE_DATA_STATUS_INT / STING_GATE_QA_STATUS_INT (0=red/1=amber/2=green).
// The badges read those automatically — no per-family logic.
//
// The two params are ensured-bound (InstanceBinding over the STING category set)
// before stamping so a fresh project needn't run Load Shared Params first.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.TagStudio
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StampGateStatusCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
            var app = ctx.App.Application;

            // ── Ensure the two gate params are bound to the STING categories ──
            string origSp = app.SharedParametersFilename;
            try
            {
                EnsureGateParamsBound(doc, app);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StampGateStatus: EnsureGateParamsBound: {ex.Message}");
            }
            finally
            {
                try { if (!string.IsNullOrEmpty(origSp)) app.SharedParametersFilename = origSp; }
                catch (Exception ex) { StingLog.Warn($"Restore shared param file: {ex.Message}"); }
            }

            // ── Collect scannable model elements (same set ComplianceScan uses) ──
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);
            if (known.Count == 0)
            {
                TaskDialog.Show("Stamp Gate Status",
                    "Tag configuration not loaded (no known categories). Run 'Load Config' first.");
                return Result.Cancelled;
            }

            var coll = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            var catEnums = SharedParamGuids.AllCategoryEnums;
            if (catEnums != null && catEnums.Length > 0)
                coll.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(catEnums)));

            var targets = coll.Where(e => e != null && e.IsValidObject &&
                                          known.Contains(ParameterHelpers.GetCategoryName(e)))
                              .ToList();

            if (targets.Count == 0)
            {
                TaskDialog.Show("Stamp Gate Status", "No taggable STING elements found in this project.");
                return Result.Succeeded;
            }

            int stamped = 0, skipped = 0;
            int[] dataHist = new int[3]; // red/amber/green
            int[] qaHist = new int[3];

            int chunkCount = (targets.Count + 249) / 250;
            var progress = StingProgressDialog.Show("Stamp Gate Status", chunkCount);
            try
            {
                using (var tx = new Transaction(doc, "STING Stamp Gate Status"))
                {
                    tx.Start();
                    for (int i = 0; i < targets.Count; i++)
                    {
                        if ((i % 250) == 0)
                        {
                            progress.Increment($"Stamping gates ({i + 1}/{targets.Count})");
                            if (EscapeChecker.IsEscapePressed())
                            {
                                StingLog.Info($"StampGateStatus: cancelled after {i} of {targets.Count}");
                                break;
                            }
                        }

                        Element el = targets[i];
                        ComplianceScan.GateResult g;
                        try { g = ComplianceScan.ComputeElementGates(doc, el); }
                        catch (Exception ex) { StingLog.Warn($"ComputeElementGates {el.Id}: {ex.Message}"); skipped++; continue; }

                        bool okData = ParameterHelpers.SetInt(el, ParamRegistry.GATE_DATA_STATUS, g.DataGate, overwrite: true);
                        bool okQa   = ParameterHelpers.SetInt(el, ParamRegistry.GATE_QA_STATUS, g.QaGate, overwrite: true);

                        if (okData || okQa)
                        {
                            stamped++;
                            if (g.DataGate >= 0 && g.DataGate <= 2) dataHist[g.DataGate]++;
                            if (g.QaGate   >= 0 && g.QaGate   <= 2) qaHist[g.QaGate]++;
                        }
                        else skipped++;
                    }
                    tx.Commit();
                }
            }
            finally
            {
                progress.Close();
            }

            // Recompute compliance so the dashboard reflects fresh gate data.
            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"InvalidateCache: {ex.Message}"); }

            var td = new TaskDialog("Stamp Gate Status — done");
            td.MainInstruction = $"Stamped {stamped} / {targets.Count} elements";
            td.MainContent =
                "Data gate  (STING_GATE_DATA_STATUS_INT):\n" +
                $"   🟢 green {dataHist[2]}   🟡 amber {dataHist[1]}   🔴 red {dataHist[0]}\n\n" +
                "QA gate    (STING_GATE_QA_STATUS_INT):\n" +
                $"   🟢 green {qaHist[2]}   🟡 amber {qaHist[1]}   🔴 red {qaHist[0]}\n\n" +
                (skipped > 0 ? $"Skipped (param unbound / read-only): {skipped}\n" : "") +
                "\nTurn on TAG_WARN_VISIBLE_BOOL on the tag instances to reveal the badges.";
            td.Show();

            StingLog.Info($"StampGateStatus: stamped={stamped}, skipped={skipped}, " +
                $"data(g/a/r)={dataHist[2]}/{dataHist[1]}/{dataHist[0]}, " +
                $"qa(g/a/r)={qaHist[2]}/{qaHist[1]}/{qaHist[0]}");
            return Result.Succeeded;
        }

        /// <summary>
        /// Ensure STING_GATE_DATA_STATUS_INT / STING_GATE_QA_STATUS_INT are bound as
        /// instance params over the STING category set. Idempotent — ReInserts to
        /// widen an existing binding, Inserts when absent.
        /// </summary>
        private static void EnsureGateParamsBound(Document doc,
            Autodesk.Revit.ApplicationServices.Application app)
        {
            string spFile = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");
            if (string.IsNullOrEmpty(spFile) || !File.Exists(spFile)) return;
            app.SharedParametersFilename = spFile;
            DefinitionFile defFile = app.OpenSharedParameterFile();
            if (defFile == null) return;

            string[] wanted = { ParamRegistry.GATE_DATA_STATUS, ParamRegistry.GATE_QA_STATUS };
            var defs = new List<ExternalDefinition>();
            foreach (string name in wanted)
            {
                var ext = FindSharedDefinition(defFile, name);
                if (ext != null) defs.Add(ext);
                else StingLog.Warn($"EnsureGateParamsBound: '{name}' not in MR_PARAMETERS.txt");
            }
            if (defs.Count == 0) return;

            var catEnums = SharedParamGuids.AllCategoryEnums;
            CategorySet cats = SharedParamGuids.BuildCategorySet(doc, catEnums);
            if (cats == null || cats.IsEmpty) return;

            using (var tx = new Transaction(doc, "STING Bind gate params"))
            {
                tx.Start();
                foreach (var ext in defs)
                {
                    try
                    {
                        InstanceBinding binding = app.Create.NewInstanceBinding(cats);
                        bool exists = doc.ParameterBindings.Contains(ext);
                        if (exists)
                            doc.ParameterBindings.ReInsert(ext, binding, GroupTypeId.IdentityData);
                        else
                            doc.ParameterBindings.Insert(ext, binding, GroupTypeId.IdentityData);
                    }
                    catch (Exception ex) { StingLog.Warn($"Bind '{ext.Name}': {ex.Message}"); }
                }
                tx.Commit();
            }
        }

        private static ExternalDefinition FindSharedDefinition(DefinitionFile defFile, string name)
        {
            if (defFile == null || string.IsNullOrEmpty(name)) return null;
            foreach (DefinitionGroup g in defFile.Groups)
                foreach (Definition d in g.Definitions)
                    if (d.Name == name && d is ExternalDefinition ext) return ext;
            return null;
        }
    }
}
