using Testably.Abstractions.Testing;
using TimeProvider = System.TimeProvider;

namespace PaperlessREST.Tests.Unit;

/// <summary>
///     Unit tests for BatchOrchestrator organized by interaction pattern.
///     ProcessAsync uses MockFileSystem for full flow tests.
///     ProcessFile uses Mock&lt;IFileSystem&gt; for direct method testing.
/// </summary>
public static class BatchOrchestratorTests
{
	#region Shared Helper Methods

	private static BatchOptions CreateOptions() =>
		new()
		{
			InputPath = InputPath,
			ArchivePath = ArchivePath,
			ErrorPath = ErrorPath,
			FilePattern = FilePattern,
			CronExpression = "* * * * *",
			TimeZoneId = "UTC"
		};

	#endregion

	// ═══════════════════════════════════════════════════════════════
	// PROCESS ASYNC TESTS
	// Full flow tests using MockFileSystem (real fake file system)
	// ═══════════════════════════════════════════════════════════════

	public sealed class ProcessAsync : IDisposable
	{
		#region Constructor

		public ProcessAsync()
		{
			_reportProcessor = _mocks.Create<IReportProcessor>();
			_logger = new FakeLogger<BatchOrchestrator>(_logCollector);

			// Setup default time provider behavior - loose mock, not verified
			_timeProvider.Setup(t => t.GetUtcNow()).Returns(DateTimeOffset.UtcNow);

			// Initialize directory structure
			_fileSystem.Directory.CreateDirectory(InputPath);
			_fileSystem.Directory.CreateDirectory(ArchivePath);
			_fileSystem.Directory.CreateDirectory(ErrorPath);
		}

		#endregion

		#region IDisposable

		public void Dispose()
		{
			TestContext.Current.SendDiagnosticMessage("Full logs:\n{0}", _logCollector.GetFullLoggerText());
			_mocks.VerifyAll();
			_mocks.VerifyNoOtherCalls();
		}

		#endregion

		#region Tests - Input Directory Missing

		[Fact]
		public async Task InputDirectoryMissing_CreatesDirectory()
		{
			// Arrange
			_fileSystem.Directory.Delete(InputPath);
			_fileSystem.Directory.Exists(InputPath).Should().BeFalse("precondition: input dir should be deleted");

			BatchOrchestrator sut = CreateSut();

			// Act
			await sut.ProcessAsync(CreateToken());

			// Assert
			_fileSystem.Directory.Exists(InputPath).Should().BeTrue("orchestrator should create input dir if missing");
		}

		#endregion

		#region Tests - File Pattern Filtering

		[Fact]
		public async Task NonMatchingFilePattern_IgnoresFiles()
		{
			// Arrange
			CreateTestFile("report.xml");
			await _fileSystem.File.WriteAllTextAsync(Path.Combine(InputPath, "readme.txt"), "Not XML",
				TestContext.Current.CancellationToken);

			_reportProcessor.Setup(p => p.ProcessAsync(
					It.Is<string>(s => s.Contains("report.xml")),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProcessingResult(1, 0));

			BatchOrchestrator sut = CreateSut();

			// Act
			await sut.ProcessAsync(CreateToken());

			// Assert
			_fileSystem.Directory.GetFiles(InputPath)
				.Should().ContainSingle(f => f.EndsWith("readme.txt"), "non-matching files should remain");
		}

		#endregion

		#region Tests - File Claiming Failure

		[Fact]
		public async Task FileClaimingFails_LogsWarningAndContinues()
		{
			// Arrange
			CreateTestFile("test.xml");
			await _fileSystem.File.WriteAllTextAsync(Path.Combine(InputPath, "test.xml.processing"), "already claimed",
				TestContext.Current.CancellationToken);

			_reportProcessor.Setup(p => p.ProcessAsync(
					It.Is<string>(s => s.Contains("test.xml.processing")),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProcessingResult(1, 0));

			BatchOrchestrator sut = CreateSut();

			// Act
			await sut.ProcessAsync(CreateToken());

			// Assert
			_logCollector.GetSnapshot()
				.Should().Contain(l =>
					l.Level == LogLevel.Warning &&
					l.Message.Contains("Could not claim", StringComparison.OrdinalIgnoreCase));
		}

		#endregion

		#region Tests - Source File Disappears

		[Fact]
		public async Task SourceFileDisappears_LogsWarningAndContinues()
		{
			// Arrange
			CreateTestFile("vanishing.xml");

			_reportProcessor.Setup(p => p.ProcessAsync(
					It.IsAny<string>(),
					It.IsAny<CancellationToken>()))
				.Callback<string, CancellationToken>((path, _) =>
				{
					// Simulate file being deleted during processing
					_fileSystem.File.Delete(path);
				})
				.ReturnsAsync(new ProcessingResult(1, 0));

			BatchOrchestrator sut = CreateSut();

			// Act
			await sut.ProcessAsync(CreateToken());

			// Assert
			_logCollector.GetSnapshot()
				.Should().Contain(l =>
					l.Level == LogLevel.Warning &&
					l.Message.Contains("no longer exists", StringComparison.OrdinalIgnoreCase));
		}

		#endregion

		#region Tests - Cancellation

		[Fact]
		public async Task CancellationRequested_ThrowsOperationCanceledException()
		{
			// Arrange
			CreateTestFile("file1.xml");
			CreateTestFile("file2.xml");

			CancellingJobCancellationToken cancellingToken = new();

			_reportProcessor.Setup(p => p.ProcessAsync(
					It.IsAny<string>(),
					It.IsAny<CancellationToken>()))
				.Callback<string, CancellationToken>((_, _) =>
				{
					cancellingToken.RequestCancellation();
				})
				.ReturnsAsync(new ProcessingResult(1, 0));

			BatchOrchestrator sut = CreateSut();

			// Act & Assert
			await Assert.ThrowsAsync<OperationCanceledException>(() => sut.ProcessAsync(cancellingToken));
		}

		#endregion

		#region Tests - Completion Logging

		[Fact]
		public async Task Completion_LogsProcessedAndQuarantinedCounts()
		{
			// Arrange
			CreateTestFile("good1.xml");
			CreateTestFile("good2.xml");
			CreateTestFile("bad.xml");

			_reportProcessor.Setup(p => p.ProcessAsync(
					It.Is<string>(s => s.Contains("good")),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProcessingResult(1, 0));

			_reportProcessor.Setup(p => p.ProcessAsync(
					It.Is<string>(s => s.Contains("bad")),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(Error.Validation("Report.Invalid", "Failed"));

			BatchOrchestrator sut = CreateSut();

			// Act
			await sut.ProcessAsync(CreateToken());

			// Assert
			_logCollector.GetSnapshot()
				.Should().Contain(l =>
					l.Level == LogLevel.Information &&
					l.Message.Contains("completed", StringComparison.OrdinalIgnoreCase) &&
					l.Message.Contains("2 processed", StringComparison.OrdinalIgnoreCase) &&
					l.Message.Contains("1 quarantined", StringComparison.OrdinalIgnoreCase));
		}

		#endregion

		#region Fields

		private readonly MockFileSystem _fileSystem = new();
		private readonly MockRepository _mocks = new(MockBehavior.Strict) { DefaultValue = DefaultValue.Empty };
		private readonly Mock<IReportProcessor> _reportProcessor;
		private readonly Mock<TimeProvider> _timeProvider = new();
		private readonly FakeLogCollector _logCollector = new();
		private readonly FakeLogger<BatchOrchestrator> _logger;

		#endregion

		#region Helper Methods

		private BatchOrchestrator CreateSut() =>
			new(
				Options.Create(CreateOptions()),
				_fileSystem,
				_timeProvider.Object,
				_reportProcessor.Object,
				_logger);

		private static TestJobCancellationToken CreateToken() => new();

		private void CreateTestFile(string fileName, string content = DefaultXmlContent) =>
			_fileSystem.File.WriteAllText(Path.Combine(InputPath, fileName), content);

		#endregion

		#region Tests - Empty Directory

		[Fact]
		public async Task EmptyInputDirectory_LogsNoFilesFound()
		{
			// Arrange
			BatchOrchestrator sut = CreateSut();

			// Act
			await sut.ProcessAsync(CreateToken());

			// Assert
			_logCollector.GetSnapshot()
				.Should().Contain(l =>
					l.Level == LogLevel.Debug &&
					l.Message.Contains("no files", StringComparison.OrdinalIgnoreCase));
		}

		[Fact]
		public async Task EmptyInputDirectory_DoesNotCallProcessor()
		{
			// Arrange
			BatchOrchestrator sut = CreateSut();

			// Act
			await sut.ProcessAsync(CreateToken());

			// Assert
			_reportProcessor.Verify(p => p.ProcessAsync(
				It.IsAny<string>(),
				It.IsAny<CancellationToken>()), Times.Never);
		}

		#endregion

		#region Tests - Single Valid File

		[Fact]
		public async Task SingleValidFile_ProcessesAndArchives()
		{
			// Arrange
			CreateTestFile("test.xml");

			_reportProcessor.Setup(p => p.ProcessAsync(
					It.Is<string>(s => s.Contains("test.xml.processing")),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProcessingResult(1, 0));

			BatchOrchestrator sut = CreateSut();

			// Act
			await sut.ProcessAsync(CreateToken());

			// Assert
			_fileSystem.Directory.GetFiles(InputPath).Should().BeEmpty("file should be moved out of input");
			_fileSystem.Directory.GetFiles(ArchivePath).Should().ContainSingle("file should be archived");
		}

		[Fact]
		public async Task SingleValidFile_LogsSuccessfulProcessing()
		{
			// Arrange
			CreateTestFile("report.xml");

			_reportProcessor.Setup(p => p.ProcessAsync(
					It.IsAny<string>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProcessingResult(5, 2));

			BatchOrchestrator sut = CreateSut();

			// Act
			await sut.ProcessAsync(CreateToken());

			// Assert
			_logCollector.GetSnapshot()
				.Should().Contain(l =>
					l.Level == LogLevel.Information &&
					l.Message.Contains("Successfully processed", StringComparison.OrdinalIgnoreCase));
		}

		#endregion

		#region Tests - Multiple Files

		[Fact]
		public async Task MultipleFiles_ProcessesAll()
		{
			// Arrange
			CreateTestFile("file1.xml");
			CreateTestFile("file2.xml");
			CreateTestFile("file3.xml");

			_reportProcessor.Setup(p => p.ProcessAsync(
					It.IsAny<string>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProcessingResult(1, 0));

			BatchOrchestrator sut = CreateSut();

			// Act
			await sut.ProcessAsync(CreateToken());

			// Assert
			_fileSystem.Directory.GetFiles(InputPath).Should().BeEmpty();
			_fileSystem.Directory.GetFiles(ArchivePath).Should().HaveCount(3);
		}

		[Fact]
		public async Task MultipleFiles_LogsFileCount()
		{
			// Arrange
			CreateTestFile("file1.xml");
			CreateTestFile("file2.xml");

			_reportProcessor.Setup(p => p.ProcessAsync(
					It.IsAny<string>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProcessingResult(1, 0));

			BatchOrchestrator sut = CreateSut();

			// Act
			await sut.ProcessAsync(CreateToken());

			// Assert
			_logCollector.GetSnapshot()
				.Should().Contain(l =>
					l.Level == LogLevel.Debug &&
					l.Message.Contains("2 file(s)", StringComparison.OrdinalIgnoreCase));
		}

		#endregion

		#region Tests - Processing Failure (Quarantine)

		[Fact]
		public async Task FileProcessingFails_MovesToErrorDirectory()
		{
			// Arrange
			CreateTestFile("invalid.xml");

			_reportProcessor.Setup(p => p.ProcessAsync(
					It.IsAny<string>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(Error.Validation("Report.InvalidXml", "XML parsing failed"));

			BatchOrchestrator sut = CreateSut();

			// Act
			await sut.ProcessAsync(CreateToken());

			// Assert
			_fileSystem.Directory.GetFiles(InputPath).Should().BeEmpty();
			_fileSystem.Directory.GetFiles(ErrorPath).Should().ContainSingle();
			_fileSystem.Directory.GetFiles(ArchivePath).Should().BeEmpty();
		}

		[Fact]
		public async Task FileProcessingFails_LogsError()
		{
			// Arrange
			CreateTestFile("invalid.xml");

			_reportProcessor.Setup(p => p.ProcessAsync(
					It.IsAny<string>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(Error.Validation("Report.InvalidXml", "XML parsing failed"));

			BatchOrchestrator sut = CreateSut();

			// Act
			await sut.ProcessAsync(CreateToken());

			// Assert
			_logCollector.GetSnapshot()
				.Should().Contain(l =>
					l.Level == LogLevel.Error &&
					l.Message.Contains("quarantined", StringComparison.OrdinalIgnoreCase));
		}

		[Fact]
		public async Task MixedSuccessAndFailure_HandlesEachCorrectly()
		{
			// Arrange
			CreateTestFile("good.xml");
			CreateTestFile("bad.xml");

			_reportProcessor.Setup(p => p.ProcessAsync(
					It.Is<string>(s => s.Contains("good.xml")),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProcessingResult(1, 0));

			_reportProcessor.Setup(p => p.ProcessAsync(
					It.Is<string>(s => s.Contains("bad.xml")),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(Error.Validation("Report.InvalidXml", "Parsing failed"));

			BatchOrchestrator sut = CreateSut();

			// Act
			await sut.ProcessAsync(CreateToken());

			// Assert
			_fileSystem.Directory.GetFiles(ArchivePath)
				.Should().ContainSingle(f => f.Contains("good.xml"));
			_fileSystem.Directory.GetFiles(ErrorPath)
				.Should().ContainSingle(f => f.Contains("bad.xml"));
		}

		#endregion

		#region Tests - Orphaned Processing Files

		[Fact]
		public async Task OrphanedProcessingFile_ReclaimsAndProcesses()
		{
			// Arrange
			await _fileSystem.File.WriteAllTextAsync(Path.Combine(InputPath, "orphan.xml.processing"), "<test/>",
				TestContext.Current.CancellationToken);

			_reportProcessor.Setup(p => p.ProcessAsync(
					It.Is<string>(s => s.Contains("orphan.xml.processing")),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProcessingResult(1, 0));

			BatchOrchestrator sut = CreateSut();

			// Act
			await sut.ProcessAsync(CreateToken());

			// Assert
			_fileSystem.Directory.GetFiles(InputPath).Should().BeEmpty();
			_fileSystem.Directory.GetFiles(ArchivePath).Should().ContainSingle();
		}

		[Fact]
		public async Task OrphanedProcessingFile_LogsReclaim()
		{
			// Arrange
			await _fileSystem.File.WriteAllTextAsync(Path.Combine(InputPath, "orphan.xml.processing"), "<test/>",
				TestContext.Current.CancellationToken);

			_reportProcessor.Setup(p => p.ProcessAsync(
					It.IsAny<string>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProcessingResult(1, 0));

			BatchOrchestrator sut = CreateSut();

			// Act
			await sut.ProcessAsync(CreateToken());

			// Assert
			_logCollector.GetSnapshot()
				.Should().Contain(l =>
					l.Level == LogLevel.Information &&
					l.Message.Contains("Reclaiming orphaned", StringComparison.OrdinalIgnoreCase));
		}

		#endregion

		#region Tests - Directory Creation

		[Fact]
		public async Task ArchiveDirectoryMissing_CreatesDirectory()
		{
			// Arrange
			_fileSystem.Directory.Delete(ArchivePath);
			CreateTestFile("test.xml");

			_reportProcessor.Setup(p => p.ProcessAsync(
					It.IsAny<string>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProcessingResult(1, 0));

			BatchOrchestrator sut = CreateSut();

			// Act
			await sut.ProcessAsync(CreateToken());

			// Assert
			_fileSystem.Directory.Exists(ArchivePath).Should().BeTrue();
			_fileSystem.Directory.GetFiles(ArchivePath).Should().ContainSingle();
		}

		[Fact]
		public async Task ErrorDirectoryMissing_CreatesDirectory()
		{
			// Arrange
			_fileSystem.Directory.Delete(ErrorPath);
			CreateTestFile("invalid.xml");

			_reportProcessor.Setup(p => p.ProcessAsync(
					It.IsAny<string>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(Error.Validation("Report.InvalidXml", "Parsing failed"));

			BatchOrchestrator sut = CreateSut();

			// Act
			await sut.ProcessAsync(CreateToken());

			// Assert
			_fileSystem.Directory.Exists(ErrorPath).Should().BeTrue();
			_fileSystem.Directory.GetFiles(ErrorPath).Should().ContainSingle();
		}

		#endregion
	}

	// ═══════════════════════════════════════════════════════════════
	// PROCESS FILE TESTS (requires internal access)
	// Direct tests using strict mocks for ProcessFileAsync
	// ═══════════════════════════════════════════════════════════════

	public sealed class ProcessFile : IDisposable
	{
		#region Constructor

		public ProcessFile()
		{
			_fs = _mocks.Create<IFileSystem>();
			_processor = _mocks.Create<IReportProcessor>();
			_logger = new FakeLogger<BatchOrchestrator>(_logCollector);
		}

		#endregion

		#region IDisposable

		public void Dispose()
		{
			TestContext.Current.SendDiagnosticMessage("Full logs:\n{0}", _logCollector.GetFullLoggerText());
			_mocks.VerifyAll();
			_mocks.VerifyNoOtherCalls();
		}

		#endregion

		#region Tests - File Not Found

		[Fact]
		public async Task WhenSourceFileDisappears_LogsWarning()
		{
			// Arrange
			SetupSuccessfulProcessing();
			SetupFileExists(false);
			SetupDirectoryCreate();
			SetupPathCombine();
			// No file move setup - file doesn't exist

			BatchOrchestrator sut = CreateSut();

			// Act
			await sut.ProcessFileAsync(
				TestFilePath,
				TestContext.Current.CancellationToken);

			// Assert
			_logCollector.GetSnapshot()
				.Should().Contain(l =>
					l.Level == LogLevel.Warning &&
					l.Message.Contains("no longer exists", StringComparison.OrdinalIgnoreCase));
		}

		#endregion

		#region Constants

		private const string TestFilePath = "/batch/input/report.xml.processing";
		private const string OriginalFileName = "report.xml";
		private const int ProcessedCount = 5;
		private const int SkippedCount = 2;

		#endregion

		#region Fields

		private readonly MockRepository _mocks = new(MockBehavior.Strict) { DefaultValue = DefaultValue.Empty };
		private readonly Mock<IFileSystem> _fs;
		private readonly Mock<IReportProcessor> _processor;
		private readonly Mock<TimeProvider> _time = new();
		private readonly FakeLogCollector _logCollector = new();
		private readonly FakeLogger<BatchOrchestrator> _logger;

		#endregion

		#region Helper Methods

		private BatchOrchestrator CreateSut()
		{
			_time.Setup(t => t.GetUtcNow()).Returns(DateTimeOffset.UtcNow);
			return new BatchOrchestrator(
				Options.Create(CreateOptions()),
				_fs.Object,
				_time.Object,
				_processor.Object,
				_logger);
		}

		private void SetupFileExists(bool exists = true) =>
			_fs.Setup(f => f.File.Exists(TestFilePath)).Returns(exists);

		private void SetupDirectoryCreate() =>
			_fs.Setup(f => f.DirectoryInfo.New(It.IsAny<string>()).Create());

		private void SetupPathCombine() =>
			_fs.Setup(f => f.Path.Combine(It.IsAny<string>(), It.IsAny<string>()))
				.Returns((string a, string b) => $"{a}/{b}");

		private void SetupFileMove(string destinationDir) =>
			_fs.Setup(f => f.File.Move(
				TestFilePath,
				It.Is<string>(s => s.Contains(destinationDir))));

		private void SetupFileMoveThrows(string destinationDir, Exception exception) =>
			_fs.Setup(f => f.File.Move(
					TestFilePath,
					It.Is<string>(s => s.Contains(destinationDir))))
				.Throws(exception);

		private void SetupSuccessfulProcessing(int processed = ProcessedCount, int skipped = SkippedCount) =>
			_processor.Setup(p => p.ProcessAsync(TestFilePath, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProcessingResult(processed, skipped));

		private void SetupFailedProcessing(string errorCode, string errorMessage) =>
			_processor.Setup(p => p.ProcessAsync(TestFilePath, It.IsAny<CancellationToken>()))
				.ReturnsAsync(Error.Validation(errorCode, errorMessage));

		#endregion

		#region Tests - Success Path

		[Fact]
		public async Task WhenProcessorSucceeds_ReturnsTrue()
		{
			// Arrange
			SetupSuccessfulProcessing();
			SetupFileExists();
			SetupDirectoryCreate();
			SetupPathCombine();
			SetupFileMove(ArchivePath);

			BatchOrchestrator sut = CreateSut();

			// Act
			bool result = await sut.ProcessFileAsync(
				TestFilePath,
				TestContext.Current.CancellationToken);

			// Assert
			result.Should().BeTrue();
		}

		[Fact]
		public async Task WhenProcessorSucceeds_MovesFileToArchive()
		{
			// Arrange
			SetupSuccessfulProcessing();
			SetupFileExists();
			SetupDirectoryCreate();
			SetupPathCombine();
			SetupFileMove(ArchivePath);

			BatchOrchestrator sut = CreateSut();

			// Act
			await sut.ProcessFileAsync(
				TestFilePath,
				TestContext.Current.CancellationToken);

			// Assert - Move is verified via mock verification in Dispose
		}

		[Fact]
		public async Task WhenProcessorSucceeds_LogsSuccess()
		{
			// Arrange
			SetupSuccessfulProcessing();
			SetupFileExists();
			SetupDirectoryCreate();
			SetupPathCombine();
			SetupFileMove(ArchivePath);

			BatchOrchestrator sut = CreateSut();

			// Act
			await sut.ProcessFileAsync(
				TestFilePath,
				TestContext.Current.CancellationToken);

			// Assert
			_logCollector.GetSnapshot()
				.Should().Contain(l =>
					l.Level == LogLevel.Information &&
					l.Message.Contains("Successfully processed", StringComparison.OrdinalIgnoreCase) &&
					l.Message.Contains(OriginalFileName, StringComparison.OrdinalIgnoreCase));
		}

		#endregion

		#region Tests - Failure Path

		[Fact]
		public async Task WhenProcessorFails_ReturnsFalse()
		{
			// Arrange
			SetupFailedProcessing("Report.InvalidXml", "XML parsing failed");
			SetupFileExists();
			SetupDirectoryCreate();
			SetupPathCombine();
			SetupFileMove(ErrorPath);

			BatchOrchestrator sut = CreateSut();

			// Act
			bool result = await sut.ProcessFileAsync(
				TestFilePath,
				TestContext.Current.CancellationToken);

			// Assert
			result.Should().BeFalse();
		}

		[Fact]
		public async Task WhenProcessorFails_MovesFileToErrorDirectory()
		{
			// Arrange
			SetupFailedProcessing("Report.InvalidXml", "XML parsing failed");
			SetupFileExists();
			SetupDirectoryCreate();
			SetupPathCombine();
			SetupFileMove(ErrorPath);

			BatchOrchestrator sut = CreateSut();

			// Act
			await sut.ProcessFileAsync(
				TestFilePath,
				TestContext.Current.CancellationToken);

			// Assert - Move to error path verified via mock in Dispose
		}

		[Fact]
		public async Task WhenProcessorFails_LogsError()
		{
			// Arrange
			SetupFailedProcessing("Report.InvalidXml", "XML parsing failed");
			SetupFileExists();
			SetupDirectoryCreate();
			SetupPathCombine();
			SetupFileMove(ErrorPath);

			BatchOrchestrator sut = CreateSut();

			// Act
			await sut.ProcessFileAsync(
				TestFilePath,
				TestContext.Current.CancellationToken);

			// Assert
			_logCollector.GetSnapshot()
				.Should().Contain(l =>
					l.Level == LogLevel.Error &&
					l.Message.Contains("quarantined", StringComparison.OrdinalIgnoreCase) &&
					l.Message.Contains("Report.InvalidXml", StringComparison.OrdinalIgnoreCase));
		}

		#endregion

		#region Tests - Move File Exceptions (MoveFileOrThrow branch)

		[Fact]
		public async Task WhenFileMoveThrows_ThrowsIOExceptionWithInfrastructureMessage()
		{
			// Arrange - Simulate file system error during move
			SetupSuccessfulProcessing();
			SetupFileExists();
			SetupDirectoryCreate();
			SetupPathCombine();
			SetupFileMoveThrows(ArchivePath, new UnauthorizedAccessException("Access denied"));

			BatchOrchestrator sut = CreateSut();

			// Act
			Func<Task> act = () => sut.ProcessFileAsync(
				TestFilePath,
				TestContext.Current.CancellationToken);

			// Assert - MoveFileOrThrow catches and rethrows as IOException
			IOException thrown = (await act.Should().ThrowAsync<IOException>()).Which;
			thrown.Message.Should().Contain("Infrastructure error moving file");
			thrown.InnerException.Should().BeOfType<UnauthorizedAccessException>();
		}

		[Fact]
		public async Task WhenFileMoveThrows_LogsError()
		{
			// Arrange
			SetupSuccessfulProcessing();
			SetupFileExists();
			SetupDirectoryCreate();
			SetupPathCombine();
			SetupFileMoveThrows(ArchivePath, new UnauthorizedAccessException("Access denied"));

			BatchOrchestrator sut = CreateSut();

			// Act
			try
			{
				await sut.ProcessFileAsync(
					TestFilePath,
					TestContext.Current.CancellationToken);
			}
			catch (IOException)
			{
				// Expected - swallow for log verification
			}

			// Assert - Should log error before rethrowing
			_logCollector.GetSnapshot()
				.Should().Contain(l =>
					l.Level == LogLevel.Error &&
					l.Message.Contains("Infrastructure error", StringComparison.OrdinalIgnoreCase) &&
					l.Message.Contains("Hangfire will retry", StringComparison.OrdinalIgnoreCase));
		}

		#endregion
	}

	#region Constants

	private const string InputPath = "/batch/input";
	private const string ArchivePath = "/batch/archive";
	private const string ErrorPath = "/batch/error";
	private const string FilePattern = "*.xml";
	private const string DefaultXmlContent = "<?xml version=\"1.0\"?><test/>";

	#endregion

	#region Test Helper Classes

	/// <summary>Test-safe implementation of IJobCancellationToken.</summary>
	private sealed class TestJobCancellationToken : IJobCancellationToken
	{
		public CancellationToken ShutdownToken => CancellationToken.None;
		public void ThrowIfCancellationRequested() { }
	}

	/// <summary>IJobCancellationToken that can be cancelled programmatically.</summary>
	private sealed class CancellingJobCancellationToken : IJobCancellationToken
	{
		private bool _cancelled;
		public CancellationToken ShutdownToken => CancellationToken.None;

		public void ThrowIfCancellationRequested()
		{
			if (_cancelled)
			{
				throw new OperationCanceledException("Job cancelled");
			}
		}

		public void RequestCancellation() => _cancelled = true;
	}

	#endregion
}
