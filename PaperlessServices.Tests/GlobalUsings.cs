// System namespaces

global using System.Text.Json;

// Microsoft namespaces
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Logging.Abstractions;
global using Microsoft.Extensions.Logging.Testing;
global using Microsoft.Extensions.Options;
global using Microsoft.Extensions.Time.Testing;

// xUnit.v3
global using Xunit;
global using Xunit.v3;

// Testing packages
global using AwesomeAssertions;
global using CreatePdf.NET;
global using Moq;

// Testcontainers
global using DotNet.Testcontainers.Builders;
global using Testcontainers.Elasticsearch;
global using Testcontainers.Minio;
global using Testcontainers.RabbitMq;

// Third-party packages
global using DotNetEnv;
global using Elastic.Clients.Elasticsearch;
global using ErrorOr;

// Minio
global using Minio;
global using Minio.DataModel;
global using Minio.DataModel.Args;
global using Minio.Exceptions;

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

// Test project namespace
global using PaperlessServices.Tests;
global using PaperlessServices.Tests.Integration;
