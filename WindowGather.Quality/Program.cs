using System.Xml.Linq;
using WindowGather.Quality;

try
{
    if (args.Length != 2)
        throw new ArgumentException("Usage: WindowGather.Quality <ReportGenerator directory> <Cobertura file>");
    var reports = Directory.GetFiles(args[0], "*.xml").Select(XDocument.Load);
    IReadOnlyList<MethodMetric> metrics = MetricsGate.ReadReports(reports);
    MetricsGate.CheckCoverage(XDocument.Load(args[1]), minLine: 95, minBranch: 80);
    foreach (MethodMetric metric in metrics.OrderByDescending(m => m.Crap))
        Console.WriteLine($"{metric.Assembly}: {metric.Class}.{metric.Method} CC={metric.Complexity} line={metric.LineCoverage}% CRAP={metric.Crap}");
    var failures = metrics.Where(m => m.Crap > 30).ToArray();
    if (failures.Length > 0) throw new InvalidDataException($"{failures.Length} methods exceed CRAP 30.");
    Console.WriteLine($"PASS: {metrics.Count} method metrics; maximum CRAP {metrics.Max(m => m.Crap)}. Line >=95%, branch >=80% per core assembly.");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"Quality gate failed: {error.Message}");
    return 1;
}
