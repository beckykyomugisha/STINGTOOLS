// PipeNetwork — a general (looped or branched) pipe network and a nodal solver.
//
// Revit-free. Where FlowTree can only describe a tree, PipeNetwork is a graph:
// junction nodes joined by links (pipes, two-port fittings, valves). It is
// solved for node pressures by Newton's method on continuity, which handles
// gridded sprinkler systems and looped gas installations exactly — no
// equivalent-K approximation at junctions and no spanning-tree shortcut.
//
// Link law:  Δh = R · Q·|Q|^(n−1)
//   water (Hazen-Williams, BS EN 12845 form): n = 1.85,
//          R = 6.05e5 · L / (C^1.85 · d^4.87)            bar, L/min, mm, m
//   gas   (Pole):                              n = 2,
//          R = s · L / (0.0071² · d⁵)                    mbar, m³/h, mm, m
// Node demand: a fixed flow (appliance), or an orifice q = K·√p (sprinkler
// head, p gauge). Static head enters as p + g·z for water (g = 0.0981 bar/m).
//
// The Jacobian of this system is symmetric positive definite (a weighted
// graph Laplacian plus the orifice terms, with the source pressure fixed), so
// each Newton step is solved by preconditioned conjugate gradients on a
// sparse matrix — the solver scales to whole installations.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Mep.Networks
{
    public enum NetNodeKind { Junction, Source, Terminal }
    public enum NetLinkKind { Pipe, Fitting, Accessory }

    public sealed class NetNode
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public NetNodeKind Kind { get; set; }
        public double ElevationM { get; set; }
        /// <summary>Orifice coefficient (sprinkler K, L/min/bar^0.5). 0 = none.</summary>
        public double KFactor { get; set; }
        /// <summary>Appliance heat input, kW (gas).</summary>
        public double LoadKw { get; set; }
        /// <summary>Element the node stands for (terminal, source or a multi-port fitting), if any.</summary>
        public string ElementId { get; set; } = "";
    }

    public sealed class NetLink
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public NetLinkKind Kind { get; set; }
        public string A { get; set; } = "";
        public string B { get; set; } = "";
        public double LengthM { get; set; }
        /// <summary>Equivalent length already in metres (fittings, valves, and shares of multi-port fittings).</summary>
        public double EquivLengthM { get; set; }
        public double BoreMm { get; set; }
        public double HazenWilliamsC { get; set; }
        public string ElementId { get; set; } = "";
        public double TotalLengthM => LengthM + EquivLengthM;
    }

    public sealed class PipeNetwork
    {
        public Dictionary<string, NetNode> Nodes { get; } = new Dictionary<string, NetNode>();
        public List<NetLink> Links { get; } = new List<NetLink>();

        public NetNode AddNode(NetNode n) { Nodes[n.Id] = n; return n; }
        public NetLink Connect(NetLink l) { Links.Add(l); return l; }

        public NetNode Source => Nodes.Values.FirstOrDefault(n => n.Kind == NetNodeKind.Source);
        public IEnumerable<NetNode> Terminals => Nodes.Values.Where(n => n.Kind == NetNodeKind.Terminal);

        /// <summary>Independent loops = links − nodes + connected components.</summary>
        public int LoopCount
        {
            get
            {
                var parent = Nodes.Keys.ToDictionary(k => k, k => k);
                string Find(string x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
                int loops = 0;
                foreach (var l in Links)
                {
                    if (!parent.ContainsKey(l.A) || !parent.ContainsKey(l.B)) continue;
                    string a = Find(l.A), b = Find(l.B);
                    if (a == b) loops++; else parent[a] = b;
                }
                return loops;
            }
        }

        /// <summary>
        /// Remove dead ends: repeatedly drop junctions with one link (and that
        /// link) until every remaining leaf is the source or a terminal.
        /// Closed-off branches and un-selected sprinkler lines carry no flow.
        /// </summary>
        public int PruneDeadEnds()
        {
            int removed = 0;
            while (true)
            {
                var degree = Nodes.Keys.ToDictionary(k => k, k => 0);
                foreach (var l in Links)
                {
                    if (degree.ContainsKey(l.A)) degree[l.A]++;
                    if (degree.ContainsKey(l.B)) degree[l.B]++;
                }
                var dead = Nodes.Values.Where(n => n.Kind == NetNodeKind.Junction && degree[n.Id] <= 1).Select(n => n.Id).ToHashSet();
                if (dead.Count == 0) return removed;
                Links.RemoveAll(l => dead.Contains(l.A) || dead.Contains(l.B));
                foreach (var d in dead) Nodes.Remove(d);
                removed += dead.Count;
            }
        }
    }

    public sealed class NetSolveResult
    {
        public bool Converged { get; set; }
        public int Iterations { get; set; }
        public double MaxImbalance { get; set; }
        /// <summary>Gauge pressure (bar for water, mbar relative to the source for gas) per node id.</summary>
        public Dictionary<string, double> Pressure { get; } = new Dictionary<string, double>();
        /// <summary>Flow A→B per link (negative = B→A), in the solver's flow unit.</summary>
        public Dictionary<NetLink, double> Flow { get; } = new Dictionary<NetLink, double>();
        /// <summary>Flow drawn at each terminal node.</summary>
        public Dictionary<string, double> Demand { get; } = new Dictionary<string, double>();
        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>
    /// Nodal Newton solver. Callers supply the link resistance and exponent,
    /// the fixed source pressure, and per-terminal either a fixed demand or
    /// an orifice K.
    /// </summary>
    public sealed class PipeNetworkSolver
    {
        private readonly PipeNetwork _net;
        private readonly Func<NetLink, double> _resistance;
        private readonly double _n;
        private readonly double _staticPerM;
        private readonly Func<NetNode, double> _fixedDemand;
        private readonly Func<NetNode, double> _orificeK;

        public int MaxIterations { get; set; } = 200;
        /// <summary>Continuity tolerance, in flow units.</summary>
        public double Tolerance { get; set; } = 1e-7;

        /// <param name="staticPerM">Pressure per metre of rise (0.0981 bar/m for water, 0 for gas).</param>
        public PipeNetworkSolver(PipeNetwork net, Func<NetLink, double> resistance, double exponent,
            double staticPerM, Func<NetNode, double> fixedDemand, Func<NetNode, double> orificeK)
        {
            _net = net ?? throw new ArgumentNullException(nameof(net));
            _resistance = resistance;
            _n = exponent;
            _staticPerM = staticPerM;
            _fixedDemand = fixedDemand ?? (_ => 0);
            _orificeK = orificeK ?? (_ => 0);
        }

        // Below this driving head the link law is linearised, so the
        // derivative stays finite when a link carries (almost) no flow.
        private const double LinearBelow = 1e-8;
        private const double OrificeLinearBelow = 1e-8;

        private double LinkFlow(double dh, double r, out double dq)
        {
            double a = Math.Abs(dh);
            if (a < LinearBelow)
            {
                double slope = Math.Pow(LinearBelow / r, 1.0 / _n) / LinearBelow;
                dq = slope;
                return dh * slope;
            }
            double q = Math.Pow(a / r, 1.0 / _n);
            dq = q / (_n * a);
            return Math.Sign(dh) * q;
        }

        private double Orifice(double p, double k, out double dq)
        {
            if (k <= 0) { dq = 0; return 0; }
            if (p < OrificeLinearBelow)
            {
                double slope = k / Math.Sqrt(OrificeLinearBelow);
                dq = slope;
                return p * slope;                         // below zero a head draws nothing real
            }
            double sp = Math.Sqrt(p);
            dq = k / (2 * sp);
            return k * sp;
        }

        public NetSolveResult Solve(double sourcePressure, IDictionary<string, double> initial = null)
        {
            var r = new NetSolveResult();
            var src = _net.Source;
            if (src == null) { r.Warnings.Add("Network has no source node."); return r; }

            var unknown = _net.Nodes.Values.Where(n => n.Kind != NetNodeKind.Source).Select(n => n.Id).ToList();
            var index = new Dictionary<string, int>();
            for (int i = 0; i < unknown.Count; i++) index[unknown[i]] = i;
            var res = new Dictionary<NetLink, double>();
            foreach (var l in _net.Links)
            {
                double rr = _resistance(l);
                if (!(rr > 0) || double.IsInfinity(rr))
                {
                    r.Warnings.Add($"{l.Label}: no resistance (bore or length missing) — link treated as closed.");
                    continue;
                }
                res[l] = rr;
            }

            var p = new double[unknown.Count];
            for (int i = 0; i < p.Length; i++)
                p[i] = initial != null && initial.TryGetValue(unknown[i], out var v0) ? v0 : sourcePressure;
            double P(string id) => id == src.Id ? sourcePressure : (index.TryGetValue(id, out var i) ? p[i] : sourcePressure);
            double Z(string id) => _net.Nodes.TryGetValue(id, out var nd) ? nd.ElevationM : 0;

            double[] Residual(double[] pp, out List<Dictionary<int, double>> jac)
            {
                var F = new double[unknown.Count];
                jac = new List<Dictionary<int, double>>(unknown.Count);
                for (int i = 0; i < unknown.Count; i++) jac.Add(new Dictionary<int, double>());
                double Pp(string id) => id == src.Id ? sourcePressure : pp[index[id]];
                foreach (var kv in res)
                {
                    var l = kv.Key;
                    if (!index.ContainsKey(l.A) && l.A != src.Id) continue;
                    if (!index.ContainsKey(l.B) && l.B != src.Id) continue;
                    double dh = (Pp(l.A) + _staticPerM * Z(l.A)) - (Pp(l.B) + _staticPerM * Z(l.B));
                    double q = LinkFlow(dh, kv.Value, out double w);
                    // q leaves A, enters B.
                    if (index.TryGetValue(l.A, out int ia))
                    {
                        F[ia] += q;
                        Add(jac[ia], ia, w);
                        if (index.TryGetValue(l.B, out int ib0)) Add(jac[ia], ib0, -w);
                    }
                    if (index.TryGetValue(l.B, out int ib))
                    {
                        F[ib] -= q;
                        Add(jac[ib], ib, w);
                        if (index.TryGetValue(l.A, out int ia0)) Add(jac[ib], ia0, -w);
                    }
                }
                for (int i = 0; i < unknown.Count; i++)
                {
                    var node = _net.Nodes[unknown[i]];
                    if (node.Kind != NetNodeKind.Terminal) continue;
                    double k = _orificeK(node);
                    if (k > 0)
                    {
                        F[i] += Orifice(pp[i], k, out double dq);
                        Add(jac[i], i, dq);
                    }
                    else F[i] += _fixedDemand(node);
                }
                return F;
            }

            double Norm(double[] v) => v.Length == 0 ? 0 : v.Max(x => Math.Abs(x));

            var Fk = Residual(p, out var J);
            double fn = Norm(Fk);
            int it = 0;
            for (; it < MaxIterations && fn > Tolerance; it++)
            {
                var neg = Fk.Select(x => -x).ToArray();
                var dp = ConjugateGradient(J, neg, 1e-12, Math.Max(200, 4 * unknown.Count));
                // Backtracking line search on the max-norm of the residual.
                double step = 1.0;
                double[] trial = null; double[] Ft = null; List<Dictionary<int, double>> Jt = null; double ftn = double.MaxValue;
                for (int ls = 0; ls < 30; ls++)
                {
                    trial = new double[p.Length];
                    for (int i = 0; i < p.Length; i++) trial[i] = p[i] + step * dp[i];
                    Ft = Residual(trial, out Jt);
                    ftn = Norm(Ft);
                    if (ftn < fn || ftn <= Tolerance) break;
                    step *= 0.5;
                }
                p = trial; Fk = Ft; J = Jt; fn = ftn;
            }
            r.Iterations = it;
            r.MaxImbalance = fn;
            r.Converged = fn <= Tolerance * 10;
            if (!r.Converged) r.Warnings.Add($"Network solver stopped after {it} iterations with a flow imbalance of {fn:E2}.");

            r.Pressure[src.Id] = sourcePressure;
            for (int i = 0; i < unknown.Count; i++) r.Pressure[unknown[i]] = p[i];
            foreach (var kv in res)
            {
                var l = kv.Key;
                double dh = (P(l.A) + _staticPerM * Z(l.A)) - (P(l.B) + _staticPerM * Z(l.B));
                r.Flow[l] = LinkFlow(dh, kv.Value, out _);
            }
            foreach (var t in _net.Terminals)
            {
                double k = _orificeK(t);
                r.Demand[t.Id] = k > 0 ? Orifice(r.Pressure[t.Id], k, out _) : _fixedDemand(t);
            }
            return r;
        }

        private static void Add(Dictionary<int, double> row, int col, double v)
            => row[col] = row.TryGetValue(col, out var x) ? x + v : v;

        /// <summary>Jacobi-preconditioned conjugate gradients for a sparse SPD system.</summary>
        internal static double[] ConjugateGradient(List<Dictionary<int, double>> A, double[] b, double tol, int maxIt)
        {
            int n = b.Length;
            var x = new double[n];
            var diag = new double[n];
            for (int i = 0; i < n; i++) diag[i] = A[i].TryGetValue(i, out var d) && d > 0 ? d : 1.0;
            double[] Mul(double[] v)
            {
                var y = new double[n];
                for (int i = 0; i < n; i++) foreach (var kv in A[i]) y[i] += kv.Value * v[kv.Key];
                return y;
            }
            var rr = (double[])b.Clone();
            var z = new double[n];
            for (int i = 0; i < n; i++) z[i] = rr[i] / diag[i];
            var pdir = (double[])z.Clone();
            double rz = Dot(rr, z);
            double bnorm = Math.Sqrt(Dot(b, b));
            if (bnorm == 0) return x;
            for (int it = 0; it < maxIt; it++)
            {
                var Ap = Mul(pdir);
                double pAp = Dot(pdir, Ap);
                if (Math.Abs(pAp) < 1e-300) break;
                double alpha = rz / pAp;
                for (int i = 0; i < n; i++) { x[i] += alpha * pdir[i]; rr[i] -= alpha * Ap[i]; }
                if (Math.Sqrt(Dot(rr, rr)) <= tol * bnorm) break;
                for (int i = 0; i < n; i++) z[i] = rr[i] / diag[i];
                double rzNew = Dot(rr, z);
                double beta = rzNew / rz;
                rz = rzNew;
                for (int i = 0; i < n; i++) pdir[i] = z[i] + beta * pdir[i];
            }
            return x;
        }

        private static double Dot(double[] a, double[] b)
        {
            double s = 0;
            for (int i = 0; i < a.Length; i++) s += a[i] * b[i];
            return s;
        }
    }
}
