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
/// Server-side search constants that don't belong on the public contract.
/// Boundary-level <see cref="PaperlessREST.Contracts.Validation.SearchConstraints"/> covers query / limit ranges.
/// </summary>
public static class SearchServiceConstraints
{
	/// <summary>Maximum query length at service layer (truncation threshold).</summary>
	public const int ServiceQueryMaxLength = 1000;
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
