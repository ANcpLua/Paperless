using System.Net;
using System.Net.Http.Json;

namespace Paperless.Tests;

/// <summary>
/// Integration tests for document search functionality.
/// </summary>
public sealed class DocumentSearchTests
{
    [ClassDataSource<PaperlessWebApplication>(Shared = SharedType.PerTestSession)]
    public required PaperlessWebApplication Application { get; init; }
    
    [Test]
    public async Task SearchDocuments_WithValidQuery_ReturnsResults()
    {
        // Arrange
        var client = Application.CreateClient();
        
        // Act
        var response = await client.GetAsync("/api/documents/search?query=test");
        
        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        
        var results = await response.Content.ReadFromJsonAsync<DocumentSearchResponse>();
        await Assert.That(results).IsNotNull();
    }

    [Test]
    [Arguments("contract", 5)]
    [Arguments("invoice", 10)]
    [Arguments("report", 3)]
    public async Task SearchDocuments_WithSpecificTerms_ReturnsExpectedMinimumCount(
        string searchTerm, int expectedMinimumCount)
    {
        // Arrange
        var client = Application.CreateClient();
        
        // Act
        var response = await client.GetAsync($"/api/documents/search?query={searchTerm}");
        
        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        
        var results = await response.Content.ReadFromJsonAsync<DocumentSearchResponse>();
        await Assert.That(results!.TotalCount).IsGreaterThanOrEqualTo(expectedMinimumCount);
    }
}

/// <summary>
/// Response model for document search operations.
/// </summary>
/// <param name="TotalCount">The total number of documents found.</param>
/// <param name="Documents">The list of documents matching the search criteria.</param>
public record DocumentSearchResponse(int TotalCount, List<DocumentSummary> Documents);

/// <summary>
/// Summary information for a document.
/// </summary>
/// <param name="Id">The unique identifier of the document.</param>
/// <param name="FileName">The name of the document file.</param>
public record DocumentSummary(Guid Id, string FileName);