FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

RUN dotnet tool install --global dotnet-ef
ENV PATH="$PATH:/root/.dotnet/tools"

COPY ["PaperlessREST/PaperlessREST.csproj", "PaperlessREST/"]
COPY ["Contract/Contract.csproj", "Contract/"]
COPY ["PaperlessServices/PaperlessServices.csproj", "PaperlessServices/"]
COPY ["PostgreSQL/PostgreSQL.csproj", "PostgreSQL/"]

RUN dotnet restore "PaperlessREST/PaperlessREST.csproj"

COPY . .
WORKDIR "/src/PaperlessREST"
RUN dotnet publish "PaperlessREST.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

RUN apt-get update && \
    apt-get install -y curl && \
    rm -rf /var/lib/apt/lists/*

RUN dotnet tool install --global dotnet-ef
ENV PATH="$PATH:/root/.dotnet/tools"

COPY --from=build /app/publish .
COPY PaperlessREST/rest-appsettings.json ./rest-appsettings.json

HEALTHCHECK --interval=5s --timeout=3s --start-period=30s --retries=3 \
  CMD curl -f http://localhost:8081/ || exit 1

ENV ASPNETCORE_ENVIRONMENT="Docker"
EXPOSE 8081

CMD /bin/bash -c 'dotnet ef database update && dotnet PaperlessREST.dll'
