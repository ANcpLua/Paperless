using Nuke.Common.IO;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace Build;

/// <summary>
///     Rule for excluding or tagging coverage entries.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Build infrastructure - tested via integration")]
public sealed record ExclusionRule(
	string Name,
	string[]? PathContains = null,
	string[]? FileSuffixes = null,
	bool ShouldExclude = true)
{
	public bool Matches(string normalizedPath) =>
		(PathContains is { Length: > 0 } && Array.Exists(PathContains,
			p => normalizedPath.Contains(p, StringComparison.OrdinalIgnoreCase))) ||
		(FileSuffixes is { Length: > 0 } && Array.Exists(FileSuffixes,
			s => normalizedPath.EndsWith(s, StringComparison.OrdinalIgnoreCase)));

	public string CreateReasonTag() =>
		ShouldExclude ? $"ExcludedByRule({Name})" : $"TaggedByRule({Name})";
}

/// <summary>
///     Well-known exclusion patterns for common code categories.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Build infrastructure - tested via integration")]
public static class WellKnownExclusionPatterns
{
	public static readonly ExclusionRule SourceGeneratedFiles = new(
		"SourceGenerated",
		["/obj/"],
		[".g.cs", ".generated.cs", ".designer.cs"]);

	public static readonly ExclusionRule Migrations = new(
		"Migrations",
		["/Migrations/"]);

	public static readonly ExclusionRule InfrastructureCode = new(
		"Infrastructure",
		["/Host/", "/Configuration/", "/Extensions/"]);

	public static readonly ExclusionRule PresentationWrappers = new(
		"PresentationWrapper",
		["/Presentation/"],
		["Endpoints.cs", "Listener.cs"],
		false);

	public static readonly ExclusionRule DataTransferObjects = new(
		"DTO",
		FileSuffixes: ["Dto.cs", "DTOs.cs", "Request.cs", "Response.cs"],
		ShouldExclude: false);

	public static readonly ExclusionRule MappingConfiguration = new(
		"MappingConfig",
		FileSuffixes: ["MappingConfig.cs", "Profile.cs"])
	;

	public static readonly IReadOnlyList<ExclusionRule> AllRules =
	[
		SourceGeneratedFiles,
		Migrations,
		InfrastructureCode,
		MappingConfiguration,
		PresentationWrappers,
		DataTransferObjects
	];

	public static ExclusionRule? GetMatchingRule(string normalizedPath) =>
		AllRules.FirstOrDefault(rule => rule.Matches(normalizedPath));

	public static bool ShouldExcludePath(string normalizedPath) =>
		GetMatchingRule(normalizedPath) is { ShouldExclude: true };
}

/// <summary>
///     Patterns for detecting compiler-generated async/iterator state machines.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Build infrastructure - tested via integration")]
public static class StateMachinePatterns
{
	private const string StateMachineMarker = "+<";
	private const string StateMachineSuffix = ">d__";

	public static bool TryExtractStateMachineMethod(string className, out string? methodName)
	{
		methodName = null;

		int plusIndex = className.IndexOf(StateMachineMarker, StringComparison.Ordinal);
		if (plusIndex < 0)
		{
			return false;
		}

		int methodStart = plusIndex + 2;
		int methodEnd = className.IndexOf(StateMachineSuffix, methodStart, StringComparison.Ordinal);
		if (methodEnd <= methodStart)
		{
			return false;
		}

		methodName = className[methodStart..methodEnd];
		return methodName.Length > 0;
	}

	public static bool IsStateMachineClass(string className) =>
		className.Contains(StateMachineMarker, StringComparison.Ordinal) &&
		className.Contains(">d__", StringComparison.Ordinal);

	public static bool IsMoveNextMethod(string methodName) =>
		methodName.Equals("MoveNext", StringComparison.Ordinal);

	public static string CreateStateMachineReason(string? originalMethodName) =>
		originalMethodName is { Length: > 0 }
			? $"CompilerGeneratedStateMachine({originalMethodName})"
			: "CompilerGeneratedStateMachine";
}


/// <summary>
///     Converts Cobertura XML coverage reports to AI-friendly per-project summaries.
///     Produces small XML/JSON files containing only uncovered/partially covered entries.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Build infrastructure - tested via integration")]
public static class CoverageSummaryConverter
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	// ── Per-Project Conversion ───────────────────────────────────────────────

	public static void ConvertPerProject(
		AbsolutePath coberturaPath,
		AbsolutePath sourceRoot,
		IReadOnlyDictionary<string, AbsolutePath> projectOutputs)
	{
		if (!coberturaPath.FileExists())
		{
			Log.Warning("Cobertura file not found: {Path}", coberturaPath);
			return;
		}

		XDocument cobertura = XDocument.Load(coberturaPath);
		if (cobertura.Root is not { } coverageElement)
		{
			Log.Warning("Invalid Cobertura file: no root element");
			return;
		}

		DateTime generatedAtUtc = DateTime.UtcNow;
		string relativeCoberturaPath = Path.GetFileName(coberturaPath);
		string sourceRootNormalized = NormalizePath(sourceRoot, null);

		Dictionary<string, CoverageFile> allFileIssues = ExtractAllFileIssues(coverageElement, sourceRootNormalized);

		foreach ((string projectName, AbsolutePath outputPath) in projectOutputs)
		{
			Dictionary<string, CoverageFile> projectFiles = allFileIssues
				.Where(kvp => kvp.Key.Contains(projectName, StringComparison.OrdinalIgnoreCase))
				.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

			WriteProjectSummary(projectName, projectFiles, outputPath, generatedAtUtc, relativeCoberturaPath);
		}
	}

	// ── Legacy API ───────────────────────────────────────────────────────────

	public static void Convert(string coberturaPath, string outputPath, string projectName, string? sourceRoot = null)
	{
		if (!File.Exists(coberturaPath))
		{
			Log.Warning("Cobertura file not found: {Path}", coberturaPath);
			return;
		}

		XDocument cobertura = XDocument.Load(coberturaPath);
		if (cobertura.Root is not { } coverageElement)
		{
			Log.Warning("Invalid Cobertura file: no root element");
			return;
		}

		DateTime generatedAtUtc = DateTime.UtcNow;
		string relativeCoberturaPath = Path.GetFileName(coberturaPath);

		CoverageSummary jsonSummary = new()
		{
			Project = projectName, Source = relativeCoberturaPath, GeneratedAtUtc = generatedAtUtc
		};

		Dictionary<string, CoverageFile>
			allFileIssues = ExtractAllFileIssues(coverageElement, sourceRoot ?? string.Empty);

		XElement xmlSummary = new("coverage-summary",
			new XAttribute("project", projectName),
			new XAttribute("generatedAtUtc", generatedAtUtc.ToString("O", CultureInfo.InvariantCulture)),
			new XAttribute("source", relativeCoberturaPath));

		foreach ((string filePath, CoverageFile file) in allFileIssues.OrderBy(kvp => kvp.Key,
			         StringComparer.OrdinalIgnoreCase))
		{
			if (file.LineDict.Count is 0 && file.BranchDict.Count is 0)
			{
				continue;
			}

			// JSON output (automatically uses the simplified lists via properties)
			jsonSummary.Files.Add(file);

			// XML output
			XElement xmlFile = new("file", new XAttribute("path", filePath));

			foreach (CoverageBranch branch in file.Branches)
			{
				xmlFile.Add(new XElement("branch",
					new XAttribute("line", branch.Line),
					new XAttribute("hits", branch.Hits),
					new XAttribute("coveredBranches", branch.CoveredBranches),
					new XAttribute("totalBranches", branch.TotalBranches),
					new XAttribute("coveragePercent",
						branch.CoveragePercent.ToString("F1", CultureInfo.InvariantCulture)),
					new XAttribute("reason", branch.Reason)));
			}

			foreach (CoverageLine line in file.Lines)
			{
				xmlFile.Add(new XElement("line",
					new XAttribute("number", line.Line),
					new XAttribute("hits", line.Hits),
					new XAttribute("reason", line.Reason)));
			}

			xmlSummary.Add(xmlFile);
		}

		EnsureDirectoryExists(outputPath);

		new XDocument(new XDeclaration("1.0", "utf-8", null), xmlSummary).Save(outputPath);

		string jsonPath = Path.Combine(
			Path.GetDirectoryName(outputPath) ?? string.Empty,
			Path.GetFileNameWithoutExtension(outputPath) + ".json");
		File.WriteAllText(jsonPath, JsonSerializer.Serialize(jsonSummary, JsonOptions));

		int filesWithIssues = allFileIssues.Count(kvp => kvp.Value.LineDict.Count > 0 || kvp.Value.BranchDict.Count > 0);
		Log.Information("Coverage summary: {XmlPath} and {JsonPath} ({FileCount} files)", outputPath, jsonPath,
			filesWithIssues);
	}

	// ── Extraction ───────────────────────────────────────────────────────────

	private static Dictionary<string, CoverageFile> ExtractAllFileIssues(XElement coverageElement, string sourceRoot)
	{
		Dictionary<string, CoverageFile> result = new(StringComparer.OrdinalIgnoreCase);

		foreach (XElement classElement in coverageElement.Descendants("class"))
		{
			string? filename = classElement.Attribute("filename")?.Value;
			if (filename is not { Length: > 0 })
			{
				continue;
			}

			string normalizedPath = NormalizePath(filename, sourceRoot);

			if (WellKnownExclusionPatterns.ShouldExcludePath(normalizedPath))
			{
				continue;
			}

			ExclusionRule? tagRule = WellKnownExclusionPatterns.GetMatchingRule(normalizedPath);
			string? ruleTag = tagRule is { ShouldExclude: false } ? tagRule.CreateReasonTag() : null;

			string? className = classElement.Attribute("name")?.Value;
			string? stateMachineMethod = null;
			if (className is { Length: > 0 } && StateMachinePatterns.IsStateMachineClass(className))
			{
				StateMachinePatterns.TryExtractStateMachineMethod(className, out stateMachineMethod);
			}

			ProcessClassIssues(classElement, normalizedPath, result, ruleTag, stateMachineMethod);
		}

		return result;
	}

	private static void ProcessClassIssues(
		XElement classElement,
		string normalizedPath,
		Dictionary<string, CoverageFile> result,
		string? ruleTag,
		string? stateMachineMethod)
	{
		CoverageFile file = GetOrCreateFileIssues(result, normalizedPath);

		foreach (XElement line in classElement.Descendants("line"))
		{
			int hits = int.Parse(line.Attribute("hits")?.Value ?? "0", CultureInfo.InvariantCulture);
			int lineNumber = int.Parse(line.Attribute("number")?.Value ?? "0", CultureInfo.InvariantCulture);
			bool isBranch = string.Equals(line.Attribute("branch")?.Value, "true", StringComparison.OrdinalIgnoreCase);
			string? conditionCoverage = line.Attribute("condition-coverage")?.Value;

			if (isBranch && conditionCoverage is { Length: > 0 })
			{
				if (CreateBranchIssue(lineNumber, hits, conditionCoverage, ruleTag, stateMachineMethod) is
				    { } branchIssue)
				{
					file.BranchDict.TryAdd(lineNumber, branchIssue);
				}
			}
			else if (hits is 0)
			{
				string reason = DetermineLineReason(ruleTag, stateMachineMethod);
				file.LineDict.TryAdd(lineNumber, new CoverageLine(lineNumber, 0, reason));
			}
		}
	}

	// ── Helpers ──────────────────────────────────────────────────────────────

	private static void WriteProjectSummary(
		string projectName,
		Dictionary<string, CoverageFile> fileIssues,
		AbsolutePath outputPath,
		DateTime generatedAtUtc,
		string sourceName)
	{
		XElement xmlSummary = new("coverage-summary",
			new XAttribute("project", projectName),
			new XAttribute("generatedAtUtc", generatedAtUtc.ToString("O", CultureInfo.InvariantCulture)),
			new XAttribute("source", sourceName));

		int filesWithIssues = 0;

		foreach ((string filePath, CoverageFile file) in fileIssues.OrderBy(kvp => kvp.Key,
			         StringComparer.OrdinalIgnoreCase))
		{
			if (file.LineDict.Count is 0 && file.BranchDict.Count is 0)
			{
				continue;
			}

			XElement xmlFile = new("file", new XAttribute("path", filePath));

			foreach (CoverageBranch branch in file.Branches)
			{
				xmlFile.Add(new XElement("branch",
					new XAttribute("line", branch.Line),
					new XAttribute("coveredBranches", branch.CoveredBranches),
					new XAttribute("totalBranches", branch.TotalBranches),
					new XAttribute("coveragePercent",
						branch.CoveragePercent.ToString("F1", CultureInfo.InvariantCulture)),
					new XAttribute("hits", branch.Hits),
					new XAttribute("reason", branch.Reason)));
			}

			foreach (CoverageLine line in file.Lines)
			{
				xmlFile.Add(new XElement("line",
					new XAttribute("number", line.Line),
					new XAttribute("hits", line.Hits),
					new XAttribute("reason", line.Reason)));
			}

			xmlSummary.Add(xmlFile);
			filesWithIssues++;
		}

		EnsureDirectoryExists(outputPath);
		new XDocument(new XDeclaration("1.0", "utf-8", null), xmlSummary).Save(outputPath);

		Log.Information("Coverage summary: {Path} ({FileCount} files with issues)", outputPath, filesWithIssues);
	}

	private static string NormalizePath(string path, string? sourceRoot)
	{
		path = path.Replace((char)92, '/');

		if (sourceRoot is { Length: > 0 })
		{
			string normalizedRoot = sourceRoot.Replace((char)92, '/');
			if (!normalizedRoot.EndsWith('/'))
			{
				normalizedRoot += '/';
			}

			if (path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
			{
				path = path[normalizedRoot.Length..];
			}
		}

		return path;
	}

	private static string DetermineLineReason(string? ruleTag, string? stateMachineMethod) =>
		stateMachineMethod is { Length: > 0 } ? StateMachinePatterns.CreateStateMachineReason(stateMachineMethod) :
		ruleTag is { Length: > 0 } ? ruleTag :
		"LineNotExecuted";

	private static CoverageBranch? CreateBranchIssue(
		int lineNumber, int hits, string conditionCoverage,
		string? ruleTag, string? stateMachineMethod)
	{
		if (ParseConditionCoverage(conditionCoverage) is not { } info)
		{
			if (hits is 0)
			{
				string reason = DetermineLineReason(ruleTag, stateMachineMethod);
				return new CoverageBranch(lineNumber, hits, 0, 1, 0, reason);
			}

			return null;
		}

		if (info.CoveredBranches >= info.TotalBranches)
		{
			return null;
		}

		string branchReason =
			stateMachineMethod is { Length: > 0 } ? StateMachinePatterns.CreateStateMachineReason(stateMachineMethod) :
			ruleTag is { Length: > 0 } ? ruleTag :
			info.CoveredBranches is 0 ? "BranchNotCovered" :
			"BranchPartiallyCovered";

		return new CoverageBranch(lineNumber, hits, info.CoveredBranches, info.TotalBranches, info.Percent, branchReason);
	}

	private static BranchCoverageInfo? ParseConditionCoverage(ReadOnlySpan<char> input)
	{
		// Format: "50% (1/2)"
		int parenStart = input.IndexOf('(');
		int parenEnd = input.IndexOf(')');
		if (parenStart < 0 || parenEnd <= parenStart)
		{
			return null;
		}

		ReadOnlySpan<char> percentPart = input[..parenStart].Trim();
		if (percentPart is not [.., '%'])
		{
			return null;
		}

		if (!double.TryParse(percentPart[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double percent))
		{
			return null;
		}

		ReadOnlySpan<char> fraction = input[(parenStart + 1)..parenEnd];
		int slashIdx = fraction.IndexOf('/');
		if (slashIdx < 0)
		{
			return null;
		}

		if (!int.TryParse(fraction[..slashIdx], NumberStyles.Integer, CultureInfo.InvariantCulture, out int covered))
		{
			return null;
		}

		if (!int.TryParse(fraction[(slashIdx + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture,
			    out int total))
		{
			return null;
		}

		return new BranchCoverageInfo(covered, total, percent);
	}

	private static CoverageFile GetOrCreateFileIssues(Dictionary<string, CoverageFile> dict, string path) =>
		dict.TryGetValue(path, out CoverageFile? issues) ? issues : dict[path] = new CoverageFile { Path = path };

	private static void EnsureDirectoryExists(string path)
	{
		if (Path.GetDirectoryName(path) is { Length: > 0 } dir)
		{
			Directory.CreateDirectory(dir);
		}
	}

	// ── Unified Smart Data Structures ────────────────────────────────────────

	private sealed class CoverageSummary
	{
		public string Project { get; init; } = "";
		public string Source { get; init; } = "";
		public DateTime GeneratedAtUtc { get; init; }
		public List<CoverageFile> Files { get; } = [];
	}

	private sealed class CoverageFile
	{
		public string Path { get; init; } = "";
		
		// Internal dictionaries for O(1) lookup during processing
		[JsonIgnore] 
		public Dictionary<int, CoverageLine> LineDict { get; } = [];
		
		[JsonIgnore]
		public Dictionary<int, CoverageBranch> BranchDict { get; } = [];

		// Public lists for JSON serialization (sorted)
		public IEnumerable<CoverageLine> Lines => LineDict.Values.OrderBy(l => l.Line);
		public IEnumerable<CoverageBranch> Branches => BranchDict.Values.OrderBy(b => b.Line);
	}

	private sealed record CoverageLine(int Line, int Hits, string Reason = "");

	private sealed record CoverageBranch(
		int Line,
		int Hits,
		int CoveredBranches,
		int TotalBranches,
		double CoveragePercent,
		string Reason = "");

	private sealed record BranchCoverageInfo(int CoveredBranches, int TotalBranches, double Percent);
}