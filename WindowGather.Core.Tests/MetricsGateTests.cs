using System.Xml.Linq;
using WindowGather.Quality;
using Xunit;

namespace WindowGather.Core.Tests;

public sealed class MetricsGateTests
{
    [Theory]
    [InlineData(10, 0, 110)]
    [InlineData(10, 0.5, 22.5)]
    [InlineData(10, 1, 10)]
    public void CrapFormulaUsesMethodLineCoverage(double cc, double rate, double expected) =>
        Assert.Equal(expected, MetricsGate.CalculateCrap(cc, rate));

    [Fact]
    public void ReadsBothAssembliesAndChecksReportedRounding()
    {
        var metrics = MetricsGate.ReadReports([
            Report("WindowGather.Domain", "10", "50", "22"),
            Report("WindowGather.Application", "20", "100", "20")]);
        Assert.Equal(2, metrics.Count);
        Assert.Equal(22, metrics[0].Crap);
        Assert.Equal("Fixture", metrics[0].Class);
        Assert.Equal("Method", metrics[0].Method);
        MetricsGate.CheckCoverage(Coverage(), 95, 80);
    }

    [Theory]
    [InlineData("NaN", "100", "1")]
    [InlineData("0", "100", "1")]
    [InlineData("1", "-1", "1")]
    [InlineData("1", "101", "1")]
    [InlineData("1", "100", "bogus")]
    [InlineData("1", "100", "99")]
    public void RejectsMalformedOrInconsistentMetrics(string cc, string coverage, string crap) =>
        Assert.Throws<InvalidDataException>(() => MetricsGate.ReadReports([
            Report("WindowGather.Domain", cc, coverage, crap),
            Report("WindowGather.Application", "1", "100", "1")]));

    [Fact]
    public void RejectsEmptyMissingOrWrongShapedMetrics()
    {
        Assert.Throws<InvalidDataException>(() => MetricsGate.ReadReports([]));
        Assert.Throws<InvalidDataException>(() => MetricsGate.ReadReports([new XDocument()]));
        Assert.Throws<InvalidDataException>(() => MetricsGate.ReadReports([XDocument.Parse("<wrong/>")]));
        var report = Report("WindowGather.Domain", "1", "100", "1");
        report.Root!.Element("Metrics")!.RemoveNodes();
        Assert.Throws<InvalidDataException>(() => MetricsGate.ReadReports([report]));
        report.Root.Element("Metrics")!.Remove();
        Assert.Throws<InvalidDataException>(() => MetricsGate.ReadReports([report]));
    }

    [Fact]
    public void CoverageAndBranchAreDistinctAndMissingRatesFail()
    {
        var document = Coverage();
        var package = document.Descendants("package").First();
        package.SetAttributeValue("branch-rate", "0.79");
        Assert.Throws<InvalidDataException>(() => MetricsGate.CheckCoverage(document, 95, 80));
        package.SetAttributeValue("branch-rate", "1");
        package.SetAttributeValue("line-rate", "0.94");
        Assert.Throws<InvalidDataException>(() => MetricsGate.CheckCoverage(document, 95, 80));
        package.SetAttributeValue("line-rate", "");
        Assert.Throws<InvalidDataException>(() => MetricsGate.CheckCoverage(document, 95, 80));
        Assert.Throws<InvalidDataException>(() => MetricsGate.CheckCoverage(XDocument.Parse("<coverage/>"), 0, 0));
    }

    private static XDocument Report(string assembly, string cc, string coverage, string crap) => new(
        new XElement("CoverageReport",
            new XElement("Summary", new XElement("Assembly", assembly), new XElement("Class", "Fixture")),
            new XElement("Metrics", new XElement("Element", new XAttribute("name", "Method"),
                new XElement("Cyclomaticcomplexity", cc), new XElement("Linecoverage", coverage), new XElement("CrapScore", crap)))));

    private static XDocument Coverage()
    {
        var packages = new XElement("packages");
        foreach (string assembly in new[] { "WindowGather.Domain", "WindowGather.Application" })
            packages.Add(new XElement("package", new XAttribute("name", assembly),
                new XAttribute("line-rate", "0.99"), new XAttribute("branch-rate", "0.9"),
                new XElement("classes", new XElement("class", new XElement("methods", new XElement("method"))))));
        return new(new XElement("coverage", packages));
    }
}
