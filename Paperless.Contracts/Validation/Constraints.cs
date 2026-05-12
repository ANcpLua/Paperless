namespace Paperless.Contracts.Validation;

/// <summary>
/// Search query validation constraints applied at the HTTP boundary.
/// </summary>
public static class SearchConstraints
{
	/// <summary>Maximum query string length for DTO validation.</summary>
	public const int QueryMaxLength = 100;

	/// <summary>Minimum query string length.</summary>
	public const int QueryMinLength = 1;

	/// <summary>Maximum results that can be requested.</summary>
	public const int MaxResultLimit = 100;

	/// <summary>Default number of results returned.</summary>
	public const int DefaultResultLimit = 10;
}

/// <summary>
/// Pagination constraints for document listing applied at the HTTP boundary.
/// </summary>
public static class PaginationConstraints
{
	/// <summary>Default page size when not specified.</summary>
	public const int DefaultPageSize = 20;

	/// <summary>Maximum page size allowed.</summary>
	public const int MaxPageSize = 100;

	/// <summary>Minimum page size allowed.</summary>
	public const int MinPageSize = 1;
}
