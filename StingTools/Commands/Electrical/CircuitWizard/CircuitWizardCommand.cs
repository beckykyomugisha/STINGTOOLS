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
                            var connector = FindElectricalConnector(elFirst);
                            if (connector == null) { failed.Add($"{proposal.ProposedLabel}: no electrical connector"); tx.RollBack(); continue; }

                            var sys = ElectricalSystem.Create(doc,
                                new List<ElementId> { (firstEl.Id as ElementId) ?? ElementId.InvalidElementId },
                                ElectricalSystemType.PowerCircuit);
                            if (sys == null) { failed.Add($"{proposal.ProposedLabel}: ElectricalSystem.Create returned null"); tx.RollBack(); continue; }

                            for (int i = 1; i < proposal.Elements.Count; i++)
                            {
                                try
                                {
                                    var elId = proposal.Elements[i].Id as ElementId;
                                    if (elId == null) continue;
                                    sys.AddToCircuit(new List<ElementId> { elId });
                                }
                                catch (Exception ex) { StingLog.Warn($"AddToCircuit: {ex.Message}"); }
                            }

                            try { sys.SelectPanel(panel); }
                            catch (Exception ex)
                            {
                                failed.Add($"{proposal.ProposedLabel}: SelectPanel failed — {ex.Message}");
                                tx.RollBack();
                                continue;
                            }

                            if (!string.IsNullOrEmpty(proposal.Phase) && proposal.Phase.Length == 1)
                            {
                                try
                                {
                                    sys.StartingPhase = proposal.Phase switch
                                    {
                                        "A" => ElectricalPhase.A,
                                        "B" => ElectricalPhase.B,
                                        "C" => ElectricalPhase.C,
                                        _ => sys.StartingPhase
                                    };
                                }
                                catch (Exception ex) { StingLog.Warn($"StartingPhase: {ex.Message}"); }
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
