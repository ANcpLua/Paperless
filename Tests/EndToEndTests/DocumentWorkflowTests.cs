using System.Net.Http.Headers;
using Contract;
using EasyNetQ;
using Elastic.Clients.Elasticsearch;
using FluentAssertions;
using Newtonsoft.Json;

namespace Tests.EndToEndTests;

[TestFixture]
public class DocumentWorkflowTests
{
    private HttpClient _client;
    private IBus _bus;
    private ElasticsearchClient _elasticClient;
    private const string DocumentNamePrefix = "HelloWorld";
    private string? _uploadedDocumentName;
    private int _uploadedDocumentId;

    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        _client = new HttpClient { BaseAddress = new Uri("http://localhost:80") };
        _bus = RabbitHutch.CreateBus("host=localhost;port=5672;username=guest;password=guest");
        _elasticClient = new ElasticsearchClient(new ElasticsearchClientSettings(new Uri("http://localhost:9200")));
    }

    [Test, Order(1)]
    public async Task UploadDocumentTest()
    {
        // Prepare a test PDF in memory
        var testFilePath = Path.Combine(TestContext.CurrentContext.TestDirectory, "EndToEndTests", "HelloWorld.pdf");
        if (!File.Exists(testFilePath))
            Assert.Fail("Test PDF file not found at " + testFilePath);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(DocumentNamePrefix), "name");
        form.Add(new StreamContent(File.OpenRead(testFilePath))
        {
            Headers = { ContentType = new MediaTypeHeaderValue("application/pdf") }
        }, "file", "HelloWorld.pdf");

        var response = await _client.PostAsync("/documents/upload", form);
        response.IsSuccessStatusCode.Should().BeTrue("Upload should succeed");

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<dynamic>(responseContent);

        ((bool)result?.success!).Should().BeTrue("API should indicate success");
        ((string)result.message).Should().Be("Document uploaded successfully");

        _uploadedDocumentId = (int)result.document.id;
        _uploadedDocumentName = (string)result.document.name;

        _uploadedDocumentId.Should().BeGreaterThan(0, "Uploaded document should have an ID assigned by DB");
        _uploadedDocumentName.Should().NotBeNullOrEmpty("Uploaded document should have a generated name");
    }

    [Test, Order(2)]
    public async Task OcrProcessingAndDocumentUpdateTest()
    {
        // The OCR worker is subscribed to document.uploaded events and publishes document.processed.
        // We need to subscribe to document.processed and wait for the message.

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        TextMessage? receivedMessage = null;

        // Subscribe to 'document.processed' topic to verify OCR result
        var subscription = await _bus.PubSub.SubscribeAsync<TextMessage>(
            subscriptionId: "test_ocr_check",
            onMessage: (msg, _) =>
            {
                if (msg.DocumentId == _uploadedDocumentId)
                {
                    receivedMessage = msg;
                }

                return Task.CompletedTask;
            },
            configure: c => c.WithTopic("document.processed"),
            cancellationToken: cts.Token
        );

        // Wait until we get the processed event or timeout
        while (receivedMessage == null && !cts.IsCancellationRequested)
        {
            await Task.Delay(500, cts.Token);
        }

        subscription.Dispose();

        receivedMessage.Should().NotBeNull("We should receive a processed message for the uploaded doc");
        receivedMessage?.DocumentId.Should().Be(_uploadedDocumentId, "Processed message should match uploaded doc ID");
        receivedMessage?.Text.Should().NotBeNullOrWhiteSpace("OCR result should not be empty");
        receivedMessage?.Text.Should().Contain("Hello World", "The OCR text should contain the known text from the PDF");

        // Optionally, we can verify the REST endpoint that gets the document, ensuring OCR text is now stored
        var getResponse = await _client.GetAsync($"/documents/{_uploadedDocumentId}");
        getResponse.IsSuccessStatusCode.Should().BeTrue("Should be able to retrieve the document after OCR processing");
        var getJson = await getResponse.Content.ReadAsStringAsync();
        dynamic getDocData = JsonConvert.DeserializeObject(getJson)!;
        string ocrText = getDocData.ocrText;
        ocrText.Should().Contain("Hello World", "The database should be updated with the OCR text");
    }

    [Test, Order(3)]
    public async Task IndexingTest()
    {
        // At this point, the DocumentService should have indexed the document into Elasticsearch.
        // We'll query Elasticsearch for "Hello" and see if our doc appears.

        // Give ElasticSearch a few seconds to refresh
        await Task.Delay(2000);

        var searchResponse = await _elasticClient.SearchAsync<DocumentDto>(s => s
            .Index("paperless-documents")
            .Query(q => q.MultiMatch(mm => mm.Query("Hello").Fields(new[] { "name", "ocrText" })))
        );

        searchResponse.IsValidResponse.Should().BeTrue("Search should succeed");
        searchResponse.Documents.Should().NotBeEmpty("We should find at least one document matching 'Hello'");

        var matchedDocs = searchResponse.Documents.Where(d => d.Id == _uploadedDocumentId).ToList();
        matchedDocs.Should().NotBeEmpty("Our uploaded and OCR-processed doc should be found by 'Hello' query");
    }

    [OneTimeTearDown]
    public void OneTimeTeardown()
    {
        _bus.Dispose();
        _client.Dispose();
    }
}
