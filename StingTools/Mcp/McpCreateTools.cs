// ════════════════════════════════════════════════════════════════════════════
// McpCreateTools — Create-stage WRITE verbs that build model geometry
//
// Thin adapters over the dialog-free StingTools.Model.ModelEngine. Each tool:
//   • runs on the Revit API thread via McpJobBridge.Run (60s, no modal UI),
//   • Guard()s the licence + open document (McpSafety),
//   • resolves type/level/family NAMES against the live doc — an explicitly named
//     type/level/family that does not resolve returns bad_args listing the options
//     (never a silent wrong-type create); an omitted name uses the project default,
//     reported back as typeUsed,
//   • dryRun:true VALIDATES only (resolution + coord/dimension sanity) and returns
//     the plan — it creates NOTHING,
//   • building_shell (and any >5-element create) requires confirm:true,
//   • returns read-back {created:[ids], count, typeUsed, warnings}.
//
// IMPORTANT — transactions: unlike the parameter/tag write verbs, the ModelEngine
// methods open (and commit/roll back) their OWN Transaction/TransactionGroup
// internally. These tools therefore do NOT wrap a second transaction — they reuse
// the engine's, and report created ids from ModelResult only on Success (== commit).
// All coordinates and dimensions are MILLIMETRES.
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.Model;

namespace StingTools.Mcp
{
    internal static class McpCreateTools
    {
        // Coordinates beyond ±10 km (mm) are almost certainly a units mistake.
        private const double MaxCoordMm = 10_000_000.0;

        // ── create_wall ──────────────────────────────────────────────────────────

        public static McpCallResult CreateWall(JObject args)
        {
            return McpJobBridge.Run(uiApp =>
            {
                var g = Guard(uiApp, out Document doc);
                if (g != null) return g;

                if (!TryNum(args, "startX", out double sx, out var e1)) return e1;
                if (!TryNum(args, "startY", out double sy, out var e2)) return e2;
                if (!TryNum(args, "endX", out double ex, out var e3)) return e3;
                if (!TryNum(args, "endY", out double ey, out var e4)) return e4;
                double heightMm = args["heightMm"]?.Value<double?>() ?? 0;

                var sane = CheckFinite(sx, sy, ex, ey, heightMm);
                if (sane != null) return sane;
                if (heightMm <= 0) return Bad("heightMm must be greater than 0.");
                double lenMm = Math.Sqrt((ex - sx) * (ex - sx) + (ey - sy) * (ey - sy));
                if (lenMm < 3.0) return Bad("Start and end points are too close to form a wall (< 3 mm).");

                if (!ResolveType<WallType>(doc, args["typeName"]?.Value<string>(), "wall type", out string wt, out var te)) return te;
                if (!ResolveLevel(doc, args["levelName"]?.Value<string>(), out string lv, out var le)) return le;

                if (McpSafety.IsDryRun(args))
                    return Plan("wall", wt, lv, new Dictionary<string, object>
                    {
                        ["lengthMm"] = Math.Round(lenMm, 1),
                        ["heightMm"] = heightMm,
                    });

                var res = new ModelEngine(doc).CreateWall(sx, sy, ex, ey, wt, lv, heightMm);
                return ReadBack(doc, res, wt);
            }, 60000).ToCallResult();
        }

        // ── create_floor ─────────────────────────────────────────────────────────

        public static McpCallResult CreateFloor(JObject args)
        {
            return McpJobBridge.Run(uiApp =>
            {
                var g = Guard(uiApp, out Document doc);
                if (g != null) return g;

                if (!TryProfile(args, out var profile, out var pe)) return pe;
                if (!ResolveType<FloorType>(doc, args["typeName"]?.Value<string>(), "floor type", out string ft, out var te)) return te;
                if (!ResolveLevel(doc, args["levelName"]?.Value<string>(), out string lv, out var le)) return le;

                if (McpSafety.IsDryRun(args))
                    return Plan("floor", ft, lv, new Dictionary<string, object> { ["points"] = profile.Count });

                var res = new ModelEngine(doc).CreateFloorFromProfile(profile, ft, lv);
                return ReadBack(doc, res, ft);
            }, 60000).ToCallResult();
        }

        // ── create_floor_in_room ─────────────────────────────────────────────────

        public static McpCallResult CreateFloorInRoom(JObject args)
        {
            return McpJobBridge.Run(uiApp =>
            {
                var g = Guard(uiApp, out Document doc);
                if (g != null) return g;

                if (!TryId(args, "roomId", out long roomId, out var re)) return re;
                if (!(doc.GetElement(new ElementId(roomId)) is Room room))
                    return McpJobResult.Error("bad_args", $"Element {roomId} is not a Room.");

                if (!ResolveType<FloorType>(doc, args["typeName"]?.Value<string>(), "floor type", out string ft, out var te)) return te;
                if (!ResolveLevel(doc, args["levelName"]?.Value<string>(), out string lv, out var le)) return le;

                if (McpSafety.IsDryRun(args))
                    return Plan("floor-in-room", ft, lv, new Dictionary<string, object> { ["roomId"] = roomId, ["room"] = SafeRoomName(room) });

                var res = new ModelEngine(doc).CreateFloorInRoom(room, ft, lv);
                return ReadBack(doc, res, ft);
            }, 60000).ToCallResult();
        }

        // ── create_roof ──────────────────────────────────────────────────────────

        public static McpCallResult CreateRoof(JObject args)
        {
            return McpJobBridge.Run(uiApp =>
            {
                var g = Guard(uiApp, out Document doc);
                if (g != null) return g;

                if (!TryProfile(args, out var profile, out var pe)) return pe;
                double slope = args["slopeDeg"]?.Value<double?>() ?? 25;
                if (slope < 0 || slope > 85) return Bad("slopeDeg must be between 0 and 85.");
                if (!ResolveType<RoofType>(doc, args["typeName"]?.Value<string>(), "roof type", out string rt, out var te)) return te;
                if (!ResolveLevel(doc, args["levelName"]?.Value<string>(), out string lv, out var le)) return le;

                if (McpSafety.IsDryRun(args))
                    return Plan("roof", rt, lv, new Dictionary<string, object> { ["points"] = profile.Count, ["slopeDeg"] = slope });

                var res = new ModelEngine(doc).CreateRoofFromProfile(profile, rt, lv, slope);
                return ReadBack(doc, res, rt);
            }, 60000).ToCallResult();
        }

        // ── create_duct ──────────────────────────────────────────────────────────

        public static McpCallResult CreateDuct(JObject args)
        {
            return McpJobBridge.Run(uiApp =>
            {
                var g = Guard(uiApp, out Document doc);
                if (g != null) return g;

                if (!TryRun3D(args, out double sx, out double sy, out double sz,
                              out double ex, out double ey, out double ez, out var re)) return re;

                double dia = args["diameterMm"]?.Value<double?>() ?? args["sizeMm"]?.Value<double?>() ?? 0;
                double w = args["widthMm"]?.Value<double?>() ?? 0;
                double h = args["heightMm"]?.Value<double?>() ?? 0;
                if (dia < 0 || w < 0 || h < 0) return Bad("Sizes must be non-negative.");

                if (!ResolveType<DuctType>(doc, args["ductTypeName"]?.Value<string>() ?? args["typeName"]?.Value<string>(),
                        "duct type", out string dt, out var te)) return te;
                if (!ResolveLevel(doc, args["levelName"]?.Value<string>(), out string lv, out var le)) return le;

                if (McpSafety.IsDryRun(args))
                    return Plan("duct", dt, lv, new Dictionary<string, object>
                    {
                        ["diameterMm"] = dia, ["widthMm"] = w, ["heightMm"] = h,
                        ["note"] = "duct system defaults to Supply Air",
                    });

                var res = new ModelEngine(doc).CreateDuct(sx, sy, sz, ex, ey, ez, dt, lv, dia, w, h);
                return ReadBack(doc, res, dt);
            }, 60000).ToCallResult();
        }

        // ── create_pipe ──────────────────────────────────────────────────────────

        public static McpCallResult CreatePipe(JObject args)
        {
            return McpJobBridge.Run(uiApp =>
            {
                var g = Guard(uiApp, out Document doc);
                if (g != null) return g;

                if (!TryRun3D(args, out double sx, out double sy, out double sz,
                              out double ex, out double ey, out double ez, out var re)) return re;

                double dia = args["diameterMm"]?.Value<double?>() ?? args["sizeMm"]?.Value<double?>() ?? 0;
                if (dia < 0) return Bad("diameterMm must be non-negative.");
                string sysType = args["systemType"]?.Value<string>() ?? "DomesticColdWater";

                if (!ResolveType<PipeType>(doc, args["pipeTypeName"]?.Value<string>() ?? args["typeName"]?.Value<string>(),
                        "pipe type", out string pt, out var te)) return te;
                if (!ResolveLevel(doc, args["levelName"]?.Value<string>(), out string lv, out var le)) return le;

                if (McpSafety.IsDryRun(args))
                    return Plan("pipe", pt, lv, new Dictionary<string, object>
                    {
                        ["diameterMm"] = dia, ["systemType"] = sysType,
                    });

                var res = new ModelEngine(doc).CreatePipe(sx, sy, sz, ex, ey, ez, pt, lv, sysType, dia);
                return ReadBack(doc, res, pt);
            }, 60000).ToCallResult();
        }

        // ── create_room ──────────────────────────────────────────────────────────

        public static McpCallResult CreateRoom(JObject args)
        {
            return McpJobBridge.Run(uiApp =>
            {
                var g = Guard(uiApp, out Document doc);
                if (g != null) return g;

                if (!TryNum(args, "x", out double x, out var e1)) return e1;
                if (!TryNum(args, "y", out double y, out var e2)) return e2;
                var sane = CheckFinite(x, y);
                if (sane != null) return sane;

                string name = args["name"]?.Value<string>();
                string number = args["number"]?.Value<string>();
                if (!ResolveLevel(doc, args["levelName"]?.Value<string>(), out string lv, out var le)) return le;

                if (McpSafety.IsDryRun(args))
                    return Plan("room", null, lv, new Dictionary<string, object>
                    {
                        ["x"] = x, ["y"] = y, ["name"] = name ?? "", ["number"] = number ?? "",
                    });

                var res = new ModelEngine(doc).PlaceRoom(x, y, name, number, lv);
                return ReadBack(doc, res, name ?? "Room");
            }, 60000).ToCallResult();
        }

        // ── place_family ─────────────────────────────────────────────────────────

        public static McpCallResult PlaceFamily(JObject args)
        {
            return McpJobBridge.Run(uiApp =>
            {
                var g = Guard(uiApp, out Document doc);
                if (g != null) return g;

                string familyName = args["familyName"]?.Value<string>()?.Trim();
                string typeName = args["typeName"]?.Value<string>()?.Trim();
                if (string.IsNullOrEmpty(familyName)) return Bad("Missing required argument: familyName.");

                if (!TryNum(args, "x", out double x, out var e1)) return e1;
                if (!TryNum(args, "y", out double y, out var e2)) return e2;
                double z = args["z"]?.Value<double?>() ?? 0;
                var sane = CheckFinite(x, y, z);
                if (sane != null) return sane;

                // Resolve the exact symbol (validation + bad_args with options).
                var symbols = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().ToList();
                if (symbols.Count == 0) return Bad("No loadable families in the project. Load one first.");

                var famPool = symbols
                    .Where(s => s.FamilyName.Equals(familyName, StringComparison.OrdinalIgnoreCase)).ToList();
                if (famPool.Count == 0)
                    famPool = symbols.Where(s => s.FamilyName.IndexOf(familyName, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                if (famPool.Count == 0)
                {
                    var fams = symbols.Select(s => s.FamilyName).Distinct().OrderBy(n => n).Take(20).ToList();
                    return McpJobResult.Error("bad_args",
                        $"No family matching '{familyName}'. Loaded families include: {string.Join(", ", fams)}" +
                        (symbols.Select(s => s.FamilyName).Distinct().Count() > 20 ? "…" : "") + ".");
                }

                FamilySymbol symbol;
                if (!string.IsNullOrEmpty(typeName))
                {
                    symbol = famPool.FirstOrDefault(s => s.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase))
                          ?? famPool.FirstOrDefault(s => s.Name.IndexOf(typeName, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (symbol == null)
                    {
                        var types = famPool.Select(s => s.Name).Distinct().OrderBy(n => n).Take(20).ToList();
                        return McpJobResult.Error("bad_args",
                            $"Family '{famPool[0].FamilyName}' has no type '{typeName}'. Available types: {string.Join(", ", types)}.");
                    }
                }
                else symbol = famPool[0];

                long? hostId = args["hostId"]?.Value<long?>();
                if (!ResolveLevel(doc, args["levelName"]?.Value<string>(), out string lv, out var le)) return le;

                if (McpSafety.IsDryRun(args))
                    return Plan("family", $"{symbol.FamilyName}: {symbol.Name}", lv, new Dictionary<string, object>
                    {
                        ["x"] = x, ["y"] = y, ["z"] = z, ["hostId"] = hostId ?? -1,
                    });

                var res = new ModelEngine(doc).PlaceFamilyByName(symbol.FamilyName, symbol.Name, x, y, z, lv, hostId);
                return ReadBack(doc, res, $"{symbol.FamilyName}: {symbol.Name}");
            }, 60000).ToCallResult();
        }

        // ── building_shell (multi-element → confirm required) ────────────────────

        public static McpCallResult BuildingShell(JObject args)
        {
            return McpJobBridge.Run(uiApp =>
            {
                var g = Guard(uiApp, out Document doc);
                if (g != null) return g;

                double w = args["widthMm"]?.Value<double?>() ?? 0;
                double d = args["depthMm"]?.Value<double?>() ?? 0;
                double h = args["heightMm"]?.Value<double?>() ?? 3000;
                double ox = args["originX"]?.Value<double?>() ?? 0;
                double oy = args["originY"]?.Value<double?>() ?? 0;

                var sane = CheckFinite(w, d, h, ox, oy);
                if (sane != null) return sane;
                if (w <= 0 || d <= 0) return Bad("widthMm and depthMm must be greater than 0.");
                if (h <= 0) return Bad("heightMm must be greater than 0.");

                if (!ResolveLevel(doc, args["levelName"]?.Value<string>(), out string lv, out var le)) return le;

                if (McpSafety.IsDryRun(args))
                    return Plan("building-shell", null, lv, new Dictionary<string, object>
                    {
                        ["widthMm"] = w, ["depthMm"] = d, ["heightMm"] = h,
                        ["elements"] = "4 walls + floor + roof (~6)",
                    });

                // Multi-element create → always requires confirm:true.
                var confirmErr = McpSafety.RequireConfirmation(6, isProjectScope: false, confirmed: McpSafety.IsConfirmed(args));
                if (confirmErr != null) return confirmErr;

                var res = new ModelEngine(doc).CreateBuildingShell(
                    w, d, h, roofSlopeDeg: 25, overhangMm: 600,
                    levelName: lv, wallTypeName: null, floorTypeName: null, roofTypeName: null,
                    originXMm: ox, originYMm: oy);
                return ReadBack(doc, res, lv ?? "(default)");
            }, 60000).ToCallResult();
        }

        // ── shared helpers ────────────────────────────────────────────────────────

        private static McpJobResult Guard(UIApplication uiApp, out Document doc)
        {
            doc = null;
            var lic = McpSafety.RequireLicense();
            if (lic != null) return lic;
            var de = McpSafety.RequireDocument(uiApp);
            if (de != null) return de;
            doc = uiApp.ActiveUIDocument.Document;
            return null;
        }

        private static McpJobResult Bad(string msg) => McpJobResult.Error("bad_args", msg);

        private static bool TryNum(JObject args, string key, out double val, out McpJobResult err)
        {
            val = 0; err = null;
            var tok = args[key];
            if (tok == null) { err = Bad($"Missing required argument: {key}."); return false; }
            double? v = tok.Type == JTokenType.String
                ? (double.TryParse(tok.Value<string>(), out double pv) ? pv : (double?)null)
                : tok.Value<double?>();
            if (v == null) { err = Bad($"Argument '{key}' must be a number (millimetres)."); return false; }
            val = v.Value;
            return true;
        }

        private static bool TryId(JObject args, string key, out long id, out McpJobResult err)
        {
            id = 0; err = null;
            var tok = args[key];
            if (tok == null) { err = Bad($"Missing required argument: {key}."); return false; }
            long? v = tok.Type == JTokenType.String
                ? (long.TryParse(tok.Value<string>(), out long pv) ? pv : (long?)null)
                : tok.Value<long?>();
            if (v == null) { err = Bad($"Argument '{key}' must be an element id."); return false; }
            id = v.Value;
            return true;
        }

        private static bool TryRun3D(JObject args,
            out double sx, out double sy, out double sz, out double ex, out double ey, out double ez,
            out McpJobResult err)
        {
            sx = sy = sz = ex = ey = ez = 0;
            if (!TryNum(args, "startX", out sx, out err)) return false;
            if (!TryNum(args, "startY", out sy, out err)) return false;
            sz = args["startZ"]?.Value<double?>() ?? 0;
            if (!TryNum(args, "endX", out ex, out err)) return false;
            if (!TryNum(args, "endY", out ey, out err)) return false;
            ez = args["endZ"]?.Value<double?>() ?? 0;
            err = CheckFinite(sx, sy, sz, ex, ey, ez);
            if (err != null) return false;
            double len = Math.Sqrt((ex - sx) * (ex - sx) + (ey - sy) * (ey - sy) + (ez - sz) * (ez - sz));
            if (len < 3.0) { err = Bad("Start and end points are too close (< 3 mm)."); return false; }
            return true;
        }

        private static bool TryProfile(JObject args, out List<(double xMm, double yMm)> profile, out McpJobResult err)
        {
            profile = new List<(double, double)>();
            err = null;
            if (!(args["profile"] is JArray arr) || arr.Count < 3)
            { err = Bad("profile must be an array of at least 3 [x, y] point pairs (mm)."); return false; }

            foreach (var item in arr)
            {
                if (!(item is JArray p) || p.Count < 2)
                { err = Bad("Each profile point must be a [x, y] pair."); return false; }
                double? x = p[0]?.Value<double?>();
                double? y = p[1]?.Value<double?>();
                if (x == null || y == null || !IsFinite(x.Value) || !IsFinite(y.Value) ||
                    Math.Abs(x.Value) > MaxCoordMm || Math.Abs(y.Value) > MaxCoordMm)
                { err = Bad("Profile point coordinates must be finite numbers within ±10 km (mm)."); return false; }
                profile.Add((x.Value, y.Value));
            }
            return true;
        }

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        private static McpJobResult CheckFinite(params double[] vals)
        {
            foreach (double v in vals)
            {
                if (!IsFinite(v)) return Bad("Coordinates/dimensions must be finite numbers.");
                if (Math.Abs(v) > MaxCoordMm) return Bad("Coordinate/dimension out of range (> ±10 km in mm — check units).");
            }
            return null;
        }

        /// <summary>Resolve a system-family type by name. Omitted → engine default (resolved=null,
        /// reported as project default). Named but absent → bad_args listing the options.</summary>
        private static bool ResolveType<T>(Document doc, string requested, string label,
            out string resolved, out McpJobResult err) where T : ElementType
        {
            resolved = null; err = null;
            var names = new FilteredElementCollector(doc).OfClass(typeof(T)).Cast<T>()
                .Select(t => t.Name).Distinct().OrderBy(n => n).ToList();
            if (names.Count == 0) { err = Bad($"No {label}s loaded in the project. Load one first."); return false; }
            if (string.IsNullOrWhiteSpace(requested)) return true;   // engine default

            var exact = names.FirstOrDefault(n => n.Equals(requested, StringComparison.OrdinalIgnoreCase));
            if (exact != null) { resolved = exact; return true; }
            var part = names.FirstOrDefault(n => n.IndexOf(requested, StringComparison.OrdinalIgnoreCase) >= 0);
            if (part != null) { resolved = part; return true; }

            err = McpJobResult.Error("bad_args",
                $"No {label} named '{requested}'. Available: {string.Join(", ", names.Take(20))}" +
                (names.Count > 20 ? "…" : "") + ".");
            return false;
        }

        private static bool ResolveLevel(Document doc, string requested, out string resolved, out McpJobResult err)
        {
            resolved = null; err = null;
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).ToList();
            if (levels.Count == 0) { err = Bad("No levels in the project. Create a level first."); return false; }
            if (string.IsNullOrWhiteSpace(requested)) return true;   // engine default (lowest level)

            var exact = levels.FirstOrDefault(l => l.Name.Equals(requested, StringComparison.OrdinalIgnoreCase));
            if (exact != null) { resolved = exact.Name; return true; }
            var part = levels.FirstOrDefault(l => l.Name.IndexOf(requested, StringComparison.OrdinalIgnoreCase) >= 0);
            if (part != null) { resolved = part.Name; return true; }

            err = McpJobResult.Error("bad_args",
                $"No level named '{requested}'. Available: {string.Join(", ", levels.Select(l => l.Name).Take(20))}" +
                (levels.Count > 20 ? "…" : "") + ".");
            return false;
        }

        private static McpJobResult Plan(string kind, string typeUsed, string levelUsed, Dictionary<string, object> details)
        {
            details ??= new Dictionary<string, object>();
            details["status"] = "dry_run";
            details["kind"] = kind;
            details["typeUsed"] = typeUsed ?? "(project default)";
            if (levelUsed != null || kind != "family") details["levelUsed"] = levelUsed ?? "(lowest level)";
            details["wouldCreate"] = kind == "building-shell" ? "~6 elements" : "1 element";
            return McpJobResult.Success(
                $"Dry run: would create {kind} [{typeUsed ?? "project default"}]; nothing created.", details);
        }

        private static McpJobResult ReadBack(Document doc, ModelResult res, string typeLabel)
        {
            if (res == null) return McpJobResult.Error("exception", "The model engine returned no result.");
            if (!res.Success)
                return McpJobResult.Error("exception", res.Error ?? res.Message ?? "Creation failed.");

            var ids = CollectIds(res);
            string typeUsed = typeLabel;
            if (ids.Count > 0)
            {
                string t = TypeNameOf(doc, new ElementId(ids[0]));
                if (!string.IsNullOrEmpty(t)) typeUsed = t;
            }

            var data = new Dictionary<string, object>
            {
                ["created"]  = ids,
                ["count"]    = ids.Count,
                ["typeUsed"] = typeUsed ?? "(default)",
                ["warnings"] = res.Warnings ?? new List<string>(),
            };
            return McpJobResult.Success(res.Message ?? $"Created {ids.Count} element(s).", data);
        }

        private static List<long> CollectIds(ModelResult res)
        {
            var ids = new List<long>();
            if (res.CreatedElementId != null && res.CreatedElementId != ElementId.InvalidElementId)
                ids.Add(res.CreatedElementId.Value);
            foreach (var id in res.CreatedElementIds)
                if (id != null && id != ElementId.InvalidElementId && !ids.Contains(id.Value))
                    ids.Add(id.Value);
            return ids;
        }

        private static string TypeNameOf(Document doc, ElementId id)
        {
            try
            {
                Element el = doc.GetElement(id);
                if (el == null) return null;
                Element t = doc.GetElement(el.GetTypeId());
                return t?.Name ?? el.Name;
            }
            catch { return null; }
        }

        private static string SafeRoomName(Room room)
        {
            try { return room.Name ?? ""; } catch { return ""; }
        }
    }
}
