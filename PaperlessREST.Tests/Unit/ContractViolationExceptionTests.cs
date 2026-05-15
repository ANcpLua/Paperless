using PaperlessREST.Host.Extensions;

namespace PaperlessREST.Tests.Unit;

/// <summary>
///     Unit tests for <see cref="ContractViolationException" /> and the related diagnostic records.
///     Covers each factory method, the <see cref="ContractViolationException.GetDiagnostics" /> projection,
///     and the single-vs-multiple-errors branches of the internal message builder.
/// </summary>
public sealed class ContractViolationExceptionTests
{
	private static readonly Error s_actualError = Error.Conflict("Document.Locked", "locked for edit");
	private static readonly Error s_secondError = Error.Validation("Document.BadField", "field x bad");

	[Fact]
	public void Constructor_OneError_BuildsMessageWithoutAggregateSuffix()
	{
		ContractViolationException ex = new(
			"GetDocumentById", [ErrorType.NotFound], s_actualError, [s_actualError]);

		ex.EndpointOperation.Should().Be("GetDocumentById");
		ex.ExpectedErrorTypes.Should().Equal(ErrorType.NotFound);
		ex.ActualError.Should().Be(s_actualError);
		ex.AllErrors.Should().HaveCount(1);
		ex.Message.Should().Contain("Contract violation in GetDocumentById");
		ex.Message.Should().Contain("Expected [NotFound]");
		ex.Message.Should().Contain("but received Conflict");
		ex.Message.Should().Contain("Document.Locked");
		ex.Message.Should().NotContain("more error(s)");
	}

	[Fact]
	public void Constructor_MultipleErrors_BuildsMessageWithAggregateSuffix()
	{
		ContractViolationException ex = new(
			"UpdateDocument",
			[ErrorType.Validation, ErrorType.NotFound],
			s_actualError,
			[s_actualError, s_secondError]);

		ex.AllErrors.Should().HaveCount(2);
		ex.Message.Should().Contain("Expected [Validation, NotFound]");
		ex.Message.Should().Contain("(+ 1 more error(s))");
	}

	[Fact]
	public void GetDiagnostics_ReturnsStructuredProjection()
	{
		Error withMetadata = Error.Custom(
			(int)ErrorType.Conflict, "Document.Locked", "locked",
			new Dictionary<string, object> { ["CurrentState"] = "Locked" });

		ContractViolationException ex = new(
			"PUT /documents/{id}",
			[ErrorType.NotFound, ErrorType.Conflict],
			withMetadata,
			[withMetadata, s_secondError]);

		ContractViolationDiagnostics diag = ex.GetDiagnostics();

		diag.Operation.Should().Be("PUT /documents/{id}");
		diag.ExpectedErrorTypes.Should().Equal("NotFound", "Conflict");
		diag.ActualErrorType.Should().Be("Conflict");
		diag.ErrorCode.Should().Be("Document.Locked");
		diag.ErrorDescription.Should().Be("locked");
		diag.AllErrors.Should().HaveCount(2);
		diag.AllErrors[0].Should().Be(new ErrorDetail("Conflict", "Document.Locked", "locked"));
		diag.AllErrors[1].Should().Be(new ErrorDetail("Validation", "Document.BadField", "field x bad"));
		diag.Metadata.Should().NotBeNull();
		diag.Metadata!["CurrentState"].Should().Be("Locked");
	}

	[Fact]
	public void ForNotFoundOnly_BuildsWithCallerNameAndNotFoundExpectation()
	{
		ContractViolationException ex = ContractViolationException.ForNotFoundOnly(
			s_actualError, [s_actualError], "GetById");

		ex.EndpointOperation.Should().Be("GetById");
		ex.ExpectedErrorTypes.Should().Equal(ErrorType.NotFound);
	}

	[Fact]
	public void ForNotFoundOnly_DefaultCallerName_UsesCallingMember()
	{
		ContractViolationException ex = ForNotFoundOnly_DefaultCallerName_UsesCallingMember_Helper();

		ex.EndpointOperation.Should().Be(nameof(ForNotFoundOnly_DefaultCallerName_UsesCallingMember_Helper));
	}

	private static ContractViolationException ForNotFoundOnly_DefaultCallerName_UsesCallingMember_Helper() =>
		ContractViolationException.ForNotFoundOnly(s_actualError, [s_actualError]);

	[Fact]
	public void ForValidationOnly_BuildsWithValidationExpectation()
	{
		ContractViolationException ex = ContractViolationException.ForValidationOnly(
			s_actualError, [s_actualError], "Validate");

		ex.ExpectedErrorTypes.Should().Equal(ErrorType.Validation);
	}

	[Fact]
	public void ForNotFoundOrConflict_BuildsWithBothTypes()
	{
		ContractViolationException ex = ContractViolationException.ForNotFoundOrConflict(
			s_actualError, [s_actualError], "UpdateOrCreate");

		ex.ExpectedErrorTypes.Should().Equal(ErrorType.NotFound, ErrorType.Conflict);
	}

	[Fact]
	public void ForCrudOperation_BuildsWithValidationNotFoundConflict()
	{
		ContractViolationException ex = ContractViolationException.ForCrudOperation(
			s_actualError, [s_actualError], "Crud");

		ex.ExpectedErrorTypes.Should()
			.Equal(ErrorType.Validation, ErrorType.NotFound, ErrorType.Conflict);
	}

	[Fact]
	public void For_BuildsWithCustomExpectedTypes()
	{
		ContractViolationException ex = ContractViolationException.For(
			s_actualError, [s_actualError], "PostThing", ErrorType.Failure, ErrorType.Unexpected);

		ex.ExpectedErrorTypes.Should().Equal(ErrorType.Failure, ErrorType.Unexpected);
		ex.EndpointOperation.Should().Be("PostThing");
	}

	[Fact]
	public void Exception_IsInvalidOperationException()
	{
		ContractViolationException ex = ContractViolationException.ForNotFoundOnly(
			s_actualError, [s_actualError], "GetById");

		ex.Should().BeAssignableTo<InvalidOperationException>();
	}
}
