using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DocumentManagement.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DocumentsController : ControllerBase
    {
        private readonly ILogger<DocumentsController> _logger;

        public DocumentsController(ILogger<DocumentsController> logger)
        {
            _logger = logger;
        }

        [HttpGet("hello")]
        public ActionResult<string> GetHelloWorld()
        {
            return Ok("Hello World from Documents API!");
        }
        
       
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<Document>), StatusCodes.Status200OK)]
        public ActionResult<IEnumerable<Document>> GetAllDocuments()
        {
            try
            {
                var documents = new List<Document>
                {
                    new Document { Id = 1, Title = "hardcoded1", FileName = "alexander.pdf", UploadDate = DateTime.UtcNow.AddDays(-1) },
                    new Document { Id = 2, Title = "hardcoded2", FileName = "stephanie.pdf", UploadDate = DateTime.UtcNow },
                    new Document { Id = 3, Title = "hardcoded3", FileName = "jasmin.pdf", UploadDate = DateTime.UtcNow }
                };

                return Ok(documents);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving documents");
                return StatusCode(500, "Internal server error while retrieving documents");
            }
        }
    }
}
public class Document
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string FileName { get; set; }
    public DateTime UploadDate { get; set; }
}

public class DocumentDto
{
    public string Title { get; set; }
    public IFormFile File { get; set; }
}

public class DocumentUpdateDto
{
    public string Title { get; set; }
}

public class SearchResult
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string FileName { get; set; }
    public double Relevance { get; set; }
}