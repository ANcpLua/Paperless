namespace PaperlessREST.Features.BatchProcessing.Application;

public sealed class ReportProcessor(
	IFileSystem fs,
	IDocumentAccessRepository repo,
	ILogger<ReportProcessor> logger) : IReportProcessor
{
	private const string SchemaFileName = "accessReport.xsd";
	private static readonly XmlSerializer s_serializer = new(typeof(AccessReportDto));

	private XmlSchemaSet Schemas => field ??= LoadSchemas();

	public async Task<ErrorOr<ProcessingResult>> ProcessAsync(string path, CancellationToken ct)
	{
		ErrorOr<(AccessReportDto Dto, DateOnly Date)> parseResult = await ParseAndValidateXmlAsync(path);
		if (parseResult.IsError)
		{
			return parseResult.Errors;
		}

		(AccessReportDto dto, DateOnly date) = parseResult.Value;

		if (dto.Documents.Count is 0)
		{
			logger.LogInformation("Report for {Date} contains no documents", date);
			return new ProcessingResult(0, 0);
		}

		Guid[] docIds = [.. dto.Documents.Select(d => d.Id).Distinct()];
		HashSet<Guid> knownIds = [.. await repo.GetExistingDocumentIdsAsync(docIds, ct)];
		int skippedCount = docIds.Length - knownIds.Count;

		if (skippedCount > 0)
		{
			Guid[] unknown = [.. docIds.Except(knownIds)];
			logger.LogWarning(
				"Report for {Date} references {SkippedCount} unknown documents (will be skipped): {Ids}",
				date, skippedCount, string.Join(", ", unknown.Take(5)));
		}

		(Guid DocumentId, long AccessCount)[] items =
		[
			.. dto.Documents
				.Where(d => knownIds.Contains(d.Id))
				.GroupBy(d => d.Id)
				.Select(g => (DocumentId: g.Key, AccessCount: g.Sum(d => d.Count)))
		];

		await repo.UpsertDailyAccessAsync(date, items, ct);

		return new ProcessingResult(items.Length, skippedCount);
	}

	private XmlSchemaSet LoadSchemas()
	{
		XmlSchemaSet schemas = new();
		string schemaPath = fs.Path.Combine(AppContext.BaseDirectory, "Schemas", SchemaFileName);

		using Stream schemaStream = fs.FileStream.New(schemaPath, FileMode.Open, FileAccess.Read, FileShare.Read);
		using XmlReader schemaReader = XmlReader.Create(schemaStream);
		schemas.Add("", schemaReader);
		schemas.Compile();
		return schemas;
	}

	private async Task<ErrorOr<(AccessReportDto Dto, DateOnly Date)>> ParseAndValidateXmlAsync(string path)
	{
		try
		{
			await using FileSystemStream stream =
				fs.FileStream.New(path, FileMode.Open, FileAccess.Read, FileShare.Read);

			XmlReaderSettings settings = new()
			{
				Schemas = Schemas,
				ValidationType = ValidationType.Schema,
				DtdProcessing = DtdProcessing.Prohibit,
				IgnoreWhitespace = true,
				Async = true,
				XmlResolver = null
			};

			List<string> validationErrors = [];
			settings.ValidationEventHandler += (_, e) =>
			{
				if (e.Severity == XmlSeverityType.Error)
				{
					validationErrors.Add(e.Message);
				}
			};

			using XmlReader reader = XmlReader.Create(stream, settings);
			AccessReportDto dto = (AccessReportDto)s_serializer.Deserialize(reader)!;

			if (validationErrors.Count > 0)
			{
				return ReportErrors.InvalidSchema(string.Join("; ", validationErrors));
			}

			if (!DateOnly.TryParseExact(dto.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
				    DateTimeStyles.None, out DateOnly date))
			{
				return ReportErrors.InvalidDate(dto.Date);
			}

			// Handle empty document list (valid case - no documents to process)
			// List<T> + [XmlElement] guarantees Documents is never null
			if (dto.Documents.Count is 0)
			{
				return (dto, date);
			}

			int emptyGuidIndex = dto.Documents.FindIndex(d => d.Id == Guid.Empty);
			if (emptyGuidIndex >= 0)
			{
				return ReportErrors.InvalidGuid(emptyGuidIndex);
			}

			return (dto, date);
		}
		catch (FileNotFoundException)
		{
			return ReportErrors.FileNotFound(path);
		}
		catch (XmlException ex)
		{
			return ReportErrors.InvalidXml(ex.Message);
		}
		catch (InvalidOperationException ex)
		{
			return ReportErrors.InvalidSchema(ex.Message);
		}
		catch (XmlSchemaException ex)
		{
			return ReportErrors.InvalidSchema(ex.Message);
		}
	}
}
