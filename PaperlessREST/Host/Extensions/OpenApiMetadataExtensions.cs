namespace PaperlessREST.Host.Extensions;

/// <summary>
///     Convenience extensions for common error response patterns.
/// </summary>
/// <remarks>
///     <para>
///         These are thin wrappers around built-in ASP.NET Core methods.
///         Descriptions come from XML doc comments on the endpoint methods via
///         the .NET 10 OpenAPI source generator.
///     </para>
///     <para>
///         For custom descriptions, use the <c>&lt;response code="..."&gt;</c> XML tag
///         on your endpoint method.
///     </para>
/// </remarks>
public static class OpenApiMetadataExtensions
{
	extension(RouteHandlerBuilder builder)
	{
		/// <summary>
		///     Adds 404 Not Found to OpenAPI metadata.
		/// </summary>
		public RouteHandlerBuilder ProducesNotFound() =>
			builder.Produces(StatusCodes.Status404NotFound);

		/// <summary>
		///     Adds 409 Conflict to OpenAPI metadata.
		/// </summary>
		public RouteHandlerBuilder ProducesConflict() =>
			builder.Produces(StatusCodes.Status409Conflict);

		/// <summary>
		///     Adds 503 Service Unavailable to OpenAPI metadata.
		/// </summary>
		public RouteHandlerBuilder ProducesServiceUnavailable() =>
			builder.ProducesProblem(StatusCodes.Status503ServiceUnavailable);

		/// <summary>
		///     Adds standard error responses for GET-by-ID operations: 404.
		/// </summary>
		public RouteHandlerBuilder ProducesGetByIdErrors() =>
			builder.ProducesNotFound();

		/// <summary>
		///     Adds standard error responses for DELETE operations: 404, optionally 409.
		/// </summary>
		public RouteHandlerBuilder ProducesDeleteErrors(bool canConflict = false)
		{
			builder.ProducesNotFound();
			if (canConflict)
			{
				builder.ProducesConflict();
			}

			return builder;
		}

		/// <summary>
		///     Adds standard error responses for write operations: 422, 500, 503.
		/// </summary>
		public RouteHandlerBuilder ProducesWriteErrors() =>
			builder
				.ProducesValidationProblem()
				.ProducesProblem(StatusCodes.Status500InternalServerError)
				.ProducesServiceUnavailable();

		/// <summary>
		///     Adds document upload error responses: 422, 500, 503.
		/// </summary>
		public RouteHandlerBuilder ProducesDocumentUploadErrors() =>
			builder.ProducesWriteErrors();
	}
}
