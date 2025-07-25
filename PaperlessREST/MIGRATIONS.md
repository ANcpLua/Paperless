# Entity Framework Core Migrations

This document explains how to run Entity Framework Core migrations for the Paperless project.

## Prerequisites

1. PostgreSQL must be running (either locally or via Docker)
2. .NET 10.0 SDK installed
3. EF Core tools installed: `dotnet tool install --global dotnet-ef`

## Running Migrations

### Option 1: Using Local PostgreSQL

1. Ensure PostgreSQL is running locally on port 5432
2. Update the connection string in `appsettings.Development.json` if needed
3. Navigate to the PaperlessREST directory:
   ```bash
   cd PaperlessREST
   ```
4. Run migrations:
   ```bash
   ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations add InitialCreate
   ASPNETCORE_ENVIRONMENT=Development dotnet ef database update
   ```

### Option 2: Using Docker Compose PostgreSQL

1. Start the PostgreSQL container from your docker-compose:
   ```bash
   docker-compose up -d postgres
   ```
2. Wait for PostgreSQL to be ready
3. Run migrations:
   ```bash
   cd PaperlessREST
   export ConnectionStrings__PaperlessDb="Host=localhost;Port=5432;Database=paperless;Username=postgres;Password=postgres;Include Error Detail=true"
   dotnet ef migrations add InitialCreate
   dotnet ef database update
   ```

### Option 3: Using the Migration Script

We've provided a helper script to run migrations:

```bash
# Make the script executable
chmod +x run-migrations.sh

# Run with local PostgreSQL
./run-migrations.sh

# Run with Docker PostgreSQL
./run-migrations.sh docker
```

## Troubleshooting

### Error: Unable to create a 'DbContext'
This error has been fixed by:
1. Creating a design-time factory (`DocumentPersistenceFactory.cs`)
2. Ensuring proper dependency injection configuration
3. Using `DocumentEntity` in the DbContext instead of the domain `Document` class

### Connection String Issues
- The application uses `"PaperlessDb"` as the connection string name
- Environment variable format: `ConnectionStrings__PaperlessDb`
- Docker Compose format: `ConnectionStrings__PaperlessDb`

### Enum Mapping Issues
The `DocumentStatus` enum is properly mapped to PostgreSQL's `document_status` type using:
- Snake case translation for enum values
- Proper enum type registration in `OnModelCreating`

## Database Schema

The migration will create:
1. A `document_status` enum type with values: `pending`, `completed`, `failed`
2. A `documents` table with columns:
   - `id` (UUID, primary key)
   - `file_name` (varchar(255))
   - `status` (document_status enum)
   - `created_at` (timestamp with timezone)
   - `storage_path` (varchar(500))
   - `content` (text, nullable)
   - `processed_at` (timestamp with timezone, nullable)
