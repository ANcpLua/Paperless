using PaperlessREST.Host.Extensions;

namespace PaperlessREST.Tests.Unit;

/// <summary>
///     Unit tests for <see cref="TypedErrorOrAsyncExtensions" />, the bridge between
///     <see cref="ErrorOr{T}" /> service results and ASP.NET typed <see cref="Results{TResult1,TResult2}" /> unions.
///     Exercises every result-shape (Ok / Validation / Failure / Unexpected / NotFound / Deleted) plus
///     the contract-violation throw paths for unexpected error types.
/// </summary>
public sealed class TypedErrorOrAsyncExtensionsTests
{
	private const string RouteName = "GetDocumentById";

	private sealed record Doc(Guid Id, string FileName);

	private sealed record DocResponse(string Id, string FileName);

	private static DocResponse Map(Doc d) => new(d.Id.ToString(), d.FileName);

	private static object RouteValues(Doc d) => new { id = d.Id };

	// ─── ErrorOr<T>.ToOkOr404 (sync overload) ───────────────────────────────

	[Fact]
	public void ToOkOr404_Sync_SuccessfulResult_ReturnsOk()
	{
		Doc doc = new(Guid.CreateVersion7(), "f.pdf");
		ErrorOr<Doc> result = doc;

		Results<Ok<DocResponse>, NotFound> output = result.ToOkOr404(Map);

		output.Result.Should().BeOfType<Ok<DocResponse>>()
			.Which.Value!.FileName.Should().Be("f.pdf");
	}

	[Fact]
	public void ToOkOr404_Sync_NotFound_ReturnsNotFound()
	{
		ErrorOr<Doc> result = Error.NotFound("Doc.NotFound", "missing");

		Results<Ok<DocResponse>, NotFound> output = result.ToOkOr404(Map);

		output.Result.Should().BeOfType<NotFound>();
	}

	[Fact]
	public void ToOkOr404_Sync_UnexpectedErrorType_ThrowsContractViolation()
	{
		ErrorOr<Doc> result = Error.Conflict("Doc.Locked", "locked");

		Action act = () => _ = result.ToOkOr404(Map, "GetDocumentById");

		act.Should().Throw<ContractViolationException>()
			.Which.ExpectedErrorTypes.Should().Equal(ErrorType.NotFound);
	}

	// ─── ErrorOr<Deleted>.ToNoContentOr404 (sync overload) ───────────────────

	[Fact]
	public void ToNoContentOr404_Sync_Success_ReturnsNoContent()
	{
		ErrorOr<Deleted> result = Result.Deleted;

		Results<NoContent, NotFound> output = result.ToNoContentOr404();

		output.Result.Should().BeOfType<NoContent>();
	}

	[Fact]
	public void ToNoContentOr404_Sync_NotFound_ReturnsNotFound()
	{
		ErrorOr<Deleted> result = Error.NotFound("Doc.NotFound", "missing");

		Results<NoContent, NotFound> output = result.ToNoContentOr404();

		output.Result.Should().BeOfType<NotFound>();
	}

	[Fact]
	public void ToNoContentOr404_Sync_UnexpectedErrorType_ThrowsContractViolation()
	{
		ErrorOr<Deleted> result = Error.Conflict("Doc.Locked", "locked");

		Action act = () => _ = result.ToNoContentOr404("Delete");

		act.Should().Throw<ContractViolationException>();
	}

	// ─── ValueTask<ErrorOr<T>>.ToOkOr404 (async overload) ────────────────────

	[Fact]
	public async Task ToOkOr404_ValueTask_Success_ReturnsOk()
	{
		Doc doc = new(Guid.CreateVersion7(), "vf.pdf");
		ValueTask<ErrorOr<Doc>> task = new(ErrorOrFactory.From(doc));

		Results<Ok<DocResponse>, NotFound> output = await task.ToOkOr404(Map);

		output.Result.Should().BeOfType<Ok<DocResponse>>()
			.Which.Value!.FileName.Should().Be("vf.pdf");
	}

	[Fact]
	public async Task ToOkOr404_ValueTask_NotFound_ReturnsNotFound()
	{
		ValueTask<ErrorOr<Doc>> task = new(Error.NotFound("Doc.NotFound", "missing"));

		Results<Ok<DocResponse>, NotFound> output = await task.ToOkOr404(Map);

		output.Result.Should().BeOfType<NotFound>();
	}

	// ─── ValueTask<ErrorOr<Deleted>>.ToNoContentOr404 (async overload) ──────

	[Fact]
	public async Task ToNoContentOr404_ValueTask_Success_ReturnsNoContent()
	{
		ValueTask<ErrorOr<Deleted>> task = new(ErrorOrFactory.From(Result.Deleted));

		Results<NoContent, NotFound> output = await task.ToNoContentOr404();

		output.Result.Should().BeOfType<NoContent>();
	}

	[Fact]
	public async Task ToNoContentOr404_ValueTask_NotFound_ReturnsNotFound()
	{
		ValueTask<ErrorOr<Deleted>> task = new(Error.NotFound("Doc.NotFound", "missing"));

		Results<NoContent, NotFound> output = await task.ToNoContentOr404();

		output.Result.Should().BeOfType<NotFound>();
	}

	// ─── Task<ErrorOr<T>>.ToOkOr404 (Task overload — delegates to ValueTask) ─

	[Fact]
	public async Task ToOkOr404_Task_Success_ReturnsOk()
	{
		Doc doc = new(Guid.CreateVersion7(), "tf.pdf");
		Task<ErrorOr<Doc>> task = Task.FromResult<ErrorOr<Doc>>(doc);

		Results<Ok<DocResponse>, NotFound> output = await task.ToOkOr404(Map);

		output.Result.Should().BeOfType<Ok<DocResponse>>();
	}

	[Fact]
	public async Task ToNoContentOr404_Task_Success_ReturnsNoContent()
	{
		Task<ErrorOr<Deleted>> task = Task.FromResult<ErrorOr<Deleted>>(Result.Deleted);

		Results<NoContent, NotFound> output = await task.ToNoContentOr404();

		output.Result.Should().BeOfType<NoContent>();
	}

	// ─── ToAcceptedAtRouteOrProblem ─────────────────────────────────────────

	[Fact]
	public async Task ToAcceptedAtRouteOrProblem_Success_ReturnsAcceptedAtRoute()
	{
		Doc doc = new(Guid.CreateVersion7(), "accepted.pdf");
		ValueTask<ErrorOr<Doc>> task = new(ErrorOrFactory.From(doc));

		Results<AcceptedAtRoute<DocResponse>, ValidationProblem, ProblemHttpResult> output =
			await task.ToAcceptedAtRouteOrProblem(Map, RouteName, RouteValues);

		AcceptedAtRoute<DocResponse>? accepted = output.Result as AcceptedAtRoute<DocResponse>;
		accepted.Should().NotBeNull();
		accepted!.RouteName.Should().Be(RouteName);
		accepted.Value!.FileName.Should().Be("accepted.pdf");
	}

	[Fact]
	public async Task ToAcceptedAtRouteOrProblem_ValidationError_Returns422WithGroupedErrors()
	{
		Error[] errors =
		[
			Error.Validation("FileName", "is required"),
			Error.Validation("FileName", "must be PDF"),
			Error.Validation("FileSize", "too large")
		];
		ValueTask<ErrorOr<Doc>> task = new(errors);

		Results<AcceptedAtRoute<DocResponse>, ValidationProblem, ProblemHttpResult> output =
			await task.ToAcceptedAtRouteOrProblem(Map, RouteName, RouteValues);

		ValidationProblem? vp = output.Result as ValidationProblem;
		vp.Should().NotBeNull();
		HttpValidationProblemDetails details = vp!.ProblemDetails;
		details.Errors.Should().ContainKey("FileName");
		details.Errors["FileName"].Should().BeEquivalentTo(["is required", "must be PDF"]);
		details.Errors.Should().ContainKey("FileSize");
	}

	[Fact]
	public async Task ToAcceptedAtRouteOrProblem_Failure_Returns500WithKebabCaseUrn()
	{
		Error failure = Error.Failure("Document.UploadFailed", "storage broke");
		ValueTask<ErrorOr<Doc>> task = new(failure);

		Results<AcceptedAtRoute<DocResponse>, ValidationProblem, ProblemHttpResult> output =
			await task.ToAcceptedAtRouteOrProblem(Map, RouteName, RouteValues);

		ProblemHttpResult? problem = output.Result as ProblemHttpResult;
		problem.Should().NotBeNull();
		problem!.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
		problem.ProblemDetails.Title.Should().Be("Document.UploadFailed");
		problem.ProblemDetails.Type.Should().Be("urn:paperless:error:document.-upload-failed");
	}

	[Fact]
	public async Task ToAcceptedAtRouteOrProblem_FailureWithMetadata_PropagatesAsCamelCasedExtensions()
	{
		Error failure = Error.Custom(
			(int)ErrorType.Failure, "Document.UploadFailed", "storage broke",
			new Dictionary<string, object> { ["AttemptCount"] = 3, ["LastTriedAt"] = "2026-05-15" });
		ValueTask<ErrorOr<Doc>> task = new(failure);

		Results<AcceptedAtRoute<DocResponse>, ValidationProblem, ProblemHttpResult> output =
			await task.ToAcceptedAtRouteOrProblem(Map, RouteName, RouteValues);

		ProblemHttpResult problem = (ProblemHttpResult)output.Result!;
		problem.ProblemDetails.Extensions.Should().ContainKey("attemptCount").WhoseValue.Should().Be(3);
		problem.ProblemDetails.Extensions.Should().ContainKey("lastTriedAt").WhoseValue.Should().Be("2026-05-15");
	}

	[Fact]
	public async Task ToAcceptedAtRouteOrProblem_Unexpected_Returns503WithRetryAfterFromMetadata()
	{
		Error unavailable = Error.Custom(
			(int)ErrorType.Unexpected, "Document.StorageUnavailable", "down",
			new Dictionary<string, object> { ["RetryAfter"] = 90, ["AffectedResource"] = "docs/x" });
		ValueTask<ErrorOr<Doc>> task = new(unavailable);

		Results<AcceptedAtRoute<DocResponse>, ValidationProblem, ProblemHttpResult> output =
			await task.ToAcceptedAtRouteOrProblem(Map, RouteName, RouteValues);

		ProblemHttpResult problem = (ProblemHttpResult)output.Result!;
		problem.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
		problem.ProblemDetails.Extensions["retryAfter"].Should().Be(90);
		problem.ProblemDetails.Extensions.Should().ContainKey("affectedResource");
		problem.ProblemDetails.Extensions["affectedResource"].Should().Be("docs/x");
	}

	[Fact]
	public async Task ToAcceptedAtRouteOrProblem_UnexpectedWithoutMetadata_DefaultsRetryAfterTo30()
	{
		Error unavailable = Error.Unexpected("Backend.Down", "no metadata");
		ValueTask<ErrorOr<Doc>> task = new(unavailable);

		Results<AcceptedAtRoute<DocResponse>, ValidationProblem, ProblemHttpResult> output =
			await task.ToAcceptedAtRouteOrProblem(Map, RouteName, RouteValues);

		ProblemHttpResult problem = (ProblemHttpResult)output.Result!;
		problem.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
		problem.ProblemDetails.Extensions["retryAfter"].Should().Be(30);
	}

	[Fact]
	public async Task ToAcceptedAtRouteOrProblem_UnexpectedRetryAfterOnly_OmitsOtherExtensions()
	{
		// Exercises BuildServiceUnavailableExtensions early return when only RetryAfter is set
		// (the "Where(kvp => kvp.Key != "RetryAfter")" filter yields nothing).
		Error unavailable = Error.Custom(
			(int)ErrorType.Unexpected, "Backend.Down", "only retry",
			new Dictionary<string, object> { ["RetryAfter"] = 5 });
		ValueTask<ErrorOr<Doc>> task = new(unavailable);

		Results<AcceptedAtRoute<DocResponse>, ValidationProblem, ProblemHttpResult> output =
			await task.ToAcceptedAtRouteOrProblem(Map, RouteName, RouteValues);

		ProblemHttpResult problem = (ProblemHttpResult)output.Result!;
		problem.ProblemDetails.Extensions.Should().ContainKey("retryAfter");
		problem.ProblemDetails.Extensions.Should().NotContainKey("affectedResource");
	}

	[Fact]
	public async Task ToAcceptedAtRouteOrProblem_UnsupportedErrorType_ThrowsContractViolation()
	{
		ValueTask<ErrorOr<Doc>> task = new(Error.NotFound("Doc.NotFound", "missing"));

		Func<Task> act = async () => _ = await task.ToAcceptedAtRouteOrProblem(
			Map, RouteName, RouteValues, "PostDocument");

		(await act.Should().ThrowAsync<ContractViolationException>())
			.Which.ExpectedErrorTypes.Should().Equal(
				ErrorType.Validation, ErrorType.Failure, ErrorType.Unexpected);
	}

	// ─── Task overload also routes through the same internals ──────────────

	[Fact]
	public async Task ToAcceptedAtRouteOrProblem_Task_Success_ReturnsAcceptedAtRoute()
	{
		Doc doc = new(Guid.CreateVersion7(), "task-success.pdf");
		Task<ErrorOr<Doc>> task = Task.FromResult<ErrorOr<Doc>>(doc);

		Results<AcceptedAtRoute<DocResponse>, ValidationProblem, ProblemHttpResult> output =
			await task.ToAcceptedAtRouteOrProblem(Map, RouteName, RouteValues);

		output.Result.Should().BeOfType<AcceptedAtRoute<DocResponse>>();
	}
}
