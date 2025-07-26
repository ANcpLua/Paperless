using System.Net;
using System.Net.Http.Json;
using PaperlessREST;

namespace Paperless.Tests;

[RecordValidation]
public class DocumentEndpointTests : IntegrationTestBase
{
    private HttpClient Client => CreateClient();
    private const string BaseUrl = "/api/v1/documents";

    // ═══════════════════════════════════════════════════════════════
    // GET /documents - Get recent documents
    // ═══════════════════════════════════════════════════════════════

    [Test]
    [DisplayName("GET /documents - Returns 50 most recent documents")]
    public async Task GetDocuments_ReturnsRecentDocuments()
    {
        // Arrange - Upload some test documents first
        await UploadTestDocumentAsync("test1.pdf");
        await UploadTestDocumentAsync("test2.pdf");

        // Act
        var documents = await Client
            .GetAsync(BaseUrl)
            .ExpectOkAsync<List<DocumentDto>>();

        // Assert
        await Assert.That(documents).IsNotNull();
        await Assert.That(documents.Count).IsLessThanOrEqualTo(50);
    }

    // ═══════════════════════════════════════════════════════════════
    // GET /documents/search - Search documents by content
    // ═══════════════════════════════════════════════════════════════

    [Test]
    [Arguments("test", 10)]
    [Arguments("contract", 5)]
    [Arguments("invoice", 20)]
    [DisplayName("GET /documents/search - Valid search returns results (query: '$query', limit: $limit)")]
    public async Task SearchDocuments_ValidQuery_ReturnsResults(string query, int limit)
    {
        // Act
        var results = await Client
            .GetAsync($"{BaseUrl}/search?query={query}&limit={limit}")
            .ExpectOkAsync<List<object>>();

        // Assert
        await Assert.That(results).IsNotNull();
        await Assert.That(results.Count).IsLessThanOrEqualTo(limit);
    }

    [Test]
    [SearchQueryValidationGenerator]
    [ArgumentDisplayFormatter<SearchQueryValidationFormatter>]
    [DisplayName("GET /documents/search - Invalid parameters return validation errors")]
    public async Task SearchDocuments_InvalidParameters_ReturnsValidationError(string query, int? limit,
        string expectedError)
    {
        // Arrange
        var url = $"{BaseUrl}/search?query={Uri.EscapeDataString(query)}";
        if (limit.HasValue)
            url += $"&limit={limit}";

        // Act
        var problem = await Client
            .GetAsync(url)
            .ExpectBadRequestAsync();

        // Assert
        await Assert.That(problem).IsNotNull();
        // The validation should contain the expected error message
    }

    // ═══════════════════════════════════════════════════════════════
    // GET /documents/{id} - Get document by ID
    // ═══════════════════════════════════════════════════════════════

    [Test]
    [DisplayName("GET /documents/{id} - Existing document returns details")]
    public async Task GetDocumentById_ExistingDocument_ReturnsDocument()
    {
        // Arrange - Upload a document first
        var uploaded = await UploadTestDocumentAsync("test-document.pdf");

        // Act
        var document = await Client
            .GetAsync($"{BaseUrl}/{uploaded.Id}")
            .ExpectOkAsync<DocumentDto>();

        // Assert
        await Assert.That(document.Id).IsEqualTo(uploaded.Id);
        await Assert.That(document.FileName).IsEqualTo("test-document.pdf");
    }

    [Test]
    [DisplayName("GET /documents/{id} - Non-existent document returns 404")]
    public async Task GetDocumentById_NonExistentDocument_ReturnsNotFound()
    {
        // Arrange
        var randomId = Guid.NewGuid();

        // Act & Assert
        await Client
            .GetAsync($"{BaseUrl}/{randomId}")
            .ExpectNotFoundAsync();
    }

    // ═══════════════════════════════════════════════════════════════
    // POST /documents - Upload a PDF document
    // ═══════════════════════════════════════════════════════════════

    [Test]
    [DisplayName("POST /documents - Valid PDF upload returns accepted")]
    public async Task UploadDocument_ValidPdf_ReturnsAccepted()
    {
        // Arrange
        var pdfContent = await PdfTestHelper.CreateTestPdf();

        // Act
        var response = await Client
            .PostPdfAsync(BaseUrl, pdfContent, "valid-document.pdf")
            .ExpectAcceptedAsync<CreateDocumentResponse>();

        // Assert
        await Assert.That(response.Id).IsNotEqualTo(Guid.Empty);
        await Assert.That(response.FileName).IsEqualTo("valid-document.pdf");
        await Assert.That(response.Status).IsEqualTo("Pending");
    }

    [Test]
    [InvalidFileUploadGenerator]
    [ArgumentDisplayFormatter<FileUploadScenarioFormatter>]
    [DisplayName("POST /documents - Invalid file upload returns validation error")]
    public async Task UploadDocument_InvalidFile_ReturnsValidationError(string filename, string content,
        string expectedError)
    {
        // Act
        SmartProblemDetails? problem;

        if (string.IsNullOrEmpty(filename))
        {
            // Test missing file
            using var emptyForm = new MultipartFormDataContent();
            problem = await Client
                .PostAsync(BaseUrl, emptyForm)
                .ExpectBadRequestAsync();
        }
        else
        {
            // Test invalid file types
            problem = await Client
                .PostTextFileAsync(BaseUrl, content, filename)
                .ExpectBadRequestAsync();
        }

        // Assert
        await Assert.That(problem).IsNotNull();
        // The error message should contain the expected validation error
    }

    [Test]
    [DisplayName("POST /documents - Duplicate filename returns business validation error")]
    public async Task UploadDocument_DuplicateFilename_ReturnsBusinessError()
    {
        // Arrange - Upload first document)
        var pdfContent = await PdfTestHelper.CreateTestPdf();
        await Client.PostPdfAsync(BaseUrl, pdfContent, "duplicate-test.pdf");

        // Act - Try to upload with same filename
        var newPdfContent = await PdfTestHelper.CreateTestPdf();
        var response = await Client.PostPdfAsync(BaseUrl, newPdfContent, "duplicate-test.pdf");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        await Assert.That(problem!.Detail).Contains("already exists");
    }

    // ═══════════════════════════════════════════════════════════════
    // DELETE /documents/{id} - Delete a document
    // ═══════════════════════════════════════════════════════════════

    [Test]
    [DocumentStatusTransitionGenerator]
    [ArgumentDisplayFormatter<DocumentStatusFormatter>]
    [DisplayName("DELETE /documents/{id} - Document deletion based on status")]
    public async Task DeleteDocument_BasedOnStatus_ReturnsExpectedResult(string status, bool canDelete)
    {
        // Arrange - Create a document with specific status
        var document = await PdfTestHelper.CreateTestPdf();

        // Act
        var response = await Client.DeleteAsync($"{BaseUrl}/{document}");

        // Assert
        if (canDelete)
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

            // Verify it's actually deleted
            await Client
                .GetAsync($"{BaseUrl}/{document}")
                .ExpectNotFoundAsync();
        }
        else
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            await Assert.That(problem!.Detail).Contains($"cannot be deleted in {status} status");
        }
    }

    [Test]
    [DisplayName("DELETE /documents/{id} - Non-existent document returns 404")]
    public async Task DeleteDocument_NonExistent_ReturnsNotFound()
    {
        // Arrange
        var randomId = Guid.NewGuid();

        // Act & Assert
        await Client
            .DeleteAsync($"{BaseUrl}/{randomId}")
            .ExpectNotFoundAsync();
    }

    // ═══════════════════════════════════════════════════════════════
    // HELPER METHODS
    // ═══════════════════════════════════════════════════════════════

    private async Task<CreateDocumentResponse> UploadTestDocumentAsync(string filename)
    {
        var pdfContent = await PdfTestHelper.CreateTestPdf();
        return await Client
            .PostPdfAsync(BaseUrl, pdfContent, filename)
            .ExpectAcceptedAsync<CreateDocumentResponse>();
    }
}

// ═══════════════════════════════════════════════════════════════
// CUSTOM ARGUMENT FORMATTERS
// ═══════════════════════════════════════════════════════════════

public class SearchQueryValidationFormatter : ArgumentDisplayFormatter
{
    public override bool CanHandle(object? value) => true; // Handle all parameters

    public override string FormatValue(object? value)
    {
        return value switch
        {
            string { Length: > 0 } query => $"Query: '{query}'",
            int limit and > 0 => $"Limit: {limit}",
            _ => "Invalid search parameters"
        };
    }
}

public class DocumentStatusFormatter : ArgumentDisplayFormatter
{
    public override bool CanHandle(object? value) => value is string;

    public override string FormatValue(object? value)
    {
        return value switch
        {
            string status => $"Status: '{status}'",
            bool canDelete => canDelete ? "Can delete" : "Cannot delete",
            _ => "Unknown status"
        };
    }
}

// ═══════════════════════════════════════════════════════════════
// EDGE CASE TESTS
// ═══════════════════════════════════════════════════════════════

public class DocumentEndpointEdgeCaseTests : IntegrationTestBase
{
    private HttpClient Client => CreateClient();
    private const string BaseUrl = "/api/v1/documents";

    [Test]
    [DisplayName("Search with special characters in query")]
    [Arguments("test&query=hack", 10)]
    [Arguments("test%20with%20spaces", 10)]
    [Arguments("test/path", 10)]
    [Arguments("test?extra=param", 10)]
    public async Task SearchDocuments_SpecialCharacters_HandledCorrectly(string query, int limit)
    {
        // Act - Properly encode the query
        var encodedQuery = Uri.EscapeDataString(query);
        var results = await Client
            .GetAsync($"{BaseUrl}/search?query={encodedQuery}&limit={limit}")
            .ExpectOkAsync<List<object>>();

        // Assert
        await Assert.That(results).IsNotNull();
    }

    [Test]
    [DisplayName("Upload empty PDF file")]
    public async Task UploadDocument_EmptyPdf_HandledGracefully()
    {
        // Arrange - Create minimal valid 
        var emptyPdf = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x30 }; 

        // Act
        var response = await Client
            .PostPdfAsync(BaseUrl, emptyPdf, "empty.pdf")
            .ExpectAcceptedAsync<CreateDocumentResponse>();

        // Assert
        await Assert.That(response.FileName).IsEqualTo("empty.pdf");
    }

   
}