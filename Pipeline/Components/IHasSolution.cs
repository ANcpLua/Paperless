using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using System.Linq;

namespace Build.Components;

/// <summary>
///     Base component providing shared Solution and path properties.
///     All other components should inherit from this to access common infrastructure.
/// </summary>
internal interface IHasSolution : INukeBuild
{
	[Solution(GenerateProjects = true)]
	Solution Solution => TryGetValue(() => Solution)!;

	/// <summary>Artifacts output directory.</summary>
	AbsolutePath ArtifactsDirectory => RootDirectory / "Artifacts";

	/// <summary>Test results directory.</summary>
	AbsolutePath TestResultsDirectory => RootDirectory / "TestResults";

	/// <summary>Coverage reports directory.</summary>
	AbsolutePath CoverageDirectory => ArtifactsDirectory / "coverage";

	/// <summary>Docker compose file path.</summary>
	AbsolutePath ComposeFile => RootDirectory / "compose.yaml";

	/// <summary>Environment file for Docker Compose.</summary>
	AbsolutePath EnvFile => RootDirectory / ".env";

	/// <summary>Safe accessor for Solution.Path with fallback.</summary>
	AbsolutePath GetSolutionPath() =>
		Solution.Path ?? RootDirectory.GlobFiles("*.sln").FirstOrDefault()
		?? throw new System.InvalidOperationException("Unable to locate solution (.sln) file");
}
