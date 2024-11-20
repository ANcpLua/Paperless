FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["PaperlessServices/PaperlessServices.csproj", "PaperlessServices/"]
COPY ["Contract/Contract.csproj", "Contract/"]
RUN dotnet restore "PaperlessServices/PaperlessServices.csproj"
COPY . .
WORKDIR "/src/PaperlessServices"
RUN dotnet build "PaperlessServices.csproj" -c Release -o /app/build
FROM build AS publish
RUN dotnet publish "PaperlessServices.csproj" -c Release -o /app/publish
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
RUN apt-get update && \
    apt-get install -y --no-install-recommends tesseract-ocr libtesseract-dev libleptonica-dev imagemagick libmagickwand-dev && \
    rm -rf /var/lib/apt/lists/*
COPY --from=publish /app/publish .
COPY PaperlessServices/service-appsettings.json /app/service-appsettings.json
EXPOSE 8082
ENTRYPOINT ["dotnet", "PaperlessServices.dll"]
