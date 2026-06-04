namespace PaperlessREST.Tests.Unit;

/// <summary>
///     Shared fixture for <see cref="DocumentService" /> unit tests.
/// </summary>
/// <remarks>
///     Owns the strict mock for every collaborator, a <see cref="FakeLogger{T}" /> + collector,
///     and a pinned <see cref="FakeTimeProvider" /> so <c>CreatedAt</c>/<c>ProcessedAt</c> and the
///     <c>documents/yyyy-MM/</c> storage path are assertable. xUnit constructs one fresh instance
///     per test, so strict-mock setup never leaks across tests. <see cref="Dispose" /> always runs
///     <c>VerifyAll</c> + <c>VerifyNoOtherCalls</c> — every derived suite inherits the
///     "no unexpected collaborator call" proof, instead of each suite redefining it (or skipping it).
/// </remarks>
public abstract class DocumentServiceTestBase : IDisposable
{
	/// <summary>Fixed clock instant; pins <c>CreatedAt</c> and the <c>documents/yyyy-MM/</c> path month.</summary>
	protected static readonly DateTimeOffset FixedInstant = new(2026, 06, 03, 10, 15, 30, TimeSpan.Zero);

	private readonly FakeLogCollector _logs = new();
	private readonly MockRepository _mocks = new(MockBehavior.Strict) { DefaultValue = DefaultValue.Empty };

	protected FakeTimeProvider Clock { get; } = new();
	protected FakeLogger<DocumentService> Logger { get; }
	protected Mock<IDocumentRepository> Repository { get; }
	protected Mock<IDocumentSearchService> Search { get; }
	protected Mock<IDocumentStorageService> Storage { get; }
	protected Mock<IRabbitMqPublisher> Publisher { get; }

	protected DocumentServiceTestBase()
	{
		Repository = _mocks.Create<IDocumentRepository>();
		Storage = _mocks.Create<IDocumentStorageService>();
		Search = _mocks.Create<IDocumentSearchService>();
		Publisher = _mocks.Create<IRabbitMqPublisher>();
		Logger = new FakeLogger<DocumentService>(_logs);
		Clock.SetUtcNow(FixedInstant);
	}

	public void Dispose()
	{
		TestContext.Current.SendDiagnosticMessage("Full logs:\n{0}", _logs.GetFullLoggerText());
		_mocks.VerifyAll();
		_mocks.VerifyNoOtherCalls();
	}

	protected DocumentService CreateSut() =>
		new(Repository.Object, Storage.Object, Search.Object, Publisher.Object, Clock, Logger);

	/// <summary>Asserts exactly one log entry at <paramref name="level" /> whose message contains every fragment.</summary>
	protected void ShouldHaveLog(LogLevel level, params string[] fragments) =>
		_logs.GetSnapshot().Should().Contain(log =>
			log.Level == level &&
			fragments.All(fragment => log.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase)));

	/// <summary>
	///     Captures a <see cref="Document" />'s field values at call time. The repository mock hands back the
	///     same mutable reference the service later mutates, so assertions must snapshot it inside the callback.
	/// </summary>
	protected static Document Snapshot(Document document) => new()
	{
		Id = document.Id,
		FileName = document.FileName,
		Status = document.Status,
		CreatedAt = document.CreatedAt,
		StoragePath = document.StoragePath,
		Content = document.Content,
		ProcessedAt = document.ProcessedAt,
		Summary = document.Summary,
		SummaryGeneratedAt = document.SummaryGeneratedAt
	};

	/// <summary>A persisted document already in the given state, for transition-failure scenarios.</summary>
	protected static Document ExistingDocument(DocumentStatus status) => status switch
	{
		DocumentStatus.Pending => new DocumentBuilder().AsPending().Build(),
		DocumentStatus.Completed => new DocumentBuilder().AsCompleted().Build(),
		DocumentStatus.Failed => new DocumentBuilder().AsFailed().Build(),
		_ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
	};
}
