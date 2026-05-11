namespace PaperlessREST.Host.Extensions;

/// <summary>
///     Converts <see cref="ErrorOr{T}" /> to ASP.NET Core <see cref="Results{TResult1,TResult2}" /> union types.
/// </summary>
public static class TypedErrorOrAsyncExtensions
{
	private static readonly NotFound NotFound = TypedResults.NotFound();
	private static readonly NoContent NoContent = TypedResults.NoContent();

	private static ValidationProblem CreateValidationProblem(IReadOnlyList<Error> errors) =>
		TypedResults.ValidationProblem(
			errors.Where(e => e.Type == ErrorType.Validation)
				.GroupBy(e => e.Code)
				.ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));

	private static ProblemHttpResult CreateServerError(Error error) =>
		TypedResults.Problem(
			error.Description,
			title: error.Code,
			statusCode: StatusCodes.Status500InternalServerError,
			type: $"urn:paperless:error:{ToKebabCase(error.Code)}",
			extensions: error.Metadata is { Count: > 0 }
				? error.Metadata.ToDictionary(
					kvp => char.ToLowerInvariant(kvp.Key[0]) + kvp.Key[1..], object? (kvp) => kvp.Value)
				: null);

	private static ProblemHttpResult CreateServiceUnavailable(Error error) =>
		TypedResults.Problem(
			error.Description,
			title: error.Code,
			statusCode: StatusCodes.Status503ServiceUnavailable,
			type: $"urn:paperless:error:{ToKebabCase(error.Code)}",
			extensions: BuildServiceUnavailableExtensions(error));

	private static Dictionary<string, object?> BuildServiceUnavailableExtensions(Error error)
	{
		Dictionary<string, object?> extensions = new()
		{
			["retryAfter"] = error.Metadata?.GetValueOrDefault("RetryAfter") ?? 30
		};

		if (error.Metadata is not { Count: > 0 })
		{
			return extensions;
		}

		foreach (KeyValuePair<string, object> kvp in error.Metadata.Where(kvp => kvp.Key != "RetryAfter"))
		{
			extensions[char.ToLowerInvariant(kvp.Key[0]) + kvp.Key[1..]] = kvp.Value;
		}

		return extensions;
	}

	private static string ToKebabCase(string value) =>
		string.Concat(value.Select((c, i) =>
			i > 0 && char.IsUpper(c) ? $"-{char.ToLowerInvariant(c)}" : char.ToLowerInvariant(c).ToString()));

	// ═══════════════════════════════════════════════════════════════════════════
	// ValueTask<ErrorOr<T>> Extensions
	// ═══════════════════════════════════════════════════════════════════════════

	extension<T>(ValueTask<ErrorOr<T>> task)
	{
		/// <summary>
		///     Converts to <see cref="Ok{TResult}" /> (200) or <see cref="NotFound" /> (404).
		/// </summary>
		[MustUseReturnValue]
		public async Task<Results<Ok<TResult>, NotFound>> ToOkOr404<TResult>(
			[InstantHandle] Func<T, TResult> mapper,
			[CallerMemberName] string callerName = "")
		{
			ErrorOr<T> result = await task.ConfigureAwait(false);

			if (!result.IsError)
			{
				return TypedResults.Ok(mapper(result.Value));
			}

			return result.FirstError.Type == ErrorType.NotFound
				? NotFound
				: throw ContractViolationException.ForNotFoundOnly(result.FirstError, result.Errors, callerName);
		}

		/// <summary>
		///     Converts to <see cref="AcceptedAtRoute{TResult}" /> (202),
		///     <see cref="ValidationProblem" /> (422), or <see cref="ProblemHttpResult" /> (500/503).
		/// </summary>
		[MustUseReturnValue]
		public async Task<Results<AcceptedAtRoute<TResult>, ValidationProblem, ProblemHttpResult>>
			ToAcceptedAtRouteOrProblem<TResult>(
				[InstantHandle] Func<T, TResult> mapper,
				string routeName,
				[InstantHandle] Func<T, object> routeValuesFactory,
				[CallerMemberName] string callerName = "")
		{
			ErrorOr<T> result = await task.ConfigureAwait(false);

			if (!result.IsError)
			{
				return TypedResults.AcceptedAtRoute(mapper(result.Value), routeName, routeValuesFactory(result.Value));
			}

			return result.FirstError.Type switch
			{
				ErrorType.Validation => CreateValidationProblem(result.Errors),
				ErrorType.Failure => CreateServerError(result.FirstError),
				ErrorType.Unexpected => CreateServiceUnavailable(result.FirstError),
				_ => throw ContractViolationException.For(
					result.FirstError, result.Errors, callerName,
					ErrorType.Validation, ErrorType.Failure, ErrorType.Unexpected)
			};
		}
	}

	extension(ValueTask<ErrorOr<Deleted>> task)
	{
		/// <summary>
		///     Converts to <see cref="NoContent" /> (204) or <see cref="NotFound" /> (404).
		/// </summary>
		[MustUseReturnValue]
		public async Task<Results<NoContent, NotFound>> ToNoContentOr404([CallerMemberName] string callerName = "")
		{
			ErrorOr<Deleted> result = await task.ConfigureAwait(false);

			if (!result.IsError)
			{
				return NoContent;
			}

			return result.FirstError.Type == ErrorType.NotFound
				? NotFound
				: throw ContractViolationException.ForNotFoundOnly(result.FirstError, result.Errors, callerName);
		}
	}

	// ═══════════════════════════════════════════════════════════════════════════
	// Task<ErrorOr<T>> Extensions (delegate to ValueTask versions)
	// ═══════════════════════════════════════════════════════════════════════════

	extension<T>(Task<ErrorOr<T>> task)
	{
		[MustUseReturnValue]
		public Task<Results<Ok<TResult>, NotFound>> ToOkOr404<TResult>(
			[InstantHandle] Func<T, TResult> mapper,
			[CallerMemberName] string callerName = "") =>
			new ValueTask<ErrorOr<T>>(task).ToOkOr404(mapper, callerName);

		[MustUseReturnValue]
		public Task<Results<AcceptedAtRoute<TResult>, ValidationProblem, ProblemHttpResult>>
			ToAcceptedAtRouteOrProblem<TResult>(
				[InstantHandle] Func<T, TResult> mapper,
				string routeName,
				[InstantHandle] Func<T, object> routeValuesFactory,
				[CallerMemberName] string callerName = "") =>
			new ValueTask<ErrorOr<T>>(task).ToAcceptedAtRouteOrProblem(mapper, routeName, routeValuesFactory, callerName);
	}

	extension(Task<ErrorOr<Deleted>> task)
	{
		[MustUseReturnValue]
		public Task<Results<NoContent, NotFound>> ToNoContentOr404([CallerMemberName] string callerName = "") =>
			new ValueTask<ErrorOr<Deleted>>(task).ToNoContentOr404(callerName);
	}
}
