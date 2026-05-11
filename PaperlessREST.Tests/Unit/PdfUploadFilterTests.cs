using PaperlessREST.Features.DocumentManagement.Presentation.Filters;

namespace PaperlessREST.Tests.Unit;

public sealed class PdfUploadFilterTests
{
	private const long ValidFileSize = FileUploadConstraints.MaxFileSizeBytes - 1;
	private const long OversizedFileSize = FileUploadConstraints.MaxFileSizeBytes + 1;

	private static IFormFile CreateMockFile(long length, string contentType = "application/pdf")
	{
		Mock<IFormFile> mock = new();
		mock.Setup(f => f.Length).Returns(length);
		mock.Setup(f => f.ContentType).Returns(contentType);
		mock.Setup(f => f.FileName).Returns("test.pdf");
		return mock.Object;
	}

	private static async Task<object?> InvokeFilter(IFormFile? file)
	{
		PdfUploadFilter filter = new();

		// Create mock context with the file as argument
		List<object?> arguments = file is not null ? [file] : [];

		Mock<EndpointFilterInvocationContext> contextMock = new();
		contextMock.Setup(c => c.Arguments).Returns(arguments);

		return await filter.InvokeAsync(contextMock.Object, Next);

		ValueTask<object?> Next(EndpointFilterInvocationContext _) => ValueTask.FromResult<object?>(Results.Ok());
	}

	private static async Task<object?> InvokeFilterWithRequest(UploadDocumentRequest? request)
	{
		PdfUploadFilter filter = new();

		List<object?> arguments = request is not null ? [request] : [];

		Mock<EndpointFilterInvocationContext> contextMock = new();
		contextMock.Setup(c => c.Arguments).Returns(arguments);

		return await filter.InvokeAsync(contextMock.Object, Next);

		ValueTask<object?> Next(EndpointFilterInvocationContext _) => ValueTask.FromResult<object?>(Results.Ok());
	}

	[Fact]
	public async Task ValidPdf_PassesThrough()
	{
		IFormFile file = CreateMockFile(ValidFileSize, "application/pdf");

		object? result = await InvokeFilter(file);

		result.Should().BeOfType<Ok>();
	}

	[Fact]
	public async Task ValidPdfInRequest_PassesThrough()
	{
		IFormFile file = CreateMockFile(ValidFileSize, "application/pdf");
		UploadDocumentRequest request = new() { File = file };

		object? result = await InvokeFilterWithRequest(request);

		result.Should().BeOfType<Ok>();
	}

	[Fact]
	public async Task NullFile_ReturnsValidationProblem()
	{
		object? result = await InvokeFilter(null);

		result.Should().BeOfType<ValidationProblem>()
			.Which.StatusCode.Should().Be(400);
	}

	[Fact]
	public async Task OversizedFile_ReturnsValidationProblem()
	{
		IFormFile file = CreateMockFile(OversizedFileSize, "application/pdf");

		object? result = await InvokeFilter(file);

		result.Should().BeOfType<ValidationProblem>()
			.Which.StatusCode.Should().Be(400);
	}

	[Fact]
	public async Task WrongContentType_ReturnsValidationProblem()
	{
		IFormFile file = CreateMockFile(ValidFileSize, "image/png");

		object? result = await InvokeFilter(file);

		result.Should().BeOfType<ValidationProblem>()
			.Which.StatusCode.Should().Be(400);
	}

	[Fact]
	public async Task ContentTypeWithParameters_ExtractsBaseType()
	{
		IFormFile file = CreateMockFile(ValidFileSize, "application/pdf; charset=binary");

		object? result = await InvokeFilter(file);

		result.Should().BeOfType<Ok>();
	}

	[Fact]
	public async Task ContentTypeCaseInsensitive_PassesThrough()
	{
		IFormFile file = CreateMockFile(ValidFileSize, "APPLICATION/PDF");

		object? result = await InvokeFilter(file);

		result.Should().BeOfType<Ok>();
	}

	[Fact]
	public async Task ExactMaxSize_PassesThrough()
	{
		IFormFile file = CreateMockFile(FileUploadConstraints.MaxFileSizeBytes, "application/pdf");

		object? result = await InvokeFilter(file);

		result.Should().BeOfType<Ok>();
	}
}
