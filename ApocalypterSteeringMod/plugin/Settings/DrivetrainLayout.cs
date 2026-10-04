using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ApocalypterSteeringMod.Settings
{
    /// <summary>
    /// A user-defined drivetrain topology (0.6.2, config only): which differentials and
    /// transfer cases exist and where each one's two outputs go, from the gearbox down to
    /// the wheels. One line of text, statements separated by ';':
    ///
    ///   gearbox -> transfer;
    ///   transfer: Open split=0.4 -> front, rear;
    ///   front: Open -> FL, FR;
    ///   rear: LSD stiffness=0.6 -> RL, RR
    ///
    /// <c>gearbox -> X</c> names what the gearbox drives (a node or a single wheel). A node is
    /// <c>name: Type [key=value ...] -> outputA, outputB</c>, where Type is Open, Locked or
    /// LSD (LimitedSlip). Keys: split (0..1, share of torque to outputA; Open diffs, and LSD
    /// in reverse), stiffness (0..1, Locked/LSD), slip (LSD slip torque N·m, 0..5000), power and
    /// coast (LSD ramps, 0..1). Outputs are node names or wheels: FL FR RL RR (first/last axle)
    /// or A&lt;n&gt;L / A&lt;n&gt;R / A&lt;n&gt; (axle n counted from the front, centre wheel without a side).
    ///
    /// The layout must be a tree: every node reachable from the gearbox, every node and wheel
    /// fed exactly once. NWH steps the powertrain by recursion through the outputs every
    /// physics tick, so a cycle would recurse until the game crashes, and a wheel fed twice
    /// would be physics-stepped twice per tick. Wheels the layout does not name are undriven.
    /// Pure parsing/validation only — no NWH types (the steering-prefix harness compiles
    /// Settings/ without them). Per-vehicle wheel resolution lives in VehicleTuner.Layout.cs.
    /// </summary>
    public sealed class DrivetrainLayout
    {
        public const int MaxNodes = 16;
        public const int MaxNameLength = 24;
        public const float SlipTorqueMax = 5000f;

        public enum TargetKind
        {
            Node,
            Wheel
        }

        public struct Target
        {
            public TargetKind Kind;
            public int Node;      // index into Nodes (Kind == Node)
            public int Axle;      // 1-based from the front; 0 = the last axle (RL/RR)
            public char Side;     // 'L', 'R' or 'C' (Kind == Wheel)
            public string Text;   // as written, for messages
        }

        public sealed class Node
        {
            public string Name;
            public DiffMode Type;             // Open, Locked or LimitedSlip (never Stock)
            public float Split = 0.5f;        // share of torque to output A (NWH biasAB = 1 - Split)
            public float Stiffness = 0.5f;
            public float SlipTorque = 400f;
            public float PowerRamp = 1f;
            public float CoastRamp = 0.5f;
            public Target A, B;
        }

        public Target Root;
        public Node[] Nodes;
        public string Source;

        private DrivetrainLayout()
        {
        }

        /// <summary>Wheel tokens and node names, for the "driven wheels" log line.</summary>
        public static string Describe(Target t, DrivetrainLayout layout)
        {
            if (t.Kind == TargetKind.Node)
            {
                return layout.Nodes[t.Node].Name;
            }
            return t.Text;
        }

        // ------------------------------------------------------------------ parsing

        public static bool TryParse(string text, out DrivetrainLayout layout, out string error)
        {
            layout = null;
            error = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "the layout is empty";
                return false;
            }

            var nodes = new List<Node>();
            var nodeTargets = new List<string[]>();   // raw output tokens, resolved after all names are known
            string rootToken = null;

            string[] statements = text.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.None);
            for (int s = 0; s < statements.Length; s++)
            {
                string st = statements[s].Trim();
                if (st.Length == 0)
                {
                    continue;
                }
                int arrow = st.IndexOf("->", StringComparison.Ordinal);
                if (arrow < 0)
                {
                    error = "'" + st + "': missing '->'";
                    return false;
                }
                string left = st.Substring(0, arrow).Trim();
                string right = st.Substring(arrow + 2).Trim();
                if (right.IndexOf("->", StringComparison.Ordinal) >= 0)
                {
                    error = "'" + st + "': one '->' per statement (separate statements with ';')";
                    return false;
                }

                int colon = left.IndexOf(':');
                if (colon < 0)
                {
                    if (!IsGearboxWord(left))
                    {
                        error = "'" + st + "': expected 'gearbox -> ...' or 'name: Type -> a, b'";
                        return false;
                    }
                    if (rootToken != null)
                    {
                        error = "'gearbox ->' is given twice";
                        return false;
                    }
                    if (right.Length == 0 || right.IndexOf(',') >= 0)
                    {
                        error = "'" + st + "': the gearbox drives exactly one thing (a node or a wheel)";
                        return false;
                    }
                    rootToken = right;
                    continue;
                }

                string name = left.Substring(0, colon).Trim();
                string spec = left.Substring(colon + 1).Trim();
                string nameError = CheckNodeName(name);
                if (nameError != null)
                {
                    error = nameError;
                    return false;
                }
                for (int i = 0; i < nodes.Count; i++)
                {
                    if (string.Equals(nodes[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        error = "node '" + name + "' is defined twice";
                        return false;
                    }
                }
                if (nodes.Count >= MaxNodes)
                {
                    error = "more than " + MaxNodes + " nodes";
                    return false;
                }

                var node = new Node { Name = name };
                string specError = ParseSpec(spec, node);
                if (specError != null)
                {
                    error = "node '" + name + "': " + specError;
                    return false;
                }

                string[] outs = right.Split(',');
                if (outs.Length != 2 || outs[0].Trim().Length == 0 || outs[1].Trim().Length == 0)
                {
                    error = "node '" + name + "': needs exactly two outputs ('-> a, b')";
                    return false;
                }
                nodes.Add(node);
                nodeTargets.Add(new[] { outs[0].Trim(), outs[1].Trim() });
            }

            if (rootToken == null)
            {
                error = "missing 'gearbox -> ...' (what the gearbox drives)";
                return false;
            }

            var result = new DrivetrainLayout { Nodes = nodes.ToArray(), Source = text };
            if (!ResolveToken(rootToken, nodes, out result.Root, out error))
            {
                return false;
            }
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!ResolveToken(nodeTargets[i][0], nodes, out nodes[i].A, out error)
                    || !ResolveToken(nodeTargets[i][1], nodes, out nodes[i].B, out error))
                {
                    error = "node '" + nodes[i].Name + "': " + error;
                    return false;
                }
            }

            error = CheckTree(result);
            if (error != null)
            {
                return false;
            }
            layout = result;
            return true;
        }

        private static bool IsGearboxWord(string s)
        {
            return string.Equals(s, "gearbox", StringComparison.OrdinalIgnoreCase)
                || string.Equals(s, "transmission", StringComparison.OrdinalIgnoreCase);
        }

        private static string CheckNodeName(string name)
        {
            if (name.Length == 0)
            {
                return "a node has no name (before ':')";
            }
            if (name.Length > MaxNameLength)
            {
                return "node name '" + name + "' is longer than " + MaxNameLength + " characters";
            }
            if (!char.IsLetter(name[0]))
            {
                return "node name '" + name + "' must start with a letter";
            }
            for (int i = 1; i < name.Length; i++)
            {
                char c = name[i];
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-')
                {
                    return "node name '" + name + "' may only use letters, digits, '_' and '-'";
                }
            }
            if (IsGearboxWord(name))
            {
                return "'" + name + "' is reserved for the gearbox";
            }
            Target t;
            if (TryParseWheel(name, out t))
            {
                return "'" + name + "' is a wheel name and cannot name a node";
            }
            return null;
        }

        private static string ParseSpec(string spec, Node node)
        {
            string[] parts = spec.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return "missing type (Open, Locked or LSD)";
            }
            if (!TryParseType(parts[0], out node.Type))
            {
                return "unknown type '" + parts[0] + "' (Open, Locked or LSD)";
            }
            for (int i = 1; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq <= 0 || eq == parts[i].Length - 1)
                {
                    return "'" + parts[i] + "' is not key=value";
                }
                string key = parts[i].Substring(0, eq).ToLowerInvariant();
                float v;
                if (!float.TryParse(parts[i].Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out v)
                    || float.IsNaN(v) || float.IsInfinity(v))
                {
                    return "'" + parts[i] + "': not a number (use '.' for decimals)";
                }
                float max = 1f;
                switch (key)
                {
                    case "split": node.Split = v; break;
                    case "stiffness": node.Stiffness = v; break;
                    case "slip": node.SlipTorque = v; max = SlipTorqueMax; break;
                    case "power": node.PowerRamp = v; break;
                    case "coast": node.CoastRamp = v; break;
                    default:
                        return "unknown key '" + key + "' (split, stiffness, slip, power, coast)";
                }
                if (v < 0f || v > max)
                {
                    return "'" + parts[i] + "' is outside 0.." + max.ToString(CultureInfo.InvariantCulture);
                }
            }
            return null;
        }

        private static bool TryParseType(string s, out DiffMode type)
        {
            type = DiffMode.Open;
            if (string.Equals(s, "Open", StringComparison.OrdinalIgnoreCase)) { type = DiffMode.Open; return true; }
            if (string.Equals(s, "Locked", StringComparison.OrdinalIgnoreCase)) { type = DiffMode.Locked; return true; }
            if (string.Equals(s, "LSD", StringComparison.OrdinalIgnoreCase)
                || string.Equals(s, "LimitedSlip", StringComparison.OrdinalIgnoreCase)) { type = DiffMode.LimitedSlip; return true; }
            return false;
        }

        /// <summary>FL FR RL RR, A&lt;n&gt;L, A&lt;n&gt;R, A&lt;n&gt; (case-insensitive).</summary>
        public static bool TryParseWheel(string token, out Target t)
        {
            t = new Target { Kind = TargetKind.Wheel, Text = token };
            string s = token.ToUpperInvariant();
            switch (s)
            {
                case "FL": t.Axle = 1; t.Side = 'L'; return true;
                case "FR": t.Axle = 1; t.Side = 'R'; return true;
                case "RL": t.Axle = 0; t.Side = 'L'; return true;
                case "RR": t.Axle = 0; t.Side = 'R'; return true;
            }
            if (s.Length < 2 || s[0] != 'A' || !char.IsDigit(s[1]))
            {
                return false;
            }
            int i = 1;
            int axle = 0;
            while (i < s.Length && char.IsDigit(s[i]))
            {
                axle = axle * 10 + (s[i] - '0');
                if (axle > 99)
                {
                    return false;
                }
                i++;
            }
            if (axle < 1)
            {
                return false;
            }
            char side = 'C';
            if (i < s.Length)
            {
                if (i != s.Length - 1 || (s[i] != 'L' && s[i] != 'R'))
                {
                    return false;
                }
                side = s[i];
            }
            t.Axle = axle;
            t.Side = side;
            return true;
        }

        private static bool ResolveToken(string token, List<Node> nodes, out Target t, out string error)
        {
            error = null;
            if (TryParseWheel(token, out t))
            {
                return true;
            }
            for (int i = 0; i < nodes.Count; i++)
            {
                if (string.Equals(nodes[i].Name, token, StringComparison.OrdinalIgnoreCase))
                {
                    t = new Target { Kind = TargetKind.Node, Node = i, Text = token };
                    return true;
                }
            }
            if (IsGearboxWord(token))
            {
                error = "the gearbox cannot be an output";
                return false;
            }
            error = "'" + token + "' is neither a defined node nor a wheel (FL FR RL RR A1L A2R ...)";
            return false;
        }

        /// <summary>Tree check: every node fed exactly once and reachable from the gearbox.</summary>
        private static string CheckTree(DrivetrainLayout l)
        {
            int n = l.Nodes.Length;
            var fedBy = new string[n];
            if (l.Root.Kind == TargetKind.Node)
            {
                fedBy[l.Root.Node] = "the gearbox";
            }
            for (int i = 0; i < n; i++)
            {
                Node node = l.Nodes[i];
                for (int k = 0; k < 2; k++)
                {
                    Target t = k == 0 ? node.A : node.B;
                    if (t.Kind != TargetKind.Node)
                    {
                        continue;
                    }
                    if (t.Node == i)
                    {
                        return "node '" + node.Name + "' feeds itself";
                    }
                    if (fedBy[t.Node] != null)
                    {
                        return "node '" + l.Nodes[t.Node].Name + "' is fed twice (by " + fedBy[t.Node] + " and by '" + node.Name + "')";
                    }
                    fedBy[t.Node] = "'" + node.Name + "'";
                }
            }

            // Reachability from the gearbox. With every node fed at most once, a node that is
            // reachable cannot be on a cycle, so this also rules out cycles.
            var reached = new bool[n];
            var stack = new Stack<int>();
            if (l.Root.Kind == TargetKind.Node)
            {
                stack.Push(l.Root.Node);
            }
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                if (reached[i])
                {
                    continue;
                }
                reached[i] = true;
                if (l.Nodes[i].A.Kind == TargetKind.Node) stack.Push(l.Nodes[i].A.Node);
                if (l.Nodes[i].B.Kind == TargetKind.Node) stack.Push(l.Nodes[i].B.Node);
            }
            for (int i = 0; i < n; i++)
            {
                if (!reached[i])
                {
                    return "node '" + l.Nodes[i].Name + "' is not connected to the gearbox";
                }
            }

            // Wheels written the same way twice (e.g. FL twice). Aliases that only collide on a
            // particular vehicle (RL vs A2L on a two-axle car) are caught when resolving per vehicle.
            var seen = new HashSet<string>();
            if (l.Root.Kind == TargetKind.Wheel)
            {
                seen.Add(WheelKey(l.Root));
            }
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < 2; k++)
                {
                    Target t = k == 0 ? l.Nodes[i].A : l.Nodes[i].B;
                    if (t.Kind == TargetKind.Wheel && !seen.Add(WheelKey(t)))
                    {
                        return "wheel " + t.Text.ToUpperInvariant() + " is driven twice";
                    }
                }
            }
            return null;
        }

        private static string WheelKey(Target t)
        {
            return t.Axle.ToString(CultureInfo.InvariantCulture) + t.Side;
        }

        /// <summary>Canonical text for one node's spec, used by the stock-layout log line.</summary>
        public static string FormatNode(string name, DiffMode type, float split, float stiffness, float slip, float power, float coast,
            string outA, string outB)
        {
            var sb = new StringBuilder();
            sb.Append(name).Append(": ").Append(type == DiffMode.LimitedSlip ? "LSD" : type.ToString());
            if (Math.Abs(split - 0.5f) > 1e-4f) sb.Append(" split=").Append(split.ToString("0.###", CultureInfo.InvariantCulture));
            if (type != DiffMode.Open)
            {
                sb.Append(" stiffness=").Append(stiffness.ToString("0.###", CultureInfo.InvariantCulture));
            }
            if (type == DiffMode.LimitedSlip)
            {
                sb.Append(" slip=").Append(slip.ToString("0.#", CultureInfo.InvariantCulture))
                  .Append(" power=").Append(power.ToString("0.###", CultureInfo.InvariantCulture))
                  .Append(" coast=").Append(coast.ToString("0.###", CultureInfo.InvariantCulture));
            }
            sb.Append(" -> ").Append(outA).Append(", ").Append(outB);
            return sb.ToString();
        }
    }
}
