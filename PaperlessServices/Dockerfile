FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:7644f992230d35cf230017189d4038c0ae0f7388b13f4f7ae1900a155bafb597 AS base

# Privileged ops (apt install of ghostscript + tesseract-ocr) require root;
# the final USER directive drops back to the base image's non-root 'app' user
# before any app code runs.
USER root
RUN apt-get update && apt-get install -y \
    ghostscript \
    tesseract-ocr \
    && rm -rf /var/lib/apt/lists/*

USER app
WORKDIR /app
EXPOSE 8082

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:ea8bde36c11b6e7eec2656d0e59101d4462f6bd630730f2c8201ed0572b295d5 AS build
ARG BUILD_CONFIGURATION=Release
ENV DOCKER_BUILD=true
WORKDIR /src

# Root-level MSBuild + NuGet configuration must be present BEFORE restore so:
#   - global.json's msbuild-sdks section resolves ANcpLua.NET.Sdk 3.4.32
#   - nuget.config clears default sources and adds nuget.org explicitly
#   - Directory.Packages.props (CPM) + Version.props supply central versions
COPY ["global.json", "nuget.config", "Directory.Packages.props", "Version.props", "./"]

# Copy project files for restore
COPY ["PaperlessServices/PaperlessServices.csproj", "PaperlessServices/"]
RUN dotnet restore "PaperlessServices/PaperlessServices.csproj"

# Copy source files
COPY ["PaperlessServices/", "PaperlessServices/"]

WORKDIR "/src/PaperlessServices"
RUN dotnet build "PaperlessServices.csproj" -c $BUILD_CONFIGURATION -o /app/build -p:DOCKER_BUILD=true

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "PaperlessServices.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false -p:DOCKER_BUILD=true

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PaperlessServices.dll"]
