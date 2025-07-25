using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace PaperlessREST;

public record SearchQuery(
    [property: Required(ErrorMessage = "Search query is required")]
    [property: MinLength(1, ErrorMessage = "Search query must be at least 1 character")]
    [property: MaxLength(500, ErrorMessage = "Search query must not exceed 500 characters")]
    [property: Description("Full-text search query")]
    string Query,
    [property: Range(1, 100, ErrorMessage = "Limit must be between 1 and 100")]
    [property: Description("Maximum number of results")]
    [property: DefaultValue(10)]
    int Limit = 10
);