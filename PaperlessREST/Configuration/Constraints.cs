namespace PaperlessREST.Configuration;

/// <summary>
/// File upload validation constraints.
/// </summary>
public static class FileUploadConstraints
{
	/// <summary>Maximum allowed file size in bytes (10 MB).</summary>
	public const long MaxFileSizeBytes = 10 * 1024 * 1024;

	/// <summary>Bytes per megabyte for display conversion.</summary>
	public const double BytesPerMegabyte = 1024 * 1024;
}

/// <summary>
/// Search query validation constraints.
/// </summary>
public static class SearchConstraints
{
	/// <summary>Maximum query string length for DTO validation.</summary>
	public const int QueryMaxLength = 100;

	/// <summary>Minimum query string length.</summary>
	public const int QueryMinLength = 1;

	/// <summary>Maximum query length at service layer (truncation threshold).</summary>
	public const int ServiceQueryMaxLength = 1000;

	/// <summary>Maximum results that can be requested.</summary>
	public const int MaxResultLimit = 100;

	/// <summary>Default number of results returned.</summary>
	public const int DefaultResultLimit = 10;
}

/// <summary>
/// Pagination constraints for document listing.
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

/// <summary>
/// Rate limiting policy names.
/// </summary>
public static class RateLimitPolicies
{
	/// <summary>Policy for read operations (higher limit).</summary>
	public const string ReadOperations = "read";

	/// <summary>Policy for write operations (lower limit).</summary>
	public const string WriteOperations = "write";

	/// <summary>Policy for search operations (moderate limit).</summary>
	public const string SearchOperations = "search";
}

/// <summary>
/// Cache policy names.
/// </summary>
public static class CachePolicies
{
	/// <summary>Short-lived cache for document lists.</summary>
	public const string DocumentList = "document-list";

	/// <summary>Per-document cache with ETag.</summary>
	public const string DocumentById = "document-by-id";
}
