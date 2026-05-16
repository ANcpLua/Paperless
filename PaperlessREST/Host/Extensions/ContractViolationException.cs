using System.Text;

namespace PaperlessREST.Host.Extensions;

/// <summary>
///     Thrown when a service returns an <see cref="ErrorType" /> that the endpoint's
///     <c>Results&lt;&gt;</c> union does not handle.
///     <para>
///         This indicates a contract violation between the service layer and the endpoint definition.
///         The endpoint should either be updated to handle the error type, or the service
///         should not return that error type for this operation.
///     </para>
/// </summary>
/// <remarks>
///     <para>
///         <b>Why this exception exists:</b> When using typed <c>Results&lt;Ok, NotFound&gt;</c> unions,
///         the endpoint explicitly declares which error responses it handles. If the service returns
///         an unexpected <see cref="ErrorType" /> (e.g., <see cref="ErrorType.Conflict" /> when only
///         <see cref="ErrorType.NotFound" /> is expected), that's a programming error that should be
///         caught during development/testing.
///     </para>
///     <para>
///         <b>Debugging:</b> The exception captures the full error context including all errors
///         (not just the first), the endpoint name, and expected vs actual error types.
///         This information is logged by <see cref="API.GlobalExceptionHandler" /> and included
///         in ProblemDetails during development.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     // Endpoint expects only NotFound errors
///     public static Task&lt;Results&lt;Ok&lt;DocumentDto&gt;, NotFound&gt;&gt; GetById(...)
///         => service.GetByIdAsync(id, ct).ToOkOr404();
///
///     // But service returns Conflict - ContractViolationException is thrown:
///     // "Contract violation in GetById: Expected [NotFound] but received Conflict.
///     //  Error: Document.Conflict - Document is locked for editing"
///     </code>
/// </example>
/// <remarks>
///     Initializes a new instance of <see cref="ContractViolationException" />.
/// </remarks>
/// <param name="endpointOperation">The HTTP operation (e.g., "GET /documents/{id}", "DELETE").</param>
/// <param name="expectedErrorTypes">The error types the endpoint was designed to handle.</param>
/// <param name="actualError">The first (primary) error returned by the service.</param>
/// <param name="allErrors">All errors returned by the service (for aggregated error scenarios).</param>
[StackTraceHidden] // Hide from stack trace since it's infrastructure
public sealed class ContractViolationException(
	string endpointOperation,
	IReadOnlyList<ErrorType> expectedErrorTypes,
	Error actualError,
	IReadOnlyList<Error> allErrors)
	: InvalidOperationException(BuildMessage(endpointOperation, expectedErrorTypes, actualError, allErrors))
{
	/// <summary>
	///     The HTTP operation where the violation occurred (e.g., "GET", "DELETE", "GetDocumentById").
	/// </summary>
	public string EndpointOperation { get; } = endpointOperation;

	/// <summary>
	///     The <see cref="ErrorType" /> values the endpoint was designed to handle.
	/// </summary>
	public IReadOnlyList<ErrorType> ExpectedErrorTypes { get; } = expectedErrorTypes;

	/// <summary>
	///     The primary (first) <see cref="Error" /> returned by the service.
	/// </summary>
	public Error ActualError { get; } = actualError;

	/// <summary>
	///     All <see cref="Error" /> instances returned by the service.
	///     May contain multiple errors in aggregated scenarios.
	/// </summary>
	public IReadOnlyList<Error> AllErrors { get; } = allErrors;

	/// <summary>
	///     Gets diagnostic information suitable for logging and debugging.
	/// </summary>
	public ContractViolationDiagnostics GetDiagnostics() => new(
		EndpointOperation,
		[.. ExpectedErrorTypes.Select(e => e.ToString())],
		ActualError.Type.ToString(),
		ActualError.Code,
		ActualError.Description,
		[.. AllErrors.Select(e => new ErrorDetail(e.Type.ToString(), e.Code, e.Description))],
		ActualError.Metadata);

	private static string BuildMessage(
		string endpointOperation,
		IReadOnlyList<ErrorType> expectedErrorTypes,
		Error actualError,
		IReadOnlyList<Error> allErrors)
	{
		StringBuilder sb = new();

		sb.Append("Contract violation in ");
		sb.Append(endpointOperation);
		sb.Append(": Expected [");
		sb.AppendJoin(", ", expectedErrorTypes);
		sb.Append("] but received ");
		sb.Append(actualError.Type);
		sb.Append(". Error: ");
		sb.Append(actualError.Code);
		sb.Append(" - ");
		sb.Append(actualError.Description);

		if (allErrors.Count <= 1)
		{
			return sb.ToString();
		}

		sb.Append(" (+ ");
		sb.Append(allErrors.Count - 1);
		sb.Append(" more error(s))");

		return sb.ToString();
	}

	// ═══════════════════════════════════════════════════════════════════════════
	// Factory Methods for Common Scenarios
	// ═══════════════════════════════════════════════════════════════════════════

	/// <summary>
	///     Creates exception for GET/DELETE operations expecting only NotFound.
	/// </summary>
	public static ContractViolationException ForNotFoundOnly(
		Error actualError,
		IReadOnlyList<Error> allErrors,
		[CallerMemberName] string operation = "") =>
		new(operation, [ErrorType.NotFound], actualError, allErrors);

	/// <summary>
	///     Creates exception for operations expecting only Validation errors.
	///     Non-validation errors (like infrastructure failures) propagate to the global handler → 500.
	/// </summary>
	public static ContractViolationException ForValidationOnly(
		Error actualError,
		IReadOnlyList<Error> allErrors,
		[CallerMemberName] string operation = "") =>
		new(operation, [ErrorType.Validation], actualError, allErrors);

	/// <summary>
	///     Creates exception for operations expecting NotFound or Conflict.
	/// </summary>
	public static ContractViolationException ForNotFoundOrConflict(
		Error actualError,
		IReadOnlyList<Error> allErrors,
		[CallerMemberName] string operation = "") =>
		new(operation, [ErrorType.NotFound, ErrorType.Conflict], actualError, allErrors);

	/// <summary>
	///     Creates exception for CRUD operations expecting Validation, NotFound, or Conflict.
	/// </summary>
	public static ContractViolationException ForCrudOperation(
		Error actualError,
		IReadOnlyList<Error> allErrors,
		[CallerMemberName] string operation = "") =>
		new(operation, [ErrorType.Validation, ErrorType.NotFound, ErrorType.Conflict], actualError, allErrors);

	/// <summary>
	///     Creates exception with custom expected error types.
	/// </summary>
	public static ContractViolationException For(
		Error actualError,
		IReadOnlyList<Error> allErrors,
		string operation,
		params ErrorType[] expectedTypes) =>
		new(operation, expectedTypes, actualError, allErrors);
}

/// <summary>
///     Structured diagnostic information for logging and ProblemDetails.
/// </summary>
public sealed record ContractViolationDiagnostics(
	string Operation,
	string[] ExpectedErrorTypes,
	string ActualErrorType,
	string ErrorCode,
	string ErrorDescription,
	ErrorDetail[] AllErrors,
	Dictionary<string, object>? Metadata);

/// <summary>
///     Individual error detail for multi-error scenarios.
/// </summary>
public sealed record ErrorDetail(string Type, string Code, string Description);
