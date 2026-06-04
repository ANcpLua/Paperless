using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Serilog;
using System;
using System.Collections.Generic;

namespace Build.Components;

/// <summary>
///     Docker image build component.
///     Builds application container images for paperless-rest and paperless-services.
/// </summary>
[ParameterPrefix(nameof(IDockerBuild))]
internal interface IDockerBuild : IHasSolution
{
	/// <summary>Docker image tag.</summary>
	[Parameter("Docker image tag")]
	string ImageTag => "latest";

	/// <summary>Docker registry prefix (e.g., ghcr.io/username).</summary>
	[Parameter("Docker registry prefix")]
	string? Registry => null;

	/// <summary>Push images after building.</summary>
	[Parameter("Push images after build")]
	bool? Push => null;

	// ══════════════════════════════════════════════════════════════════════════
	// IMAGE NAMES
	// ══════════════════════════════════════════════════════════════════════════

	private string RestImageName => FormatImageName("paperless-rest");
	private string ServicesImageName => FormatImageName("paperless-services");

	private string FormatImageName(string name) =>
		string.IsNullOrEmpty(Registry)
			? $"{name}:{ImageTag}"
			: $"{Registry}/{name}:{ImageTag}";

	// ══════════════════════════════════════════════════════════════════════════
	// TARGETS
	// ══════════════════════════════════════════════════════════════════════════

	/// <summary>Build all application Docker images.</summary>
	Target DockerImageBuild => d => d
		.Description("Build all application Docker images")
		.Executes(() =>
		{
			BuildImage("paperless-rest", RootDirectory / "PaperlessREST" / "Dockerfile", RestImageName);
			BuildImage("paperless-services", RootDirectory / "PaperlessServices" / "Dockerfile", ServicesImageName);
		});

	/// <summary>Build only the REST API image.</summary>
	Target DockerBuildRest => d => d
		.Description("Build paperless-rest Docker image")
		.Executes(() =>
		{
			BuildImage("paperless-rest", RootDirectory / "PaperlessREST" / "Dockerfile", RestImageName);
		});

	/// <summary>Build only the Services image.</summary>
	Target DockerBuildServices => d => d
		.Description("Build paperless-services Docker image")
		.Executes(() =>
		{
			BuildImage("paperless-services", RootDirectory / "PaperlessServices" / "Dockerfile", ServicesImageName);
		});

	/// <summary>Push all images to registry.</summary>
	Target DockerImagePush => d => d
		.Description("Push Docker images to registry")
		.DependsOn<IDockerBuild>(x => x.DockerImageBuild)
		.Requires(() => Registry)
		.Executes(() =>
		{
			Log.Information("Pushing images to registry: {Registry}", Registry);

			PushImage(RestImageName);
			PushImage(ServicesImageName);

			Log.Information("Images pushed successfully");
		});

	// ══════════════════════════════════════════════════════════════════════════
	// HELPERS
	// ══════════════════════════════════════════════════════════════════════════

	private void BuildImage(string name, AbsolutePath dockerfile, string tag)
	{
		Log.Information("Building image: {Name} → {Tag}", name, tag);

		string args = $"build -f \"{dockerfile}\" -t {tag} --pull \"{RootDirectory}\"";

		IProcess? process = ProcessTasks.StartProcess(
			"docker",
			args,
			RootDirectory,
			environmentVariables: new Dictionary<string, string> { ["DOCKER_BUILDKIT"] = "1" },
			logOutput: true);

		process.AssertWaitForExit();

		if (process.ExitCode != 0)
			throw new InvalidOperationException($"docker build failed for {name}");

		Log.Information("Image built: {Tag}", tag);
	}

	private void PushImage(string tag)
	{
		Log.Information("Pushing image: {Tag}", tag);

		IProcess? process = ProcessTasks.StartProcess(
			"docker",
			$"push {tag}",
			RootDirectory,
			logOutput: true);

		process.AssertWaitForExit();

		if (process.ExitCode != 0)
			throw new InvalidOperationException($"docker push failed for {tag}");
	}
}
