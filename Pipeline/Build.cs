using Build.Components;
using DotCov.Nuke;
using Nuke.Common;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.IO;
using Serilog;

namespace Build;

/// <summary>
///     NUKE Build composition root.
///     All targets are inherited from interface-based build components.
/// </summary>
/// <remarks>
///     Components:
///     - IHasSolution: Shared Solution, paths
///     - IRestore: NuGet package restore
///     - ICompile: Build with GitVersion
///     - ITest: xUnit v3 / MTP testing
///     - ICoverage: Code coverage with ReportGenerator
///     - ICoverageReport (DotCov.Nuke): Cobertura aggregation, gating, and Markdown summary
///     - IChangelog: Git-based changelog generation
///     - IDockerBuild: Application image builds
///     - IDockerCompose: Docker Compose orchestration
///     - ITestContainers: CI Docker configuration
/// </remarks>
[GitHubActions(
	"ci",
	GitHubActionsImage.UbuntuLatest,
	AutoGenerate = false,
	OnPushBranches = ["main", "develop", "feature/*"],
	OnPullRequestBranches = ["main", "develop"],
	InvokedTargets = [nameof(ITest.Test)],
	FetchDepth = 0)]
internal sealed class Build : NukeBuild,
	// Core build pipeline
	IRestore,
	ICompile,
	IChangelog,
	// Testing
	ITest,
	ICoverage,
	ICoverageReport,
	ITestContainers,
	// Docker
	IDockerBuild,
	IDockerCompose
{
	// DotCov.Nuke scans this directory for **/coverage.cobertura.xml.
	// Point it at the same per-project subdirs ICoverage writes to.
	AbsolutePath ICoverageReport.CoverageSearchDirectory => ((IHasSolution)this).CoverageDirectory;

	/// <summary>Entry point - default target is Test.</summary>
	public static int Main() => Execute<Build>(x => ((ITest)x).Test);

	/// <summary>Print build info at start.</summary>
	Target Print => d => d
		.Unlisted()
		.Before<ICompile>(x => x.Clean)
		.Executes(() =>
		{
			var compile = (ICompile)this;
			var gitVersion = compile.GitVersion;

			Log.Information("═══════════════════════════════════════════════════════════════");
			Log.Information("  Configuration : {Configuration}", compile.Configuration);
			Log.Information("  Version       : {Version}", gitVersion?.FullSemVer ?? "N/A");
			Log.Information("  Branch        : {Branch}", gitVersion?.BranchName ?? "N/A");
			Log.Information("  Commit        : {Sha}", gitVersion?.Sha?[..8] ?? "N/A");
			Log.Information("  Solution      : {Solution}", ((IHasSolution)this).Solution.FileName);
			Log.Information("  IsServerBuild : {IsServer}", IsServerBuild);
			Log.Information("═══════════════════════════════════════════════════════════════");
		});

	/// <summary>Full CI pipeline: Clean → Compile → Test → Coverage.</summary>
	Target CI => d => d
		.Description("Full CI pipeline")
		.DependsOn<ICompile>(x => x.Clean)
		.DependsOn<ICoverage>(x => x.Coverage)
		.Executes(() =>
		{
			Log.Information("CI pipeline completed successfully");
		});

	/// <summary>Start full stack for development.</summary>
	Target Dev => d => d
		.Description("Start development environment (Docker + compile)")
		.DependsOn<IDockerCompose>(x => x.DockerUp)
		.DependsOn<ICompile>(x => x.Compile)
		.Executes(() =>
		{
			Log.Information("Development environment ready");
			Log.Information("  Dashboard: http://localhost:18888");
			Log.Information("  API:       http://localhost:8080");
		});
}
