FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER root
RUN apt-get update && apt-get install -y \
    ghostscript \
    tesseract-ocr \
    && rm -rf /var/lib/apt/lists/*
ARG APP_UID=0
USER $APP_UID
WORKDIR /app
EXPOSE 8082

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
ENV DOCKER_BUILD=true
WORKDIR /src

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
