// Every rejection the model can produce has a code. verify/ asserts that each Error code is
// triggered by at least one negative control, so a new rejection path cannot ship untested.
using System.Collections.Generic;
using System.Text;

namespace ApocalypterDrivetrain.Model
{
    public enum ErrorCode
    {
        // Text / structure
        JsonSyntax,
        DuplicateKey,
        MissingField,
        UnknownField,
        WrongType,
        NotFinite,
        OutOfRange,
        UnsupportedSchema,
        BadName,
        ParamNotApplicable,       // a valid key that does not apply to this variant
        // Engine / curves
        IdleNotBelowRedline,
        RedlineAboveRevLimit,
        CurveSourceAmbiguous,     // both or neither of points / expression
        CurveTooFewPoints,
        CurveNotIncreasing,
        CurveDoesNotCoverRange,
        CurveNegativeTorque,
        ExpressionSyntax,
        ExpressionNotFinite,
        // Clutch
        EngagementBelowIdle,
        EngagementCurveEndpoints,
        EngagementCurveDecreasing,
        LaunchRpmOutOfRange,
        // Gearbox
        NoForwardGears,
        ForwardRatioNotPositive,
        ReverseRatioNotNegative,
        ShiftLogicUnknown,
        ShiftRpmOrder,
        // Differentials
        DiffOutputCount,
        DiffOutputDuplicate,
        // Axles / wiring
        NoAxles,
        DuplicateAxlePosition,
        DuplicateName,
        UnknownNode,
        InputFedTwice,
        WiringCycle,
    }

    public enum WarningCode
    {
        DisconnectedNode,
        ForwardRatiosNotDescending,
        ShiftWouldHunt,
    }

    public struct Diagnostic
    {
        public bool IsError;
        public ErrorCode Error;
        public WarningCode Warning;
        public string Path;      // e.g. engine.torqueCurve.points[2][0]
        public int Line, Column; // 0 = no source position (whole-document issue)
        public string Message;

        public string CodeName { get { return IsError ? Error.ToString() : Warning.ToString(); } }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(IsError ? "error " : "warning ").Append(CodeName).Append(' ');
            if (Line > 0) sb.Append("(line ").Append(Line).Append(", col ").Append(Column).Append(") ");
            if (!string.IsNullOrEmpty(Path)) sb.Append(Path).Append(": ");
            sb.Append(Message);
            return sb.ToString();
        }
    }

    public sealed class Diagnostics
    {
        public readonly List<Diagnostic> Items = new List<Diagnostic>();

        public int ErrorCount
        {
            get { int n = 0; foreach (Diagnostic d in Items) if (d.IsError) n++; return n; }
        }

        public int WarningCount { get { return Items.Count - ErrorCount; } }

        public void Error(ErrorCode code, string path, JsonValue at, string message)
        {
            Items.Add(new Diagnostic
            {
                IsError = true, Error = code, Path = path, Message = message,
                Line = at != null ? at.Line : 0, Column = at != null ? at.Column : 0,
            });
        }

        public void Warn(WarningCode code, string path, JsonValue at, string message)
        {
            Items.Add(new Diagnostic
            {
                IsError = false, Warning = code, Path = path, Message = message,
                Line = at != null ? at.Line : 0, Column = at != null ? at.Column : 0,
            });
        }

        public bool Has(ErrorCode code)
        {
            foreach (Diagnostic d in Items) if (d.IsError && d.Error == code) return true;
            return false;
        }

        public bool Has(WarningCode code)
        {
            foreach (Diagnostic d in Items) if (!d.IsError && d.Warning == code) return true;
            return false;
        }
    }
}
