namespace PaperlessREST.Configuration;

/// <summary>
/// Cross-platform path normalization utilities.
/// </summary>
public static class PathNormalization
{
	/// <summary>
	/// Gets a platform-appropriate string comparer for path comparisons.
	/// Case-insensitive on Windows and macOS, case-sensitive on Linux.
	/// </summary>
	public static StringComparer PlatformComparer =>
		OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
			? StringComparer.OrdinalIgnoreCase
			: StringComparer.Ordinal;

	/// <summary>
	/// Normalizes a path by resolving to full path and trimming trailing separators.
	/// Returns empty string for null or whitespace input.
	/// </summary>
	public static string Normalize(string? value) =>
		string.IsNullOrWhiteSpace(value)
			? string.Empty
			: Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
}
