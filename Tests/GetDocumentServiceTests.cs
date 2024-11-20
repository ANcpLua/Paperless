using AutoMapper;
using Microsoft.Extensions.Logging;
using Moq;
using PaperlessREST.AutoMapper;
using PaperlessREST.Services;
using PostgreSQL.Persistence;

namespace Tests;

[TestFixture] 
public class GetDocumentServiceTests
{
    private Mock<IDocumentRepository> _mockRepo = null!;
    private IMapper _mapper = null!;
    private Mock<ILogger<GetDocumentService>> _mockLogger = null!;
    private GetDocumentService _serviceTests = null!;

    [SetUp]
    public void Setup()
    {
        _mockRepo = new Mock<IDocumentRepository>();
        _mockLogger = new Mock<ILogger<GetDocumentService>>();
           
        var mapperConfig = new MapperConfiguration(cfg => 
        {
            cfg.AddProfile<DocumentProfile>();
        });
        _mapper = mapperConfig.CreateMapper();
           
        _serviceTests = new GetDocumentService(_mockRepo.Object, _mapper, _mockLogger.Object);
            
    }

    [Test]
    public async Task GetById_ExistingDocument_ReturnsDocument()
    {
        // Arrange 
        var documentId = 1;
        var document = new PostgreSQL.Entities.Document
        {
            Id = documentId,
            Name = "test.pdf", 
            FilePath = "uploads/test.pdf",
            DateUploaded = DateTime.UtcNow.AddMinutes(-1)
        };

        _mockRepo.Setup(r => r.GetByIdAsync(documentId, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(document);

        // Act
        var result = await _serviceTests.GetByIdAsync(documentId);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo(documentId));
    }

    [Test]
    public async Task GetById_NonExistingDocument_ReturnsNull()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((PostgreSQL.Entities.Document?)null);

        // Act
        var result = await _serviceTests.GetByIdAsync(999);

        // Assert
        Assert.That(result, Is.Null);
    }
}
