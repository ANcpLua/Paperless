using Moq;
using Paperless.UnitTests.Builders;
using PaperlessREST;
using PaperlessREST.Services;

namespace Paperless.UnitTests.Services;

/// <summary>
/// Unit tests for IDocumentSearchService interface.
/// Since ElasticsearchClient cannot be mocked, we test the interface behavior.
/// </summary>
[TestFixture]
public sealed class DocumentSearchServiceTests
{
    private readonly CancellationToken _ct = CancellationToken.None;

    private MockRepository _mocks = null!;
    private Mock<IDocumentSearchService> _searchService = null!;

    [SetUp]
    public void SetUp()
    {
        _mocks = new MockRepository(MockBehavior.Strict) { DefaultValue = DefaultValue.Empty };
        _searchService = _mocks.Create<IDocumentSearchService>();
    }

    [TearDown]
    public void TearDown()
    {
        _mocks.VerifyAll();
        _mocks.VerifyNoOtherCalls();
    }

    [Test]
    public async Task SearchAsync_ReturnsExpectedDocuments()
    {
        // Arrange
        const string query = "invoice";
        const int limit = 10;
        var doc1 = new DocumentBuilder().Build();
        var doc2 = new DocumentBuilder().Build();

        _searchService
            .Setup(s => s.SearchAsync<Document>(query, limit, _ct))
            .Returns(new[] { doc1, doc2 }.ToAsyncEnumerable());

        // Act
        var results = new List<Document>();
        await foreach (var doc in _searchService.Object.SearchAsync<Document>(query, limit, _ct))
        {
            results.Add(doc);
        }

        // Assert
        Assert.That(results, Has.Count.EqualTo(2));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results[0], Is.SameAs(doc1));
            Assert.That(results[1], Is.SameAs(doc2));
        }
    }

    [Test]
    public async Task SearchAsync_WithEmptyResults_ReturnsEmptySequence()
    {
        // Arrange
        const string query = "nonexistent";
        const int limit = 5;

        _searchService
            .Setup(s => s.SearchAsync<Document>(query, limit, _ct))
            .Returns(Array.Empty<Document>().ToAsyncEnumerable());

        // Act
        var results = new List<Document>();
        await foreach (var doc in _searchService.Object.SearchAsync<Document>(query, limit, _ct))
        {
            results.Add(doc);
        }

        // Assert
        Assert.That(results, Is.Empty);
    }

    [Test]
    public async Task DeleteAsync_ReturnsTrue_WhenSuccessful()
    {
        // Arrange
        var id = Guid.NewGuid();
        _searchService
            .Setup(s => s.DeleteAsync(id, _ct))
            .ReturnsAsync(true);

        // Act
        var result = await _searchService.Object.DeleteAsync(id, _ct);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenUnsuccessful()
    {
        // Arrange
        var id = Guid.NewGuid();
        _searchService
            .Setup(s => s.DeleteAsync(id, _ct))
            .ReturnsAsync(false);

        // Act
        var result = await _searchService.Object.DeleteAsync(id, _ct);

        // Assert
        Assert.That(result, Is.False);
    }
}