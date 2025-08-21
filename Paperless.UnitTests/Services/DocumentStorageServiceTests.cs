// using System.Net;
// using Microsoft.Extensions.Logging;
// using Microsoft.Extensions.Options;
// using Minio;
// using Minio.DataModel.Args;
// using Minio.DataModel.Response;
// using Moq;
// using Paperless.UnitTests.Helpers;
// using PaperlessREST;
// using PaperlessREST.Services;
//
// namespace Paperless.UnitTests.Services;
//
// [TestFixture]
// public class DocumentStorageServiceTests
// {
//     private Mock<IMinioClient> _minio;
//     private Mock<IOptions<MinioOptions>> _options;
//     private FakeLoggerFluent<DocumentStorageService> _logger;
//     private DocumentStorageService _sut;
//     private readonly CancellationToken _ct = CancellationToken.None;
//
//     [SetUp]
//     public void Setup()
//     {
//         _minio = new Mock<IMinioClient>();
//         _options = new Mock<IOptions<MinioOptions>>();
//         _options.Setup(o => o.Value).Returns(new MinioOptions { BucketName = "test-bucket" });
//         _logger = FakeLoggerFluent.CreateStrict<DocumentStorageService>();
//         _sut = new DocumentStorageService(_minio.Object, _options.Object, _logger);
//     }
//
//     [TearDown]
//     public void TearDown()
//     {
//         // Verify all logs were expected in strict mode
//         _logger.VerifyNoOtherCalls();
//           
//     }
//
//     [Test]
//     public async Task DeleteAsync_WhenSuccessful_ReturnsTrueAndLogsInformation()
//     {
//         // Arrange
//         const string storagePath = "docs/test.pdf";
//         _minio.Setup(m => m.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), _ct))
//             .Returns(Task.CompletedTask);
//
//         // Act
//         var result = await _sut.DeleteAsync(storagePath, _ct);
//
//         // Assert
//         Assert.That(result, Is.True);
//         _logger.VerifyLog(LogLevel.Information, "Document removed from storage at {StoragePath}");
//     }
//
//     [Test]
//     public async Task DeleteAsync_WhenMinioThrows_ReturnsFalseAndLogsError()
//     {
//         // Arrange
//         const string storagePath = "docs/fail.pdf";
//         var ex = new Exception("Minio delete error");
//         _minio.Setup(m => m.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), _ct))
//             .ThrowsAsync(ex);
//
//         // Act
//         var result = await _sut.DeleteAsync(storagePath, _ct);
//
//         // Assert
//         Assert.That(result, Is.False);
//         _logger.VerifyLog(LogLevel.Error, "Failed to remove document from storage at {StoragePath}");
//     }
//
//     [Test]
//     public async Task UploadAsync_WhenSuccessful_ReturnsPathAndLogsDebugAndInformation()
//     {
//         // Arrange
//         var stream = new MemoryStream([1, 2, 3]);
//         const string path = "docs/upload.pdf";
//         const long size = 3L;
//         _minio.Setup(m => m.PutObjectAsync(It.IsAny<PutObjectArgs>(), _ct))
//             .ReturnsAsync(() =>
//                 new PutObjectResponse(HttpStatusCode.OK, string.Empty, new Dictionary<string, string>(), size, path));
//
//         // Act
//         var result = await _sut.UploadAsync(stream, path, size, _ct);
//
//         // Assert
//         Assert.That(result, Is.EqualTo(path));
//         _logger.VerifyLog(LogLevel.Debug, "Uploading to MinIO - Bucket: {BucketName}, Path: {Path}, Size: {Size}");
//         _logger.VerifyLog(LogLevel.Information, "Document uploaded to storage at {StoragePath}");
//     }
//
//     [Test]
//     public void UploadAsync_WhenMinioThrows_ThrowsExceptionAndLogsError()
//     {
//         // Arrange
//         var stream = new MemoryStream([5]);
//         const string path = "docs/error.pdf";
//         var ex = new InvalidOperationException("upload failed");
//         _minio.Setup(m => m.PutObjectAsync(It.IsAny<PutObjectArgs>(), _ct))
//             .ThrowsAsync(ex);
//
//         // Act & Assert
//         var thrown = Assert.ThrowsAsync<InvalidOperationException>(() =>
//             _sut.UploadAsync(stream, path, stream.Length, _ct)
//         );
//         Assert.That(thrown, Is.EqualTo(ex));
//         _logger.VerifyLog(LogLevel.Debug, "Uploading to MinIO - Bucket: {BucketName}, Path: {Path}, Size: {Size}");
//         _logger.VerifyLog(LogLevel.Error, "Failed to upload document to MinIO");
//     }
// }