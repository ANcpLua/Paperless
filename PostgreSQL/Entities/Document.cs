namespace PostgreSQL.Entities;

public class Document
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;
    public string FilePath { get; set; } = default!;
    public DateTime DateUploaded { get; set; }
}
