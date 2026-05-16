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
}
