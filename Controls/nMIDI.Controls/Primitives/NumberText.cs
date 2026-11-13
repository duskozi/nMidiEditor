using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace nMIDI.Controls.Primitives;

/// <summary>Number parsing for typed values (no WPF dependency, so it is unit-tested on any OS).</summary>
internal static partial class NumberText
{
    // A sign, then digits with separators, or a separator followed by digits (".5").
    [GeneratedRegex(@"[-+]?(\d[\d.,]*|[.,]\d[\d.,]*)")]
    private static partial Regex NumberRegex();

    /// <summary>
    /// Parses user text, tolerating units ("120 bpm") and either decimal separator: "1.5", "1,5", ".5", "-,5".
    /// When both separators appear ("1,234.5" or "1.234,5") the last one is the decimal point and the other
    /// groups thousands. Text with the same separator twice ("1.2.3") is rejected.
    /// </summary>
    public static bool TryParse(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        Match m = NumberRegex().Match(text);
        if (!m.Success) return false;
        string s = m.Value.TrimEnd('.', ',');
        int dots = s.Count(c => c == '.'), commas = s.Count(c => c == ',');
        if (dots > 0 && commas > 0)
        {
            char dec = s.LastIndexOf('.') > s.LastIndexOf(',') ? '.' : ',';
            char group = dec == '.' ? ',' : '.';
            if ((dec == '.' ? dots : commas) > 1) return false;
            s = s.Replace(group.ToString(), "");
        }
        else if (dots > 1 || commas > 1) return false;
        s = s.Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
               && !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
