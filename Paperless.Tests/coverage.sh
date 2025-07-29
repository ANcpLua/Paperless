#!/bin/bash
echo "Running tests with coverage..."

# Run coverage with coverlet (works with TUnit)
coverlet bin/Debug/net10.0/Paperless.Tests.dll \
  --target "dotnet" \
  --targetargs "run --no-build" \
  --format opencover \
  --output coverage.xml \
  --exclude "[*]*.g.cs" \
  --exclude "[*]*.generated.cs" \
  --exclude "[TUnit*]*" \
  --exclude "[*]*.Migrations.*" | tail -n 20

# Generate report silently
reportgenerator \
  -reports:coverage.xml \
  -targetdir:coveragereport \
  -reporttypes:Html \
  -verbosity:Off

echo "Coverage report generated!"
#open coveragereport/index.html
