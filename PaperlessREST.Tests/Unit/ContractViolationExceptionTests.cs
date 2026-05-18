using PaperlessREST.Host.Extensions;

namespace PaperlessREST.Tests.Unit;

/// <summary>
///     Unit tests for <see cref="ContractViolationException" /> and its diagnostics records.
/// </summary>
/// <remarks>
///     The record-equality tests at the bottom of this file
///     (<c>ContractViolationDiagnostics_RecordEquality_*</c>, <c>ErrorDetail_RecordEquality_*</c>)
///     intentionally exercise the C# compiler's synthesized record equality so that the
///     <see cref="ContractViolationDiagnostics" /> and <see cref="ErrorDetail" /> records'
///     array-typed members are documented as <em>reference-equal</em>,
///     not deep-equal. If someone "fixes" these records to deep-compare their array members
///     (e.g., by switching to <c>ImmutableArray&lt;Error&gt;</c> with value semantics or by
///     overriding Equals manually), these tests will break and force the change to be
///     deliberate. Without them, the footgun is silent and a future deep-compare attempt
///     might pass tests while changing observable behavior in production callers.
/// </remarks>
public sealed class ContractViolationExceptionTests
{
	private const string Op = "GetById";
	private const string Code = "Document.NotFound";
	private const string Desc = "Document 42 not found";

	private static Error MakeError(ErrorType type, string code = Code, string description = Desc,
		Dictionary<string, object>? metadata = null) =>
		type switch
		{
			ErrorType.NotFound => Error.NotFound(code, description, metadata),
			ErrorType.Validation => Error.Validation(code, description, metadata),
			ErrorType.Conflict => Error.Conflict(code, description, metadata),
			ErrorType.Failure => Error.Failure(code, description, metadata),
			ErrorType.Unexpected => Error.Unexpected(code, description, metadata),
			ErrorType.Unauthorized => Error.Unauthorized(code, description, metadata),
			ErrorType.Forbidden => Error.Forbidden(code, description, metadata),
			_ => Error.Custom((int)type, code, description, metadata)
		};

	[Fact]
	public void ForNotFoundOnly_PopulatesExpectedTypesAndMessage()
	{
		Error err = MakeError(ErrorType.NotFound);
		ContractViolationException ex = ContractViolationException.ForNotFoundOnly(err, [err], Op);

		ex.EndpointOperation.Should().Be(Op);
		ex.ExpectedErrorTypes.Should().ContainSingle().Which.Should().Be(ErrorType.NotFound);
		ex.ActualError.Should().Be(err);
		ex.AllErrors.Should().ContainSingle().Which.Should().Be(err);
		ex.Message.Should().Contain("Contract violation in GetById")
			.And.Contain("Expected [NotFound]")
			.And.Contain($"Error: {Code} - {Desc}");
		ex.Message.Should().NotContain("more error(s)");
	}

	[Fact]
	public void ForValidationOnly_PopulatesExpectedTypes()
	{
		Error err = MakeError(ErrorType.Validation, "Validation.PageSize", "PageSize is required");
		ContractViolationException ex = ContractViolationException.ForValidationOnly(err, [err], Op);

		ex.ExpectedErrorTypes.Should().ContainSingle().Which.Should().Be(ErrorType.Validation);
		ex.Message.Should().Contain("Expected [Validation]");
	}

	[Fact]
	public void ForNotFoundOrConflict_ListsBothTypes()
	{
		Error err = MakeError(ErrorType.NotFound);
		ContractViolationException ex = ContractViolationException.ForNotFoundOrConflict(err, [err], Op);

		ex.ExpectedErrorTypes.Should().BeEquivalentTo(new[] { ErrorType.NotFound, ErrorType.Conflict },
			opts => opts.WithStrictOrdering());
		ex.Message.Should().Contain("Expected [NotFound, Conflict]");
	}

	[Fact]
	public void ForCrudOperation_ListsThreeTypes()
	{
		Error err = MakeError(ErrorType.NotFound);
		ContractViolationException ex = ContractViolationException.ForCrudOperation(err, [err], Op);

		ex.ExpectedErrorTypes.Should().BeEquivalentTo(
			new[] { ErrorType.Validation, ErrorType.NotFound, ErrorType.Conflict },
			opts => opts.WithStrictOrdering());
		ex.Message.Should().Contain("Expected [Validation, NotFound, Conflict]");
	}

	[Fact]
	public void For_WithCustomTypes_RoundTripsParamsArray()
	{
		Error err = MakeError(ErrorType.Failure, "Document.StorageFailed", "Storage failed");
		ContractViolationException ex = ContractViolationException.For(
			err, [err], Op, ErrorType.Failure, ErrorType.Unexpected);

		ex.ExpectedErrorTypes.Should().BeEquivalentTo(
			new[] { ErrorType.Failure, ErrorType.Unexpected }, opts => opts.WithStrictOrdering());
		ex.Message.Should().Contain("Expected [Failure, Unexpected]");
	}

	[Fact]
	public void BuildMessage_SingleError_OmitsMoreErrorsSuffix()
	{
		Error err = MakeError(ErrorType.NotFound);
		ContractViolationException ex = ContractViolationException.ForNotFoundOnly(err, [err], Op);

		ex.Message.Should().NotContain("more error(s)");
	}

	[Fact]
	public void BuildMessage_ThreeErrors_AppendsExactSuffix()
	{
		Error first = MakeError(ErrorType.Validation, "Validation.A", "A");
		Error second = MakeError(ErrorType.Validation, "Validation.B", "B");
		Error third = MakeError(ErrorType.Validation, "Validation.C", "C");

		ContractViolationException ex = ContractViolationException.ForValidationOnly(
			first, [first, second, third], Op);

		ex.Message.Should().EndWith("(+ 2 more error(s))");
	}

	[Fact]
	public void GetDiagnostics_AllErrorsMatchInputOrder()
	{
		Error first = MakeError(ErrorType.Validation, "Validation.A", "A");
		Error second = MakeError(ErrorType.Validation, "Validation.B", "B");
		ContractViolationException ex = ContractViolationException.ForValidationOnly(
			first, [first, second], Op);

		ContractViolationDiagnostics diag = ex.GetDiagnostics();

		diag.Operation.Should().Be(Op);
		diag.ExpectedErrorTypes.Should().Equal("Validation");
		diag.ActualErrorType.Should().Be("Validation");
		diag.ErrorCode.Should().Be("Validation.A");
		diag.ErrorDescription.Should().Be("A");
		diag.AllErrors.Should().HaveCount(2);
		diag.AllErrors[0].Should().Be(new ErrorDetail("Validation", "Validation.A", "A"));
		diag.AllErrors[1].Should().Be(new ErrorDetail("Validation", "Validation.B", "B"));
	}

	[Fact]
	public void GetDiagnostics_WithMetadata_PopulatesMetadataDictionary()
	{
		Dictionary<string, object> meta = new() { ["RetryAfter"] = 30, ["AffectedResource"] = "x.pdf" };
		Error err = MakeError(ErrorType.Unexpected, "Document.StorageUnavailable", "tmp", meta);
		ContractViolationException ex = ContractViolationException.For(err, [err], Op, ErrorType.Unexpected);

		ContractViolationDiagnostics diag = ex.GetDiagnostics();

		diag.Metadata.Should().NotBeNull();
		diag.Metadata!["RetryAfter"].Should().Be(30);
		diag.Metadata["AffectedResource"].Should().Be("x.pdf");
	}

	[Fact]
	public void GetDiagnostics_WithNullMetadata_LeavesMetadataNull()
	{
		Error err = MakeError(ErrorType.NotFound);
		ContractViolationException ex = ContractViolationException.ForNotFoundOnly(err, [err], Op);

		ContractViolationDiagnostics diag = ex.GetDiagnostics();

		diag.Metadata.Should().BeNull();
	}

	[Fact]
	public void ContractViolationDiagnostics_RecordEquality_HoldsWhenReferenceArraysAreShared()
	{
		// Records compare arrays by reference (no value semantics on T[]).
		// Share the same array instances so equality holds.
		string[] expectedTypes = ["NotFound"];
		ErrorDetail[] errs = [new("NotFound", "X", "Y")];
		ContractViolationDiagnostics left = new("Op", expectedTypes, "NotFound", "X", "Y", errs, null);
		ContractViolationDiagnostics right = new("Op", expectedTypes, "NotFound", "X", "Y", errs, null);

		left.Should().Be(right);
		(left == right).Should().BeTrue();
		left.GetHashCode().Should().Be(right.GetHashCode());
	}

	[Fact]
	public void ContractViolationDiagnostics_RecordEquality_FailsWhenArraysAreDifferentInstances()
	{
		// Counterpart to the shared-reference case: documents that the synthesized
		// equality uses reference equality on T[] members.
		ContractViolationDiagnostics left = new(
			"Op", ["NotFound"], "NotFound", "X", "Y", [new ErrorDetail("NotFound", "X", "Y")], null);
		ContractViolationDiagnostics right = new(
			"Op", ["NotFound"], "NotFound", "X", "Y", [new ErrorDetail("NotFound", "X", "Y")], null);

		left.Should().NotBe(right);
	}

	[Fact]
	public void ContractViolationDiagnostics_WithExpression_ReturnsNewInstanceWithUpdatedProperty()
	{
		ContractViolationDiagnostics original = new(
			"Op", ["NotFound"], "NotFound", "X", "Y", [new ErrorDetail("NotFound", "X", "Y")], null);
		ContractViolationDiagnostics mutated = original with { Operation = "Other" };

		mutated.Operation.Should().Be("Other");
		mutated.Should().NotBe(original);
		mutated.ErrorCode.Should().Be(original.ErrorCode);
	}

	[Fact]
	public void ErrorDetail_RecordEquality_HoldsForSameValues()
	{
		ErrorDetail left = new("NotFound", "X", "Y");
		ErrorDetail right = new("NotFound", "X", "Y");

		left.Should().Be(right);
		(left == right).Should().BeTrue();
		left.GetHashCode().Should().Be(right.GetHashCode());
	}

	[Fact]
	public void ErrorDetail_WithExpression_ReturnsNewInstance()
	{
		ErrorDetail original = new("NotFound", "X", "Y");
		ErrorDetail mutated = original with { Code = "Z" };

		mutated.Code.Should().Be("Z");
		mutated.Should().NotBe(original);
		mutated.Type.Should().Be(original.Type);
	}

	[Fact]
	public void ForNotFoundOnly_DefaultsOperationToCallerMemberName()
	{
		Error err = MakeError(ErrorType.NotFound);
		ContractViolationException ex = ContractViolationException.ForNotFoundOnly(err, [err]);

		ex.EndpointOperation.Should().Be(nameof(ForNotFoundOnly_DefaultsOperationToCallerMemberName));
	}
}
