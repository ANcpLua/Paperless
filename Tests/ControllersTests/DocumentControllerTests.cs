using AutoMapper;
using Contract;
using EasyNetQ;
using Elastic.Clients.Elasticsearch;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PaperlessREST.Controllers;
using PaperlessServices.BL;
using PaperlessServices.MinIoStorage;

namespace Tests.ControllersTests;

[TestFixture]
public class DocumentControllerTests
{
    [SetUp]
    public void Setup()
    {
        _documentService = new Mock<IDocumentService>();
        _storageService = new Mock<IMinioStorageService>();
        _elasticClient = new Mock<ElasticsearchClient>();
        _bus = new Mock<IBus>();
        _mapper = new Mock<IMapper>();

        var pubSub = new Mock<IPubSub>();
        _bus.Setup(x => x.PubSub).Returns(pubSub.Object);

        _controller = new DocumentController(
            _documentService.Object,
            _storageService.Object,
            _elasticClient.Object,
            _bus.Object,
            _mapper.Object
        );

        _cancellationToken = CancellationToken.None;
    }

    private DocumentController _controller;
    private Mock<IDocumentService> _documentService;
    private Mock<IMinioStorageService> _storageService;
    private Mock<ElasticsearchClient> _elasticClient;
    private Mock<IBus> _bus;
    private Mock<IMapper> _mapper;

    private CancellationToken _cancellationToken;

    [Test]
    public async Task Upload_ValidInput_ReturnsOkWithDocumentDto()
    {
        // Arrange
        var document = CreateTestDocument();
        var file = new Mock<IFormFile>();

        _documentService
            .Setup(x => x.Upload(It.IsAny<DocumentDto>(), _cancellationToken))
            .ReturnsAsync(document);

        // Act
        var result = await _controller.Upload(document.Name, file.Object, _cancellationToken);

        // Assert
        var okResult = result.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null, "Expected OkObjectResult");

        var dto = okResult?.Value as DocumentDto;
        Assert.That(dto, Is.Not.Null, "Expected a DocumentDto in OkObjectResult");
        Assert.Multiple(() =>
        {
            Assert.That(dto!.Id, Is.EqualTo(document.Id));
            Assert.That(dto.Name, Is.EqualTo(document.Name));
        });
    }

    [Test]
    public async Task Get_ValidId_ReturnsOkWithDocumentDto()
    {
        // Arrange
        var document = CreateTestDocument();
        _documentService
            .Setup(x => x.GetDocument(document.Id, _cancellationToken))
            .ReturnsAsync(document);

        // Act
        var result = await _controller.Get(document.Id, _cancellationToken);

        // Assert
        var okResult = result.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null, "Expected OkObjectResult");

        var dto = okResult?.Value as DocumentDto;
        Assert.That(dto, Is.Not.Null, "Expected a DocumentDto in OkObjectResult");
        Assert.That(dto!.Id, Is.EqualTo(document.Id));
    }

    [Test]
    public async Task GetAll_ValidRequest_ReturnsOkWithDocumentList()
    {
        // Arrange
        var documents = new List<DocumentDto>
        {
            CreateTestDocument(1, "test1.pdf"),
            CreateTestDocument(2, "test2.pdf")
        };

        _documentService
            .Setup(x => x.GetAllDocuments(_cancellationToken))
            .ReturnsAsync(documents);

        // Act
        var result = await _controller.GetAll(_cancellationToken);

        // Assert
        var okResult = result.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null, "Expected OkObjectResult");

        var docList = okResult?.Value as IEnumerable<DocumentDto>;
        var documentDtos = docList?.ToList();
        Assert.That(documentDtos, Is.Not.Null, "Expected a list of DocumentDto");
        Assert.That(documentDtos!, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Download_ValidId_ReturnsFileStream()
    {
        // Arrange
        var document = CreateTestDocument();
        var fileStream = new MemoryStream();

        _documentService.Setup(x => x.GetDocument(document.Id, _cancellationToken))
                        .ReturnsAsync(document);

        _storageService.Setup(x => x.GetFileAsync(document.FilePath, _cancellationToken))
                       .ReturnsAsync(fileStream);

        // Act
        var result = await _controller.Download(document.Id, _cancellationToken);

        // Assert
        Assert.That(result, Is.InstanceOf<FileStreamResult>(), "Expected a FileStreamResult");

        var fileResult = result as FileStreamResult;
        Assert.That(fileResult, Is.Not.Null, "FileStreamResult should not be null");
        Assert.That(fileResult!.FileStream, Is.SameAs(fileStream), "FileStream should match the mock");
        Assert.That(fileResult.FileDownloadName, Is.EqualTo(document.Name));
    }

    [Test]
    public async Task Delete_ValidId_ReturnsNoContent()
    {
        // Arrange
        var documentId = 1;

        // Act
        var result = await _controller.Delete(documentId, _cancellationToken);

        // Assert
        Assert.That(result, Is.TypeOf<NoContentResult>(), "Expected a NoContentResult");
        _documentService.Verify(x => x.DeleteDocument(documentId, _cancellationToken), Times.Once);
    }

    private static DocumentDto CreateTestDocument(int id = 1, string name = "test.pdf")
    {
        return new DocumentDto
        {
            Id = id,
            Name = name,
            FilePath = $"path/{name}"
        };
    }
}
