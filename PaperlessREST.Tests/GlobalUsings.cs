// System namespaces

global using System.Diagnostics.CodeAnalysis;
global using System.IO.Abstractions;
global using System.Net;
global using System.Net.Http.Headers;
global using System.Net.Http.Json;
global using System.Text.Json;

// Microsoft namespaces
global using Microsoft.AspNetCore.Diagnostics;
global using Microsoft.AspNetCore.Http;
global using Microsoft.AspNetCore.Http.HttpResults;
global using Microsoft.AspNetCore.Http.Metadata;
global using Microsoft.AspNetCore.Mvc;
global using Microsoft.AspNetCore.Mvc.Testing;
global using Microsoft.AspNetCore.Routing;
global using Microsoft.AspNetCore.Routing.Patterns;
global using Microsoft.AspNetCore.TestHost;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.EntityFrameworkCore.Diagnostics;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.DependencyInjection.Extensions;
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
global using Testably.Abstractions;

// Testcontainers
global using DotNet.Testcontainers.Builders;
global using Testcontainers.Elasticsearch;
global using Testcontainers.Minio;
global using Testcontainers.PostgreSql;
global using Testcontainers.RabbitMq;


// Third-party packages
global using DotNetEnv;
global using Hangfire;
global using Hangfire.MemoryStorage;
global using Npgsql;

// Minio
global using Minio;
global using Minio.DataModel.Args;
global using Minio.DataModel.Response;

// RabbitMQ messaging
global using SWEN3.Paperless.RabbitMq;
global using SWEN3.Paperless.RabbitMq.Consuming;
global using SWEN3.Paperless.RabbitMq.Models;
global using SWEN3.Paperless.RabbitMq.Publishing;
global using SWEN3.Paperless.RabbitMq.Sse;

// Type alias to avoid ambiguity with Elastic.Clients.Elasticsearch.Document
global using Document = PaperlessREST.Features.DocumentManagement.Application.Document;

global using System.ComponentModel.DataAnnotations;

// Project namespaces - Configuration
global using PaperlessREST.Configuration;

// Project namespaces - API
global using PaperlessREST.API;

// Project namespaces - Document Management Feature
global using PaperlessREST.Features.DocumentManagement.Application;
global using PaperlessREST.Features.DocumentManagement.Infrastructure.Persistence;
global using PaperlessREST.Features.DocumentManagement.Infrastructure.Search;
global using PaperlessREST.Features.DocumentManagement.Infrastructure.Storage;
global using PaperlessREST.Features.DocumentManagement.Presentation.Dto;
global using PaperlessREST.Features.DocumentManagement.Presentation.Endpoints;

// Project namespaces - transport contracts
global using PaperlessREST.Contracts.BatchProcessing;
global using PaperlessREST.Contracts.DocumentManagement;
global using PaperlessREST.Contracts.Validation;

// ErrorOr for result pattern
global using ErrorOr;

// Project namespaces - Batch Processing Feature
global using PaperlessREST.Features.BatchProcessing.Application;

// Project namespaces - Event Processing Feature
global using PaperlessREST.Features.EventProcessing.Presentation;

// Test project namespace
global using PaperlessREST.Tests;
global using PaperlessREST.Tests.Integration;
