namespace PaperlessREST.Tests.Unit;

public sealed class PathNormalizationTests
{
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("  \t")]
	public void Normalize_NullOrWhitespace_ReturnsEmpty(string? value) =>
		PathNormalization.Normalize(value).Should().BeEmpty();

	[Fact]
	public void Normalize_RelativePath_ReturnsFullPathWithoutTrailingSeparator()
	{
		string relativePath = Path.Combine("batch", "input") + Path.DirectorySeparatorChar;

		string result = PathNormalization.Normalize(relativePath);

		result.Should().Be(Path.Combine(Environment.CurrentDirectory, "batch", "input"));
		Path.IsPathFullyQualified(result).Should().BeTrue();
	}

	[Fact]
	public void PlatformComparer_UsesRunningPlatformCaseSensitivity()
	{
		bool expectedToIgnoreCase = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

		PathNormalization.PlatformComparer
			.Equals("Batch/Input", "batch/input")
			.Should().Be(expectedToIgnoreCase);
	}

	[Fact]
	public void HasDistinctPaths_DifferentNormalizedPaths_ReturnsTrue()
	{
		BatchOptions options = CreateOptions("input", "archive", "error");

		options.HasDistinctPaths.Should().BeTrue();
	}

	[Fact]
	public void HasDistinctPaths_SamePathWithDifferentSyntax_ReturnsFalse()
	{
		string inputPath = Path.Combine("batch", "input");
		BatchOptions options = CreateOptions(inputPath, inputPath + Path.DirectorySeparatorChar, "error");

		options.HasDistinctPaths.Should().BeFalse();
	}

	[Fact]
	public void HasDistinctPaths_PathsDifferingOnlyByCase_UsesRunningPlatformPolicy()
	{
		BatchOptions options = CreateOptions("batch/input", "BATCH/INPUT", "batch/error");
		bool expectedToBeDistinct = !(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());

		options.HasDistinctPaths.Should().Be(expectedToBeDistinct);
	}

	private static BatchOptions CreateOptions(string inputPath, string archivePath, string errorPath) =>
		new()
		{
			InputPath = inputPath,
			ArchivePath = archivePath,
			ErrorPath = errorPath,
			FilePattern = "*.xml",
			CronExpression = "0 0 * * *",
			TimeZoneId = "UTC"
		};
}
