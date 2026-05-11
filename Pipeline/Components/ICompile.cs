using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitVersion;
using Serilog;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

namespace Build.Components;

/// <summary>
///     Compilation component with GitVersion integration.
/// </summary>
internal interface ICompile : IHasSolution
{
	[GitVersion(Framework = "net10.0", NoCache = true, NoFetch = true)]
	GitVersion? GitVersion => TryGetValue(() => GitVersion);

	[Parameter("Build configuration (Debug/Release)")]
	Configuration Configuration => TryGetValue(() => Configuration)
		?? (IsLocalBuild ? Configuration.Debug : Configuration.Release);

	Target Compile => d => d
		.Description("Build the solution")
		.TryDependsOn<IRestore>()
		.Executes(() =>
		{
			var settings = new DotNetBuildSettings()
				.SetProjectFile(GetSolutionPath())
				.SetConfiguration(Configuration)
				.EnableNoRestore()
				.SetDeterministic(IsServerBuild)
				.SetContinuousIntegrationBuild(IsServerBuild);

			if (GitVersion is not null)
			{
				settings = settings
					.SetAssemblyVersion(GitVersion.AssemblySemVer)
					.SetFileVersion(GitVersion.AssemblySemFileVer)
					.SetInformationalVersion(GitVersion.InformationalVersion);
			}

			DotNetBuild(settings);

			Log.Information("Compiled: {Solution} [{Configuration}]",
				Solution.FileName,
				Configuration);
		});

	Target Clean => d => d
		.Description("Clean build outputs")
		.TryBefore<IRestore>()
		.Executes(() =>
		{
			RootDirectory.GlobDirectories("**/bin", "**/obj").DeleteDirectories();
			ArtifactsDirectory.CreateOrCleanDirectory();

			Log.Information("Cleaned: {ArtifactsDirectory}", ArtifactsDirectory);
		});
}
