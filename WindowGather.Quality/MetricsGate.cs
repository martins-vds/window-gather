using System.Globalization;
using System.Xml.Linq;

namespace WindowGather.Quality;

public sealed record MethodMetric(string Assembly, string Class, string Method,
    double Complexity, double LineCoverage, double Crap);

public static class MetricsGate
{
    private static readonly string[] RequiredAssemblies = ["WindowGather.Domain", "WindowGather.Application"];

    public static double CalculateCrap(double complexity, double coverageFraction) =>
        complexity * complexity * Math.Pow(1 - coverageFraction, 3) + complexity;

    public static IReadOnlyList<MethodMetric> ReadReports(IEnumerable<XDocument> documents)
    {
        var metrics = new List<MethodMetric>();
        foreach (XDocument document in documents)
        {
            XElement root = document.Root ?? throw new InvalidDataException("Missing report root.");
            if (root.Name != "CoverageReport") throw new InvalidDataException("Not a ReportGenerator XML report.");
            if ((string?)root.Attribute("scope") == "Summary") continue;
            string? assembly = (string?)root.Element("Summary")?.Element("Assembly");
            if (string.IsNullOrWhiteSpace(assembly)) throw new InvalidDataException("Missing report assembly.");
            if (!RequiredAssemblies.Contains(assembly)) continue;
            string className = RequiredText(root.Element("Summary")?.Element("Class"), "class");
            XElement elements = root.Element("Metrics") ?? throw new InvalidDataException($"Missing method metrics for {className}.");
            var methods = elements.Elements("Element").ToArray();
            if (methods.Length == 0) throw new InvalidDataException($"Empty method metrics for {className}.");
            foreach (XElement method in methods)
            {
                string name = (string?)method.Attribute("name") ??
                    throw new InvalidDataException("Missing method name.");
                if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("Empty method name.");
                double cc = Number(method.Element("Cyclomaticcomplexity"), "complexity", 1, double.MaxValue);
                double coverage = Number(method.Element("Linecoverage"), "line coverage", 0, 100);
                double crap = Number(method.Element("CrapScore"), "CRAP", 0, double.MaxValue);
                double computed = Math.Round(CalculateCrap(cc, coverage / 100), 0);
                if (crap != computed)
                    throw new InvalidDataException($"Inconsistent method CRAP for {className}.{name}: {crap} vs {computed}.");
                metrics.Add(new(assembly, className, name, cc, coverage, crap));
            }
        }
        foreach (string assembly in RequiredAssemblies)
            if (!metrics.Any(m => m.Assembly == assembly))
                throw new InvalidDataException($"No method metrics for required assembly {assembly}.");
        return metrics;
    }

    public static void CheckCoverage(XDocument document, double minLine, double minBranch)
    {
        XElement root = document.Root ?? throw new InvalidDataException("Missing Cobertura root.");
        if (root.Name != "coverage") throw new InvalidDataException("Not a Cobertura report.");
        foreach (string assembly in RequiredAssemblies)
        {
            XElement package = root.Element("packages")?.Elements("package")
                .SingleOrDefault(p => (string?)p.Attribute("name") == assembly) ??
                throw new InvalidDataException($"Missing coverage for {assembly}.");
            double line = AttributeNumber(package, "line-rate") * 100;
            double branch = AttributeNumber(package, "branch-rate") * 100;
            if (!package.Descendants("method").Any())
                throw new InvalidDataException($"Empty method coverage for {assembly}.");
            if (line < minLine || branch < minBranch)
                throw new InvalidDataException($"{assembly}: line {line:F2}% (minimum {minLine}%), branch {branch:F2}% (minimum {minBranch}%).");
        }
    }

    private static double AttributeNumber(XElement element, string name) =>
        Number(new XElement(name, (string?)element.Attribute(name) ?? ""), name, 0, 1);

    private static string RequiredText(XElement? element, string label) =>
        !string.IsNullOrWhiteSpace(element?.Value) ? element.Value : throw new InvalidDataException($"Missing {label}.");

    private static double Number(XElement? element, string label, double min, double max)
    {
        if (element is null || !double.TryParse(element.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
            !double.IsFinite(value) || value < min || value > max)
            throw new InvalidDataException($"Invalid or missing method {label}.");
        return value;
    }
}
