using Microsoft.Extensions.Logging;
using Moq;
using PaperlessREST.Services;
using PostgreSQL.Persistence;

namespace Tests;

[TestFixture]
public class DeleteDocumentServiceTests  
{
    private Mock<IDocumentRepository> _mockRepo = null!;
    private Mock<ILogger<DeleteDocumentService>> _mockLogger = null!;
    private DeleteDocumentService _service = null!;

    [SetUp]
    public void Setup()
    {
        _mockRepo = new Mock<IDocumentRepository>();
        _mockLogger = new Mock<ILogger<DeleteDocumentService>>();
           
        _service = new DeleteDocumentService(
            _mockRepo.Object,
            _mockLogger.Object);
    }

    [Test]
    public async Task Delete_ExistingDocument_DeletesSuccessfully()
    {
        // Arrange
        var documentId = 1;
        var document = new PostgreSQL.Entities.Document
        {
            Id = documentId,
            FilePath = "uploads/test.pdf"
        };

        _mockRepo.Setup(r => r.GetByIdAsync(documentId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(document);

        // Act
        await _service.DeleteAsync(documentId);

        // Assert 
        _mockRepo.Verify(r => r.DeleteAsync(documentId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Delete_NonExistingDocument_DoesNotThrow()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((PostgreSQL.Entities.Document?)null);

        // Act & Assert
        Assert.DoesNotThrowAsync(() => _service.DeleteAsync(999));
    }
}
