using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

if (args.Length != 1)
{
    Console.Error.WriteLine("Uso: LumaCast.CoverageGate <coverage.config.json>");
    return 2;
}

var configurationPath = Path.GetFullPath(args[0]);
if (!File.Exists(configurationPath))
{
    Console.Error.WriteLine($"Configuração de cobertura não encontrada: {configurationPath}");
    return 2;
}

var configuration = JsonSerializer.Deserialize<CoverageGateConfiguration>(
    await File.ReadAllTextAsync(configurationPath),
    new JsonSerializerOptions(JsonSerializerDefaults.Web));

if (configuration is null ||
    string.IsNullOrWhiteSpace(configuration.ReportPath) ||
    !IsValidPercentage(configuration.MinimumLineCoverage) ||
    !IsValidPercentage(configuration.MinimumBranchCoverage))
{
    Console.Error.WriteLine("A configuração de cobertura é inválida.");
    return 2;
}

var configurationDirectory = Path.GetDirectoryName(configurationPath) ?? Directory.GetCurrentDirectory();
var reportPath = Path.GetFullPath(configuration.ReportPath, configurationDirectory);
if (!File.Exists(reportPath))
{
    Console.Error.WriteLine($"Relatório de cobertura não encontrado: {reportPath}");
    return 2;
}

await using var reportStream = File.OpenRead(reportPath);
var report = await XDocument.LoadAsync(
    reportStream,
    LoadOptions.None,
    CancellationToken.None);
var coverage = report.Root;
if (coverage is null ||
    !TryReadPercentage(coverage, "line-rate", out var lineCoverage) ||
    !TryReadPercentage(coverage, "branch-rate", out var branchCoverage))
{
    Console.Error.WriteLine("O relatório Cobertura não possui métricas válidas de linhas e branches.");
    return 2;
}

var linePassed = lineCoverage >= configuration.MinimumLineCoverage;
var branchPassed = branchCoverage >= configuration.MinimumBranchCoverage;
var result = $"Cobertura: linhas {lineCoverage:F2}% (mínimo {configuration.MinimumLineCoverage:F2}%), " +
             $"branches {branchCoverage:F2}% (mínimo {configuration.MinimumBranchCoverage:F2}%).";

Console.WriteLine(result);
await WriteGitHubSummaryAsync(result, linePassed && branchPassed);

if (linePassed && branchPassed) return 0;

Console.Error.WriteLine("A cobertura ficou abaixo do limite obrigatório.");
return 1;

static bool IsValidPercentage(double value) => value is >= 0 and <= 100;

static bool TryReadPercentage(XElement coverage, string attributeName, out double percentage)
{
    var rawValue = coverage.Attribute(attributeName)?.Value;
    var parsed = double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate);
    percentage = rate * 100;
    return parsed && IsValidPercentage(percentage);
}

static async Task WriteGitHubSummaryAsync(string result, bool passed)
{
    var summaryPath = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
    if (string.IsNullOrWhiteSpace(summaryPath)) return;

    var status = passed ? "✅ Aprovada" : "❌ Reprovada";
    var summary = $"## Cobertura de código — {status}{Environment.NewLine}{Environment.NewLine}{result}{Environment.NewLine}";
    await File.AppendAllTextAsync(summaryPath, summary, Encoding.UTF8);
}

internal sealed record CoverageGateConfiguration(
    string ReportPath,
    double MinimumLineCoverage,
    double MinimumBranchCoverage);
