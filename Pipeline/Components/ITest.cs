using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Serilog;
using System;
using System.Collections.Generic;

namespace Build.Components;

/// <summary>
///     Testing component for xUnit v3 with Microsoft Testing Platform (MTP).
/// </summary>
[ParameterPrefix(nameof(ITest))]
internal interface ITest : ICompile
{
	[Parameter("Test filter expression (xUnit v3 query syntax)")]
	string? TestFilter => null;

	[Parameter("Stop on first test failure")]
	bool? StopOnFail => null;

	[Parameter("Show live test output")]
	bool? LiveOutput => null;

	/// <summary>Test projects to run.</summary>
	Project[] TestProjects =>
	[
		Solution.GetProject("PaperlessREST.Tests")
			?? throw new InvalidOperationException("PaperlessREST.Tests not found"),
		Solution.GetProject("PaperlessServices.Tests")
			?? throw new InvalidOperationException("PaperlessServices.Tests not found")
	];

	Target Test => d => d
		.Description("Run all tests")
		.DependsOn<ICompile>(x => x.Compile)
		.Executes(() => RunTests(new TestOptions(Filter: TestFilter)));

	Target UnitTests => d => d
		.Description("Run unit tests only")
		.DependsOn<ICompile>(x => x.Compile)
		.Executes(() => RunTests(new TestOptions(
			NamespaceFilter: "*.Unit",
			ReportPrefix: "Unit")));

	Target IntegrationTests => d => d
		.Description("Run integration tests only")
		.DependsOn<ICompile>(x => x.Compile)
		.Executes(() => RunTests(new TestOptions(
			NamespaceFilter: "*.Integration",
			ReportPrefix: "Integration")));

	// ══════════════════════════════════════════════════════════════════════════
	// IMPLEMENTATION
	// ══════════════════════════════════════════════════════════════════════════

	private void RunTests(TestOptions options)
	{
		foreach (Project project in TestProjects)
		{
			RunTestProject(project, options);
		}
	}

	private void RunTestProject(Project project, TestOptions options)
	{
		string reportName = options.ReportPrefix is { Length: > 0 } prefix
			? $"{project.Name}.{prefix}.trx"
			: $"{project.Name}.trx";

		Log.Information("Running tests: {Project}", project.Name);

		MtpArgumentsBuilder mtp = MtpExtensions.Mtp()
			.ReportTrx(reportName)
			.IgnoreExitCode(8);

		if (options.Filter is { Length: > 0 } filter)
			mtp.FilterQuery(filter);

		if (options.NamespaceFilter is { Length: > 0 } ns)
			mtp.FilterNamespace(ns);

		if (options.WithCoverage && options.CoverageOutput is { } coverageFile)
			mtp.CoverageCobertura(coverageFile);

		if (StopOnFail == true)
			mtp.StopOnFail();

		if (LiveOutput == true || IsLocalBuild)
			mtp.ShowLiveOutput();

		ExecuteMtpTestInternal(project, mtp);
	}

	void ExecuteMtpTestInternal(Project project, MtpArgumentsBuilder mtp)
	{
		IReadOnlyList<string> mtpArgs = mtp.BuildArgs();

		List<string> args =
		[
			"test",
			"--project", project.Path!,
			"--configuration", Configuration,
			"--no-build",
			"--no-restore",
			"--results-directory", TestResultsDirectory,
			.. mtpArgs.Count > 0 ? ["--", .. mtpArgs] : Array.Empty<string>()
		];

		Log.Debug("Executing: dotnet {Arguments}", string.Join(" ", args));

		IProcess? process = ProcessTasks.StartProcess(
			ToolPathResolver.GetPathExecutable("dotnet"),
			string.Join(" ", args),
			RootDirectory,
			logOutput: true);

		process.AssertWaitForExit();

		if (process.ExitCode is not (0 or 8))
			throw new InvalidOperationException($"dotnet test failed with exit code {process.ExitCode}");

		if (process.ExitCode is 8)
			Log.Warning("Zero tests matched filter (exit code 8)");
	}

	/// <summary>Test run options.</summary>
	protected readonly record struct TestOptions(
		string? Filter = null,
		string? NamespaceFilter = null,
		string ReportPrefix = "",
		bool WithCoverage = false,
		AbsolutePath? CoverageOutput = null);
}
