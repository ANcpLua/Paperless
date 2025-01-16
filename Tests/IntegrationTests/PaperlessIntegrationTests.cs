using System.Net;
using Newtonsoft.Json;
using Contract;

namespace Tests.IntegrationTests;

[TestFixture]
[Ignore("Disabled")]
public class PaperlessIntegrationTests : TestBase
{
    private int _createdDocId;
    private const string TestPdfName = "HelloWorld.pdf";
    private string _testFilePath;

    [SetUp]
    public void SetUp()
    {
        _testFilePath = Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "IntegrationTests",
            TestPdfName
        );
    }

    [Test, Order(1)]
    public async Task UploadDocument_ShouldReturnOkAndCreateDocument()
    {
        // Arrange
        using var formData = new MultipartFormDataContent();
        formData.Add(new StringContent("Test Document"), "name");
        formData.Add(new StreamContent(File.OpenRead(_testFilePath)), "file", TestPdfName);

        // Act
        var response = await Client.PostAsync("/documents/upload", formData);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "Uploading a valid PDF should return 200 OK.");

        var body = await response.Content.ReadAsStringAsync();
        var createdDoc = JsonConvert.DeserializeObject<DocumentDto>(body);

        Assert.That(createdDoc, Is.Not.Null,
            "Deserialized object from upload should be a valid DocumentDto");
        Assert.That(createdDoc.Id, Is.GreaterThan(0),
            "Document ID must be > 0 after upload");
        Assert.That(createdDoc.Name, Is.EqualTo(TestPdfName),
            "Document name should match the uploaded file name");

        // Store the doc ID for subsequent tests
        _createdDocId = createdDoc.Id;
    }

    [Test, Order(2)]
    public async Task SearchDocument_ShouldFindUploadedDocument()
    {
        // Act
        var response = await Client.GetAsync($"/documents/{_createdDocId}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            $"Searching for Document {_createdDocId} should return 200 OK.");

        var body = await response.Content.ReadAsStringAsync();
        var foundDoc = JsonConvert.DeserializeObject<DocumentDto>(body);

        Assert.That(foundDoc, Is.Not.Null, "Response body should deserialize to DocumentDto");
        Assert.Multiple(() =>
        {
            Assert.That(foundDoc.Id, Is.EqualTo(_createdDocId), "Found document ID should match the uploaded doc ID");
            Assert.That(foundDoc.Name, Is.EqualTo(TestPdfName),
                "Found document name should match the uploaded file name");
        });
    }

    [Test, Order(3)]
    public async Task DownloadDocument_ShouldReturnFile()
    {
        // Act
        var response = await Client.GetAsync($"/documents/{_createdDocId}/download");

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(response.StatusCode,
                Is.EqualTo(HttpStatusCode.OK),
                $"Document {_createdDocId} should be downloadable (200 OK).");
            Assert.That(response.Content.Headers.ContentType?.MediaType,
                Is.EqualTo("application/octet-stream"),
                "Downloaded file should have 'application/octet-stream' content type");
        });

        var downloadedStream = await response.Content.ReadAsStreamAsync();
        Assert.That(downloadedStream.Length, Is.GreaterThan(0),
            "Downloaded file should not be empty");

        var originalSize = new FileInfo(_testFilePath).Length;
        Assert.That(downloadedStream.Length, Is.EqualTo(originalSize),
            "Downloaded file size should match original test PDF size");
    }

    [Test, Order(4)]
    public async Task DeleteDocument_ShouldReturnNoContentAndRemoveIt()
    {
        // Act
        var response = await Client.DeleteAsync($"/documents/{_createdDocId}");

        // Assert
        Assert.That(response.StatusCode,
            Is.EqualTo(HttpStatusCode.NoContent),
            $"Deleting Document {_createdDocId} should return 204 NoContent.");

        var getResponse = await Client.GetAsync($"/documents/{_createdDocId}");
        Assert.That(getResponse.StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound).Or.EqualTo(HttpStatusCode.NoContent),
            "After deletion, the document should no longer be retrievable.");
    }
}
