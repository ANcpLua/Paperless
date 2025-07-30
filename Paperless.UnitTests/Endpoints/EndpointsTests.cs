// File: DocumentEndpointsTests.cs  (.NET10 | NUnit4.4.0 | Moq4.20.72 | 2025‑07‑30)

using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using Paperless.UnitTests.Builders;
using PaperlessREST;
using PaperlessREST.Extensions;
using PaperlessREST.Services;

namespace Paperless.UnitTests.Endpoints;

[TestFixture]
[Category("Endpoints")]
public sealed class DocumentEndpointsTests
{
    private const string FileA = "a.pdf";
    private const string FileB = "b.pdf";
    private const string UploadFileName = "upload.pdf";

    private static readonly Document DocA = new DocumentBuilder()
        .WithFileName(FileA)
        .Build();

    private static readonly DocumentDto DtoA = new DocumentDtoBuilder()
        .WithId(DocA.Id)
        .WithFileName(FileA)
        .WithStatus(DocA.Status)
        .WithCreatedAt(DocA.CreatedAt)
        .Build();

    private static readonly Document DocB = new DocumentBuilder()
        .WithFileName(FileB)
        .Build();

    private static readonly DocumentDto DtoB = new DocumentDtoBuilder()
        .WithId(DocB.Id)
        .WithFileName(FileB)
        .WithStatus(DocB.Status)
        .WithCreatedAt(DocB.CreatedAt)
        .Build();

    private readonly CancellationToken _ct = CancellationToken.None;
    private Mock<IDocumentService> _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new Mock<IDocumentService>(MockBehavior.Strict)
            .SetupAllProperties();
    }

    [TearDown]
    public void TearDown()
    {
        _service.VerifyAll();
        _service.VerifyNoOtherCalls();
    }

    private static IEnumerable<object[]> DocumentsAndDtos()
    {
        yield return [DocA, DtoA];
        yield return [DocB, DtoB];
    }

    [Test, TestCaseSource(nameof(DocumentsAndDtos))]
    [Description("GetDocumentById returns expected DTO for an existing document")]
    public async Task GetDocumentById_ReturnsExpectedDto(Document doc, DocumentDto expectedDto)
    {
        _service
            .Setup(s => s.GetDocumentByIdAsync(doc.Id, _ct))
            .ReturnsAsync(doc);

        var result = await DocumentEndpoints.GetDocumentById(doc.Id, _service.Object, _ct);

        Assert.That(result, Is.InstanceOf<Results<Ok<DocumentDto>, NotFound>>());
        await Assert.ThatAsync(
            () => Task.FromResult(result.Result),
            Is.InstanceOf<Ok<DocumentDto>>()
        );

        using (Assert.EnterMultipleScope())
        {
            var ok = (Ok<DocumentDto>)result.Result;
            Assert.That(ok.Value, Is.Not.Null);
            Assert.That(ok.Value!.Id, Is.EqualTo(expectedDto.Id));
            Assert.That(ok.Value.FileName, Is.EqualTo(expectedDto.FileName));
            Assert.That(ok.Value.Status, Is.EqualTo(expectedDto.Status));
            Assert.That(ok.Value.CreatedAt, Is.EqualTo(expectedDto.CreatedAt));
        }
    }

    [Test]
    [Description("GetDocumentById returns 404 NotFound for a missing document")]
    public async Task GetDocumentById_WhenMissing_ReturnsNotFound()
    {
        var missingId = Guid.NewGuid();
        _service
            .Setup(s => s.GetDocumentByIdAsync(missingId, _ct))
            .ReturnsAsync((Document?)null);

        var result = await DocumentEndpoints.GetDocumentById(missingId, _service.Object, _ct);

        await Assert.ThatAsync(
            () => Task.FromResult(result.Result),
            Is.InstanceOf<NotFound>()
        );
    }

    [Test]
    [Description("GetDocuments returns 200 OK with a list of the most recent documents")]
    public async Task GetDocuments_WhenCalled_ReturnsOkWithList()
    {
        var docs = new[] { DocA, DocB };
        _service
            .Setup(s => s.GetRecentDocumentsAsync(_ct))
            .Returns(docs.ToAsyncEnumerable());

        var response = await DocumentEndpoints.GetDocuments(_service.Object, _ct);

        Assert.That(response, Is.InstanceOf<Ok<List<DocumentDto>>>());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Value, Has.Count.EqualTo(2));
            Assert.That(response.Value![0].FileName, Is.EqualTo(FileA));
            Assert.That(response.Value[1].FileName, Is.EqualTo(FileB));
        }
    }

    [Test]
    [Description("SearchDocuments returns 200 OK with matching DocumentDto results")]
    public async Task SearchDocuments_WhenMatchesFound_ReturnsOkWithResults()
    {
        var query = new SearchQueryBuilder()
            .WithQuery("test")
            .WithLimit(2)
            .Build();

        var hits = new List<object> { DtoA, DtoB };
        _service
            .Setup(s => s.SearchDocumentsAsync(query.Query, query.Limit, _ct))
            .Returns(hits.ToAsyncEnumerable());

        var response = await DocumentEndpoints.SearchDocuments(query, _service.Object, _ct);

        Assert.That(response, Is.InstanceOf<Ok<List<object>>>());
        using (Assert.EnterMultipleScope())
        {
            var list = response.Value!;
            Assert.That(list, Has.Count.EqualTo(2));
            Assert.That(((DocumentDto)list[0]).FileName, Is.EqualTo(FileA));
            Assert.That(((DocumentDto)list[1]).FileName, Is.EqualTo(FileB));
        }
    }

    [Test]
    [Description("UploadDocument returns 202 AcceptedAtRoute with the new document’s ID")]
    public async Task UploadDocument_WhenRequestValid_ReturnsAcceptedAtRoute()
    {
        var req = new UploadDocumentRequestBuilder()
            .WithValidPdf()
            .Build();

        var doc = new DocumentBuilder()
            .WithFileName(UploadFileName)
            .Build();

        _service
            .Setup(s => s.UploadDocumentAsync(req, _ct))
            .ReturnsAsync(doc);

        var response = await DocumentEndpoints.UploadDocument(req, _service.Object, _ct);

        Assert.That(response, Is.InstanceOf<AcceptedAtRoute<CreateDocumentResponse>>());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RouteName, Is.EqualTo(nameof(DocumentEndpoints.GetDocumentById)));
            Assert.That(response.RouteValues["id"], Is.EqualTo(doc.Id));
            Assert.That(response.Value!.Id, Is.EqualTo(doc.Id));
        }
    }

    [Test]
    [Description("DeleteDocument returns 204 NoContent when the document exists")]
    public async Task DeleteDocument_WhenExists_ReturnsNoContent()
    {
        var id = Guid.NewGuid();
        _service
            .Setup(s => s.DeleteDocumentAsync(id, _ct))
            .Returns(Task.CompletedTask);

        var response = await DocumentEndpoints.DeleteDocument(id, _service.Object, _ct);

        Assert.That(response, Is.InstanceOf<NoContent>());
    }

    [Test]
    [Description("GetDocuments using LINQ‑to‑Mocks returns OK with latest list and no extra calls")]
    public async Task GetDocuments_WhenCalled_UsingLinqToMocks()
    {
        var docs = new[] { DocA, DocB };
        var svc = Mock.Of<IDocumentService>(s =>
            s.GetRecentDocumentsAsync(_ct) == docs.ToAsyncEnumerable());

        var response = await DocumentEndpoints.GetDocuments(svc, _ct);

        Assert.That(response, Is.InstanceOf<Ok<List<DocumentDto>>>());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Value, Has.Count.EqualTo(2));
            Assert.That(response.Value, Has.Some.Property(nameof(DocumentDto.FileName)).EqualTo(FileA));
            Assert.That(response.Value, Has.Some.Property(nameof(DocumentDto.FileName)).EqualTo(FileB));
        }

        var svcMock = Mock.Get(svc);
        svcMock.Verify(s => s.GetRecentDocumentsAsync(_ct), Times.Once);
        svcMock.VerifyNoOtherCalls();
    }
}