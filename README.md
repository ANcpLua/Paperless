<h2 align="center">Alexander Nachtann, Jasmin Mondre and Stephanie Rauscher
<p>2024/2025 SWKOM</h2>
</p>
<h3 align="center">A document management system with features including archiving, OCR processing, tagging, and full-text search.
<p></p>

## Sprint Overview and Technologies
  
### Sprint 1: REST Service API
- ASP.NET Core 7.0+
- Swagger/OpenAPI (Swashbuckle.AspNetCore)
- API Controller implementation(hardcoded with openapi is this intended? Could be easly done manualy e.g. https://github.com/ANcpLua/TourPlanner/blob/main/API/Controllers/TourController.cs or https://git.technikum-wien.at/swen/swen3/csharp/npaperless-api-mock/-/tree/main/Controllers?ref_type=heads 

### Sprint 2: Web-UI Integration
- Option A: React maybe even with Fluent UI https://react.fluentui.dev/?path=/docs/concepts-introduction--docs
- Option B: Blazor WebAssembly (Microsoft.AspNetCore.Components.WebAssembly)

### Sprint 3: DAL (Persistence)
- Entity Framework Core (Microsoft.EntityFrameworkCore)
- PostgreSQL (Npgsql.EntityFrameworkCore.PostgreSQL)

### Sprint 4: Message Broker
- RabbitMQ (RabbitMQ.Client)
- MassTransit (MassTransit, MassTransit.RabbitMQ)

### Sprint 5: Worker Services
- Hangfire (Hangfire.AspNetCore)
- TesseractOCR (Tesseract)

### Sprint 6: ElasticSearch
- NEST (Elasticsearch.Net, NEST)

### Sprint 7: Integration-Test
The best way to avoid failure is to fail constantly

## Cross-Cutting Concerns 
- Logging: Serilog (Serilog.AspNetCore)
- Exception Handling: Global exception middleware
- Validation: FluentValidation 
- Mapping: AutoMapper 
- Dependency Injection: Autofac

## Key Use Cases
1. Upload document
2. Search for a document

## Getting Started (Sprint 1)
1. Clone the repository
2. Navigate to the project directory
3. Run `docker-compose up --build`
4. Access the API at `http://localhost:8081`
5. Swagger documentation: `http://localhost:8081/swagger`

## API Endpoints (Sprint 1)
http://localhost:8081/swagger
- GET /documents
- GET /documents/{id}
- POST /documents
- PUT /documents/{id}
- DELETE /documents/{id}
- GET /documents/search (placeholder for future implementation)

## Sprint 2: Web-UI Integration (Sprint 2) 
Blazor WebAssembly hardcoded endpoint to api controller (Microsoft.AspNetCore.Components.WebAssembly)
