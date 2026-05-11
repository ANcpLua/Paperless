namespace PaperlessREST.Features.DocumentManagement.Application;

/// <summary>
///     Domain errors for the DocumentManagement feature.
/// </summary>
/// <remarks>
///     <para>
///         <b>Error Type Semantics:</b>
///     </para>
///     <list type="table">
///         <listheader>
///             <term>ErrorType</term>
///             <description>HTTP Status / Meaning</description>
///         </listheader>
///         <item>
///             <term>
///                 <see cref="ErrorType.NotFound" />
///             </term>
///             <description>404 - Resource doesn't exist</description>
///         </item>
///         <item>
///             <term>
///                 <see cref="ErrorType.Validation" />
///             </term>
///             <description>422 - Business rule violation</description>
///         </item>
///         <item>
///             <term>
///                 <see cref="ErrorType.Conflict" />
///             </term>
///             <description>409 - Concurrent modification</description>
///         </item>
///         <item>
///             <term>
///                 <see cref="ErrorType.Failure" />
///             </term>
///             <description>500 - Permanent server error (bug, corruption)</description>
///         </item>
///         <item>
///             <term>
///                 <see cref="ErrorType.Unexpected" />
///             </term>
///             <description>503 - Transient infrastructure error (retry later)</description>
///         </item>
///     </list>
/// </remarks>
public static class DocumentErrors
{
	/// <summary>
	///     Document with the specified ID was not found.
	/// </summary>
	public static Error NotFound(Guid id) => Error.NotFound(
		"Document.NotFound",
		$"Document {id} not found");

	/// <summary>
	///     Invalid state transition attempted (e.g., completing an already-completed document).
	/// </summary>
	public static Error InvalidStateTransition(DocumentStatus from, DocumentStatus to) => Error.Validation(
		"Document.InvalidStateTransition",
		$"Cannot transition from {from} to {to}");

	/// <summary>
	///     Cannot mark document as completed - not in Pending state.
	/// </summary>
	public static Error CannotComplete(DocumentStatus currentStatus) => Error.Validation(
		"Document.CannotComplete",
		$"Cannot complete document in {currentStatus} status");

	/// <summary>
	///     Cannot mark document as failed - not in Pending state.
	/// </summary>
	public static Error CannotFail(DocumentStatus currentStatus) => Error.Validation(
		"Document.CannotFail",
		$"Cannot fail document in {currentStatus} status");

	/// <summary>
	///     Storage service (MinIO) is temporarily unavailable.
	/// </summary>
	/// <remarks>
	///     This is a transient error - the client should retry after the Retry-After header.
	/// </remarks>
	public static Error StorageUnavailable(string storagePath) => Error.Unexpected(
		"Document.StorageUnavailable",
		$"Storage service temporarily unavailable for {storagePath}");

	public static Error StorageTimeout(string storagePath) => Error.Unexpected(
		"Document.StorageTimeout",
		$"Storage service did not respond within timeout for {storagePath}");

	public static Error StorageServerError(string storagePath, int statusCode) => Error.Unexpected(
		"Document.StorageServerError",
		$"Storage service returned {statusCode} for {storagePath}");

	public static Error StorageConnectionFailed(string storagePath) => Error.Unexpected(
		"Document.StorageConnectionFailed",
		$"Cannot connect to storage service for {storagePath}");

	/// <summary>
	///     Search service (Elasticsearch) is temporarily unavailable.
	/// </summary>
	public static Error SearchUnavailable() => Error.Unexpected(
		"Document.SearchUnavailable",
		"Search service temporarily unavailable");

	/// <summary>
	///     Message broker (RabbitMQ) is temporarily unavailable.
	/// </summary>
	public static Error MessageBrokerUnavailable() => Error.Unexpected(
		"Document.MessageBrokerUnavailable",
		"Message broker temporarily unavailable");

	public static Error MessageBrokerUnavailable(string reason) => Error.Unexpected(
		"Document.MessageBrokerUnavailable",
		$"Message broker temporarily unavailable: {reason}");

	/// <summary>
	///     Permanent storage failure - file may be corrupted or permissions issue.
	/// </summary>
	/// <remarks>
	///     This is NOT a transient error - requires investigation.
	/// </remarks>
	public static Error StorageFailed(string storagePath) => Error.Failure(
		"Document.StorageFailed",
		$"Failed to store document at {storagePath}");

	/// <summary>
	///     Permanent delete failure - database constraint or orphaned data.
	/// </summary>
	public static Error DeleteFailed(Guid id) => Error.Failure(
		"Document.DeleteFailed",
		$"Failed to delete document {id}");
}
