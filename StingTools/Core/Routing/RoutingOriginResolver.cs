using StingTools.Core;
// StingTools — RoutingOriginResolver.
//
// One origin contract shared across the conduit routers. Both the v4 drop
// engine (DropEngineBase.TryDropFromFixture) and the cable-manifest router
// (ConduitAutoRouteCommand) must start a run from the same place: the
// fixture's conduit connector when it has one, NOT the family insertion
// point. Historically ConduitAutoRouteCommand read LocationPoint directly
// and so began conduit runs at the family origin rather than the authored
// terminal — a separate silo that ignored placed-fixture connectors.
//
// Preference order (mirrors DropEngineBase.FindBestFreeConnector + its
// LocationPoint fallback):
//   1. a FREE Domain.DomainCableTrayConduit connector origin;
//   2. any FREE connector origin (covers electrical/power terminals);
//   3. any connector origin (even connected — still better than insertion pt);
//   4. the family LocationPoint.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace StingTools.Core.Routing
{
    public static class RoutingOriginResolver
    {
        /// <summary>Resolve the routing origin for an element. <paramref name="source"/>
        /// reports which rung of the contract supplied the point
        /// ("conduit-connector" / "free-connector" / "connector" / "location-point").</summary>
        public static XYZ Resolve(Element el, out string source)
        {
            source = "none";
            if (el == null) return null;

            XYZ anyFree = null, any = null;
            foreach (var c in GetConnectors(el))
            {
                XYZ o;
                try { o = c.Origin; } catch { continue; }
                if (o == null) continue;
                if (any == null) any = o;

                bool connected;
                try { connected = c.IsConnected; } catch { connected = false; }
                if (!connected && anyFree == null) anyFree = o;

                Domain d;
                try { d = c.Domain; } catch { d = Domain.DomainUndefined; }
                if (!connected && d == Domain.DomainCableTrayConduit)
                {
                    source = "conduit-connector";
                    return o;
                }
            }

            if (anyFree != null) { source = "free-connector"; return anyFree; }
            if (any != null)     { source = "connector";      return any; }

            var lp = el.Location as LocationPoint;
            if (lp?.Point != null) { source = "location-point"; return lp.Point; }
            return null;
        }

        /// <summary>Convenience overload without the diagnostic source out-param.</summary>
        public static XYZ Resolve(Element el) => Resolve(el, out _);

        private static IEnumerable<Connector> GetConnectors(Element el)
        {
            ConnectorManager cm = null;
            try
            {
                if (el is FamilyInstance fi) cm = fi.MEPModel?.ConnectorManager;
                else if (el is MEPCurve mc)  cm = mc.ConnectorManager;
            }
            catch { cm = null; }
            if (cm == null) yield break;
            ConnectorSet set;
            try { set = cm.Connectors; } catch { yield break; }
            if (set == null) yield break;
            foreach (Connector c in set) if (c != null) yield return c;
        }
    }
}
