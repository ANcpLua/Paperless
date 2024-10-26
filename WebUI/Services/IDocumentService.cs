using System.Reflection.Metadata;
using Microsoft.AspNetCore.Components.Forms;
using Document=WebUI.Document;

public interface IDocumentService
{
    Task<string> GetHelloWorldAsync();
}