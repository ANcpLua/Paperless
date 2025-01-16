using Contract;
using Elastic.Clients.Elasticsearch;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Tests;

[TestFixture]
public class ElasticsearchTests
{
    [SetUp]
    public async Task Setup()
    {
        var configBuilder = new ConfigurationBuilder()
                            .AddJsonFile("service-appsettings.json")
                            .AddEnvironmentVariables();

        _configuration = configBuilder.Build();

        var elasticUri = _configuration["Elasticsearch:Uri"] ?? "http://localhost:9200";
        var settings = new ElasticsearchClientSettings(new Uri(elasticUri))
            .DefaultIndex(TestIndex);

        _elasticsearchClient = new ElasticsearchClient(settings);

        var loggerFactory = LoggerFactory.Create(builder => { builder.AddConsole(); });
        _logger = loggerFactory.CreateLogger<ElasticsearchTests>();

        var indexExistsResponse = await _elasticsearchClient.Indices.ExistsAsync(TestIndex);
        if (!indexExistsResponse.Exists)
        {
            var createIndexResponse = await _elasticsearchClient.Indices.CreateAsync(TestIndex);
            createIndexResponse.ElasticsearchServerError.Should().BeNull(
                $"Failed to create index '{TestIndex}': {createIndexResponse.DebugInformation}"
            );
            _logger.LogInformation($"Created Elasticsearch index: {TestIndex}");
        }

        _testDocument = new DocumentDto
        {
            Id = TestDocumentId,
            Name = "Test Document",
            OcrText = "This is a sample OCR text for testing."
        };
    }

    [OneTimeTearDown]
    public async Task Cleanup()
    {
        var deleteResponse = await _elasticsearchClient.DeleteAsync<DocumentDto>(_testDocument.Id, d => d
            .Index(TestIndex)
            .Refresh(Refresh.True)
        );

        if (deleteResponse.ElasticsearchServerError == null ||
            deleteResponse.ElasticsearchServerError.Status == 404)
            _logger.LogInformation($"Cleanup: Ensured document with ID {_testDocument.Id} is deleted.");
        else
            _logger.LogWarning(
                $"Cleanup: Failed to delete document with ID {_testDocument.Id}: {deleteResponse.DebugInformation}");
    }

    private ElasticsearchClient _elasticsearchClient;
    private IConfiguration _configuration;
    private ILogger<ElasticsearchTests> _logger;
    private const string TestIndex = "paperless-documents";
    private const int TestDocumentId = 1;
    private DocumentDto _testDocument;

    [Test]
    [Order(1)]
    public async Task IndexDocument_ToElasticsearch_Succeeds()
    {
        var indexResponse = await _elasticsearchClient.IndexAsync(_testDocument, idx => idx
            .Index(TestIndex)
            .Id(_testDocument.Id.ToString())
            .Refresh(Refresh.True)
        );

        indexResponse.ElasticsearchServerError.Should().BeNull(
            $"Indexing failed: {indexResponse.DebugInformation}"
        );
        _logger.LogInformation($"Indexed document with ID: {_testDocument.Id}");

        var getResponse = await _elasticsearchClient.GetAsync<DocumentDto>(_testDocument.Id, g => g.Index(TestIndex));
        getResponse.Found.Should().BeTrue("Document was not found after indexing.");
    }

    [Test]
    [Order(2)]
    public async Task SearchDocument_InElasticsearch_Succeeds()
    {
        var searchQuery = "sample OCR text";

        var searchResponse = await _elasticsearchClient.SearchAsync<DocumentDto>(s => s
            .Index(TestIndex)
            .From(0)
            .Size(10)
            .Query(q => q
                .MultiMatch(mm => mm
                                  .Query(searchQuery)
                                  .Fields(new[] { "name", "ocrText" })
                )
            )
        );

        searchResponse.ElasticsearchServerError.Should().BeNull(
            $"Search failed: {searchResponse.DebugInformation}"
        );

        searchResponse.Documents.Should().NotBeEmpty("No documents found in search results.");
        searchResponse.Documents.Should()
                      .ContainSingle(d => d.Id == _testDocument.Id, "Document not found in search results.");
    }

    [Test]
    [Order(3)]
    public async Task DeleteDocument_FromElasticsearch_Succeeds()
    {
        var deleteResponse = await _elasticsearchClient.DeleteAsync<DocumentDto>(_testDocument.Id, d => d
            .Index(TestIndex)
            .Refresh(Refresh.True)
        );

        deleteResponse.ElasticsearchServerError.Should().BeNull(
            $"Deletion failed: {deleteResponse.DebugInformation}"
        );

        _logger.LogInformation($"Deleted document with ID: {_testDocument.Id}");

        var getResponse = await _elasticsearchClient.GetAsync<DocumentDto>(_testDocument.Id, g => g
            .Index(TestIndex)
        );

        getResponse.Found.Should().BeFalse("Document still exists after deletion.");
    }
}
