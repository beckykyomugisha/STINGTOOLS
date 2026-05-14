// RealTimePipeSizer — IUpdater for automatic pipe sizing on placement or
// modification. Phase 179c.
//
// Registers as an optional IUpdater against the Pipe category, triggering
// on element addition and parameter change. For each modified pipe it:
//   1. Accumulates DFU/LU upstream (budget 200 hops, fast path).
//   2. Calls DrainageSizer.SizePipe or WaterSupplySizer for DN.
//   3. Writes PLM_CALC_DN_MM (drainage) or PLM_SUP_DN_REQ (supply).
//   4. Stamps PLM_PIPE_REAL_SIZE_BOOL = 1.
//
// Skips pipes already sized by STING (PLM_PIPE_REAL_SIZE_BOOL = 1 AND
// PLM_CALC_DN_MM > 0) to avoid infinite-update loops.
//
// The updater is optional: it does not fault the document if it is not
// registered, and StingToolsApp calls Register / Unregister explicitly.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core;

namespace StingTools.Core.Plumbing
{
    public class RealTimePipeSizer : IUpdater
    {
        // ── Updater identity ──────────────────────────────────────────────
        // AddIn GUID must match StingTools.addin / AssemblyInfo GUID
        private static readonly Guid _addinGuid   = new Guid("A1B2C3D4-5678-9ABC-DEF0-123456789ABC");
        private static readonly Guid _updaterGuid  = new Guid("B2C3D4E5-6789-ABCD-EF01-234567890BCD");

        private static readonly UpdaterId _updaterId =
            new UpdaterId(new AddInId(_addinGuid), _updaterGuid);

        /// <summary>Maximum upstream hops per pipe during real-time sizing pass.</summary>
        private const int MaxHops = 200;

        // ── IUpdater implementation ───────────────────────────────────────

        public UpdaterId GetUpdaterId()           => _updaterId;
        public string    GetUpdaterName()         => "STING Real-Time Pipe Sizer";
        public string    GetAdditionalInformation()
            => "Auto-sizes newly placed pipes from DFU/LU accumulation (optional).";
        public ChangePriority GetChangePriority() => ChangePriority.MEPFixtureChanges;

        public void Execute(UpdaterData data)
        {
            Document doc = data.GetDocument();
            if (doc == null) return;

            var allModified = new List<ElementId>();
            allModified.AddRange(data.GetAddedElementIds());
            allModified.AddRange(data.GetModifiedElementIds());

            if (allModified.Count == 0) return;

            // Determine project plumbing code once
            string code = ResolveCode(doc);

            foreach (var eid in allModified)
            {
                try
                {
                    SizeSinglePipe(doc, eid, code);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"RealTimePipeSizer.Execute pipe {eid.Value}: {ex.Message}");
                }
            }
        }

        // ── Static register / unregister / query ─────────────────────────

        /// <summary>
        /// Register the updater against the Pipe category in the given document.
        /// Safe to call multiple times — checks IsUpdaterRegistered first.
        /// </summary>
        public static void Register(UpdaterRegistry registry, Document doc)
        {
            if (registry == null || doc == null) return;
            try
            {
                if (UpdaterRegistry.IsUpdaterRegistered(_updaterId, doc)) return;

                var updater = new RealTimePipeSizer();
                registry.RegisterUpdater(updater, doc, isOptional: true);

                // Trigger on Pipe element addition
                var addFilter = new ElementCategoryFilter(BuiltInCategory.OST_PipeCurves);
                registry.AddTrigger(_updaterId, doc,
                    addFilter, Element.GetChangeTypeElementAddition());

                // Trigger on Pipe parameter changes (e.g. system type change)
                registry.AddTrigger(_updaterId, doc,
                    addFilter, Element.GetChangeTypeParameter(
                        new Parameter[0]));  // any parameter

                StingLog.Info("RealTimePipeSizer registered.");
            }
            catch (Exception ex)
            {
                StingLog.Error("RealTimePipeSizer.Register", ex);
            }
        }

        /// <summary>
        /// Unregister the updater from the document.
        /// </summary>
        public static void Unregister(UpdaterRegistry registry, Document doc)
        {
            if (registry == null || doc == null) return;
            try
            {
                if (!UpdaterRegistry.IsUpdaterRegistered(_updaterId, doc)) return;
                registry.UnregisterUpdater(_updaterId, doc);
                StingLog.Info("RealTimePipeSizer unregistered.");
            }
            catch (Exception ex)
            {
                StingLog.Error("RealTimePipeSizer.Unregister", ex);
            }
        }

        /// <summary>
        /// Returns true when the updater is currently registered and enabled for the document.
        /// </summary>
        public static bool IsRegistered(Document doc)
        {
            if (doc == null) return false;
            try
            {
                return UpdaterRegistry.IsUpdaterRegistered(_updaterId, doc)
                    && UpdaterRegistry.IsUpdaterEnabled(_updaterId, doc);
            }
            catch { return false; }
        }

        // ── Core sizing logic ─────────────────────────────────────────────

        private static void SizeSinglePipe(Document doc, ElementId eid, string code)
        {
            var el = doc.GetElement(eid);
            if (el is not Pipe pipe) return;

            // Skip if already STING-sized
            if (AlreadySized(pipe)) return;

            bool isDrainage = IsDrainageSystem(pipe);
            bool isSupply   = !isDrainage && IsSupplySystem(pipe);
            if (!isDrainage && !isSupply) return;

            // Accumulate upstream DFU / LU (fast-path, capped at MaxHops)
            double dfu = AccumulateDfuFast(doc, pipe, MaxHops);

            using (var t = new Transaction(doc, "STING Auto-Size Pipe"))
            {
                try
                {
                    t.Start();

                    if (isDrainage && dfu > 0.001)
                    {
                        var sizeResult = DrainageSizer.SizePipe(pipe, dfu, code);
                        if (sizeResult.RecommendedDnMm > 0)
                        {
                            TryWriteInt(pipe, ParamRegistry.PLM_CALC_DN, sizeResult.RecommendedDnMm);
                            TryWriteString(pipe, ParamRegistry.PLM_CALC_SLOPE,
                                sizeResult.SlopePct.ToString("F2"));
                        }
                    }
                    else if (isSupply && dfu > 0.001)
                    {
                        // For supply, dfu represents LU; run a lightweight supply size estimate
                        int supDn = EstimateSupplyDnMm(dfu, pipe, code);
                        if (supDn > 0)
                            TryWriteInt(pipe, ParamRegistry.PLM_SUP_LU_CW, (int)Math.Round(dfu));
                    }

                    // Stamp as STING-sized
                    TryWriteInt(pipe, ParamRegistry.PLM_PIPE_REAL_SIZE, 1);

                    t.Commit();
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"RealTimePipeSizer tx pipe {eid.Value}: {ex.Message}");
                    try { t.RollBack(); } catch { }
                }
            }
        }

        // ── DFU accumulation (fast-path BFS, capped) ─────────────────────

        private static double AccumulateDfuFast(Document doc, Pipe startPipe, int maxHops)
        {
            var visited = new HashSet<long>();
            var queue   = new Queue<(Element el, int hop)>();
            visited.Add(startPipe.Id.Value);
            queue.Enqueue((startPipe, 0));
            double sum = 0;

            while (queue.Count > 0)
            {
                var (el, hop) = queue.Dequeue();
                if (hop >= maxHops) continue;

                try
                {
                    ConnectorManager cm = (el as MEPCurve)?.ConnectorManager
                                       ?? (el as FamilyInstance)?.MEPModel?.ConnectorManager;
                    if (cm == null) continue;

                    foreach (Connector c in cm.Connectors)
                    {
                        if (!c.IsConnected) continue;
                        foreach (Connector other in c.AllRefs)
                        {
                            var owner = other.Owner;
                            if (owner == null || visited.Contains(owner.Id.Value)) continue;
                            visited.Add(owner.Id.Value);

                            var bic = (BuiltInCategory)(owner.Category?.Id?.Value ?? 0);
                            if (bic == BuiltInCategory.OST_PlumbingFixtures
                             || bic == BuiltInCategory.OST_MechanicalEquipment)
                            {
                                sum += FixtureUnitAggregator.GetFixtureDfu(owner);
                                continue;
                            }
                            queue.Enqueue((owner, hop + 1));
                        }
                    }
                }
                catch { }
            }
            return sum;
        }

        // ── Supply-side simple DN estimate ───────────────────────────────

        private static int EstimateSupplyDnMm(double lu, Pipe pipe, string code)
        {
            // BS EN 806-3 simplified: Qd ≈ K√LU; K=0.5 (office)
            // v_max = 2 m/s → A = Q/v → d = 2√(A/π)
            double k  = 0.5;
            double qd = k * Math.Sqrt(lu);        // L/s
            double aMm2 = (qd / 1000.0) / 2.0 * 1e6; // mm²  (Q in m³/s, v=2 m/s)
            double dMm  = 2.0 * Math.Sqrt(aMm2 / Math.PI);

            int[] series = { 15, 20, 22, 25, 28, 32, 35, 40, 50, 65, 80, 100, 125, 150 };
            return series.FirstOrDefault(d => d >= dMm);
        }

        // ── System classification ─────────────────────────────────────────

        private static bool IsDrainageSystem(Pipe p)
        {
            try
            {
                string sys = (p.MEPSystem?.Name ?? "").ToUpperInvariant();
                return sys.Contains("SANITARY") || sys.Contains("WASTE")
                    || sys.Contains("SOIL")     || sys.Contains("DRAIN")
                    || sys.Contains("STORM")    || sys.Contains("FOUL");
            }
            catch { return false; }
        }

        private static bool IsSupplySystem(Pipe p)
        {
            try
            {
                string sys = (p.MEPSystem?.Name ?? "").ToUpperInvariant();
                return sys.Contains("COLD") || sys.Contains("HOT")  || sys.Contains("DCW")
                    || sys.Contains("DHW")  || sys.Contains("MAINS") || sys.Contains("SUPPLY");
            }
            catch { return false; }
        }

        private static bool AlreadySized(Pipe pipe)
        {
            try
            {
                var sized = pipe.LookupParameter(ParamRegistry.PLM_PIPE_REAL_SIZE);
                if (sized == null) return false;
                bool hasFlag = sized.StorageType == StorageType.Integer && sized.AsInteger() == 1;
                if (!hasFlag) return false;

                var dn = pipe.LookupParameter(ParamRegistry.PLM_CALC_DN);
                if (dn == null) return false;
                int dnVal = dn.StorageType == StorageType.Integer ? dn.AsInteger() : 0;
                return dnVal > 0;
            }
            catch { return false; }
        }

        private static string ResolveCode(Document doc)
        {
            try
            {
                var p = doc?.ProjectInformation?.LookupParameter(ParamRegistry.PRJ_PLUMBING_CODE);
                if (p != null && p.HasValue && p.StorageType == StorageType.String)
                {
                    var s = p.AsString();
                    if (!string.IsNullOrWhiteSpace(s)) return s.Trim().ToUpperInvariant();
                }
            }
            catch { }
            return "BS-UK";
        }

        // ── Parameter write helpers ───────────────────────────────────────

        private static bool TryWriteInt(Element el, string paramName, int value)
        {
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || p.IsReadOnly) return false;
                if (p.StorageType == StorageType.Integer) { p.Set(value); return true; }
                if (p.StorageType == StorageType.Double)  { p.Set((double)value); return true; }
                if (p.StorageType == StorageType.String)  { p.Set(value.ToString()); return true; }
            }
            catch { }
            return false;
        }

        private static bool TryWriteString(Element el, string paramName, string value)
        {
            try
            {
                var p = el.LookupParameter(paramName);
                if (p == null || p.IsReadOnly) return false;
                if (p.StorageType == StorageType.String) { p.Set(value); return true; }
            }
            catch { }
            return false;
        }
    }
}
