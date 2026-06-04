FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base

# Privileged ops (apt + timezone) require root; the final USER directive
# drops back to the base image's non-root 'app' user before any app code runs.
USER root
RUN apt-get update && apt-get install -y tzdata && \
    ln -sf /usr/share/zoneinfo/Europe/Vienna /etc/localtime && \
    echo "Europe/Vienna" > /etc/timezone && \
    apt-get clean && rm -rf /var/lib/apt/lists/*
ENV TZ=Europe/Vienna

USER app
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
ENV DOCKER_BUILD=true
WORKDIR /src

# Root-level MSBuild + NuGet configuration must be present BEFORE restore so:
#   - global.json's msbuild-sdks section resolves ANcpLua.NET.Sdk.Web
#   - nuget.config clears default sources and adds nuget.org explicitly
#   - Directory.Packages.props (CPM) + Version.props supply central versions
COPY ["global.json", "nuget.config", "Directory.Packages.props", "Version.props", "./"]

# Copy the project file and restore. (Paperless.Contracts was folded into
# PaperlessREST in #39 — there is no separate Contracts project to copy.)
COPY ["PaperlessREST/PaperlessREST.csproj", "PaperlessREST/"]
RUN dotnet restore "PaperlessREST/PaperlessREST.csproj"

# Copy source files
COPY ["PaperlessREST/", "PaperlessREST/"]

WORKDIR "/src/PaperlessREST"
RUN dotnet build "PaperlessREST.csproj" -c $BUILD_CONFIGURATION -o /app/build -p:DOCKER_BUILD=true

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "PaperlessREST.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false -p:DOCKER_BUILD=true

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "PaperlessREST.dll"]
