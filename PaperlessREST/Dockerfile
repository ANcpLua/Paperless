FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["PaperlessREST/PaperlessREST.csproj", "PaperlessREST/"]
COPY ["Contract/Contract.csproj", "Contract/"]
RUN dotnet restore "PaperlessREST/PaperlessREST.csproj"

COPY . .
WORKDIR "/src/PaperlessREST"
RUN dotnet build "PaperlessREST.csproj" -c Release -o /app/build


FROM build AS publish
RUN dotnet publish "PaperlessREST.csproj" \
    -c Release \
    -r linux-x64 \
    --no-self-contained \
    -o /app/publish


FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY PaperlessREST/rest-appsettings.json /app/rest-appsettings.json

ENTRYPOINT ["dotnet", "PaperlessREST.dll"]
