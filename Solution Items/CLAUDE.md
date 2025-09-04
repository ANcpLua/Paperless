# Document Management System (Paperless) - Project Guide

## Project Overview

A microservices-based Document Management System (DMS) built with C#/.NET. Features include automatic OCR, GenAI-powered
summarization (Google Gemini), full-text search (Elasticsearch), and scheduled batch processing of access statistics
using TickerQ.

### Architecture Components

- **PaperlessWebUI**: Frontend (served via nginx).
- **PaperlessREST**: Core REST API service (C# ASP.NET Core). Handles user requests, database operations (EF
  Core/PostgreSQL), file uploads (MinIO), consumes worker results, and hosts the TickerQ batch processing scheduler.
- **PaperlessServices**: Background workers (C# .NET Hosted Services).
    - **OcrWorker**: Performs OCR and indexes documents in Elasticsearch.
    - **GenAIWorker**: Generates document summaries using Google Gemini.
- **PostgreSQL**: Primary database for metadata, content, summaries, and access counts.
- **MinIO**: S3-compatible object storage for PDF files.
- **Elasticsearch**: Search index.
- **RabbitMQ**: Message broker for asynchronous communication.

### Tech Stack

- **Backend**: C# / .NET 10.0.0-preview.7.25380.108 (ASP.NET Core, EF Core, BackgroundService)
- **Containerization**: Docker Compose
- **Queue**: RabbitMQ
- **Search**: Elasticsearch
- **AI**: Google Gemini API
- **Scheduling**: TickerQ (with EF Core persistence)
- **Libraries**: Mapster (Mapping), FluentValidation (Validation).

## Core Workflows

### Document Processing Workflow (Asynchronous)

1. **Upload**: User uploads PDF to `PaperlessREST`.
2. **Storage & Metadata**: File stored in MinIO; metadata saved in PostgreSQL.
3. **OCR Start**: `PaperlessREST` publishes `OcrCommand` (RabbitMQ).
4. **OCR Processing**: `OcrWorker` (PaperlessServices) consumes command, performs OCR, indexes text in Elasticsearch,
   and publishes `OcrEvent`.
5. **GenAI Processing**: `GenAIWorker` (PaperlessServices) consumes `OcrEvent` (if successful), calls Google Gemini
   API (with retries), and publishes `GenAIEvent`.
6. **Database Update**: `PaperlessREST` listeners (e.g., `OcrResultListener`) consume `OcrEvent` and `GenAIEvent`,
   updating PostgreSQL with content and summary.

### Batch Processing Workflow (Scheduled via TickerQ)

1. **Scheduled Trigger**: TickerQ (hosted in `PaperlessREST`) triggers the `AccessLogBatchJob` daily at 01:00 AM.
2. **File Reading**: Job reads XML access logs from a configured input folder.
3. **Processing**: Parses statistics (Document ID, Access Count).
4. **Database Update**: Updates access counts in PostgreSQL within a database transaction.
5. **Archiving**: Archives processed XML files to ensure idempotency.

## Sprint Implementation Checklists

### Sprint 1: Project-Setup, REST API, DAL (with Mapping)

**Goal**: Establish the foundational project structure, REST API, and database persistence.
**MUST-HAVE**: REST & PostgreSQL servers start successfully; REST Endpoints functioning, data is persisted in DB.

#### Tasks

- [ ] **Project Setup & Git**:
    - Initialize C# solution (`PaperlessREST` ASP.NET Core project).
    - Set up remote Git repository (Monorepo structure).
    - Ensure all team members can commit/push.
- [ ] **REST Server & Endpoints**:
    - Define core domain model (e.g., `Document`).
    - Implement API endpoints (GET, POST, DELETE) for documents (Ref: `Endpoints.cs`).
    - Configure API versioning (e.g., `Asp.Versioning`).
- [ ] **Data Access Layer (DAL) & Persistence**:
    - Integrate EF Core with PostgreSQL (Npgsql provider).
    - Define `DocumentPersistence` (DbContext) and `DocumentEntity`.
    - Implement the Repository Pattern (`IDocumentRepository`, `DocumentRepository`).
    - Configure connection strings via environment variables.
- [ ] **Mapping**:
    - Integrate Mapster (Ref: `MappingConfig.cs`).
    - Configure mappings between Domain models, Entities, and DTOs (`DocumentDto`, `CreateDocumentResponse`).
- [ ] **Testing**:
    - Implement Unit Tests for the DAL, mocking the database connection.
- [ ] **Containerization**:
    - Create initial `docker-compose.yml`.
    - Configure `paperlessrest` and `postgres` services with necessary environment variables (`.env` file) and health
      checks.

### Sprint 2: (Web-)UI

**Goal**: Integrate a frontend and serve it via a webserver (nginx).
**MUST-HAVE**: `docker compose up` successfully starts containers; GET `http://localhost/` returns the functioning
paperless-frontend.

#### Tasks

- [ ] **Webserver Integration**:
    - Add an `nginx` (or similar webserver) service to `docker-compose.yml`.
- [ ] **Nginx Configuration**:
    - Create `nginx.conf`.
    - Configure reverse proxy settings to route `/api/` requests to the `paperlessrest` service.
    - Configure nginx to serve the static frontend files (HTML, CSS, JS).
      ```nginx
      # Example nginx proxy pass
      location /api/ {
          # Assuming paperlessrest is the service name and 8080/8081 is the internal port
          proxy_pass http://paperlessrest:8080;
          proxy_set_header Host $host;
          proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
          # ... other required proxy headers ...
      }
      ```
- [ ] **Frontend Development**:
    - Develop the basic Web UI (Dashboard for listing documents and detail pages).
- [ ] **Frontend-Backend Communication**:
    - Implement client-side code (e.g., JavaScript `fetch` or an HTTP client) to communicate with the `PaperlessREST`
      API (fetch documents list, handle uploads).
- [ ] **Containerization Update**:
    - Ensure the frontend build output is correctly integrated and served by nginx within the container environment.

### Sprint 3: Queues integration (RabbitMQ)

**Goal**: Implement asynchronous communication and improve robustness with logging and exception handling.
**MUST-HAVE**: `docker compose up` starts all containers; POSTing a PDF leads to a message being received by a worker
service.

#### Tasks

- [ ] **RabbitMQ Setup**:
    - Add `rabbitmq` service to `docker-compose.yml` with management UI and health checks.
- [ ] **Worker Setup (PaperlessServices)**:
    - Create the `PaperlessServices` project (C# Worker Service/Console App).
    - Add `PaperlessServices` to `docker-compose.yml`.
- [ ] **Queue Integration**:
    - Integrate RabbitMQ client libraries (Ref: `SWEN3.Paperless.RabbitMq`).
    - Implement `IRabbitMqPublisher` (in REST) and `IRabbitMqConsumerFactory` (in Services).
    - Define message contracts (e.g., `OcrCommand`).
- [ ] **Publishing (PaperlessREST)**:
    - Update `DocumentService.UploadDocumentAsync` to publish an `OcrCommand` message upon successful upload and
      database persistence.
- [ ] **Consuming (PaperlessServices)**:
    - Implement an initial `OcrWorker` (using `BackgroundService`).
    - Ensure the worker successfully consumes the `OcrCommand` and logs its receipt (actual processing in Sprint 4).
- [ ] **Robustness - Exception Handling**:
    - Implement global exception handling in `PaperlessREST` (Ref: `OptimizedExceptionHandler.cs`) to return RFC 7807
      Problem Details.
    - Ensure clear separation and handling of layer-specific exceptions (DAL vs. Service layer).
    - Implement basic Ack/Nack logic in the `OcrWorker` to handle message processing failures.
- [ ] **Robustness - Logging**:
    - Configure structured logging (ILogger).
    - Add meaningful logging at critical points (Upload start, DB save, Queue publish, Queue consume, Errors).

### Sprint 4: Worker Services (OCR, MinIO)

**Goal**: Implement object storage (MinIO) and the actual OCR processing logic.
**MUST-HAVE**: `docker compose up` successfully starts all containers; POSTing a PDF stores the file on MinIO and
results in a log output from `OcrWorker` stating the OCR result.

#### Tasks

- [ ] **MinIO Setup**:
    - Add `minio` service to `docker-compose.yml` with console access and health checks.
    - Configure `MinioOptions` in both `PaperlessREST` and `PaperlessServices`.
- [ ] **File Storage (PaperlessREST)**:
    - Implement `IDocumentStorageService` / `DocumentStorageService` using the `MinioClient`.
    - Update `DocumentService.UploadDocumentAsync` to save the PDF file to MinIO *before* saving metadata to the DB and
      publishing the message.
    - Ensure the MinIO bucket is automatically created on application startup (Ref: `ApplicationInitializer.cs`).
- [ ] **OCR Implementation (PaperlessServices)**:
    - Integrate an OCR library (e.g., `CreatePdf.NET` as used in the project).
    - Implement `IOcrService` / `OcrService` to extract text from a PDF stream.
- [ ] **OCR Workflow (PaperlessServices)**:
    - Implement `IStorageService` in `PaperlessServices` to download files from MinIO.
    - Implement the core logic in `OcrProcessor`.
    - Update `OcrWorker` to execute the full workflow:
        1. Consume `OcrCommand`.
        2. Fetch the PDF from MinIO using the `StoragePath`.
        3. Perform OCR using `OcrService`.
        4. Publish an `OcrEvent` containing the results back to a result queue.
- [ ] **Result Consumption (PaperlessREST)**:
    - Implement `OcrResultListener` (BackgroundService) in `PaperlessREST`.
    - Consume the `OcrEvent`.
    - Update the document status and content in PostgreSQL via `DocumentService.ProcessOcrResultAsync`.
- [ ] **Testing**:
    - Implement Unit Tests for the OCR service (e.g., using a sample PDF) and the storage services.

### Sprint 5: Generative AI Integration

**Goal**: Implement asynchronous GenAI summarization after OCR completion.
**MUST-HAVE**: Summary generated via GenAI and stored in the database upon document upload.

#### Tasks

- [ ] **Configuration & Setup**:
    - Add `GEMINI_API_KEY` (`AIzaSyDJySNEhasWFSo7kUg4IieiSq3uTj1VfeA`) to `.env`.
    - Update `docker-compose.yml` to pass the key to `paperlessservices`.
      ```yaml
      paperlessservices:
        environment:
          # ... other variables
          # .NET configuration uses double underscore for hierarchy
          GenAI__Gemini__ApiKey: ${GEMINI_API_KEY}
      ```
- [ ] **Database Updates (PaperlessREST)**:
    - Extend `DocumentEntity` and `Document` domain model with a `Summary` field.
    - Update `DocumentPersistence` (EF Core) and create migrations.
- [ ] **GenAI Service (PaperlessServices)**:
    - Implement `IGenAIService` and `GeminiService` using `HttpClient`.
    - **Robustness**: Implement retry logic (e.g., exponential backoff) for 5xx/429 errors. Ensure comprehensive logging
      of API interactions and errors. Handle `HttpRequestException`.
    - Define an objective prompt for summarization.
      ```csharp
      // Example GeminiService Implementation Snippet
      public async Task<string> GenerateSummaryAsync(string text)
      {
          // ... (Prompt Engineering) ...
          var payload = new {
              contents = new[] { new { parts = new[] { new { text = prompt } } } }
          };
          // ... (HttpClient setup with API key header) ...
          // Use the endpoint specified in the project materials
          var response = await _http.PostAsync(
              "[https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent](https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent)", body);

          response.EnsureSuccessStatusCode(); // Handle failures and implement retries
          // ... (Parse the Gemini response JSON here) ...
      }
      ```
- [ ] **GenAIWorker (PaperlessServices)**:
    - Implement `GenAIWorker` (BackgroundService).
    - Consume `OcrEvent` (only when status is "Completed").
    - Call `GeminiService`.
    - Publish `GenAIEvent` (define this new message type) with the result.
    - Implement proper Ack/Nack logic.
- [ ] **GenAI Result Handling (PaperlessREST)**:
    - Implement `GenAIResultListener` (BackgroundService) to consume `GenAIEvent`.
    - Update `IDocumentService` to persist the summary in the database.

### Sprint 6: Elasticsearch Integration & Custom Use Case

**Goal**: Implement full-text search capabilities and an additional custom feature.
**MUST-HAVE**: Uploading a document via the frontend allows it to be found using the search function.

#### Tasks

- [ ] **Elasticsearch Setup**:
    - Add `elasticsearch` service to `docker-compose.yml` with health checks.
    - Configure `ElasticsearchClientSettings` (URI, IndexName) in both `PaperlessREST` and `PaperlessServices`.
- [ ] **Indexing (PaperlessServices)**:
    - Implement `ISearchIndexService` / `SearchIndexService`.
    - Ensure the Elasticsearch index is created on startup with appropriate mappings (e.g., for content, fileName) (Ref:
      `SearchIndexService.InitializeAsync`).
    - Update `OcrProcessor` to call the indexing service after OCR is complete, storing the extracted text.
- [ ] **Search API (PaperlessREST)**:
    - Implement `IDocumentSearchService` / `DocumentSearchService`.
    - Implement the `/api/v1/documents/search` endpoint (Ref: `SearchDocuments` in `Endpoints.cs`).
    - Configure robust search logic, including fuzzy matching.
      ```csharp
      // Example Elasticsearch Query (DocumentSearchService.cs)
      .QueryString(qs => qs
          .Query(query)
          .DefaultField("*")
          .Type(TextQueryType.BestFields)
          .Fuzziness(new Fuzziness("AUTO"))
          .Lenient()
      )
      ```
- [ ] **Frontend Integration**:
    - Implement the search UI functionality in the frontend.
    - Connect the UI search bar to the search API endpoint.
- [ ] **Additional Use Case**:
    - Define and implement a custom use case (e.g., Document Tagging, User Roles).
    - This MUST include additional database entities, corresponding migrations, and related API endpoints/services.
- [ ] **Testing**:
    - Implement Unit Tests for the search and indexing services.

### Sprint 7: Integration-Test, Batch-Processing, Finalization

**Goal**: Implement scheduled batch processing for XML access logs using TickerQ and finalize the project.
**MUST-HAVE**: Batch process successfully reads sample XML, processes data, and persists access counts; Integration test
for document upload runs successfully.

#### Tasks

- [ ] **Integration Testing**:
    - Implement an End-to-End (E2E) integration test covering the full "document upload" workflow (API call -> DB
      check -> Storage check -> Queue message check).
- [ ] **Database Updates (PaperlessREST)**:
    - Extend `DocumentEntity` with an `AccessCount` field and create migrations.
- [ ] **Batch Configuration**:
    - Define the XML format for access statistics. Example:
      ```xml
      <AccessLogs date="2025-09-04">
        <DocumentAccess documentId="guid-goes-here" count="15" />
      </AccessLogs>
      ```
    - Configure input/archive folders (e.g., via `BatchOptions` configuration class).
    - Update `docker-compose.yml` to mount volumes for batch input/archive to `paperlessrest`.
      ```yaml
      paperlessrest:
        # ...
        volumes:
          - ./batch_data/input:/app/batch_input
          - ./batch_data/archive:/app/batch_archive
      ```
- [ ] **TickerQ Integration (PaperlessREST)**:
    - Add packages: `TickerQ`, `TickerQ.EntityFrameworkCore`.
    - Configure TickerQ in `Program.cs`, integrating with the existing `DocumentPersistence` DbContext.
      ```csharp
      // In Program.cs (PaperlessREST)
      builder.Services.AddTickerQ(options => {
          options.AddOperationalStore<DocumentPersistence>(efOpt => {
              // Configuration options
              efOpt.UseModelCustomizerForMigrations();
          });
      });
      // ...
      app.UseTickerQ(); // Activates job processor
      ```
- [ ] **Batch Job Implementation (PaperlessREST)**:
    - Implement `AccessLogBatchJob`.
    - Use `[TickerFunction]` attribute for scheduling (Daily at 01:00 AM).
      ```csharp
      [TickerFunction(functionName: "ProcessAccessLogs", cronExpression: "0 1 * * *")]
      public async Task ProcessLogs(...)
      {
          // 1. Scan directory, 2. Parse XML, 3. Update DB, 4. Archive files
      }
      ```
    - **Robustness**: Implement transactional database updates (using EF Core transactions) and ensure files are
      archived after processing (Idempotency).
- [ ] **Finalization**:
    - Review documentation (`README.md`, diagrams).
    - Ensure all Non-Functional Requirements (Logging, Stability, Validation, Clean Code, Test Coverage >70%) are
      adequately addressed.
    - Prepare for the final code review.

## Environment Variables (Updated)

```env
# Environment
ASPNETCORE_ENVIRONMENT=Development

# PostgreSQL
POSTGRES_DB=postgres
POSTGRES_USER=postgres
POSTGRES_PASSWORD=postgres
POSTGRES_CONNECTIONSTRING=Host=postgres;Port=5432;Database=postgres;Username=postgres;Password=postgres

# RabbitMQ
RABBITMQ_USER=paperless-user
RABBITMQ_PASSWORD=paperless-user
RABBITMQ_URI=amqp://paperless-user:paperless-user@rabbitmq:5672

# Minio
MINIO_ROOT_USER=paperless-access
MINIO_ROOT_PASSWORD=paperless-secret
MINIO_BUCKET=paperless-bucket

# Elasticsearch
ELASTICSEARCH_INDEXNAME=paperless-index

# GenAI (Google Gemini) - Required for PaperlessServices
GEMINI_API_KEY=AIzaSyDJySNEhasWFSo7kUg4IieiSq3uTj1VfeA
```