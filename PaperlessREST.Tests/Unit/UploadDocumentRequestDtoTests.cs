namespace PaperlessREST.Tests.Unit;

/// <summary>
///     Coverage for the <see cref="UploadDocumentRequest" /> record's compiler-generated
///     copy constructor (used by <c>with</c> expressions). The other members (File getter/setter)
///     are exercised by every test that uploads a document.
/// </summary>
public sealed class UploadDocumentRequestDtoTests
{
	[Fact]
	public void With_NewFile_ProducesNewInstanceWithCopiedThenOverriddenValue()
	{
		// Arrange — initial request via the production constructor
		Mock<IFormFile> originalFile = new();
		originalFile.Setup(f => f.FileName).Returns("original.pdf");
		UploadDocumentRequest original = new() { File = originalFile.Object };

		Mock<IFormFile> replacementFile = new();
		replacementFile.Setup(f => f.FileName).Returns("replacement.pdf");

		// Act — exercises the synthesized copy-ctor `(UploadDocumentRequest other)`
		UploadDocumentRequest copy = original with { File = replacementFile.Object };

		// Assert
		copy.Should().NotBeSameAs(original);
		copy.File.Should().BeSameAs(replacementFile.Object);
		copy.File.FileName.Should().Be("replacement.pdf");
		original.File.FileName.Should().Be("original.pdf");
	}

	[Fact]
	public void With_EmptyChange_CopiesAllValuesPreservingFile()
	{
		// Arrange
		Mock<IFormFile> file = new();
		file.Setup(f => f.FileName).Returns("doc.pdf");
		UploadDocumentRequest original = new() { File = file.Object };

		// Act — `with { }` still routes through the copy-ctor
		UploadDocumentRequest copy = original with { };

		// Assert — record value equality + copy-ctor preserves reference
		copy.Should().NotBeSameAs(original);
		copy.Should().Be(original);
		copy.File.Should().BeSameAs(file.Object);
	}
}
