namespace PaperlessServices.Tests.Integration;

[Collection(SharedContainerCollection.Name)]
public class StorageIntegrationTests(SharedContainerFixture fixture)
{
	private IStorageService Storage => fixture.Services.GetRequiredService<IStorageService>();

	[Fact]
	public async Task UploadAndDownload_RoundTripSuccess()
	{
		// Arrange
		string storagePath = await fixture.UploadPdfAsync("Storage round trip test");

		// Act
		await using Stream stream = await Storage.DownloadAsync(storagePath, TestContext.Current.CancellationToken);

		// Assert
		stream.Should().NotBeNull();
		stream.Length.Should().BePositive();
	}

	[Fact]
	public async Task DownloadNonExistent_ThrowsException()
	{
		// Arrange
		string missingPath = $"missing/{Guid.NewGuid()}.pdf";

		// Act & Assert
		await FluentActions
			.Invoking(() => Storage.DownloadAsync(missingPath, TestContext.Current.CancellationToken))
			.Should().ThrowAsync<Exception>();
	}
}
