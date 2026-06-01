using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.ReportGenerator;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using static Nuke.Common.Tools.ReportGenerator.ReportGeneratorTasks;

namespace Build.Components;

/// <summary>
///     Code coverage component with ReportGenerator integration.
/// </summary>
internal interface ICoverage : ITest
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	Target Coverage => d => d
		.Description("Run tests with code coverage")
		.DependsOn<ICompile>(x => x.Compile)
		.Produces(CoverageDirectory / "**")
		.Executes(() =>
		{
			CoverageDirectory.CreateOrCleanDirectory();
			TestResultsDirectory.CreateOrCleanDirectory();

			foreach (var project in TestProjects)
			{
				var coverageFile = CoverageDirectory / project.Name / "coverage.cobertura.xml";
				coverageFile.Parent.CreateDirectory();
				RunTestWithCoverage(project, coverageFile);
			}

			GenerateCoverageReports();
			GenerateAiCoverageSummary();
		});

	private void RunTestWithCoverage(Project project, AbsolutePath coverageOutput)
	{
		Log.Information("Running tests with coverage: {Project}", project.Name);

		var mtp = MtpExtensions.Mtp()
			.ReportTrx($"{project.Name}.trx")
			.IgnoreExitCode(8)
			.CoverageCobertura(coverageOutput);

		if (StopOnFail == true)
			mtp.StopOnFail();

		if (LiveOutput == true || IsLocalBuild)
			mtp.ShowLiveOutput();

		ExecuteMtpTestInternal(project, mtp);
	}

	private void GenerateCoverageReports()
	{
		IReadOnlyCollection<AbsolutePath>? coverageFiles = CoverageDirectory.GlobFiles("**/coverage.cobertura.xml");

		if (coverageFiles.Count is 0)
		{
			Log.Warning("No coverage files found in {Directory}", CoverageDirectory);
			return;
		}

		Log.Information("Generating coverage reports from {Count} file(s)", coverageFiles.Count);

		var settings = new ReportGeneratorSettings()
			.SetReports(coverageFiles.Select(f => f.ToString()))
			.SetTargetDirectory(CoverageDirectory)
			.SetReportTypes(
				ReportTypes.Html,
				ReportTypes.Cobertura,
				ReportTypes.TextSummary,
				ReportTypes.Badges)
			.SetAssemblyFilters("-Microsoft.*", "-System.*", "-xunit.*", "-*.Tests")
			.SetClassFilters("-*.Migrations.*", "-*.Generated.*", "-*+<*>d__*");

		if (IsServerBuild && GitVersion is not null)
		{
			settings = settings.SetTag(GitVersion.FullSemVer);
		}

		ReportGenerator(settings);

		Log.Information("Coverage reports generated: {Directory}", CoverageDirectory);
	}

	private void GenerateAiCoverageSummary()
	{
		var summaryFile = CoverageDirectory / "Summary.txt";
		var jsonOutput = CoverageDirectory / "coverage.summary.json";

		if (!summaryFile.FileExists())
		{
			Log.Warning("Summary.txt not found, skipping AI summary generation");
			return;
		}

		try
		{
			var lines = File.ReadAllLines(summaryFile);
			var summary = ParseCoverageSummary(lines);
			File.WriteAllText(jsonOutput, JsonSerializer.Serialize(summary, JsonOptions));
			Log.Information("AI coverage summary: {Path}", jsonOutput);
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "Failed to generate AI coverage summary");
		}
	}

	private static Dictionary<string, object> ParseCoverageSummary(string[] lines)
	{
		Dictionary<string, object> metrics = new();

		foreach (var line in lines)
		{
			if (line.Contains("Line coverage:"))
				metrics["lineCoverage"] = ExtractPercentage(line);
			else if (line.Contains("Branch coverage:"))
				metrics["branchCoverage"] = ExtractPercentage(line);
			else if (line.Contains("Method coverage:"))
				metrics["methodCoverage"] = ExtractPercentage(line);
			else if (line.Contains("Coverable lines:"))
				metrics["coverableLines"] = ExtractNumber(line);
			else if (line.Contains("Covered lines:"))
				metrics["coveredLines"] = ExtractNumber(line);
		}

		return new Dictionary<string, object>
		{
			["generatedAt"] = DateTime.UtcNow.ToString("O"),
			["metrics"] = metrics
		};
	}

	private static double ExtractPercentage(ReadOnlySpan<char> line)
	{
		var colonIdx = line.IndexOf(':');
		var percentIdx = line.IndexOf('%');
		if (colonIdx < 0 || percentIdx <= colonIdx) return 0;
		return double.TryParse(line[(colonIdx + 1)..percentIdx].Trim(), out var result) ? result : 0;
	}

	private static int ExtractNumber(ReadOnlySpan<char> line)
	{
		var colonIdx = line.IndexOf(':');
		if (colonIdx < 0) return 0;
		return int.TryParse(line[(colonIdx + 1)..].Trim(), out var result) ? result : 0;
	}
}
