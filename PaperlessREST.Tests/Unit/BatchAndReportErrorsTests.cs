namespace PaperlessREST.Tests.Unit;

/// <summary>
///     Unit tests for the static error factories in <see cref="ReportErrors" />.
///     Tiny shape-only tests: each factory is one expression producing an <see cref="Error" />
///     with a well-known code, type, and description.
/// </summary>
public sealed class BatchAndReportErrorsTests
{
	[Fact]
	public void ReportErrors_FileNotFound_ReturnsNotFoundWithPath()
	{
		Error e = ReportErrors.FileNotFound("/tmp/missing.xml");
		e.Type.Should().Be(ErrorType.NotFound);
		e.Code.Should().Be("Report.FileNotFound");
		e.Description.Should().Contain("/tmp/missing.xml");
	}

	[Fact]
	public void ReportErrors_InvalidSchema_ReturnsValidationWithDetails()
	{
		Error e = ReportErrors.InvalidSchema("schema mismatch");
		e.Type.Should().Be(ErrorType.Validation);
		e.Code.Should().Be("Report.InvalidSchema");
		e.Description.Should().Contain("schema mismatch");
	}

	[Fact]
	public void ReportErrors_InvalidDate_ReturnsValidationWithRawValue()
	{
		Error e = ReportErrors.InvalidDate("01/15/2024");
		e.Type.Should().Be(ErrorType.Validation);
		e.Code.Should().Be("Report.InvalidDate");
		e.Description.Should().Contain("01/15/2024");
		e.Description.Should().Contain("yyyy-MM-dd");
	}

	[Fact]
	public void ReportErrors_InvalidGuid_ReturnsValidationWithIndex()
	{
		Error e = ReportErrors.InvalidGuid(7);
		e.Type.Should().Be(ErrorType.Validation);
		e.Code.Should().Be("Report.InvalidGuid");
		e.Description.Should().Contain("index 7");
	}
}
