using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Serilog;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Build.Components;

/// <summary>
///     Changelog generation component using git history.
/// </summary>
internal interface IChangelog : ICompile
{
	AbsolutePath ChangelogDirectory => ArtifactsDirectory / "changelog";

	Target Changelog => d => d
		.Description("Generate changelog from git history")
		.DependsOn<ICompile>(x => x.Compile)
		.Produces(ChangelogDirectory / "*.md")
		.Executes(() =>
		{
			ChangelogDirectory.CreateDirectory();

			AbsolutePath outputFile = ChangelogDirectory / "CHANGELOG_FROM_LAST_COMMIT.md";
			StringBuilder sb = new();

			// Header
			sb.AppendLine("# Changelog")
				.AppendLine()
				.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC")
				.AppendLine();

			// Current state
			string branch = RunGit("rev-parse --abbrev-ref HEAD").Trim();
			string commit = RunGit("rev-parse --short HEAD").Trim();
			string commitFull = RunGit("rev-parse HEAD").Trim();

			sb.AppendLine("## Current State")
				.AppendLine()
				.AppendLine($"- **Branch:** `{branch}`")
				.AppendLine($"- **Commit:** `{commit}` ({commitFull})");

			if (GitVersion is { } gv)
			{
				sb.AppendLine($"- **Version:** `{gv.FullSemVer}`")
					.AppendLine($"- **Informational Version:** `{gv.InformationalVersion}`");
			}

			sb.AppendLine();

			// Recent commits
			sb.AppendLine("## Recent Commits").AppendLine();
			string commitLog = RunGit("log --oneline -10 --pretty=format:\"- `%h` %s (%an, %ar)\"");
			sb.AppendLine(commitLog is { Length: > 0 } ? commitLog : "_No commits found_").AppendLine();

			// Changed files
			sb.AppendLine("## Changed Files (HEAD~1..HEAD)").AppendLine();
			string changedFiles = RunGit("diff --name-status HEAD~1..HEAD 2>/dev/null || echo \"(no previous commit)\"");

			if (changedFiles is { Length: > 0 } && !changedFiles.Contains("(no previous commit)"))
			{
				sb.AppendLine("| Status | File |")
					.AppendLine("|--------|------|");

				foreach (string line in changedFiles.Split('\n', StringSplitOptions.RemoveEmptyEntries))
				{
					string[] parts = line.Split('\t', 2);
					if (parts.Length is 2)
					{
						string status = parts[0] switch
						{
							"A" => "Added",
							"M" => "Modified",
							"D" => "Deleted",
							"R" => "Renamed",
							"C" => "Copied",
							_ => parts[0]
						};
						sb.AppendLine($"| {status} | `{parts[1]}` |");
					}
				}
			}
			else
			{
				sb.AppendLine("_No changes detected or this is the first commit_");
			}

			sb.AppendLine();

			// Uncommitted changes
			sb.AppendLine("## Uncommitted Changes").AppendLine();
			string gitStatus = RunGit("status --porcelain");
			sb.AppendLine(gitStatus is { Length: > 0 }
				? $"```\n{gitStatus}\n```"
				: "_Working directory is clean_");
			sb.AppendLine();

			// Tags
			sb.AppendLine("## Recent Tags").AppendLine();
			string tags = RunGit("tag --sort=-creatordate | head -5 2>/dev/null || echo \"(no tags)\"");

			if (tags is { Length: > 0 } && !tags.Contains("(no tags)"))
			{
				foreach (string tag in tags.Split('\n', StringSplitOptions.RemoveEmptyEntries))
					sb.AppendLine($"- `{tag}`");
			}
			else
			{
				sb.AppendLine("_No tags found_");
			}

			File.WriteAllText(outputFile, sb.ToString());
			Log.Information("Changelog written to: {Path}", outputFile);
		});

	private string RunGit(string arguments)
	{
		try
		{
			IProcess? process = ProcessTasks.StartProcess(
				"git", arguments, RootDirectory,
				logOutput: false, logInvocation: false);

			process.WaitForExit();

			return process.ExitCode is 0
				? string.Join('\n', process.Output.Select(o => o.Text))
				: string.Empty;
		}
		catch
		{
			return string.Empty;
		}
	}
}
