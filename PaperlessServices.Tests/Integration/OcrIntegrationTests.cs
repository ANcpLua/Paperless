namespace PaperlessServices.Tests.Integration;

[Collection(SharedContainerCollection.Name)]
public class OcrIntegrationTests(SharedContainerFixture fixture)
{
	private IOcrProcessor OcrProcessor => fixture.Services.GetRequiredService<IOcrProcessor>();

	[Fact]
	public async Task ExtractsInvoiceNumber()
	{
		// Arrange
		string storagePath = await fixture.UploadPdfAsync("INV-2024-001");
		OcrCommand command = new(Guid.NewGuid(), "invoice.pdf", storagePath, TimeProvider.System.GetUtcNow().AddMinutes(-5));

		// Act
		ErrorOr<OcrEvent> errorOrResult =
			await OcrProcessor.ProcessDocumentAsync(command, TestContext.Current.CancellationToken);

		// Assert
		errorOrResult.IsError.Should().BeFalse();
		errorOrResult.Value.Should().Satisfy<OcrEvent>(result =>
		{
			result.Status.Should().Be("Completed");
			result.Text.Should().Contain("INV-2024-001");
		});
	}

	[Fact]
	public async Task ProcessMultipleDocuments_Concurrently()
	{
		// Arrange & Act
		IEnumerable<Task<ErrorOr<OcrEvent>>> tasks = Enumerable.Range(1, 3).Select(async i =>
		{
			string path = await fixture.UploadPdfAsync($"Document {i} content. Amount: ${i * 1000:N2}");
			OcrCommand command = new(Guid.NewGuid(), $"doc-{i}.pdf", path, TimeProvider.System.GetUtcNow().AddMinutes(-5));
			return await OcrProcessor.ProcessDocumentAsync(command, TestContext.Current.CancellationToken);
		});

		ErrorOr<OcrEvent>[] errorOrResults = await Task.WhenAll(tasks);

		// Assert
		errorOrResults.Should().AllSatisfy(r =>
		{
			r.IsError.Should().BeFalse();
			r.Value.Status.Should().Be("Completed");
		});
	}
}
