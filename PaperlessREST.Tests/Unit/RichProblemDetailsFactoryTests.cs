using PaperlessREST.Host.Extensions;

namespace PaperlessREST.Tests.Unit;

/// <summary>
///     Unit tests for <see cref="RichProblemDetailsFactory" /> and <see cref="ErrorMetadataExtensions" />.
///     Covers every <see cref="ErrorType" /> branch, metadata projection (PascalCase → camelCase), and
///     the RFC 7807 extensions wired in for retryAfter / currentState / validation cases.
/// </summary>
public sealed class RichProblemDetailsFactoryTests
{
	private const string ResourcePath = "documents/2025-01/abc.pdf";
	private const string RequestPath = "/api/documents/42";
	private const string TraceId = "test-trace-id";

	public static IEnumerable<TheoryDataRow<ErrorType, int>> ErrorTypeStatusCases()
	{
		yield return new TheoryDataRow<ErrorType, int>(ErrorType.Failure, StatusCodes.Status500InternalServerError)
			.WithTestDisplayName("Failure → 500");
		yield return new TheoryDataRow<ErrorType, int>(ErrorType.Unexpected, StatusCodes.Status503ServiceUnavailable)
			.WithTestDisplayName("Unexpected → 503");
		yield return new TheoryDataRow<ErrorType, int>(ErrorType.Validation, StatusCodes.Status422UnprocessableEntity)
			.WithTestDisplayName("Validation → 422");
		yield return new TheoryDataRow<ErrorType, int>(ErrorType.Conflict, StatusCodes.Status409Conflict)
			.WithTestDisplayName("Conflict → 409");
		yield return new TheoryDataRow<ErrorType, int>(ErrorType.NotFound, StatusCodes.Status404NotFound)
			.WithTestDisplayName("NotFound → 404");
		yield return new TheoryDataRow<ErrorType, int>(ErrorType.Unauthorized, StatusCodes.Status401Unauthorized)
			.WithTestDisplayName("Unauthorized → 401");
		yield return new TheoryDataRow<ErrorType, int>(ErrorType.Forbidden, StatusCodes.Status403Forbidden)
			.WithTestDisplayName("Forbidden → 403");
	}

	[Theory]
	[MemberData(nameof(ErrorTypeStatusCases))]
	public void CreateFromError_MapsErrorTypeToStatusCode(ErrorType type, int expectedStatus)
	{
		Error error = Error.Custom((int)type, "Some.Code", "description");

		ProblemDetails problem = RichProblemDetailsFactory.CreateFromError(error);

		problem.Status.Should().Be(expectedStatus);
	}

	[Fact]
	public void CreateFromError_PascalCaseCode_RendersKebabCaseUrn()
	{
		Error error = Error.NotFound("Document.NotFound", "missing");

		ProblemDetails problem = RichProblemDetailsFactory.CreateFromError(error);

		problem.Type.Should().Be("urn:paperless:error:document.-not-found");
		problem.Title.Should().Be("Document.NotFound");
		problem.Detail.Should().Be("missing");
	}

	[Fact]
	public void CreateFromError_HttpContextProvided_PopulatesInstanceAndCorrelationId()
	{
		DefaultHttpContext ctx = new() { TraceIdentifier = TraceId };
		ctx.Request.Path = RequestPath;
		Error error = Error.NotFound("Document.NotFound", "missing");

		ProblemDetails problem = RichProblemDetailsFactory.CreateFromError(error, ctx);

		problem.Instance.Should().Be(RequestPath);
		problem.Extensions.Should().ContainKey("correlationId").WhoseValue.Should().Be(TraceId);
	}

	[Fact]
	public void CreateFromError_NoHttpContext_LeavesInstanceNullAndOmitsCorrelationId()
	{
		Error error = Error.NotFound("Document.NotFound", "missing");

		ProblemDetails problem = RichProblemDetailsFactory.CreateFromError(error);

		problem.Instance.Should().BeNull();
		problem.Extensions.Should().NotContainKey("correlationId");
	}

	[Fact]
	public void CreateFromError_MetadataKeys_AreCamelCasedIntoExtensions()
	{
		Error error = Error.Custom(
			(int)ErrorType.NotFound, "Document.NotFound", "missing",
			new Dictionary<string, object> { ["DocumentId"] = "abc", ["Suggestion"] = "try-newer" });

		ProblemDetails problem = RichProblemDetailsFactory.CreateFromError(error);

		problem.Extensions.Should().ContainKey("documentId").WhoseValue.Should().Be("abc");
		problem.Extensions.Should().ContainKey("suggestion").WhoseValue.Should().Be("try-newer");
	}

	[Fact]
	public void CreateFromError_Unexpected_WithRetryAfterMetadata_PreservesValue()
	{
		Error error = Error.Custom(
			(int)ErrorType.Unexpected, "Document.StorageUnavailable", "down",
			new Dictionary<string, object> { ["RetryAfter"] = 60 });

		ProblemDetails problem = RichProblemDetailsFactory.CreateFromError(error);

		problem.Status.Should().Be(StatusCodes.Status503ServiceUnavailable);
		problem.Extensions.Should().ContainKey("retryAfter").WhoseValue.Should().Be(60);
	}

	[Fact]
	public void CreateFromError_UnexpectedWithoutRetryAfter_DefaultsTo30Seconds()
	{
		Error error = Error.Custom((int)ErrorType.Unexpected, "Backend.Down", "no metadata");

		ProblemDetails problem = RichProblemDetailsFactory.CreateFromError(error);

		problem.Extensions.Should().ContainKey("retryAfter").WhoseValue.Should().Be(30);
	}

	[Fact]
	public void CreateFromError_Conflict_WithCurrentStateMetadata_SetsCurrentState()
	{
		Error error = Error.Custom(
			(int)ErrorType.Conflict, "Document.Locked", "locked",
			new Dictionary<string, object> { ["CurrentState"] = "Locked" });

		ProblemDetails problem = RichProblemDetailsFactory.CreateFromError(error);

		problem.Status.Should().Be(StatusCodes.Status409Conflict);
		problem.Extensions.Should().ContainKey("currentState").WhoseValue.Should().Be("Locked");
	}

	[Fact]
	public void CreateFromError_ConflictWithoutCurrentState_OmitsExtension()
	{
		Error error = Error.Conflict("Document.Locked", "locked");

		ProblemDetails problem = RichProblemDetailsFactory.CreateFromError(error);

		// The error has no metadata at all, so currentState should not appear from the switch branch.
		problem.Extensions.Should().NotContainKey("currentState");
	}

	[Fact]
	public void CreateFromError_Validation_DoesNotAddRetryAfter()
	{
		Error error = Error.Validation("Document.Invalid", "bad");

		ProblemDetails problem = RichProblemDetailsFactory.CreateFromError(error);

		problem.Status.Should().Be(StatusCodes.Status422UnprocessableEntity);
		problem.Extensions.Should().NotContainKey("retryAfter");
	}

	[Fact]
	public void CreateFromError_UnsupportedErrorType_ThrowsArgumentOutOfRange()
	{
		Error error = Error.Custom(999, "Bogus.Type", "bogus");

		Action act = () => RichProblemDetailsFactory.CreateFromError(error);

		act.Should().Throw<ArgumentOutOfRangeException>();
	}

	[Fact]
	public void CreateProblemResult_WrapsCreateFromErrorInProblemHttpResult()
	{
		Error error = Error.NotFound("Document.NotFound", "missing");

		ProblemHttpResult result = RichProblemDetailsFactory.CreateProblemResult(error);

		result.StatusCode.Should().Be(StatusCodes.Status404NotFound);
		result.ProblemDetails.Title.Should().Be("Document.NotFound");
	}

	[Fact]
	public void CreateProblemResult_WithHttpContext_PropagatesExtensions()
	{
		DefaultHttpContext ctx = new() { TraceIdentifier = TraceId };
		ctx.Request.Path = RequestPath;
		Error error = Error.NotFound("Document.NotFound", "missing");

		ProblemHttpResult result = RichProblemDetailsFactory.CreateProblemResult(error, ctx);

		result.ProblemDetails.Instance.Should().Be(RequestPath);
		result.ProblemDetails.Extensions.Should().ContainKey("correlationId");
	}

	// ─── ErrorMetadataExtensions ────────────────────────────────────────────────

	[Fact]
	public void StorageUnavailable_BuildsErrorWithRetryAfterAndAffectedResource()
	{
		Error error = ErrorMetadataExtensions.StorageUnavailable(ResourcePath, 45);

		error.Type.Should().Be(ErrorType.Unexpected);
		error.Code.Should().Be("Document.StorageUnavailable");
		error.Description.Should().Contain(ResourcePath);
		error.Metadata.Should().NotBeNull();
		error.Metadata!["RetryAfter"].Should().Be(45);
		error.Metadata["AffectedResource"].Should().Be(ResourcePath);
	}

	[Fact]
	public void StorageUnavailable_DefaultRetryIs30()
	{
		Error error = ErrorMetadataExtensions.StorageUnavailable(ResourcePath);

		error.Metadata!["RetryAfter"].Should().Be(30);
	}

	[Fact]
	public void DocumentLocked_BuildsConflictErrorWithLockMetadata()
	{
		Guid id = Guid.CreateVersion7();
		DateTimeOffset until = DateTimeOffset.UnixEpoch.AddYears(60);

		Error error = ErrorMetadataExtensions.DocumentLocked(id, "alice", until);

		error.Type.Should().Be(ErrorType.Conflict);
		error.Code.Should().Be("Document.Locked");
		error.Metadata.Should().NotBeNull();
		error.Metadata!["DocumentId"].Should().Be(id);
		error.Metadata["LockedBy"].Should().Be("alice");
		error.Metadata["LockedUntil"].Should().Be(until);
		error.Metadata["CurrentState"].Should().Be("Locked");
	}

	[Fact]
	public void InvalidField_WithAttemptedValue_IncludesItInMetadata()
	{
		Error error = ErrorMetadataExtensions.InvalidField("Email", "bad format", "not-an-email");

		error.Type.Should().Be(ErrorType.Validation);
		error.Code.Should().Be("Validation.Email");
		error.Metadata.Should().NotBeNull();
		error.Metadata!["Field"].Should().Be("Email");
		error.Metadata["Reason"].Should().Be("bad format");
		error.Metadata["AttemptedValue"].Should().Be("not-an-email");
	}

	[Fact]
	public void InvalidField_WithoutAttemptedValue_OmitsThatKey()
	{
		Error error = ErrorMetadataExtensions.InvalidField("Email", "bad format");

		error.Metadata.Should().NotBeNull();
		error.Metadata!.Should().NotContainKey("AttemptedValue");
	}

	[Fact]
	public void DocumentNotFound_WithSuggestion_IncludesIt()
	{
		Guid id = Guid.CreateVersion7();

		Error error = ErrorMetadataExtensions.DocumentNotFound(id, "use newer id");

		error.Type.Should().Be(ErrorType.NotFound);
		error.Code.Should().Be("Document.NotFound");
		error.Metadata.Should().NotBeNull();
		error.Metadata!["DocumentId"].Should().Be(id);
		error.Metadata.Should().ContainKey("SearchedAt");
		error.Metadata["Suggestion"].Should().Be("use newer id");
	}

	[Fact]
	public void DocumentNotFound_WithoutSuggestion_OmitsThatKey()
	{
		Error error = ErrorMetadataExtensions.DocumentNotFound(Guid.CreateVersion7());

		error.Metadata.Should().NotBeNull();
		error.Metadata!.Should().NotContainKey("Suggestion");
		error.Metadata.Should().ContainKey("SearchedAt");
	}

	[Fact]
	public void CreateFromError_MetadataFromExtensionHelper_PropagatesCorrectly()
	{
		Error error = ErrorMetadataExtensions.StorageUnavailable(ResourcePath, 90);

		ProblemDetails problem = RichProblemDetailsFactory.CreateFromError(error);

		problem.Status.Should().Be(StatusCodes.Status503ServiceUnavailable);
		problem.Extensions.Should().ContainKey("retryAfter").WhoseValue.Should().Be(90);
		problem.Extensions.Should().ContainKey("affectedResource").WhoseValue.Should().Be(ResourcePath);
	}
}
