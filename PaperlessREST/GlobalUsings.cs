// System namespaces

global using System.ComponentModel;
global using System.ComponentModel.DataAnnotations;
global using System.Diagnostics;
global using System.Diagnostics.CodeAnalysis;
global using System.Globalization;
global using System.IO.Abstractions;
global using System.Runtime.CompilerServices;
global using System.Text.Json;
global using System.Text.Json.Serialization;
global using System.Threading.RateLimiting;
global using System.Xml;
global using System.Xml.Schema;
global using System.Xml.Serialization;

// Microsoft namespaces
global using Microsoft.AspNetCore.Diagnostics;
global using Microsoft.AspNetCore.Http;
global using Microsoft.AspNetCore.Http.HttpResults;
global using Microsoft.AspNetCore.HttpLogging;
global using Microsoft.AspNetCore.Mvc;
global using Microsoft.AspNetCore.OpenApi;
global using Microsoft.AspNetCore.OutputCaching;
global using Microsoft.AspNetCore.RateLimiting;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.EntityFrameworkCore.Design;
global using Microsoft.EntityFrameworkCore.Diagnostics;
global using Microsoft.Extensions.Options;

// Third-party packages
global using Asp.Versioning;
global using Asp.Versioning.Builder;
global using DotNetEnv;
global using Elastic.Clients.Elasticsearch;
global using Elastic.Clients.Elasticsearch.QueryDsl;
global using ErrorOr;
global using Hangfire;
global using Hangfire.PostgreSql;
global using JetBrains.Annotations;
global using Mapster;
global using Minio;
global using Minio.DataModel.Args;
global using Npgsql;
global using RabbitMQ.Client.Exceptions;
global using Scalar.AspNetCore;
global using Testably.Abstractions;

// Type aliases for disambiguation

// RabbitMQ messaging
global using SWEN3.Paperless.RabbitMq;
global using SWEN3.Paperless.RabbitMq.Consuming;
global using SWEN3.Paperless.RabbitMq.Models;
global using SWEN3.Paperless.RabbitMq.Publishing;
global using SWEN3.Paperless.RabbitMq.Sse;

// Project namespaces - Configuration
global using PaperlessREST.Configuration;

// Project namespaces - Document Management Feature
global using PaperlessREST.Features.DocumentManagement.Application;
global using PaperlessREST.Features.DocumentManagement.Infrastructure.Persistence;
global using PaperlessREST.Features.DocumentManagement.Infrastructure.Search;
global using PaperlessREST.Features.DocumentManagement.Infrastructure.Storage;
global using PaperlessREST.Features.DocumentManagement.Presentation.Dto;
global using PaperlessREST.Features.DocumentManagement.Presentation.Endpoints;

// Project namespaces - Batch Processing Feature
global using PaperlessREST.Features.BatchProcessing.Application;

// Project namespaces - transport contracts
global using PaperlessREST.Contracts.BatchProcessing;
global using PaperlessREST.Contracts.DocumentManagement;
global using PaperlessREST.Contracts.Validation;

// Project namespaces - Event Processing Feature
global using PaperlessREST.Features.EventProcessing.Presentation;
