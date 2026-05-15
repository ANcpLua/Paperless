using PaperlessREST.Features.BatchProcessing.Application;

namespace PaperlessREST.Tests.Unit;

/// <summary>
///     Unit tests for the static error factories in <see cref="ReportErrors" /> and
///     <see cref="BatchErrors" />. These are tiny shape-only tests: each factory is one expression
///     producing an <see cref="Error" /> with a well-known code, type, and description. Coverage
///     comes from invoking each factory once and verifying the code/type contract.
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
	public void ReportErrors_InvalidXml_ReturnsValidationWithDetails()
	{
		Error e = ReportErrors.InvalidXml("unclosed tag");
		e.Type.Should().Be(ErrorType.Validation);
		e.Code.Should().Be("Report.InvalidXml");
		e.Description.Should().Contain("unclosed tag");
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

	[Fact]
	public void BatchErrors_PathRequired_FormatsPropertyAndSection()
	{
		Error e = BatchErrors.PathRequired("InputPath");
		e.Type.Should().Be(ErrorType.Validation);
		e.Code.Should().Be("Batch.PathRequired");
		e.Description.Should().Contain("InputPath");
		e.Description.Should().Contain(BatchOptions.SectionName);
	}

	[Fact]
	public void BatchErrors_InvalidPath_IncludesPropertyAndDetails()
	{
		Error e = BatchErrors.InvalidPath("ArchivePath", "not absolute");
		e.Type.Should().Be(ErrorType.Validation);
		e.Code.Should().Be("Batch.InvalidPath");
		e.Description.Should().Contain("ArchivePath");
		e.Description.Should().Contain("not absolute");
	}

	[Fact]
	public void BatchErrors_PathsNotDistinct_DescribesTheThreeAffectedFields()
	{
		Error e = BatchErrors.PathsNotDistinct();
		e.Type.Should().Be(ErrorType.Validation);
		e.Code.Should().Be("Batch.PathsNotDistinct");
		e.Description.Should().Contain("InputPath");
		e.Description.Should().Contain("ArchivePath");
		e.Description.Should().Contain("ErrorPath");
	}

	[Fact]
	public void BatchErrors_InvalidTimeZone_QuotesOfferingValue()
	{
		Error e = BatchErrors.InvalidTimeZone("Mars/Olympus");
		e.Type.Should().Be(ErrorType.Validation);
		e.Code.Should().Be("Batch.InvalidTimeZone");
		e.Description.Should().Contain("Mars/Olympus");
	}
}
