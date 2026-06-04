namespace PaperlessREST.Tests.Integration;

public sealed class DocumentEndpointTests : IClassFixture<SharedRestContainerFixture>, IAsyncLifetime
{
	#region Constructor

	public DocumentEndpointTests(SharedRestContainerFixture fixture)
	{
		_fixture = fixture;
		_cleanup = new AsyncCleanup(async () =>
		{
			if (_createdDocIds.Count == 0) return;
			await using var scope = _fixture.CreateAsyncScope();
			var factory =
				scope.ServiceProvider.GetRequiredService<IDbContextFactory<DocumentPersistence>>();
			await using var db = await factory.CreateDbContextAsync();
			await db.Documents.Where(d => _createdDocIds.Contains(d.Id)).ExecuteDeleteAsync();
		});
	}

	#endregion

	#region Tests - GetDocuments

	[Fact]
	public async Task Get_ReturnsOkWithDocuments()
	{
		// Arrange & Act — explicit pageSize avoids any [AsParameters]
		// default-binding edge cases on the cursor-paginated endpoint.
		var response = await _fixture.Client.GetAsync(
			$"{DocumentsEndpoint}?pageSize=20",
			TestContext.Current.CancellationToken);

		// Assert — capture body on failure so future regressions show the
		// actual problem-details payload, not a JSON-parser error.
		if (!response.IsSuccessStatusCode)
		{
			var body = await response.Content.ReadAsStringAsync(
				TestContext.Current.CancellationToken);
			throw new InvalidOperationException(
				$"GET {DocumentsEndpoint}?pageSize=20 returned {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
		}

		var page = await response.Content.ReadFromJsonAsync<PaginatedDocumentsResponse>(
			TestContext.Current.CancellationToken);
		page.Should().NotBeNull();
		page!.Items.Should().NotBeNull();
	}

	#endregion

	#region Tests - Upload

	[Fact]
	public async Task Upload_ValidPdf_Returns202WithLocation()
	{
		// Arrange
		var uniqueFileName = $"{TestFilePrefix}-upload-{Guid.NewGuid():N}.pdf";
		using var content = await CreatePdfUploadAsync(uniqueFileName);

		// Act
		var response = await _fixture.Client.PostAsync(
			DocumentsEndpoint,
			content,
			TestContext.Current.CancellationToken);

		// Assert
		response.StatusCode.Should().Be(HttpStatusCode.Accepted);

		var result = await response.Content.ReadFromJsonAsync<CreateDocumentResponse>(
			TestContext.Current.CancellationToken);

		result!.Id.Should().NotBeEmpty();
		response.Headers.Location?.ToString().Should().Contain(result.Id.ToString());

		_createdDocIds.Add(result.Id);
	}

	#endregion

	#region Tests - GetById

	[Fact]
	public async Task GetById_ExistingDocument_ReturnsDocument()
	{
		// Arrange
		var docId = await SeedDocumentAsync($"{TestFilePrefix}-get-{Guid.NewGuid():N}.pdf");

		// Act
		var response = await _fixture.Client.GetAsync(
			$"{DocumentsEndpoint}/{docId}",
			TestContext.Current.CancellationToken);

		// Assert
		var doc = await response.Content.ReadFromJsonAsync<DocumentDto>(
			TestContext.Current.CancellationToken);
		doc!.Id.Should().Be(docId);
	}

	#endregion

	#region Tests - Delete

	[Fact]
	public async Task Delete_ExistingDocument_Returns204()
	{
		// Arrange
		var docId = await SeedDocumentAsync($"{TestFilePrefix}-delete-{Guid.NewGuid():N}.pdf");

		// Act
		var response = await _fixture.Client.DeleteAsync(
			$"{DocumentsEndpoint}/{docId}",
			TestContext.Current.CancellationToken);

		// Assert
		response.StatusCode.Should().Be(HttpStatusCode.NoContent);

		// Remove from cleanup list since it's already deleted
		_createdDocIds.Remove(docId);
	}

	#endregion

	#region Constants

	private const string DocumentsEndpoint = "/api/v1/documents";
	private const string ContentTypePdf = "application/pdf";
	private const string TestFilePrefix = "endpoint-test";

	#endregion

	#region Fields

	private readonly SharedRestContainerFixture _fixture;
	private readonly List<Guid> _createdDocIds = [];
	private readonly AsyncCleanup _cleanup;

	#endregion

	#region IAsyncLifetime

	public ValueTask InitializeAsync() => ValueTask.CompletedTask;

	public ValueTask DisposeAsync() => _cleanup.DisposeAsync();

	#endregion

	#region Helper Methods

	private async Task<Guid> SeedDocumentAsync(string fileName)
	{
		await using var scope = _fixture.CreateAsyncScope();
		var factory =
			scope.ServiceProvider.GetRequiredService<IDbContextFactory<DocumentPersistence>>();
		await using var db = await factory.CreateDbContextAsync(
			TestContext.Current.CancellationToken);

		var entity = new DocumentBuilder()
			.WithFileName(fileName)
			.BuildEntity();

		db.Documents.Add(entity);
		await db.SaveChangesAsync(TestContext.Current.CancellationToken);
		_createdDocIds.Add(entity.Id);
		return entity.Id;
	}

	[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
		Justification = "The ByteArrayContent's ownership transfers to the returned MultipartFormDataContent, "
		                + "which every caller disposes via 'using var content = ...'.")]
	private static async Task<MultipartFormDataContent> CreatePdfUploadAsync(string fileName)
	{
		var pdf = new ByteArrayContent(await TestPdf.BytesAsync("Test Document"))
		{
			Headers = { ContentType = MediaTypeHeaderValue.Parse(ContentTypePdf) }
		};
		return new MultipartFormDataContent { { pdf, "file", fileName } };
	}

	#endregion
}
