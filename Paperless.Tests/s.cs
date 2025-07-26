using System.Net;
using PaperlessREST;
using TUnit.Extensions.Http;

namespace Paperless.Tests;

[RecordValidation] // Enable validation telemetry
public class DocumentEndpointsTests : IntegrationTestBase
{
    private HttpClient Client => CreateClient();
    private const string BaseUrl = "/api/v1/documents";

    // ============== HAPPY PATH ==============
    
    [Test]
    public async Task DocumentLifecycle_CompleteFlow_SuccessfullyProcessesDocument()
    {
        // Step 1: Upload a valid PDF document
        var uploadedDocument = await Client
            .PostFileAsync(BaseUrl, await PdfTestHelper.CreateTestPdf(), filename: "integration-test.pdf")
            .ExpectAccepted<CreateDocumentResponse>();
        
        // Verify upload response
        using (Assert.Multiple())
        {
            await Assert.That(uploadedDocument.Id).IsNotEqualTo(Guid.Empty);
            await Assert.That(uploadedDocument.FileName).IsEqualTo("integration-test.pdf");
            await Assert.That(uploadedDocument.Status).IsEqualTo("Pending");
            await Assert.That(uploadedDocument.CreatedAt).IsEqualTo(DateTimeOffset.UtcNow).Within(TimeSpan.FromSeconds(5));
        }

        // Step 2: Retrieve the uploaded document
        var retrievedDocument = await Client
            .GetAsync($"{BaseUrl}/{uploadedDocument.Id}")
            .ExpectOk<DocumentDto>();
        
        // Verify retrieved document matches upload
        using (Assert.Multiple())
        {
            await Assert.That(retrievedDocument.Id).IsEqualTo(uploadedDocument.Id);
            await Assert.That(retrievedDocument.FileName).IsEqualTo(uploadedDocument.FileName);
            await Assert.That(retrievedDocument.Status).IsEqualTo(uploadedDocument.Status);
            await Assert.That(retrievedDocument.CreatedAt).IsEqualTo(uploadedDocument.CreatedAt);
            await Assert.That(retrievedDocument.StoragePath).Matches(@"^documents/\d{4}-\d{2}/[a-f0-9-]+\.pdf$");
            await Assert.That(retrievedDocument.Content).IsNull();
            await Assert.That(retrievedDocument.ProcessedAt).IsNull();
        }

        // Step 3: Search for documents
        var searchResults = await Client
            .GetAsync($"{BaseUrl}/search?query=test&limit=50")
            .ExpectOk<List<object>>();
        
        await Assert.That(searchResults).IsNotEmpty();

        // Step 4: Get recent documents list
        var recentDocuments = await Client
            .GetAsync(BaseUrl)
            .ExpectOk<List<DocumentDto>>();
        
        var ourDocument = recentDocuments.FirstOrDefault(d => d.Id == uploadedDocument.Id);
        await Assert.That(ourDocument).IsNotNull()
            .Because("newly uploaded document should appear in recent documents list");

        // Step 5: Delete the document
        await Client
            .DeleteAsync($"{BaseUrl}/{uploadedDocument.Id}")
            .ExpectNoContent();

        // Step 6: Verify document is gone
        await Client
            .GetAsync($"{BaseUrl}/{uploadedDocument.Id}")
            .ExpectNotFound();
    }

    // ============== UNHAPPY PATH ==============
    
    [Test]
    public async Task DocumentValidation_InvalidRequests_ReturnsAppropriateErrors()
    {
        // Test 1: Upload non-PDF file
        var problem1 = await Client
            .PostTextFileAsync(BaseUrl, "This is not a PDF", "document.txt")
            .ExpectBadRequest();
        
        await problem1.AssertValidation("File", ".pdf extension");

        // Test 2: Upload with missing file
        using var emptyContent = new MultipartFormDataContent();
        var problem2 = await Client
            .PostAsync(BaseUrl, emptyContent)
            .ExpectBadRequest();
        
        await problem2.AssertValidation("File", "required");

        // Test 3: Search with invalid parameters
        var problem3 = await Client
            .GetAsync($"{BaseUrl}/search?query=&limit=0")
            .ExpectBadRequest();
        
        await problem3.AssertValidations(
            ("Query", "at least 1 character"),
            ("Limit", "between 1 and 100")
        );

        // Test 4: Search with query too long
        var longQuery = new string('x', 501);
        var problem4 = await Client
            .GetAsync($"{BaseUrl}/search?query={Uri.EscapeDataString(longQuery)}&limit=10")
            .ExpectBadRequest();
        
        await problem4.AssertValidation("Query", "must not exceed 500 characters");

        // Test 5: Search with limit out of range
        var problem5 = await Client
            .GetAsync($"{BaseUrl}/search?query=test&limit=101")
            .ExpectBadRequest();
        
        await problem5.AssertValidation("Limit", "between 1 and 100");

        // Test 6: Get non-existent document
        await Client
            .GetAsync($"{BaseUrl}/{Guid.NewGuid()}")
            .ExpectNotFound();

        // Test 7: Delete non-existent document
        await Client
            .DeleteAsync($"{BaseUrl}/{Guid.NewGuid()}")
            .ExpectNotFound();

        // Test 8: Invalid GUID format in URL
        var problem8 = await Client
            .GetAsync($"{BaseUrl}/not-a-guid")
            .ExpectBadRequest();
        
        await problem8.AssertValidation("id");
    }

    // ============== ADVANCED SCENARIOS ==============
    
    [Test]
    public async Task GetDocumentById_UnionResult_HandlesNotFoundScenario()
    {
        // The endpoint returns Results<Ok<DocumentDto>, NotFound>
        var nonExistentId = Guid.NewGuid();
        
        await Client
            .GetAsync($"{BaseUrl}/{nonExistentId}")
            .ExpectNotFound();
    }

    [Test]
    public async Task DeleteDocument_UnionResult_HandlesBothScenarios()
    {
        // First create a document to delete
        var created = await Client
            .PostFileAsync(BaseUrl, await PdfTestHelper.CreateTestPdf())
            .ExpectAccepted<CreateDocumentResponse>();
        
        // Test the NoContent branch (successful deletion)
        await Client
            .DeleteAsync($"{BaseUrl}/{created.Id}")
            .ExpectNoContent();
        
        // Test the NotFound branch (already deleted)
        await Client
            .DeleteAsync($"{BaseUrl}/{created.Id}")
            .ExpectNotFound();
    }

    [Test]
    public async Task AcceptedAtRoute_VerifyLocationHeader()
    {
        // Upload and get the raw response to check headers
        var response = await Client.PostFileAsync(BaseUrl, await PdfTestHelper.CreateTestPdf());
        
        await Assert.That(response).Status(HttpStatusCode.Accepted);
        
        // Verify Location header
        var location = response.Headers.Location;
        await Assert.That(location).IsNotNull();
        await Assert.That(location!.ToString()).Matches(@"/api/v1/documents/[a-f0-9-]+$");
        
        // Follow the Location header
        var document = await Client
            .GetAsync(location)
            .ExpectOk<DocumentDto>();
        
        await Assert.That(document).IsNotNull();
    }

    // ============== EDGE CASES ==============
    
    [Test]
    public async Task DocumentSearch_EdgeCases_HandlesCorrectly()
    {
        // Empty search with valid limit uses default
        var results = await Client
            .GetAsync($"{BaseUrl}/search?query=test")
            .ExpectOk<List<object>>();
        
        await Assert.That(results.Count).IsLessThanOrEqualTo(10);

        // Maximum allowed limit
        var maxResults = await Client
            .GetAsync($"{BaseUrl}/search?query=test&limit=100")
            .ExpectOk<List<object>>();
        
        await Assert.That(maxResults.Count).IsLessThanOrEqualTo(100);
    }

    // ============== DEMONSTRATING CLEAN PATTERNS ==============
    
    [Test]
    public async Task MultipleValidationErrors_CleanAssertion()
    {
        // One beautiful line for complex validation
        var problem = await Client
            .GetAsync($"{BaseUrl}/search?query=&limit=999")
            .ExpectBadRequest();

        await problem.AssertValidations(
            ("Query", "at least 1 character"),
            ("Limit", "between 1 and 100")
        );
    }

    [Test]
    public async Task FileUpload_MultipleScenarios()
    {
        // Test various file types in a clean way
        var testCases = new[]
        {
            ("empty.txt", "", "File must have .pdf extension"),
            ("large.txt", new string('x', 10_000), "File must have .pdf extension"),
            ("script.js", "alert('test');", "File must have .pdf extension")
        };

        foreach (var (filename, content, expectedError) in testCases)
        {
            var problem = await Client
                .PostTextFileAsync(BaseUrl, content, filename)
                .ExpectBadRequest();
            
            await problem.AssertValidation("File", expectedError);
        }
    }
}