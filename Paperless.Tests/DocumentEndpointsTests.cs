using System.Net.Http.Json;
using PaperlessREST;

namespace Paperless.Tests;

public class DocumentEndpointTests : IntegrationTestBase
{
    private const string BaseUrl = "/api/v1/documents";

    [Test]
    [DisplayName("GET /documents - Returns most recent documents")]
    public async Task GetDocuments_ReturnsRecentDocuments()
    {
        // Arrange

        // Act
        var response = await Client.GetAsync(BaseUrl);

        // Extract
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var error = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Response Status: {response.StatusCode}");
            Console.WriteLine($"Response Content: {error}");
        }
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        var documents = await response.Content.ReadFromJsonAsync<List<DocumentDto>>(
            AppJsonSerializerContext.Default.ListDocumentDto);
        var count = documents?.Count ?? 0;

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(mediaType).IsEqualTo("application/json");
        await Assert.That(documents).IsNotNull();
        await Assert.That(count).IsLessThanOrEqualTo(50);
    }

    // ═══════════════════════════════════════════════════════════════
    // GET /documents/search - Search endpoints are not tested here
    // Search functionality requires Elasticsearch indexing which is
    // handled by the OCR microservice, not the web API
    // ═══════════════════════════════════════════════════════════════

    [Test]
    [DisplayName("GET /documents/{id} - Non-existent document returns 404")]
    public async Task GetDocumentById_NonExistentDocument_ReturnsNotFound()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var url = $"{BaseUrl}/{nonExistentId}";

        // Act
        var response = await Client.GetAsync(url);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    [DisplayName("GET /documents/{id} - Existing document returns document data")]
    public async Task GetDocumentById_ExistingDocument_ReturnsDocument()
    {
        // Arrange
        var pdfContent = await PdfTestHelper.CreateTestPdf();
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(pdfContent), "file", "test-document.pdf");
        var uploadResponse = await Client.PostAsync(BaseUrl, content);
        var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<CreateDocumentResponse>(
            AppJsonSerializerContext.Default.CreateDocumentResponse);
        var documentId = uploadResult!.Id;
        var url = $"{BaseUrl}/{documentId}";

        // Act
        var response = await Client.GetAsync(url);

        // Extract
        var document = await response.Content.ReadFromJsonAsync<DocumentDto>(
            AppJsonSerializerContext.Default.DocumentDto);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(document).IsNotNull();
        await Assert.That(document!.Id).IsEqualTo(documentId);
        await Assert.That(document.FileName).IsEqualTo("test-document.pdf");
    }

    // ═══════════════════════════════════════════════════════════════
    // POST /documents - Upload a PDF document
    // ═══════════════════════════════════════════════════════════════

    [Test]
    [DisplayName("POST /documents - Valid PDF upload returns Status202Accepted response")]
    public async Task UploadDocument_ValidPdf_Returns202Accepted()
    {
        // Arrange
        var pdfContent = await PdfTestHelper.CreateTestPdf();
        using var content = new MultipartFormDataContent();
        var fileName = $"test-{Guid.NewGuid()}.pdf";
        content.Add(new ByteArrayContent(pdfContent), "file", fileName);

        // Act
        var response = await Client.PostAsync(BaseUrl, content);

        // Extract
        var result = await response.Content.ReadFromJsonAsync<CreateDocumentResponse>(
            AppJsonSerializerContext.Default.CreateDocumentResponse);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.FileName).IsEqualTo(fileName);
        await Assert.That(result.Status).IsEqualTo("Pending");
        await Assert.That(result.Id).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    [DisplayName("POST /documents - Non-PDF file returns validation error")]
    public async Task UploadDocument_NonPdfFile_ReturnsValidationError()
    {
        // Arrange
        using var content = new MultipartFormDataContent();
        var textContent = "This is not a PDF"u8.ToArray();
        content.Add(new ByteArrayContent(textContent), "file", "not-a-pdf.txt");

        // Act
        var response = await Client.PostAsync(BaseUrl, content);

        // Extract
        var problemDetails = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            AppJsonSerializerContext.Default.HttpValidationProblemDetails);
        var errors = problemDetails!.Errors.SelectMany(kvp => kvp.Value).ToList();

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(problemDetails).IsNotNull();
        await Assert.That(errors.Any(e => e.Contains("Only PDF files are allowed"))).IsTrue();
    }

    [Test]
    [DisplayName(
        "POST /documents - Duplicate filename returns ValidationMessage containing 'A document with this filename already exists'")]
    public async Task UploadDocument_DuplicateFilename_ReturnsBusinessError()
    {
        // Arrange
        var pdfContent = await PdfTestHelper.CreateTestPdf();
        var duplicateFileName = $"duplicate-{Guid.NewGuid()}.pdf";

        using var content1 = new MultipartFormDataContent();
        content1.Add(new ByteArrayContent(pdfContent), "file", duplicateFileName);
        _ = await Client.PostAsync(BaseUrl, content1);

        using var content2 = new MultipartFormDataContent();
        content2.Add(new ByteArrayContent(pdfContent), "file", duplicateFileName);

        // Act
        var response = await Client.PostAsync(BaseUrl, content2);

        // Extract
        var problemDetails = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            AppJsonSerializerContext.Default.HttpValidationProblemDetails);
        var errors = problemDetails!.Errors.SelectMany(kvp => kvp.Value).ToList();

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(problemDetails).IsNotNull();
        await Assert.That(errors.Any(e => e.Contains("A document with this filename already exists"))).IsTrue();
    }

    // ═══════════════════════════════════════════════════════════════
    // DELETE /documents/{id} - Delete a document
    // ═══════════════════════════════════════════════════════════════

    [Test]
    [DisplayName("DELETE /documents/{id} - Existing document returns 204 NoContent")]
    public async Task DeleteDocument_ExistingDocument_ReturnsNoContent()
    {
        // Arrange
        var pdfContent = await PdfTestHelper.CreateTestPdf();
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(pdfContent), "file", $"to-delete-{Guid.NewGuid()}.pdf");
        var uploadResponse = await Client.PostAsync(BaseUrl, content);
        var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<CreateDocumentResponse>(
            AppJsonSerializerContext.Default.CreateDocumentResponse);
        var documentId = uploadResult!.Id;
        var url = $"{BaseUrl}/{documentId}";

        // Act
        var response = await Client.DeleteAsync(url);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }

    [Test]
    [DisplayName("DELETE /documents/{id} - Non-existent document returns 404 NotFound")]
    public async Task DeleteDocument_NonExistent_ReturnsNoContent()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var url = $"{BaseUrl}/{nonExistentId}";

        // Act
        var response = await Client.DeleteAsync(url);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    [DisplayName("DELETE /documents/{id} - Verify document is removed after deletion")]
    public async Task DeleteDocument_VerifyRemoval_DocumentNotFound()
    {
        // Arrange
        var pdfContent = await PdfTestHelper.CreateTestPdf();
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(pdfContent), "file", $"verify-delete-{Guid.NewGuid()}.pdf");
        var uploadResponse = await Client.PostAsync(BaseUrl, content);
        var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<CreateDocumentResponse>(
            AppJsonSerializerContext.Default.CreateDocumentResponse);
        var documentId = uploadResult!.Id;
        var deleteUrl = $"{BaseUrl}/{documentId}";

        // Act
        var deleteResponse = await Client.DeleteAsync(deleteUrl);
        var getResponse = await Client.GetAsync(deleteUrl);

        // Assert
        await Assert.That(deleteResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(getResponse.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }
}