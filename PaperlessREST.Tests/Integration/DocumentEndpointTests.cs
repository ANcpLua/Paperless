using Elastic.Clients.Elasticsearch;
using AwesomeAssertions.Execution;

namespace PaperlessREST.Tests.Integration;

[Collection(SharedRestContainerCollection.Name)]
public sealed class DocumentEndpointTests : IAsyncLifetime
{
	#region Constructor

	public DocumentEndpointTests(SharedRestContainerFixture fixture)
	{
		_fixture = fixture;
		_elastic = fixture.Services.GetRequiredService<ElasticsearchClient>();
		_search = fixture.Services.GetRequiredService<IDocumentSearchService>();
		_cleanup = new AsyncCleanup(async () =>
		{
			if (_createdDocIds.Count > 0)
			{
				await using var scope = _fixture.CreateAsyncScope();
				var factory =
					scope.ServiceProvider.GetRequiredService<IDbContextFactory<DocumentPersistence>>();
				await using var db = await factory.CreateDbContextAsync();
				await db.Documents.Where(d => _createdDocIds.Contains(d.Id)).ExecuteDeleteAsync();
			}

			foreach (var id in _indexedDocIds)
			{
				await _search.DeleteAsync(id);
			}
		});
	}

	#endregion

	#region Tests - GetDocuments

	[Fact]
	public async Task Get_WithoutPageSize_UsesDefaultAndCursorReturnsRemainder()
	{
		var documentIds = await SeedDocumentsAsync(
			PaginationConstraints.DefaultPageSize + 1,
			$"{TestFilePrefix}-page-{Guid.NewGuid():N}");

		using var firstResponse = await _fixture.Client.GetAsync(
			DocumentsEndpoint,
			TestContext.Current.CancellationToken);
		var firstPage = await ReadSuccessJsonAsync<PaginatedDocumentsResponse>(firstResponse);

		using AssertionScope _ = new();
		firstPage.Items.Should().HaveCount(PaginationConstraints.DefaultPageSize);
		firstPage.HasMore.Should().BeTrue();
		firstPage.NextCursor.Should().Be(firstPage.Items[^1].Id);

		using var secondResponse = await _fixture.Client.GetAsync(
			$"{DocumentsEndpoint}?cursor={firstPage.NextCursor}",
			TestContext.Current.CancellationToken);
		var secondPage = await ReadSuccessJsonAsync<PaginatedDocumentsResponse>(secondResponse);

		secondPage.Items.Should().ContainSingle();
		secondPage.HasMore.Should().BeFalse();
		secondPage.NextCursor.Should().BeNull();
		firstPage.Items.Select(d => d.Id)
			.Concat(secondPage.Items.Select(d => d.Id))
			.Should().BeEquivalentTo(documentIds);
	}

	#endregion

	#region Tests - Search endpoint

	[Fact]
	public async Task Search_WithoutLimit_UsesDefaultAndMapsIndexedDocuments()
	{
		var marker = $"invoice{Guid.NewGuid():N}";
		List<DocumentSearchResult> indexed = [];
		for (var i = 0; i <= SearchConstraints.DefaultResultLimit; i++)
		{
			indexed.Add(await IndexSearchDocumentAsync(
				$"{TestFilePrefix}-search-{i}.pdf",
				$"{marker} account statement {i}",
				$"Summary {i}"));
		}

		using var response = await _fixture.Client.GetAsync(
			$"{DocumentsEndpoint}/search?query={marker}",
			TestContext.Current.CancellationToken);
		var results = await ReadSuccessJsonAsync<List<DocumentSearchResultDto>>(response);

		results.Should().HaveCount(SearchConstraints.DefaultResultLimit);
		foreach (var result in results)
		{
			var expected = indexed.Single(document => document.Id == result.Id);
			result.FileName.Should().Be(expected.FileName);
			result.Content.Should().Be(expected.Content);
			result.Summary.Should().Be(expected.Summary);
			result.CreatedAt.Should().Be(expected.CreatedAt);
			result.Status.Should().Be(expected.Status);
		}
	}

	public static IEnumerable<TheoryDataRow<string>> InvalidSearchRequests()
	{
		yield return new TheoryDataRow<string>($"{DocumentsEndpoint}/search")
			.WithTestDisplayName("missing query");
		yield return new TheoryDataRow<string>($"{DocumentsEndpoint}/search?query=")
			.WithTestDisplayName("empty query");
		yield return new TheoryDataRow<string>(
			$"{DocumentsEndpoint}/search?query={new string('q', SearchConstraints.QueryMaxLength + 1)}")
			.WithTestDisplayName("query above maximum");
		yield return new TheoryDataRow<string>($"{DocumentsEndpoint}/search?query=invoice&limit=0")
			.WithTestDisplayName("zero limit");
		yield return new TheoryDataRow<string>(
			$"{DocumentsEndpoint}/search?query=invoice&limit={SearchConstraints.MaxResultLimit + 1}")
			.WithTestDisplayName("limit above maximum");
	}

	[Theory]
	[MemberData(nameof(InvalidSearchRequests))]
	public async Task Search_InvalidQueryOrLimit_ReturnsBadRequest(string requestUri)
	{
		using var response = await _fixture.Client.GetAsync(
			requestUri,
			TestContext.Current.CancellationToken);

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	#endregion

	#region Tests - Upload

	[Fact]
	public async Task Upload_ValidPdf_ReturnsAcceptedDocument()
	{
		var uniqueFileName = $"{TestFilePrefix}-upload-{Guid.NewGuid():N}.pdf";
		using var content = await CreatePdfUploadAsync(uniqueFileName);

		using var response = await _fixture.Client.PostAsync(
			DocumentsEndpoint,
			content,
			TestContext.Current.CancellationToken);

		response.StatusCode.Should().Be(HttpStatusCode.Accepted);
		var result = await response.Content.ReadFromJsonAsync<CreateDocumentResponse>(
			TestContext.Current.CancellationToken);

		result.Should().NotBeNull();
		result!.Id.Should().NotBeEmpty();
		result.FileName.Should().Be(uniqueFileName);
		result.Status.Should().Be(DocumentStatus.Pending.ToString());
		_createdDocIds.Add(result.Id);
	}

	[Fact]
	public async Task Upload_AboveMaximumSize_ReturnsBadRequest()
	{
		using var content = CreateUpload(
			$"{TestFilePrefix}-oversized.pdf",
			ContentTypePdf,
			new byte[FileUploadConstraints.MaxFileSizeBytes + 1]);

		using var response = await _fixture.Client.PostAsync(
			DocumentsEndpoint,
			content,
			TestContext.Current.CancellationToken);
		var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

		using AssertionScope _ = new();
		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		body.Should().Contain("File size cannot exceed 10 MB");
	}

	[Fact]
	public async Task Upload_NonPdfContentType_ReturnsBadRequest()
	{
		using var content = CreateUpload(
			$"{TestFilePrefix}-text.txt",
			"text/plain",
			[1, 2, 3]);

		using var response = await _fixture.Client.PostAsync(
			DocumentsEndpoint,
			content,
			TestContext.Current.CancellationToken);
		var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

		using AssertionScope _ = new();
		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		body.Should().Contain("Only PDF files are allowed");
	}

	#endregion

	#region Tests - GetById and summary

	[Fact]
	public async Task GetById_ExistingDocument_ReturnsDocument()
	{
		var docId = await SeedDocumentAsync($"{TestFilePrefix}-get-{Guid.NewGuid():N}.pdf");

		using var response = await _fixture.Client.GetAsync(
			$"{DocumentsEndpoint}/{docId}",
			TestContext.Current.CancellationToken);
		var doc = await ReadSuccessJsonAsync<DocumentDto>(response);

		doc.Id.Should().Be(docId);
	}

	[Fact]
	public async Task GetSummary_ExistingDocument_ReturnsSummary()
	{
		const string Summary = "A concise account statement summary.";
		var docId = await SeedDocumentAsync(
			$"{TestFilePrefix}-summary-{Guid.NewGuid():N}.pdf",
			Summary);

		using var response = await _fixture.Client.GetAsync(
			$"{DocumentsEndpoint}/{docId}/summary",
			TestContext.Current.CancellationToken);
		var result = await ReadSuccessJsonAsync<SummaryDto>(response);

		result.Summary.Should().Be(Summary);
	}

	#endregion

	#region Tests - Delete endpoint

	[Fact]
	public async Task Delete_ExistingDocument_Returns204()
	{
		var docId = await SeedDocumentAsync($"{TestFilePrefix}-delete-{Guid.NewGuid():N}.pdf");

		using var response = await _fixture.Client.DeleteAsync(
			$"{DocumentsEndpoint}/{docId}",
			TestContext.Current.CancellationToken);

		response.StatusCode.Should().Be(HttpStatusCode.NoContent);
		_createdDocIds.Remove(docId);
	}

	#endregion

	#region Tests - DocumentSearchService

	[Fact]
	public async Task SearchService_MatchingAndMissingQueries_ReturnExpectedDocuments()
	{
		var marker = $"receivable{Guid.NewGuid():N}";
		var expected = await IndexSearchDocumentAsync(
			$"{TestFilePrefix}-matching.pdf",
			$"Open {marker} balance");

		var matches = await _search.SearchAsync(
			marker,
			10,
			TestContext.Current.CancellationToken);
		var missing = await _search.SearchAsync(
			$"absent{Guid.NewGuid():N}",
			10,
			TestContext.Current.CancellationToken);

		matches.Should().ContainSingle().Which.Should().BeEquivalentTo(expected);
		missing.Should().BeEmpty();
	}

	[Fact]
	public async Task SearchService_LimitRestrictsReturnedDocuments()
	{
		var marker = $"limited{Guid.NewGuid():N}";
		List<Guid> indexedIds = [];
		for (var i = 0; i < 3; i++)
		{
			var document = await IndexSearchDocumentAsync(
				$"{TestFilePrefix}-limit-{i}.pdf",
				$"{marker} entry {i}");
			indexedIds.Add(document.Id);
		}

		var results = await _search.SearchAsync(
			marker,
			2,
			TestContext.Current.CancellationToken);

		results.Should().HaveCount(2);
		results.Select(result => result.Id).Should().BeSubsetOf(indexedIds);
	}

	[Fact]
	public async Task SearchService_DeleteExistingAndMissingIds_IsIdempotent()
	{
		var document = await IndexSearchDocumentAsync(
			$"{TestFilePrefix}-search-delete.pdf",
			$"delete{Guid.NewGuid():N}");

		await _search.DeleteAsync(document.Id, TestContext.Current.CancellationToken);
		var response = await _elastic.GetAsync<DocumentSearchResult>(
			document.Id.ToString(),
			get => get.Index(_elastic.ElasticsearchClientSettings.DefaultIndex),
			TestContext.Current.CancellationToken);

		response.Found.Should().BeFalse();
		await _search.DeleteAsync(document.Id, TestContext.Current.CancellationToken);
		await _search.DeleteAsync(Guid.CreateVersion7(), TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task SearchService_CanceledSearch_StopsElasticsearchRequest()
	{
		using CancellationTokenSource cancellation = new();
		await cancellation.CancelAsync();

		Func<Task> act = async () =>
			await _search.SearchAsync("invoice", 10, cancellation.Token);

		var exception = await act.Should().ThrowAsync<OperationCanceledException>();
		exception.Which.CancellationToken.Should().Be(cancellation.Token);
	}

	[Fact]
	public async Task SearchService_CanceledDelete_PropagatesCallerToken()
	{
		using CancellationTokenSource cancellation = new();
		await cancellation.CancelAsync();

		Func<Task> act = () => _search.DeleteAsync(Guid.CreateVersion7(), cancellation.Token);

		var exception = await act.Should().ThrowAsync<OperationCanceledException>();
		exception.Which.CancellationToken.Should().Be(cancellation.Token);
	}

	[Fact]
	public async Task WaitForDocument_CallerCancellation_PropagatesCallerToken()
	{
		using CancellationTokenSource cancellation = new();
		await cancellation.CancelAsync();

		Func<Task> act = () => _fixture.WaitForDocumentAsync<DocumentSearchResult>(
			Guid.CreateVersion7().ToString(),
			cancellation.Token);

		var exception = await act.Should().ThrowAsync<OperationCanceledException>();
		exception.Which.CancellationToken.Should().Be(cancellation.Token);
	}

	[Fact]
	public async Task WaitForSearchResults_CallerCancellation_PropagatesCallerToken()
	{
		using CancellationTokenSource cancellation = new();
		await cancellation.CancelAsync();

		Func<Task> act = () => _fixture.WaitForSearchResultsAsync<DocumentSearchResult>(
			search => search.Size(1),
			cancellation.Token);

		var exception = await act.Should().ThrowAsync<OperationCanceledException>();
		exception.Which.CancellationToken.Should().Be(cancellation.Token);
	}

	#endregion

	#region Constants and fields

	private const string DocumentsEndpoint = "/api/v1/documents";
	private const string ContentTypePdf = "application/pdf";
	private const string TestFilePrefix = "endpoint-test";

	private readonly SharedRestContainerFixture _fixture;
	private readonly ElasticsearchClient _elastic;
	private readonly IDocumentSearchService _search;
	private readonly List<Guid> _createdDocIds = [];
	private readonly List<Guid> _indexedDocIds = [];
	private readonly AsyncCleanup _cleanup;

	#endregion

	#region IAsyncLifetime

	public ValueTask InitializeAsync() => ValueTask.CompletedTask;

	public ValueTask DisposeAsync() => _cleanup.DisposeAsync();

	#endregion

	#region Helper methods

	private async Task<Guid> SeedDocumentAsync(string fileName, string? summary = null)
	{
		await using var scope = _fixture.CreateAsyncScope();
		var factory =
			scope.ServiceProvider.GetRequiredService<IDbContextFactory<DocumentPersistence>>();
		await using var db = await factory.CreateDbContextAsync(
			TestContext.Current.CancellationToken);

		var builder = new DocumentBuilder().WithFileName(fileName);
		if (summary is not null)
		{
			builder.WithSummary(summary);
		}

		var entity = builder.BuildEntity();
		db.Documents.Add(entity);
		await db.SaveChangesAsync(TestContext.Current.CancellationToken);
		_createdDocIds.Add(entity.Id);
		return entity.Id;
	}

	private async Task<List<Guid>> SeedDocumentsAsync(int count, string fileNamePrefix)
	{
		await using var scope = _fixture.CreateAsyncScope();
		var factory =
			scope.ServiceProvider.GetRequiredService<IDbContextFactory<DocumentPersistence>>();
		await using var db = await factory.CreateDbContextAsync(
			TestContext.Current.CancellationToken);

		var entities = Enumerable.Range(0, count)
			.Select(i => new DocumentBuilder()
				.WithFileName($"{fileNamePrefix}-{i}.pdf")
				.BuildEntity())
			.ToList();
		db.Documents.AddRange(entities);
		await db.SaveChangesAsync(TestContext.Current.CancellationToken);

		var ids = entities.ConvertAll(entity => entity.Id);
		_createdDocIds.AddRange(ids);
		return ids;
	}

	private async Task<DocumentSearchResult> IndexSearchDocumentAsync(
		string fileName,
		string content,
		string? summary = null)
	{
		var document = new DocumentSearchResult
		{
			Id = Guid.CreateVersion7(),
			FileName = fileName,
			Status = DocumentStatus.Completed.ToString(),
			CreatedAt = new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero),
			Content = content,
			ProcessedAt = new DateTimeOffset(2026, 8, 1, 10, 1, 0, TimeSpan.Zero),
			Summary = summary,
			SummaryGeneratedAt = summary is null
				? null
				: new DateTimeOffset(2026, 8, 1, 10, 2, 0, TimeSpan.Zero)
		};

		var response = await _elastic.IndexAsync(
			document,
			index => index
				.Index(_elastic.ElasticsearchClientSettings.DefaultIndex)
				.Id(document.Id.ToString())
				.Refresh(Refresh.True),
			TestContext.Current.CancellationToken);

		response.IsValidResponse.Should().BeTrue();
		_indexedDocIds.Add(document.Id);
		return document;
	}

	[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
		Justification = "The ByteArrayContent's ownership transfers to the returned MultipartFormDataContent, "
		                + "which every caller disposes via 'using var content = ...'.")]
	private static MultipartFormDataContent CreateUpload(string fileName, string contentType, byte[] bytes)
	{
		var file = new ByteArrayContent(bytes)
		{
			Headers = { ContentType = MediaTypeHeaderValue.Parse(contentType) }
		};
		return new MultipartFormDataContent { { file, "file", fileName } };
	}

	private static async Task<MultipartFormDataContent> CreatePdfUploadAsync(string fileName) =>
		CreateUpload(fileName, ContentTypePdf, await TestPdf.BytesAsync("Test Document"));

	private static async Task<T> ReadSuccessJsonAsync<T>(HttpResponseMessage response)
	{
		var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException(
				$"Request returned {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
		}

		return JsonSerializer.Deserialize<T>(body, JsonSerializerOptions.Web)
		       ?? throw new InvalidOperationException("The response body was empty or invalid JSON");
	}

	#endregion
}
