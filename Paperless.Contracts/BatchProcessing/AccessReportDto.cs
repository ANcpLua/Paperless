using System.Diagnostics.CodeAnalysis;
using System.Xml.Serialization;

namespace Paperless.Contracts.BatchProcessing;

[XmlRoot("accessReport")]
[ExcludeFromCodeCoverage(Justification = "DTO record - XML serialization tested via ReportProcessor tests")]
public sealed record AccessReportDto
{
	[XmlAttribute("date")] public required string Date { get; init; }

	// List<T> + [XmlElement]: XmlSerializer calls Add() on existing list, respects initializer
	[XmlElement("document")] public List<Doc> Documents { get; } = [];

	[ExcludeFromCodeCoverage(Justification = "DTO - pure data container for XML deserialization")]
	public sealed record Doc
	{
		[XmlAttribute("id")] public Guid Id { get; init; }

		[XmlAttribute("accessCount")] public long Count { get; init; }
	}
}
