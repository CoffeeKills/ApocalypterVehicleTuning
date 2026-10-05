// Torque-curve expressions, e.g. "clamp(120 + 0.06*rpm - 0.0000075*rpm^2, 0, 400)".
// Grammar (whitespace-insensitive):
//   expr    := term (('+' | '-') term)*
//   term    := unary (('*' | '/') unary)*
//   unary   := '-' unary | power
//   power   := primary ('^' INTEGER)?          integer exponent 0..8 only
//   primary := NUMBER | 'rpm' | FUNC '(' expr (',' expr)* ')' | '(' expr ')'
//   FUNC    := min(a,b) | max(a,b) | clamp(x,lo,hi) | abs(x) | lerp(a,b,t)
// No transcendental functions on purpose (SPO.Vehicle: no libm in the model). Expressions are
// evaluated only at load, sampled into a PiecewiseLinear; nothing here runs per tick.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ApocalypterDrivetrain.Model
{
    public sealed class ExpressionException : Exception
    {
        public readonly int Position;   // 1-based character position inside the expression
        public ExpressionException(int position, string message) : base(message) { Position = position; }
    }

    public sealed class CurveExpression
    {
        private abstract class Node { public abstract double Eval(double rpm); }
        private sealed class Num : Node { public double V; public override double Eval(double rpm) { return V; } }
        private sealed class Var : Node { public override double Eval(double rpm) { return rpm; } }
        private sealed class Neg : Node { public Node A; public override double Eval(double rpm) { return -A.Eval(rpm); } }
        private sealed class Bin : Node
        {
            public char Op; public Node A, B;
            public override double Eval(double rpm)
            {
                double a = A.Eval(rpm), b = B.Eval(rpm);
                switch (Op)
                {
                    case '+': return a + b;
                    case '-': return a - b;
                    case '*': return a * b;
                    default: return a / b;   // division by zero -> Infinity/NaN, caught by the sampler's finite check
                }
            }
        }
        private sealed class Pow : Node
        {
            public Node A; public int N;
            public override double Eval(double rpm)
            {
                double a = A.Eval(rpm), r = 1.0;
                for (int i = 0; i < N; i++) r *= a;
                return r;
            }
        }
        private sealed class Call : Node
        {
            public string F; public Node[] Args;
            public override double Eval(double rpm)
            {
                switch (F)
                {
                    case "min": return Math.Min(Args[0].Eval(rpm), Args[1].Eval(rpm));
                    case "max": return Math.Max(Args[0].Eval(rpm), Args[1].Eval(rpm));
                    case "abs": return Math.Abs(Args[0].Eval(rpm));
                    case "clamp":
                    {
                        double x = Args[0].Eval(rpm), lo = Args[1].Eval(rpm), hi = Args[2].Eval(rpm);
                        return x < lo ? lo : (x > hi ? hi : x);
                    }
                    default:   // lerp
                    {
                        double a = Args[0].Eval(rpm), b = Args[1].Eval(rpm), t = Args[2].Eval(rpm);
                        return a + (b - a) * t;
                    }
                }
            }
        }

        private static readonly Dictionary<string, int> Arity = new Dictionary<string, int>
        {
            { "min", 2 }, { "max", 2 }, { "clamp", 3 }, { "abs", 1 }, { "lerp", 3 },
        };

        private readonly Node _root;
        public readonly string Source;

        private CurveExpression(Node root, string source) { _root = root; Source = source; }

        public double Evaluate(double rpm) { return _root.Eval(rpm); }

        public static CurveExpression Compile(string source)
        {
            if (source == null || source.Trim().Length == 0) throw new ExpressionException(1, "expression is empty");
            var p = new P(source);
            Node n = p.Expr();
            p.Ws();
            if (!p.End) throw new ExpressionException(p.Pos + 1, "unexpected '" + source[p.Pos] + "'");
            return new CurveExpression(n, source);
        }

        private sealed class P
        {
            private readonly string _s;
            public int Pos;
            public P(string s) { _s = s; }
            public bool End { get { return Pos >= _s.Length; } }
            public void Ws() { while (!End && char.IsWhiteSpace(_s[Pos])) Pos++; }
            private bool Eat(char c) { Ws(); if (!End && _s[Pos] == c) { Pos++; return true; } return false; }
            private ExpressionException Err(string m) { return new ExpressionException(Pos + 1, m); }

            public Node Expr()
            {
                Node a = Term();
                while (true)
                {
                    if (Eat('+')) a = new Bin { Op = '+', A = a, B = Term() };
                    else if (Eat('-')) a = new Bin { Op = '-', A = a, B = Term() };
                    else return a;
                }
            }

            private Node Term()
            {
                Node a = Unary();
                while (true)
                {
                    if (Eat('*')) a = new Bin { Op = '*', A = a, B = Unary() };
                    else if (Eat('/')) a = new Bin { Op = '/', A = a, B = Unary() };
                    else return a;
                }
            }

            private Node Unary()
            {
                if (Eat('-')) return new Neg { A = Unary() };
                Node a = Primary();
                if (Eat('^'))
                {
                    Ws();
                    int start = Pos;
                    while (!End && char.IsDigit(_s[Pos])) Pos++;
                    if (Pos == start) throw Err("'^' needs a whole-number exponent (0..8)");
                    if (!End && (_s[Pos] == '.' || char.IsLetter(_s[Pos]))) throw Err("'^' needs a whole-number exponent (0..8)");
                    int n = int.Parse(_s.Substring(start, Pos - start), CultureInfo.InvariantCulture);
                    if (n > 8) throw new ExpressionException(start + 1, "exponent " + n + " is above the limit of 8");
                    a = new Pow { A = a, N = n };
                }
                return a;
            }

            private Node Primary()
            {
                Ws();
                if (End) throw Err("unexpected end of expression");
                char c = _s[Pos];
                if (c == '(')
                {
                    Pos++;
                    Node e = Expr();
                    if (!Eat(')')) throw Err("missing ')'");
                    return e;
                }
                if (char.IsDigit(c) || c == '.')
                {
                    int start = Pos;
                    while (!End && (char.IsDigit(_s[Pos]) || _s[Pos] == '.')) Pos++;
                    if (!End && (_s[Pos] == 'e' || _s[Pos] == 'E'))
                    {
                        Pos++;
                        if (!End && (_s[Pos] == '+' || _s[Pos] == '-')) Pos++;
                        while (!End && char.IsDigit(_s[Pos])) Pos++;
                    }
                    double v;
                    if (!double.TryParse(_s.Substring(start, Pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                        throw new ExpressionException(start + 1, "malformed number '" + _s.Substring(start, Pos - start) + "'");
                    return new Num { V = v };
                }
                if (char.IsLetter(c))
                {
                    int start = Pos;
                    while (!End && char.IsLetterOrDigit(_s[Pos])) Pos++;
                    string id = _s.Substring(start, Pos - start);
                    if (id == "rpm") return new Var();
                    int arity;
                    if (!Arity.TryGetValue(id, out arity))
                        throw new ExpressionException(start + 1, "unknown name '" + id + "' (allowed: rpm, min, max, clamp, abs, lerp)");
                    if (!Eat('(')) throw Err("'" + id + "' must be followed by '('");
                    var args = new List<Node> { Expr() };
                    while (Eat(',')) args.Add(Expr());
                    if (!Eat(')')) throw Err("missing ')' after the arguments of " + id);
                    if (args.Count != arity)
                        throw new ExpressionException(start + 1, id + " takes " + arity + " argument(s), got " + args.Count);
                    return new Call { F = id, Args = args.ToArray() };
                }
                throw Err("unexpected '" + c + "'");
            }
        }
    }
}
