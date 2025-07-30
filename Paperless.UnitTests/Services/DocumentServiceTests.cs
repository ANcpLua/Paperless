

using FluentValidation;
using Microsoft.Extensions.Logging;
using Moq;
using Paperless.UnitTests.Builders;
using Paperless.UnitTests.Helpers;
using PaperlessREST;
using PaperlessREST.Services;
using SWEN3.Paperless.RabbitMq.Publishing;

namespace Paperless.UnitTests.Services;

/// <summary>
/// Unit tests for <see cref="DocumentService"/> that verify both behaviour and logging.
/// These tests use the Moq.ILogger VerifyLog extension to assert that the service
/// writes the expected structured log messages and parameters for each operation.
/// </summary>
[TestFixture]
public sealed class DocumentServiceTests
{
    private readonly CancellationToken _ct = CancellationToken.None;

    private MockRepository _mocks;
    private Mock<IDocumentRepository> _repo;
    private Mock<IDocumentStorageService> _storage;
    private Mock<IDocumentSearchService> _search;
    private Mock<IRabbitMqPublisher> _pub;
    private Mock<IValidator<UploadDocumentRequest>> _validator;
    private FakeLoggerFluent<DocumentService> _logger;

    private DocumentService _sut;

    [SetUp]
    public void SetUp()
    {
        // Use a strict repository for all dependencies except the logger. Strict mocks
        // ensure that any unexpected interaction will cause the test to fail.
        _mocks = new MockRepository(MockBehavior.Strict) { DefaultValue = DefaultValue.Empty };

        _repo = _mocks.Create<IDocumentRepository>();
        _storage = _mocks.Create<IDocumentStorageService>();
        _search = _mocks.Create<IDocumentSearchService>();
        _pub = _mocks.Create<IRabbitMqPublisher>();
        _validator = _mocks.Create<IValidator<UploadDocumentRequest>>();
        _logger = FakeLoggerFluent.CreateStrict<DocumentService>();
        // The logger is created separately with loose behaviour so that the
        // service can write logs without requiring explicit setups. We will
        // verify the emitted log messages using the VerifyLog extension.

        _sut = new DocumentService(
            _repo.Object,
            _storage.Object,
            _search.Object,
            _pub.Object,
            _logger,
            _validator.Object);
    }

    [TearDown]
    public void TearDown()
    {
        // Validate that all configured expectations on the strict mocks were met
        _mocks.VerifyAll();
        _mocks.VerifyNoOtherCalls();
        // Verify all logs were expected in strict mode
        _logger.VerifyNoOtherCalls();
    }

    [Test]
    public async Task DeleteDocumentAsync_WhenDocumentExists_DeletesFromAllSources()
    {
        // Arrange
        var doc = new DocumentBuilder().Build();

        _repo.Setup(r => r.GetByIdAsync(doc.Id, _ct)).ReturnsAsync(doc);
        _repo.Setup(r => r.DeleteAsync(doc.Id, _ct)).ReturnsAsync(true);
        _storage.Setup(s => s.DeleteAsync(doc.StoragePath, _ct)).ReturnsAsync(true);
        _search.Setup(s => s.DeleteAsync(doc.Id, _ct)).ReturnsAsync(true);

        // Act
        await _sut.DeleteDocumentAsync(doc.Id, _ct);

        // Assert
        // Verify that the service logged a success message with the correct document ID
        _logger.VerifyLog(LogLevel.Information, "Document {DocumentId} deleted successfully");
        // Ensure no other log messages were written
    }

    [Test]
    public void DeleteDocumentAsync_WhenDocumentIsNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repo.Setup(r => r.GetByIdAsync(id, _ct)).ReturnsAsync((Document?)null);

        // Act & Assert
        var ex = Assert.ThrowsAsync<KeyNotFoundException>(() => _sut.DeleteDocumentAsync(id, _ct));
        Assert.That(ex?.Message, Is.EqualTo($"Document {id} not found"));

        // There should be no logs if the service throws immediately
    }

    [Test]
    public void DeleteDocumentAsync_WhenRepositoryDeleteFails_PropagatesException()
    {
        // Arrange
        var doc = new DocumentBuilder().Build();
        _repo.Setup(r => r.GetByIdAsync(doc.Id, _ct)).ReturnsAsync(doc);
        _storage.Setup(s => s.DeleteAsync(doc.StoragePath, _ct)).ReturnsAsync(true);
        _repo.Setup(r => r.DeleteAsync(doc.Id, _ct))
            .ThrowsAsync(new InvalidOperationException("Database connection lost"));

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => _sut.DeleteDocumentAsync(doc.Id, _ct));

        // No log messages should have been written
    }

    [Test]
    public void DeleteDocumentAsync_WhenStorageDeleteFails_PropagatesException()
    {
        // Arrange
        var doc = new DocumentBuilder().Build();
        var storageException = new IOException("Storage unavailable");

        _repo.Setup(r => r.GetByIdAsync(doc.Id, _ct)).ReturnsAsync(doc);
        // Repository delete happens before storage delete
        _repo.Setup(r => r.DeleteAsync(doc.Id, _ct)).ReturnsAsync(true);
        _storage.Setup(s => s.DeleteAsync(doc.StoragePath, _ct)).ThrowsAsync(storageException);

        // Act & Assert
        var ex = Assert.ThrowsAsync<IOException>(() => _sut.DeleteDocumentAsync(doc.Id, _ct));
        Assert.That(ex, Is.SameAs(storageException));

        // No log messages should have been written
    }

    [Test]
    public void DeleteDocumentAsync_WhenSearchDeleteFails_DoesNotThrowAndCompletes()
    {
        // Arrange
        var doc = new DocumentBuilder().Build();

        _repo.Setup(r => r.GetByIdAsync(doc.Id, _ct)).ReturnsAsync(doc);
        _repo.Setup(r => r.DeleteAsync(doc.Id, _ct)).ReturnsAsync(true);
        _storage.Setup(s => s.DeleteAsync(doc.StoragePath, _ct)).ReturnsAsync(true);
        // Simulate failure in search deletion
        _search.Setup(s => s.DeleteAsync(doc.Id, _ct))
            .ThrowsAsync(new InvalidOperationException("Elasticsearch unavailable"));

        // Act & Assert
        // The service should swallow the exception and complete successfully
        Assert.DoesNotThrowAsync(() => _sut.DeleteDocumentAsync(doc.Id, _ct));

        // Assert logs: one warning about the search deletion failure
        _logger.VerifyLog(LogLevel.Warning, 
            "Failed to delete document {DocumentId} from search index. This is expected if the document was not indexed.");
        // And one informational message signalling success
        _logger.VerifyLog(LogLevel.Information, "Document {DocumentId} deleted successfully");
        // Note: VerifyNoOtherCalls is not implemented for simple counting
    }

    [Test]
    public async Task ProcessOcrResultAsync_WhenOcrSucceeds_UpdatesDocumentWithCompletedStatusAndContent()
    {
        // Arrange
        const string ocrText = "Sample OCR text.";
        var originalDoc = new DocumentBuilder()
            .WithStatus(DocumentStatus.Pending)
            .WithContent(string.Empty)
            .Build();

        _repo.Setup(r => r.GetByIdAsync(originalDoc.Id, _ct))
            .ReturnsAsync(originalDoc);

        // Expect the repository update to be called with the modified document
        _repo.Setup(r => r.UpdateAsync(
                It.Is<Document>(d =>
                    d.Id == originalDoc.Id &&
                    d.Status == DocumentStatus.Completed &&
                    d.Content == ocrText),
                _ct))
            .ReturnsAsync((Document?)null);

        // Act
        var result = await _sut.ProcessOcrResultAsync(originalDoc.Id, "Completed", ocrText, _ct);

        // Assert
        Assert.That(result, Is.True);
        _logger.VerifyLog(LogLevel.Information, "Document {DocumentId} processed with status {Status}");
    }

    [Test]
    public async Task ProcessOcrResultAsync_WhenOcrFails_UpdatesDocumentWithFailedStatus()
    {
        // Arrange
        var originalDoc = new DocumentBuilder()
            .WithStatus(DocumentStatus.Pending)
            .WithContent("Original content")
            .Build();

        _repo.Setup(r => r.GetByIdAsync(originalDoc.Id, _ct))
            .ReturnsAsync(originalDoc);

        _repo.Setup(r => r.UpdateAsync(
                It.Is<Document>(d =>
                    d.Id == originalDoc.Id &&
                    d.Status == DocumentStatus.Failed &&
                    d.Content == originalDoc.Content),
                _ct))
            .ReturnsAsync((Document?)null);

        // Act
        var result = await _sut.ProcessOcrResultAsync(originalDoc.Id, "Failed", null, _ct);

        // Assert
        Assert.That(result, Is.True);
        _logger.VerifyLog(LogLevel.Information, "Document {DocumentId} processed with status {Status}");
    }


    [Test]
    public async Task SearchDocumentsAsync_WhenCalled_ReturnsResultsFromSearchService()
    {
        // Arrange
        const string query = "test query";
        const int limit = 20;
        var searchResults = new[] { new object(), new object() };

        _search.Setup(s => s.SearchAsync<object>(query, limit, _ct))
            .Returns(searchResults.ToAsyncEnumerable());

        // Act
        var results = await _sut.SearchDocumentsAsync(query, limit, _ct).ToListAsync(_ct);

        // Assert
        Assert.That(results, Is.EqualTo(searchResults));
    }

    [Test]
    public async Task GetRecentDocumentsAsync_WhenCalled_ReturnsResultsFromRepository()
    {
        // Arrange
        var recentDocs = new[] { new DocumentBuilder().Build(), new DocumentBuilder().Build() };
        _repo.Setup(r => r.GetRecentDocumentsAsync(50, _ct))
            .Returns(recentDocs.ToAsyncEnumerable());

        // Act
        var results = await _sut.GetRecentDocumentsAsync(_ct).ToListAsync(_ct);

        // Assert
        Assert.That(results, Is.EqualTo(recentDocs));
    }

    [Test]
    public async Task GetDocumentByIdAsync_WhenDocumentExists_ReturnsDocument()
    {
        // Arrange
        var doc = new DocumentBuilder().Build();
        _repo.Setup(r => r.GetByIdAsync(doc.Id, _ct))
            .ReturnsAsync(doc);

        // Act
        var result = await _sut.GetDocumentByIdAsync(doc.Id, _ct);

        // Assert
        Assert.That(result, Is.SameAs(doc));
    }

    [Test]
    public async Task GetDocumentByIdAsync_WhenDocumentDoesNotExist_ReturnsNull()
    {
        // Arrange
        var missingId = Guid.NewGuid();
        _repo.Setup(r => r.GetByIdAsync(missingId, _ct))
            .ReturnsAsync((Document?)null);

        // Act
        var result = await _sut.GetDocumentByIdAsync(missingId, _ct);

        // Assert
        Assert.That(result, Is.Null);
    }
}



// [Test]
// public async Task UploadDocumentAsync_WithValidRequest_UploadsAndPublishesSuccessfully()
// {
//     // Arrange
//     var request = new UploadDocumentRequestBuilder().Build();
//     var savedDoc = new DocumentBuilder().Build();
//
//     _validator.Setup(v => v.ValidateAsync(request, _ct))
//         .ReturnsAsync(new ValidationResult());
//
//     _storage.Setup(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<long>(), _ct))
//         .ReturnsAsync("some/path");
//
//     _repo.Setup(r => r.AddAsync(It.IsAny<Document>(), _ct))
//         .ReturnsAsync(savedDoc);
//
//     _pub.Setup(p => p.PublishOcrCommandAsync(It.IsAny<OcrCommand>()))
//         .Returns(Task.CompletedTask);
//
//     // Act
//     var result = await _sut.UploadDocumentAsync(request, _ct);
//
//     // Assert
//     Assert.That(result, Is.SameAs(savedDoc));
//     _logger.VerifyLog(LogLevel.Information, "Document {DocumentId} uploaded successfully");
// }
//
// [Test]
// public void UploadDocumentAsync_WithInvalidRequest_ThrowsValidationException()
// {
//     // Arrange
//     var request = new UploadDocumentRequestBuilder().Build();
//     var validationFailure = new ValidationFailure("File", "File cannot be empty.");
//
//     _validator.Setup(v => v.ValidateAsync(request, _ct))
//         .ReturnsAsync(new ValidationResult([validationFailure]));
//
//     // Act & Assert
//     Assert.ThrowsAsync<ValidationException>(() => _sut.UploadDocumentAsync(request, _ct));
//
//     // No log messages should have been written
// }