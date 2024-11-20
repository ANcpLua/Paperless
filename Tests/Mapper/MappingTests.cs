using AutoMapper;
using Contract;
using Microsoft.AspNetCore.Http;
using Moq;
using PaperlessServices.Entities;
using PaperlessServices.Mapping;
using PostgreSQL.Entities;

namespace Tests.Mapper;

[TestFixture]
public class MappingTests
{
    private IMapper _mapper;
    private MapperConfiguration _configuration;

    [OneTimeSetUp]
    public void Initialize()
    {
        _configuration = new MapperConfiguration(cfg =>
        {
            cfg.AddProfile<ServiceMapping>();
        });
        _mapper = _configuration.CreateMapper();
    }

    # region Mapping from Document to BlDocument
    
    [Test]
    public void Configuration_IsValid()
    {
        // Assert
        _configuration.AssertConfigurationIsValid();
    }

    [Test]
    public void Map_DocumentToBlDocument_MapsCorrectly()
    {
        // Arrange
        var source = new Document { Id = 1, Name = "Test Document", FilePath = "/test/path.pdf", DateUploaded = DateTime.UtcNow };

        // Act
        var result = _mapper.Map<BlDocument>(source);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Id, Is.EqualTo(source.Id));
            Assert.That(result.Name, Is.EqualTo(source.Name));
            Assert.That(result.FilePath, Is.EqualTo(source.FilePath));
            Assert.That(result.DateUploaded, Is.EqualTo(source.DateUploaded));
            Assert.That(result.File, Is.Null);  // Ignored
        });
    }

    [Test]
    public void Map_BlDocumentToDocument_WithZeroId_DoesNotMapId()
    {
        // Arrange
        var source = new BlDocument
        {
            Id = 0, // Default value
            Name = "Test Document",
            FilePath = "/test/path.pdf",
            DateUploaded = DateTime.UtcNow
        };
        
        // Act
        var result = _mapper.Map<Document>(source);

        // Assert
        Assert.That(result.Id, Is.EqualTo(0));
    }

    # endregion
    
    # region Mapping from DocumentUploadDto to BlDocument
    [Test]
    public void Map_DocumentUploadDtoToBlDocument_MapsCorrectly()
    {
        // Arrange
        var mockFile = new Mock<IFormFile>();
        var source = new DocumentDto
        {
            Name = "Test Upload",
            File = mockFile.Object
        };

        // Act
        var result = _mapper.Map<BlDocument>(source);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Name, Is.EqualTo(source.Name));
            Assert.That(result.File, Is.EqualTo(source.File));
            Assert.That(result.Id, Is.EqualTo(0)); //  default
            Assert.That(result.FilePath, Is.Null); // ignored
            Assert.That(result.DateUploaded.Date, Is.EqualTo(DateTime.UtcNow.Date));
        });
    }
    
    [Test]
    public void Map_NullDocumentToBlDocument_ReturnsNull()
    {
        // Act
        var result = _mapper.Map<BlDocument>(null);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Map_NullDocumentDtoToBlDocument_ReturnsNull()
    {
        // Act
        var result = _mapper.Map<BlDocument>(null);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Map_PartialDocumentDtoToBlDocument_SetsDefaults()
    {
        // Arrange
        var source = new DocumentDto
        {
            Name = "Partial Document"
        };

        // Act
        var result = _mapper.Map<BlDocument>(source);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Name, Is.EqualTo(source.Name));
            Assert.That(result.FilePath, Is.Null); // Default for missing FilePath
            Assert.That(result.DateUploaded, Is.Not.EqualTo(default(DateTime))); // Defaults to current UTC date
        });
    }

    [Test]
    public void Map_BlDocumentToDocumentDto_FileIsIgnored()
    {
        // Arrange
        var source = new BlDocument
        {
            Id = 1,
            Name = "Test Document",
            File = Mock.Of<IFormFile>()
        };

        // Act
        var result = _mapper.Map<DocumentDto>(source);

        // Assert
        Assert.That(result.File, Is.Null);
    }

    [Test]
    public void Map_BlDocumentToDocument_MapsFilePathCorrectly()
    {
        // Arrange
        var source = new BlDocument
        {
            Id = 1,
            Name = "Test Document",
            FilePath = "test.pdf"
        };

        // Act
        var result = _mapper.Map<Document>(source);

        // Assert
        Assert.That(result.FilePath, Is.EqualTo("test.pdf"));
    }

    [Test]
    public void Map_DocumentListToBlDocumentList_MapsCorrectly()
    {
        // Arrange
        var sourceList = new List<Document>
        {
            new Document { Id = 1, Name = "Doc1", FilePath = "/path1.pdf" },
            new Document { Id = 2, Name = "Doc2", FilePath = "/path2.pdf" }
        };

        // Act
        var resultList = _mapper.Map<List<BlDocument>>(sourceList);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(resultList.Count, Is.EqualTo(sourceList.Count));
            Assert.That(resultList[0].Name, Is.EqualTo(sourceList[0].Name));
            Assert.That(resultList[1].FilePath, Is.EqualTo(sourceList[1].FilePath));
        });
    }

    [Test]
    public void Map_DocumentWithLongFilePath_MapsCorrectly()
    {
        // Arrange
        var longFilePath = new string('a', 500); // 500 characters
        var source = new Document { Id = 1, Name = "Long Path Document", FilePath = longFilePath };

        // Act
        var result = _mapper.Map<BlDocument>(source);

        // Assert
        Assert.That(result.FilePath, Is.EqualTo(longFilePath));
    }

    [Test]
    public void Map_DocumentWithInvalidDateUploaded_MapsCorrectly()
    {
        // Arrange
        var source = new Document
        {
            Id = 1,
            Name = "Invalid Date Document",
            FilePath = "/path.pdf",
            DateUploaded = DateTime.MinValue
        };

        // Act
        var result = _mapper.Map<BlDocument>(source);

        // Assert
        Assert.That(result.DateUploaded, Is.EqualTo(DateTime.MinValue));
    }

    [Test]
    public void Map_DocumentToBlDocumentAndBack_RoundTrip()
    {
        // Arrange
        var source = new Document
        {
            Id = 1,
            Name = "Round Trip Document",
            Content = "Sample Content",
            FilePath = "/path.pdf",
            DateUploaded = DateTime.UtcNow,
            OcrText = "Sample OCR Text"
        };

        // Act
        var intermediate = _mapper.Map<BlDocument>(source);
        var result = _mapper.Map<Document>(intermediate);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Id, Is.EqualTo(source.Id));
            Assert.That(result.Name, Is.EqualTo(source.Name));
            Assert.That(result.Content, Is.EqualTo(source.Content));
            Assert.That(result.FilePath, Is.EqualTo(source.FilePath));
            Assert.That(result.DateUploaded, Is.EqualTo(source.DateUploaded));
            Assert.That(result.OcrText, Is.EqualTo(source.OcrText));
        });
    }

    [Test]
    public void Map_BlDocumentToDocumentDto_MapsCorrectly()
    {
        // Arrange
        var source = new BlDocument
        {
            Id = 1,
            Name = "Test Document",
            FilePath = "/test/path.pdf",
            DateUploaded = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            File = Mock.Of<IFormFile>()
        };

        // Act
        var result = _mapper.Map<DocumentDto>(source);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Name, Is.EqualTo(source.Name));
            Assert.That(result.FilePath, Is.EqualTo(source.FilePath));
            Assert.That(result.DateUploaded, Is.EqualTo(source.DateUploaded));
            Assert.That(result.File, Is.Null); // Ignored
        });
    }

    # endregion
    
    # region Mapping from BlDocumentDto to Document
    
    [Test]
    public void Map_DocumentDtoToBlDocument_MapsCorrectly()
    {
        // Arrange
        var source = new DocumentDto
        {
            Id = 1,
            Name = "Test Document",
            FilePath = "/test/path.pdf",
            DateUploaded = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc)
        };

        // Act
        var result = _mapper.Map<BlDocument>(source);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.Id, Is.EqualTo(source.Id));
            Assert.That(result.Name, Is.EqualTo(source.Name));
            Assert.That(result.FilePath, Is.EqualTo(source.FilePath));
            Assert.That(result.DateUploaded, Is.EqualTo(source.DateUploaded));
        });
    }
    
    # endregion
    
    # region Mapping from BlDocument to Document
    [Test]
    public void Map_BlDocumentToDocument_WithNonZeroId_MapsId()
    {
        // Arrange
        var source = new BlDocument
        {
            Id = 42,
            Name = "Test Document",
            FilePath = "/test/path.pdf",
            DateUploaded = DateTime.UtcNow
        };

        // Act
        var result = _mapper.Map<Document>(source);

        // Assert
        Assert.That(result.Id, Is.EqualTo(source.Id));
    }
    
    # endregion
}