using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Electrical.CircuitWizard
{
    /// <summary>
    /// Materialises the proposed circuits emitted by CircuitWizardDialog into
    /// real <see cref="ElectricalSystem"/> objects.
    /// Required state: <see cref="PendingCircuits"/> + <see cref="PendingPanelName"/>
    /// must be set by the dialog immediately before calling
    /// StingElectricalCommandHandler.SetCommand("Circuit_CreateWizard").
    /// All work runs in a single TransactionGroup so the entire batch rolls
    /// back on any unexpected failure.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CircuitWizardCommand : IExternalCommand
    {
        public static List<ProposedCircuit> PendingCircuits { get; set; } = new();
        public static string PendingPanelName { get; set; } = "";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var proposals = PendingCircuits ?? new List<ProposedCircuit>();
            string panelName = PendingPanelName ?? "";
            if (proposals.Count == 0 || string.IsNullOrEmpty(panelName))
            {
                TaskDialog.Show("STING Circuit Wizard",
                    "No proposed circuits queued. Open the wizard, propose circuits, then click Create.");
                return Result.Cancelled;
            }

            var panel = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .FirstOrDefault(p => string.Equals(p.Name, panelName, StringComparison.OrdinalIgnoreCase));
            if (panel == null)
            {
                TaskDialog.Show("STING Circuit Wizard", $"Panel '{panelName}' not found.");
                return Result.Failed;
            }

            int created = 0;
            var failed = new List<string>();

            using (var tg = new TransactionGroup(doc, "STING Create Circuits Wizard"))
            {
                tg.Start();
                foreach (var proposal in proposals)
                {
                    using (var tx = new Transaction(doc, $"STING Create Circuit {proposal.ProposedLabel}"))
                    {
                        try
                        {
                            tx.Start();
                            var firstEl = proposal.Elements.FirstOrDefault();
                            if (firstEl?.Id == null) { failed.Add($"{proposal.ProposedLabel}: no source element"); tx.RollBack(); continue; }
                            var elFirst = doc.GetElement(firstEl.Id as ElementId);
                            if (elFirst == null) { failed.Add($"{proposal.ProposedLabel}: source element missing"); tx.RollBack(); continue; }
                            // ElectricalSystem.Create(Connector, ElectricalSystemType) is the
                            // Revit 2025+ static factory — takes a SINGLE seed connector. The
                            // remaining elements are added afterwards via ElectricalSystem.Add(ConnectorSet).
                            var seedConnector = FindElectricalConnector(elFirst);
                            if (seedConnector == null)
                            { failed.Add($"{proposal.ProposedLabel}: no electrical connector"); tx.RollBack(); continue; }

                            ElectricalSystem sys = null;
                            try { sys = ElectricalSystem.Create(seedConnector, ElectricalSystemType.PowerCircuit); }
                            catch (Exception ex)
                            {
                                failed.Add($"{proposal.ProposedLabel}: ElectricalSystem.Create failed — {ex.Message}");
                                tx.RollBack();
                                continue;
                            }
                            if (sys == null) { failed.Add($"{proposal.ProposedLabel}: ElectricalSystem.Create returned null"); tx.RollBack(); continue; }

                            // Add the remaining members via their own connectors.
                            for (int i = 1; i < proposal.Elements.Count; i++)
                            {
                                try
                                {
                                    var elId = proposal.Elements[i].Id as ElementId;
                                    if (elId == null) continue;
                                    var el = doc.GetElement(elId);
                                    if (el == null) continue;
                                    var c2 = FindElectricalConnector(el);
                                    if (c2 == null) continue;
                                    var set = new ConnectorSet();
                                    set.Insert(c2);
                                    sys.Add(set);
                                }
                                catch (Exception ex) { StingLog.Warn($"Add connector to circuit: {ex.Message}"); }
                            }

                            try { sys.SelectPanel(panel); }
                            catch (Exception ex)
                            {
                                failed.Add($"{proposal.ProposedLabel}: SelectPanel failed — {ex.Message}");
                                tx.RollBack();
                                continue;
                            }

                            // Phase assignment via "Phase" display-name parameter — best-effort.
                            // The parameter is read-only on most installations because phase comes
                            // from the panel slot. We probe by display name (the BIP enum varies
                            // between Revit versions) and write inside try/catch.
                            if (!string.IsNullOrEmpty(proposal.Phase) && proposal.Phase.Length == 1)
                            {
                                try
                                {
                                    var phaseParam = sys.LookupParameter("Phase")
                                                  ?? sys.LookupParameter("Circuit Phase")
                                                  ?? sys.LookupParameter("Starting Phase");
                                    if (phaseParam != null && !phaseParam.IsReadOnly)
                                    {
                                        if (phaseParam.StorageType == StorageType.Integer)
                                        {
                                            int v = proposal.Phase switch { "A" => 0, "B" => 1, "C" => 2, _ => 0 };
                                            phaseParam.Set(v);
                                        }
                                        else if (phaseParam.StorageType == StorageType.String)
                                        {
                                            phaseParam.Set(proposal.Phase);
                                        }
                                    }
                                }
                                catch (Exception ex) { StingLog.Info($"Phase param write soft-fail: {ex.Message}"); }
                            }
                            try { sys.LoadName = proposal.ProposedLabel; }
                            catch (Exception ex) { StingLog.Warn($"LoadName: {ex.Message}"); }

                            tx.Commit();
                            created++;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Error($"Create circuit {proposal.ProposedLabel}: {ex.Message}", ex);
                            failed.Add($"{proposal.ProposedLabel}: {ex.Message}");
                            try { if (tx.HasStarted()) tx.RollBack(); } catch { }
                        }
                    }
                }
                tg.Assimilate();
            }

            try { ComplianceScan.InvalidateCache(); } catch { }
            string failTail = failed.Count == 0
                ? ""
                : "\n\nFailed:\n  " + string.Join("\n  ", failed.Take(8))
                  + (failed.Count > 8 ? $"\n  …and {failed.Count - 8} more" : "");
            TaskDialog.Show("STING Circuit Wizard",
                $"{created} circuit(s) created on '{panelName}'.{failTail}");
            return Result.Succeeded;
        }

        private static Connector FindElectricalConnector(Element el)
        {
            try
            {
                if (el is FamilyInstance fi)
                {
                    var cm = fi.MEPModel?.ConnectorManager;
                    if (cm == null) return null;
                    foreach (Connector c in cm.Connectors)
                    {
                        if (c.Domain == Domain.DomainElectrical) return c;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"FindElectricalConnector: {ex.Message}"); }
            return null;
        }
    }
}
