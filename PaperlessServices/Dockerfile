FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS base
USER root
RUN apt-get update && apt-get install -y \
    ghostscript \
    tesseract-ocr \
    && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["PaperlessServices/PaperlessServices.csproj", "PaperlessServices/"]
RUN dotnet restore "PaperlessServices/PaperlessServices.csproj"
COPY . .
WORKDIR "/src/PaperlessServices"
RUN dotnet build "PaperlessServices.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "PaperlessServices.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PaperlessServices.dll"]