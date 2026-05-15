using PaperlessREST.Host.Extensions;

namespace PaperlessREST.Tests.Unit;

public sealed class TypedErrorOrAsyncExtensionsTests
{
	private const string RouteName = "GetById";

	// Helpers return Task-wrapped ErrorOr; Task<T> doesn't trigger CA2012.
	// Tests target the Task<ErrorOr<T>> overload, which internally delegates to
	// the ValueTask<ErrorOr<T>> overload, exercising both layers.
	private static Task<ErrorOr<T>> TaskValue<T>(T value) =>
		Task.FromResult<ErrorOr<T>>(value);

	private static Task<ErrorOr<T>> TaskFromError<T>(Error err) =>
		Task.FromResult<ErrorOr<T>>(err);

	private static Task<ErrorOr<T>> TaskFromErrors<T>(List<Error> errors) =>
		Task.FromResult<ErrorOr<T>>(errors);

	// ─── ErrorOr<T>.ToOkOr404 (sync) ─────────────────────────────────────────

	[Fact]
	public void ToOkOr404_Sync_Success_ReturnsOkWithMappedValue()
	{
		ErrorOr<int> result = (ErrorOr<int>)7;
		Results<Ok<string>, NotFound> typed = result.ToOkOr404(v => $"v={v}");

		Ok<string>? ok = typed.Result.Should().BeOfType<Ok<string>>().Subject;
		ok.Value.Should().Be("v=7");
	}

	[Fact]
	public void ToOkOr404_Sync_NotFound_ReturnsNotFoundResult()
	{
		ErrorOr<int> result = (ErrorOr<int>)Error.NotFound("X", "missing");
		Results<Ok<string>, NotFound> typed = result.ToOkOr404(v => v.ToString());

		typed.Result.Should().BeOfType<NotFound>();
	}

	[Fact]
	public void ToOkOr404_Sync_NonNotFoundError_ThrowsContractViolation()
	{
		ErrorOr<int> result = (ErrorOr<int>)Error.Conflict("X", "conflict");

		Action act = () => result.ToOkOr404(v => v.ToString());

		ContractViolationException ex = act.Should().Throw<ContractViolationException>().Which;
		ex.ExpectedErrorTypes.Should().ContainSingle().Which.Should().Be(ErrorType.NotFound);
		ex.ActualError.Code.Should().Be("X");
	}

	// ─── ErrorOr<Deleted>.ToNoContentOr404 (sync) ────────────────────────────

	[Fact]
	public void ToNoContentOr404_Sync_Success_ReturnsNoContent()
	{
		ErrorOr<Deleted> result = (ErrorOr<Deleted>)Result.Deleted;
		Results<NoContent, NotFound> typed = result.ToNoContentOr404();

		typed.Result.Should().BeOfType<NoContent>();
	}

	[Fact]
	public void ToNoContentOr404_Sync_NotFound_ReturnsNotFoundResult()
	{
		ErrorOr<Deleted> result = (ErrorOr<Deleted>)Error.NotFound("X", "missing");
		Results<NoContent, NotFound> typed = result.ToNoContentOr404();

		typed.Result.Should().BeOfType<NotFound>();
	}

	[Fact]
	public void ToNoContentOr404_Sync_NonNotFoundError_ThrowsContractViolation()
	{
		ErrorOr<Deleted> result = (ErrorOr<Deleted>)Error.Failure("X", "boom");

		Action act = () => result.ToNoContentOr404();

		ContractViolationException ex = act.Should().Throw<ContractViolationException>().Which;
		ex.ExpectedErrorTypes.Should().ContainSingle().Which.Should().Be(ErrorType.NotFound);
	}

	// ─── ValueTask<ErrorOr<T>>.ToOkOr404 ──────────────────────────────────────

	[Fact]
	public async Task ToOkOr404_ValueTask_Success_ReturnsOk()
	{
		Results<Ok<string>, NotFound> typed = await TaskValue(42).ToOkOr404(v => v.ToString());

		Ok<string>? ok = typed.Result.Should().BeOfType<Ok<string>>().Subject;
		ok.Value.Should().Be("42");
	}

	[Fact]
	public async Task ToOkOr404_ValueTask_NotFound_ReturnsNotFound()
	{
		Results<Ok<string>, NotFound> typed =
			await TaskFromError<int>(Error.NotFound("X", "missing")).ToOkOr404(v => v.ToString());

		typed.Result.Should().BeOfType<NotFound>();
	}

	[Fact]
	public async Task ToOkOr404_ValueTask_NonNotFoundError_ThrowsContractViolation()
	{
		Func<Task> act = async () =>
			await TaskFromError<int>(Error.Failure("X", "boom")).ToOkOr404(v => v.ToString());

		ContractViolationException ex = (await act.Should().ThrowAsync<ContractViolationException>()).Which;
		ex.ExpectedErrorTypes.Should().ContainSingle().Which.Should().Be(ErrorType.NotFound);
	}

	// ─── ValueTask<ErrorOr<Deleted>>.ToNoContentOr404 ────────────────────────

	[Fact]
	public async Task ToNoContentOr404_ValueTask_Success_ReturnsNoContent()
	{
		Results<NoContent, NotFound> typed = await TaskValue(Result.Deleted).ToNoContentOr404();

		typed.Result.Should().BeOfType<NoContent>();
	}

	[Fact]
	public async Task ToNoContentOr404_ValueTask_NotFound_ReturnsNotFound()
	{
		Results<NoContent, NotFound> typed =
			await TaskFromError<Deleted>(Error.NotFound("X", "missing")).ToNoContentOr404();

		typed.Result.Should().BeOfType<NotFound>();
	}

	[Fact]
	public async Task ToNoContentOr404_ValueTask_NonNotFoundError_ThrowsContractViolation()
	{
		Func<Task> act = async () =>
			await TaskFromError<Deleted>(Error.Conflict("X", "conflict")).ToNoContentOr404();

		await act.Should().ThrowAsync<ContractViolationException>();
	}

	// ─── Task<ErrorOr<T>>.ToOkOr404 (delegates to ValueTask) ─────────────────

	[Fact]
	public async Task ToOkOr404_Task_Success_ReturnsOk()
	{
		Results<Ok<string>, NotFound> typed = await TaskValue(99).ToOkOr404(v => $"#{v}");

		Ok<string>? ok = typed.Result.Should().BeOfType<Ok<string>>().Subject;
		ok.Value.Should().Be("#99");
	}

	[Fact]
	public async Task ToOkOr404_Task_NotFound_ReturnsNotFound()
	{
		Results<Ok<string>, NotFound> typed =
			await TaskFromError<int>(Error.NotFound("X", "missing")).ToOkOr404(v => v.ToString());

		typed.Result.Should().BeOfType<NotFound>();
	}

	// ─── Task<ErrorOr<Deleted>>.ToNoContentOr404 (delegates) ─────────────────

	[Fact]
	public async Task ToNoContentOr404_Task_Success_ReturnsNoContent()
	{
		Results<NoContent, NotFound> typed = await TaskValue(Result.Deleted).ToNoContentOr404();

		typed.Result.Should().BeOfType<NoContent>();
	}

	[Fact]
	public async Task ToNoContentOr404_Task_NotFound_ReturnsNotFound()
	{
		Results<NoContent, NotFound> typed =
			await TaskFromError<Deleted>(Error.NotFound("X", "missing")).ToNoContentOr404();

		typed.Result.Should().BeOfType<NotFound>();
	}

	// ─── ToAcceptedAtRouteOrProblem — success ────────────────────────────────

	[Fact]
	public async Task ToAcceptedAtRouteOrProblem_ValueTask_Success_ReturnsAcceptedAtRouteWithMappedValueAndRouteValues()
	{
		Results<AcceptedAtRoute<string>, ValidationProblem, ProblemHttpResult> typed =
			await TaskValue(7).ToAcceptedAtRouteOrProblem(
				v => $"v={v}",
				RouteName,
				v => new { id = v });

		AcceptedAtRoute<string>? accepted = typed.Result.Should().BeOfType<AcceptedAtRoute<string>>().Subject;
		accepted.RouteName.Should().Be(RouteName);
		accepted.Value.Should().Be("v=7");
		accepted.RouteValues["id"].Should().Be(7);
	}

	[Fact]
	public async Task ToAcceptedAtRouteOrProblem_Task_Success_ReturnsAcceptedAtRoute()
	{
		Results<AcceptedAtRoute<int>, ValidationProblem, ProblemHttpResult> typed =
			await TaskValue(5).ToAcceptedAtRouteOrProblem(v => v, RouteName, v => new { id = v });

		AcceptedAtRoute<int>? accepted = typed.Result.Should().BeOfType<AcceptedAtRoute<int>>().Subject;
		accepted.Value.Should().Be(5);
	}

	// ─── Validation path ─────────────────────────────────────────────────────

	[Fact]
	public async Task ToAcceptedAtRouteOrProblem_Validation_GroupsErrorsByCode()
	{
		List<Error> errors =
		[
			Error.Validation("FileName", "FileName required"),
			Error.Validation("FileName", "FileName must be PDF"),
			Error.Validation("Size", "Size too large")
		];

		Results<AcceptedAtRoute<int>, ValidationProblem, ProblemHttpResult> typed =
			await TaskFromErrors<int>(errors).ToAcceptedAtRouteOrProblem(
				v => v, RouteName, v => new { id = v });

		ValidationProblem? vp = typed.Result.Should().BeOfType<ValidationProblem>().Subject;
		HttpValidationProblemDetails details = vp.ProblemDetails;
		details.Errors.Should().ContainKey("FileName");
		details.Errors["FileName"].Should().BeEquivalentTo("FileName required", "FileName must be PDF");
		details.Errors.Should().ContainKey("Size");
		details.Errors["Size"].Should().BeEquivalentTo("Size too large");
		details.Errors.Should().HaveCount(2);
	}

	// ─── Failure → 500, kebab URN, camelCase extensions ──────────────────────

	[Fact]
	public async Task ToAcceptedAtRouteOrProblem_Failure_ReturnsServerErrorWithKebabUrnAndCamelCaseExtensions()
	{
		Dictionary<string, object> metadata = new()
		{
			["StoragePath"] = "documents/abc.pdf",
			["AttemptCount"] = 3
		};
		Error err = Error.Failure("Document.StorageFailed", "Failed to store", metadata);

		Results<AcceptedAtRoute<int>, ValidationProblem, ProblemHttpResult> typed =
			await TaskFromError<int>(err).ToAcceptedAtRouteOrProblem(v => v, RouteName, v => new { id = v });

		ProblemHttpResult? problem = typed.Result.Should().BeOfType<ProblemHttpResult>().Subject;
		problem.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
		// ToKebabCase emits a dash before every internal uppercase char, including
		// the one right after the dot in "Document.StorageFailed".
		problem.ProblemDetails.Type.Should().Be("urn:paperless:error:document.-storage-failed");
		problem.ProblemDetails.Title.Should().Be("Document.StorageFailed");
		problem.ProblemDetails.Detail.Should().Be("Failed to store");
		problem.ProblemDetails.Extensions["storagePath"].Should().Be("documents/abc.pdf");
		problem.ProblemDetails.Extensions["attemptCount"].Should().Be(3);
	}

	[Fact]
	public async Task ToAcceptedAtRouteOrProblem_Failure_NullMetadata_OmitsExtensions()
	{
		Error err = Error.Failure("Plain.Failure", "no metadata");

		Results<AcceptedAtRoute<int>, ValidationProblem, ProblemHttpResult> typed =
			await TaskFromError<int>(err).ToAcceptedAtRouteOrProblem(v => v, RouteName, v => new { id = v });

		ProblemHttpResult? problem = typed.Result.Should().BeOfType<ProblemHttpResult>().Subject;
		problem.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
		// "Plain.Failure" → 'P' becomes 'p' (i=0), '.' stays, 'F' becomes '-f'.
		problem.ProblemDetails.Type.Should().Be("urn:paperless:error:plain.-failure");
		problem.ProblemDetails.Extensions.Should().NotContainKey("storagePath");
	}

	[Fact]
	public async Task ToAcceptedAtRouteOrProblem_Failure_EmptyMetadata_OmitsExtensions()
	{
		Dictionary<string, object> emptyMeta = [];
		Error err = Error.Failure("Plain.Failure", "empty meta", emptyMeta);

		Results<AcceptedAtRoute<int>, ValidationProblem, ProblemHttpResult> typed =
			await TaskFromError<int>(err).ToAcceptedAtRouteOrProblem(v => v, RouteName, v => new { id = v });

		ProblemHttpResult? problem = typed.Result.Should().BeOfType<ProblemHttpResult>().Subject;
		problem.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
		problem.ProblemDetails.Extensions.Should().NotContainKey("retryAfter");
	}

	// ─── Unexpected → 503 retryAfter ─────────────────────────────────────────

	[Fact]
	public async Task ToAcceptedAtRouteOrProblem_Unexpected_WithRetryAfterMetadata_UsesProvidedValue()
	{
		Dictionary<string, object> metadata = new()
		{
			["RetryAfter"] = 120,
			["AffectedResource"] = "documents/x.pdf"
		};
		Error err = Error.Unexpected("Document.StorageUnavailable", "tmp", metadata);

		Results<AcceptedAtRoute<int>, ValidationProblem, ProblemHttpResult> typed =
			await TaskFromError<int>(err).ToAcceptedAtRouteOrProblem(v => v, RouteName, v => new { id = v });

		ProblemHttpResult? problem = typed.Result.Should().BeOfType<ProblemHttpResult>().Subject;
		problem.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
		problem.ProblemDetails.Type.Should().Be("urn:paperless:error:document.-storage-unavailable");
		problem.ProblemDetails.Extensions["retryAfter"].Should().Be(120);
		problem.ProblemDetails.Extensions["affectedResource"].Should().Be("documents/x.pdf");
		problem.ProblemDetails.Extensions.Should().NotContainKey("RetryAfter");
	}

	[Fact]
	public async Task ToAcceptedAtRouteOrProblem_Unexpected_WithoutRetryAfterMetadata_Defaults30()
	{
		Dictionary<string, object> metadata = new() { ["AffectedResource"] = "x.pdf" };
		Error err = Error.Unexpected("Document.StorageUnavailable", "tmp", metadata);

		Results<AcceptedAtRoute<int>, ValidationProblem, ProblemHttpResult> typed =
			await TaskFromError<int>(err).ToAcceptedAtRouteOrProblem(v => v, RouteName, v => new { id = v });

		ProblemHttpResult? problem = typed.Result.Should().BeOfType<ProblemHttpResult>().Subject;
		problem.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
		problem.ProblemDetails.Extensions["retryAfter"].Should().Be(30);
		problem.ProblemDetails.Extensions["affectedResource"].Should().Be("x.pdf");
	}

	[Fact]
	public async Task ToAcceptedAtRouteOrProblem_Unexpected_NullMetadata_Defaults30AndEmpty()
	{
		Error err = Error.Unexpected("Document.SearchUnavailable", "Search down");

		Results<AcceptedAtRoute<int>, ValidationProblem, ProblemHttpResult> typed =
			await TaskFromError<int>(err).ToAcceptedAtRouteOrProblem(v => v, RouteName, v => new { id = v });

		ProblemHttpResult? problem = typed.Result.Should().BeOfType<ProblemHttpResult>().Subject;
		problem.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
		problem.ProblemDetails.Extensions["retryAfter"].Should().Be(30);
	}

	[Fact]
	public async Task ToAcceptedAtRouteOrProblem_Unexpected_EmptyMetadata_Defaults30NoExtras()
	{
		Dictionary<string, object> emptyMeta = [];
		Error err = Error.Unexpected("Document.SearchUnavailable", "Search down", emptyMeta);

		Results<AcceptedAtRoute<int>, ValidationProblem, ProblemHttpResult> typed =
			await TaskFromError<int>(err).ToAcceptedAtRouteOrProblem(v => v, RouteName, v => new { id = v });

		ProblemHttpResult? problem = typed.Result.Should().BeOfType<ProblemHttpResult>().Subject;
		problem.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
		problem.ProblemDetails.Extensions["retryAfter"].Should().Be(30);
		problem.ProblemDetails.Extensions.Should().ContainSingle(kv => kv.Key == "retryAfter");
	}

	// ─── Unhandled error type → contract violation ──────────────────────────

	[Fact]
	public async Task ToAcceptedAtRouteOrProblem_UnhandledErrorType_ThrowsContractViolation()
	{
		Error err = Error.Conflict("Document.Locked", "locked");

		Func<Task> act = async () =>
			await TaskFromError<int>(err).ToAcceptedAtRouteOrProblem(v => v, RouteName, v => new { id = v });

		ContractViolationException ex = (await act.Should().ThrowAsync<ContractViolationException>()).Which;
		ex.ExpectedErrorTypes.Should().BeEquivalentTo(
			new[] { ErrorType.Validation, ErrorType.Failure, ErrorType.Unexpected },
			opts => opts.WithStrictOrdering());
		ex.ActualError.Code.Should().Be("Document.Locked");
	}

	// ─── ToKebabCase indirect coverage — mid-string uppercase forces dash ────

	[Fact]
	public async Task ToAcceptedAtRouteOrProblem_Failure_LowercaseStartCamelMid_DashesOnlyInternalUppercase()
	{
		Error err = Error.Failure("iPadOrNot", "x");

		Results<AcceptedAtRoute<int>, ValidationProblem, ProblemHttpResult> typed =
			await TaskFromError<int>(err).ToAcceptedAtRouteOrProblem(v => v, RouteName, v => new { id = v });

		ProblemHttpResult? problem = typed.Result.Should().BeOfType<ProblemHttpResult>().Subject;
		problem.ProblemDetails.Type.Should().Be("urn:paperless:error:i-pad-or-not");
	}

	[Fact]
	public async Task ToAcceptedAtRouteOrProblem_Failure_SingleLowercaseToken_NoDashes()
	{
		Error err = Error.Failure("simple", "x");

		Results<AcceptedAtRoute<int>, ValidationProblem, ProblemHttpResult> typed =
			await TaskFromError<int>(err).ToAcceptedAtRouteOrProblem(v => v, RouteName, v => new { id = v });

		ProblemHttpResult? problem = typed.Result.Should().BeOfType<ProblemHttpResult>().Subject;
		problem.ProblemDetails.Type.Should().Be("urn:paperless:error:simple");
	}
}
