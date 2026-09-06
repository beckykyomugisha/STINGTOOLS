// ══════════════════════════════════════════════════════════════════════════
//  RoomFinishGatherer.cs — MAT-SCHED-3: finishes as the model actually
//  records them.
//
//  The layer-based source (CompoundTakeoffBuilder.ReadTiledFinish) reads a
//  type's compound structure. On the first real model it found ONE finish
//  layer across ten wall/floor types, and that one was Gypsum Wall Board. That
//  is not a broken model — most architects never layer finishes. They record
//  them on the ROOM, and Revit has first-class fields for it.
//
//  So this is a SECOND source, not a fallback guess: it measures what the room
//  states, from the room's own Area and Perimeter. A model that layers its
//  finishes keeps using the layer source; a model that schedules them starts
//  working. Neither invents a finish the model does not name.
//
//  Skirting arrives here for free. It runs to a room's PERIMETER, which is
//  exactly why the layer source could not measure it and said so.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using StingTools.BOQ.Takeoff;
using StingTools.Core;
using StingTools.Core.MaterialSchedule;

namespace StingTools.BOQ.MaterialSchedule
{
    public static class RoomFinishGatherer
    {
        private const string TracePrefix = "room";

        /// <summary>
        /// Constituent rows derived from room finish fields.
        ///
        /// <paramref name="layerSourceProducedTiling"/> suppresses the TILE rows
        /// only. Both sources measure the same surface from opposite ends, so
        /// running them together would price it twice; skirting has no layer
        /// equivalent and is never suppressed.
        /// </summary>
        public static List<ConstituentInput> Gather(Document doc, bool layerSourceProducedTiling,
                                                    RoomFinishTally tally)
        {
            var rows = new List<ConstituentInput>();
            if (doc == null) return rows;
            if (tally == null) tally = new RoomFinishTally();
            tally.TilingSuppressedByLayerSource = layerSourceProducedTiling;

            try
            {
                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType();

                foreach (Element el in rooms)
                {
                    var room = el as Room;
                    if (room == null) continue;

                    // An unplaced or redundant room reports Area 0. Measuring it
                    // would mint finishes for a room not in the building.
                    double areaM2 = ToM2(SafeDouble(() => room.Area));
                    if (areaM2 <= 0) continue;
                    tally.RoomsRead++;

                    double perimM = ToM(SafeDouble(() => room.Perimeter));
                    double heightM = ToM(SafeDouble(() => room.UnboundedHeight));

                    string floorFinish = Finish(room, BuiltInParameter.ROOM_FINISH_FLOOR, ParamRegistry.ROOM_FINISH_FLR);
                    string wallFinish = Finish(room, BuiltInParameter.ROOM_FINISH_WALL, ParamRegistry.ROOM_FINISH_WALL);
                    string baseFinish = Finish(room, BuiltInParameter.ROOM_FINISH_BASE, ParamRegistry.ROOM_FINISH_BASE);

                    if (!layerSourceProducedTiling)
                    {
                        if (FinishTextClassifier.IsTile(floorFinish))
                        {
                            tally.FloorsTiled++;
                            AddTiling(rows, doc, el, areaM2, false, floorFinish);
                        }
                        else if (FinishTextClassifier.IsRealFinish(floorFinish))
                        {
                            tally.UnrecognisedFloorFinishes.Add(floorFinish.Trim());
                        }

                        // Wall tiling measures the room's own height, because
                        // that is what the model states. A splashback modelled
                        // as a full-height tiled wall is a modelling decision,
                        // not a number for this code to second-guess.
                        if (FinishTextClassifier.IsTile(wallFinish) && perimM > 0 && heightM > 0)
                        {
                            tally.WallsTiled++;
                            AddTiling(rows, doc, el, perimM * heightM, true, wallFinish);
                        }
                    }

                    if (FinishTextClassifier.IsRealFinish(baseFinish) && perimM > 0)
                    {
                        tally.SkirtingRuns++;
                        rows.Add(new ConstituentInput
                        {
                            ConstituentKind = "skirting",
                            Category = "Rooms",
                            TypeName = baseFinish,
                            Description = "Skirting — " + baseFinish,
                            Unit = "m",
                            Quantity = Math.Round(perimM, 4),
                            LevelCode = LevelOf(doc, el),
                            TraceRef = TracePrefix + ":" + el.Id
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn("RoomFinishGatherer.Gather: " + ex.Message);
            }
            return rows;
        }

        private static void AddTiling(List<ConstituentInput> rows, Document doc, Element el,
                                      double areaM2, bool isWall, string finishName)
        {
            var cover = CompoundTakeoffBuilder.TileCoverages(finishName);
            var lines = CompoundTakeoff.TiledFinish(new TiledFinishInput
            {
                AreaM2 = areaM2,
                IsWall = isWall,
                TileLabel = finishName,
                AdhesiveKgPerM2 = cover.AdhesiveKgPerM2,
                GroutKgPerM2 = cover.GroutKgPerM2
            });

            foreach (var l in lines)
                rows.Add(new ConstituentInput
                {
                    ConstituentKind = l.Kind,
                    Category = "Rooms",
                    TypeName = finishName,
                    Description = l.Description,
                    Unit = l.Unit,
                    Quantity = l.Quantity,
                    LevelCode = LevelOf(doc, el),
                    TraceRef = TracePrefix + ":" + el.Id
                });
        }

        /// <summary>
        /// Built-in first: it is what the architect typed. The BLE_* copy is
        /// written by the parameter-mapping pass, which may never have run in
        /// this document.
        /// </summary>
        private static string Finish(Room room, BuiltInParameter bip, string sharedName)
        {
            try
            {
                var p = room.get_Parameter(bip);
                string v = p == null ? null : p.AsString();
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
            catch (Exception ex) { StingLog.Warn("Suppressed: " + ex.Message); }
            return ParameterHelpers.GetString(room, sharedName) ?? "";
        }

        private static string LevelOf(Document doc, Element el)
        {
            try { return ParameterHelpers.GetLevelCode(doc, el) ?? ""; }
            catch (Exception ex) { StingLog.Warn("Suppressed: " + ex.Message); return ""; }
        }

        private static double SafeDouble(Func<double> f)
        {
            try { return f(); }
            catch (Exception ex) { StingLog.Warn("Suppressed: " + ex.Message); return 0; }
        }

        private static double ToM2(double internalUnits)
        {
            try { return UnitUtils.ConvertFromInternalUnits(internalUnits, UnitTypeId.SquareMeters); }
            catch (Exception ex) { StingLog.Warn("Suppressed: " + ex.Message); return 0; }
        }

        private static double ToM(double internalUnits)
        {
            try { return UnitUtils.ConvertFromInternalUnits(internalUnits, UnitTypeId.Meters); }
            catch (Exception ex) { StingLog.Warn("Suppressed: " + ex.Message); return 0; }
        }
    }
}
