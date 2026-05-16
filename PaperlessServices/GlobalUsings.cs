// System namespaces

global using System.ComponentModel.DataAnnotations;
global using System.Diagnostics.CodeAnalysis;

// Microsoft namespaces
global using Microsoft.Extensions.Options;

// Third-party packages
global using CreatePdf.NET;
global using DotNetEnv;
global using Elastic.Clients.Elasticsearch;
global using Elastic.Clients.Elasticsearch.IndexManagement;
global using ErrorOr;
global using Minio;
global using Minio.DataModel.Args;

// Type aliases for disambiguation
global using ExistsResponse = Elastic.Clients.Elasticsearch.IndexManagement.ExistsResponse;

// RabbitMQ messaging
global using SWEN3.Paperless.RabbitMq;
global using SWEN3.Paperless.RabbitMq.Consuming;
global using SWEN3.Paperless.RabbitMq.GenAI;
global using SWEN3.Paperless.RabbitMq.Models;
global using SWEN3.Paperless.RabbitMq.Publishing;

// Project namespaces - Configuration
global using PaperlessServices.Configuration;

// Project namespaces - OCR Processing Feature
global using PaperlessServices.Features.OcrProcessing.Application;
global using PaperlessServices.Features.OcrProcessing.Infrastructure.PdfExtractor;
global using PaperlessServices.Features.OcrProcessing.Infrastructure.Search;
global using PaperlessServices.Features.OcrProcessing.Infrastructure.Storage;
global using PaperlessServices.Features.OcrProcessing.Presentation;

// Project namespaces - Extensions
