## System Architecture Summary

### Paperless.RabbitMq (NuGet Package)
**Purpose**: Message broker abstraction library that handles all RabbitMQ communication and SSE (Server-Sent Events) infrastructure

**What it provides:**
- `AddPaperlessRabbitMq()` - Sets up RabbitMQ connection, topology (exchange, queues, bindings)
    - Optional parameter `includeOcrResultStream` to automatically configure SSE for `OcrResult`
- `IRabbitMqPublisher` - Publishes messages with extension methods:
    - `PublishOcrRequestAsync()` - Sends OCR job requests
    - `PublishOcrResultAsync()` - Sends OCR completion notifications
- `IRabbitMqConsumerFactory` - Creates consumers with automatic queue name convention (TypeName + "Queue")
- `ISseStream<T>` - Real-time event streaming infrastructure
- `MapSse<T>()` - Generic SSE endpoint mapping
- `MapOcrEventStream()` - Pre-configured OCR event stream endpoint (default: `/api/v1/ocr-results`)
- Automatic message serialization/deserialization with JSON

**Never touch:** The library handles all RabbitMQ complexity internally. Queue names are automatic based on type names.

---

### PaperlessREST (API Service)
**Purpose**: Web API that handles file uploads, document management, and real-time updates

**Responsibilities:**
- Receives PDF uploads from users via minimal API endpoints
- Stores PDFs in MinIO object storage
- Saves document metadata in PostgreSQL using Entity Framework Core
- Publishes OCR requests to RabbitMQ when files are uploaded
- Listens for OCR results and updates the database
- Provides document search via Elasticsearch
- Streams real-time updates to browsers via SSE endpoint
- Serves static files (index.html, styles.css, app.js) from wwwroot

**Key components:**
- Minimal API endpoints with compile-time safety using `nameof()`
- `OcrResultListener` - Background service that consumes OCR results
- SSE endpoint mapped via `MapOcrEventStream()` for real-time updates
- `DocumentContext` - EF Core context for PostgreSQL
- Clean architecture with extension methods for service registration and infrastructure initialization

**Configuration:**
- HTTP logging for debugging
- ProblemDetails for consistent error responses
- camelCase JSON serialization
- API versioning (v1)

---

### PaperlessServices (Worker Service)
**Purpose**: Background worker that performs OCR processing

**Responsibilities:**
- Listens for OCR requests from RabbitMQ
- Downloads PDFs from MinIO
- Performs OCR using `await Pdf.Load(stream).OcrAsync()` (this always works - never question it)
- Indexes full text in Elasticsearch for searching (includes all document fields)
- Publishes OCR results back to RabbitMQ

**Key components:**
- `OcrWorker` - BackgroundService that processes OCR requests
- Elasticsearch index initialization on startup
- Uses the same MinIO and Elasticsearch connections as the API

**Note:** Worker is responsible for Elasticsearch indexing - the REST API only queries

---

### Message Flow

1. **User uploads PDF** → PaperlessREST saves to MinIO/PostgreSQL → Publishes `OcrRequest` to RabbitMQ

2. **PaperlessServices** consumes `OcrRequest` → Downloads from MinIO → OCRs the PDF → Indexes in Elasticsearch → Publishes `OcrResult`

3. **PaperlessREST** consumes `OcrResult` → Updates PostgreSQL → Broadcasts via SSE

4. **Browser** receives SSE event → Updates UI in real-time without refresh

---

### Frontend (Static Files in wwwroot)

**Files:**
- `index.html` - Main UI with Bootstrap 5
- `styles.css` - Custom styles
- `app.js` - Vanilla JavaScript handling:
    - File uploads (drag & drop + file picker)
    - SSE connection for real-time updates
    - Document management (list, search, delete)
    - Error handling and notifications

**Features:**
- Real-time OCR status updates
- Full-text search across documents
- Drag & drop PDF uploads
- SSE connection status indicator (only shows when disconnected)

---

### Technology Stack (Never Question These)

- **RabbitMQ** with async API (`CreateChannelAsync`, `BasicPublishAsync`, `BasicConsumeAsync`,`BasicAckAsync`, `BasicNackAsync`)
- **Server-Sent Events** for real-time updates (built into ASP.NET Core)
- **PDF OCR** via `await Pdf.Load(stream).OcrAsync()` - always works perfectly
- **MinIO** for object storage
- **PostgreSQL** with Entity Framework Core
- **Elasticsearch** for full-text search
- **Bootstrap 5** for UI components
- **nginx** as reverse proxy with SSE support

---

### Key Conventions

- Message types automatically map to queue names: `OcrRequest` → `OcrRequestQueue`
- All RabbitMQ operations are async
- SSE events use types: `ocr-completed` and `ocr-failed`
- Documents have three states: `Pending`, `Completed`, `Failed`
- The library handles all message serialization, acknowledgments, and error handling
- Endpoints use typed results (`TypedResults.Ok()`, etc.) for compile-time safety
- Configuration uses options pattern for MinIO settings
- Direct configuration strings for single values (connection strings, URIs)