using System.Net;
using System.Net.Http.Headers;

namespace Paperless.Tests;

/// <summary>
/// Integration tests for document management endpoints.
/// </summary>
public sealed class DocumentManagementTests : IntegrationTestBase
{
    [Test]
    public async Task UploadDocument_WithValidPdf_ReturnsCreated()
    {
        // Arrange
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("Test PDF content"u8.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", "test-document.pdf");

        // Act
        var response = await CreateClient().PostAsync("/api/documents", content);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
    }

    [Test]
    public async Task GetDocuments_ReturnsOkWithDocumentList()
    {
        // Act
        var response = await CreateClient().GetAsync("/api/documents");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }
}