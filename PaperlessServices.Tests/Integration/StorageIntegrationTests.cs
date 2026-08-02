namespace PaperlessServices.Tests.Integration;

[Collection(SharedContainerCollection.Name)]
public class StorageIntegrationTests(SharedContainerFixture fixture)
{
	private IStorageService Storage => fixture.Services.GetRequiredService<IStorageService>();

	[Fact]
	public async Task UploadAndDownload_PreservesExactPdfBytesAndStartsAtBeginning()
	{
		byte[] expected = await TestPdf.BytesAsync("Storage round trip test");
		string storagePath = await fixture.UploadPdfAsync(expected);

		await using Stream stream = await Storage.DownloadAsync(storagePath, TestContext.Current.CancellationToken);
		stream.Position.Should().Be(0);
		await using MemoryStream downloaded = new();
		await stream.CopyToAsync(downloaded, TestContext.Current.CancellationToken);

		downloaded.ToArray().Should().Equal(expected);
	}

	[Fact]
	public async Task DownloadNonExistent_ThrowsException()
	{
		// Arrange
		var missingPath = $"missing/{Guid.NewGuid()}.pdf";

		// Act & Assert
		await FluentActions
			.Invoking(() => Storage.DownloadAsync(missingPath, TestContext.Current.CancellationToken))
			.Should().ThrowAsync<Exception>();
	}
}
