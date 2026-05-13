namespace PaperlessREST.Host.Extensions;

/// <summary>
///     RFC 7807 Problem Details extensions for domain errors.
/// </summary>
/// <remarks>
///     <para>
///         RFC 7807 allows custom extensions beyond the standard fields (type, title, status, detail, instance).
///         This enables rich, machine-readable error responses that clients can programmatically handle.
///     </para>
///     <para>
///         Example response with extensions:
///     </para>
///     <code>
///     {
///       "type": "urn:paperless:error:document-storage-unavailable",
///       "title": "Document.StorageUnavailable",
///       "status": 503,
///       "detail": "Storage service temporarily unavailable for documents/2025-01/abc.pdf",
///       "retryAfter": 30,
///       "affectedResource": "documents/2025-01/abc.pdf",
///       "correlationId": "abc123"
///     }
///     </code>
/// </remarks>
public static class RichProblemDetailsFactory
{
	/// <summary>
	///     Creates a <see cref="ProblemDetails" /> with RFC 7807 extensions from an <see cref="Error" />.
	/// </summary>
	/// <param name="error">The domain error.</param>
	/// <param name="httpContext">The HTTP context for trace information.</param>
	/// <returns>A <see cref="ProblemDetails" /> with extensions from error metadata.</returns>
	public static ProblemDetails CreateFromError(Error error, HttpContext? httpContext = null)
	{
		int statusCode = MapErrorTypeToStatusCode(error.Type);

		ProblemDetails problem = new()
		{
			Type = $"urn:paperless:error:{ToKebabCase(error.Code)}",
			Title = error.Code,
			Status = statusCode,
			Detail = error.Description,
			Instance = httpContext?.Request.Path
		};

		// Add trace ID if available
		if (httpContext?.TraceIdentifier is { } traceId)
		{
			problem.Extensions["correlationId"] = traceId;
		}

		// Add error metadata as RFC 7807 extensions
		if (error.Metadata is { Count: > 0 })
		{
			foreach (KeyValuePair<string, object> kvp in error.Metadata)
			{
				// Convert PascalCase to camelCase for JSON conventions
				string key = char.ToLowerInvariant(kvp.Key[0]) + kvp.Key[1..];
				problem.Extensions[key] = kvp.Value;
			}
		}

		// Add standard extensions based on error type
		switch (error.Type)
		{
			case ErrorType.Unexpected:
				// 503: Add Retry-After hint
				problem.Extensions["retryAfter"] = error.Metadata?.GetValueOrDefault("RetryAfter") ?? 30;
				break;

			case ErrorType.Conflict:
				// 409: Add current state info if available
				if (error.Metadata?.TryGetValue("CurrentState", out object? state) == true)
				{
					problem.Extensions["currentState"] = state;
				}

				break;

			case ErrorType.Validation:
				// 422: Could add field-level errors here
				break;
			case ErrorType.Failure:
			case ErrorType.NotFound:
			case ErrorType.Unauthorized:
			case ErrorType.Forbidden:
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(error), error.Type, "Unsupported error type");
		}

		return problem;
	}

	/// <summary>
	///     Creates a <see cref="ProblemHttpResult" /> from an <see cref="Error" />.
	/// </summary>
	public static ProblemHttpResult CreateProblemResult(Error error, HttpContext? httpContext = null)
	{
		ProblemDetails problem = CreateFromError(error, httpContext);
		return TypedResults.Problem(problem);
	}

	private static int MapErrorTypeToStatusCode(ErrorType type) => type switch
	{
		ErrorType.Failure => StatusCodes.Status500InternalServerError,
		ErrorType.Unexpected => StatusCodes.Status503ServiceUnavailable,
		ErrorType.Validation => StatusCodes.Status422UnprocessableEntity,
		ErrorType.Conflict => StatusCodes.Status409Conflict,
		ErrorType.NotFound => StatusCodes.Status404NotFound,
		ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
		ErrorType.Forbidden => StatusCodes.Status403Forbidden,
		_ => StatusCodes.Status500InternalServerError
	};

	private static string ToKebabCase(string value) =>
		string.Concat(value.Select((c, i) =>
			i > 0 && char.IsUpper(c) ? $"-{char.ToLowerInvariant(c)}" : char.ToLowerInvariant(c).ToString()));
}

/// <summary>
///     Extensions for creating domain errors with rich metadata.
/// </summary>
public static class ErrorMetadataExtensions
{
	/// <summary>
	///     Creates a storage unavailable error with retry hint.
	/// </summary>
	public static Error StorageUnavailable(string path, int retryAfterSeconds = 30) =>
		Error.Custom(
			(int)ErrorType.Unexpected,
			"Document.StorageUnavailable",
			$"Storage service temporarily unavailable for {path}",
			new Dictionary<string, object> { ["RetryAfter"] = retryAfterSeconds, ["AffectedResource"] = path });

	/// <summary>
	///     Creates a conflict error with current state information.
	/// </summary>
	public static Error DocumentLocked(Guid id, string lockedBy, DateTimeOffset lockedUntil) =>
		Error.Custom(
			(int)ErrorType.Conflict,
			"Document.Locked",
			$"Document {id} is locked for editing",
			new Dictionary<string, object>
			{
				["DocumentId"] = id,
				["LockedBy"] = lockedBy,
				["LockedUntil"] = lockedUntil,
				["CurrentState"] = "Locked"
			});

	/// <summary>
	///     Creates a validation error with affected field information.
	/// </summary>
	public static Error InvalidField(string fieldName, string reason, object? attemptedValue = null)
	{
		Dictionary<string, object> metadata = new() { ["Field"] = fieldName, ["Reason"] = reason };

		if (attemptedValue is not null)
		{
			metadata["AttemptedValue"] = attemptedValue;
		}

		return Error.Custom(
			(int)ErrorType.Validation,
			$"Validation.{fieldName}",
			$"Invalid value for {fieldName}: {reason}",
			metadata);
	}

	/// <summary>
	///     Creates a not found error with search hints.
	/// </summary>
	public static Error DocumentNotFound(Guid id, string? suggestion = null)
	{
		Dictionary<string, object> metadata = new()
		{
			["DocumentId"] = id,
			["SearchedAt"] = TimeProvider.System.GetUtcNow()
		};

		if (suggestion is not null)
		{
			metadata["Suggestion"] = suggestion;
		}

		return Error.Custom(
			(int)ErrorType.NotFound,
			"Document.NotFound",
			$"Document {id} not found",
			metadata);
	}
}
