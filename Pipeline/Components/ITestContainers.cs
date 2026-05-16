using Nuke.Common;
using Serilog;
using System;

namespace Build.Components;

/// <summary>
///     Testcontainers configuration component.
///     Sets up Docker environment for integration tests in CI.
/// </summary>
internal interface ITestContainers : INukeBuild
{
	Target SetupTestcontainers => d => d
		.Description("Configure Testcontainers for CI")
		.Unlisted()
		.Executes(() =>
		{
			if (IsServerBuild)
			{
				Environment.SetEnvironmentVariable("DOCKER_HOST", "unix:///var/run/docker.sock");
				Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "false");

				Log.Information("Testcontainers: Configured for CI");
				Log.Debug("  DOCKER_HOST = unix:///var/run/docker.sock");
			}
			else
			{
				Log.Debug("Testcontainers: Using local Docker configuration");
			}
		});
}
