using Contract.Logger;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace Tests;

[TestFixture]
public class MinioTests
{
    [SetUp]
    public async Task Setup()
    {
        var configBuilder = new ConfigurationBuilder()
            .AddJsonFile("service-appsettings.json")
            .AddEnvironmentVariables();

        _configuration = configBuilder.Build();

        _minioClient = new MinioClient()
            .WithEndpoint(_configuration["MinIO:Endpoint"] ?? "localhost:9000")
            .WithCredentials(
                _configuration["MinIO:AccessKey"],
                _configuration["MinIO:SecretKey"])
            .Build();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = new OperationLogger(
            loggerFactory.CreateLogger<OperationLogger>(),
            _configuration["Environment"] ?? "Development");

        var found = await _minioClient.BucketExistsAsync(new BucketExistsArgs().WithBucket(TestBucket));
        if (!found)
        {
            await _minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(TestBucket));

            var operationAttr = new LogOperationAttribute("Minio", "Setup");
            await _logger.LogOperation(operationAttr, nameof(Setup),
                [$"Created bucket: {TestBucket}"]);
        }
    }

    [TearDown]
    public void TearDown()
    {
        if (_minioClient is IDisposable disposable) disposable.Dispose();
    }

    private IMinioClient _minioClient = null!;
    private IConfiguration _configuration = null!;
    private IOperationLogger _logger = null!;

    private const string TestBucket = "documents";
    private const string TestFileName = "HelloWorld.pdf";

    private readonly string _testFilePath =
        Path.Combine(TestContext.CurrentContext.TestDirectory, "IntegrationTests", "HelloWorld.pdf");

    [Test]
    [Order(1)]
    public async Task UploadFile_ToMinio_Succeeds()
    {
        // Arrange
        await using var fileStream = File.OpenRead(_testFilePath);
        var putObjectArgs = new PutObjectArgs()
            .WithBucket(TestBucket)
            .WithObject(TestFileName)
            .WithStreamData(fileStream)
            .WithObjectSize(fileStream.Length)
            .WithContentType("application/pdf");

        // Act
        await _minioClient.PutObjectAsync(putObjectArgs);

        // Verify the object exists
        var stat = await _minioClient.StatObjectAsync(
            new StatObjectArgs()
                .WithBucket(TestBucket)
                .WithObject(TestFileName));

        // Assert
        stat.Should().NotBeNull();
        stat.ObjectName.Should().Be(TestFileName);

        var operationAttr = new LogOperationAttribute("Minio", "UploadFile");
        await _logger.LogOperation(operationAttr, nameof(UploadFile_ToMinio_Succeeds),
            [$"Uploaded file: {TestFileName}"]);
    }

    [Test]
    [Order(2)]
    public async Task GetFile_FromMinio_Succeeds()
    {
        // Arrange
        using var memoryStream = new MemoryStream();

        var getObjectArgs = new GetObjectArgs()
            .WithBucket(TestBucket)
            .WithObject(TestFileName)
            .WithCallbackStream(stream =>
            {
                stream.CopyTo(memoryStream);
                memoryStream.Position = 0;
            });

        // Act
        await _minioClient.GetObjectAsync(getObjectArgs);

        // Assert
        memoryStream.Length.Should().BeGreaterThan(0);
        memoryStream.Position = 0;

        await using var downloadedStream = File.OpenRead(_testFilePath);
        downloadedStream.Length.Should().Be(memoryStream.Length);

        var operationAttr = new LogOperationAttribute("Minio", "GetFile");
        await _logger.LogOperation(operationAttr, nameof(GetFile_FromMinio_Succeeds),
            [$"Downloaded file: {TestFileName}"]);
    }

    [Test]
    [Order(3)]
    public async Task DeleteFile_FromMinio_Succeeds()
    {
        // Arrange
        var removeObjectArgs = new RemoveObjectArgs()
            .WithBucket(TestBucket)
            .WithObject(TestFileName);

        // Act
        await _minioClient.RemoveObjectAsync(removeObjectArgs);

        Func<Task> act = async () => await _minioClient.StatObjectAsync(
            new StatObjectArgs()
                .WithBucket(TestBucket)
                .WithObject(TestFileName));

        // Assert
        await act.Should().ThrowAsync<ObjectNotFoundException>();

        var operationAttr = new LogOperationAttribute("Minio", "DeleteFile");
        await _logger.LogOperation(operationAttr, nameof(DeleteFile_FromMinio_Succeeds),
            [$"Deleted file: {TestFileName}"]);
    }
}
