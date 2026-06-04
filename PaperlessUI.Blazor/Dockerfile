FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER root
RUN apt-get update && apt-get install -y tzdata && \
    ln -sf /usr/share/zoneinfo/Europe/Vienna /etc/localtime && \
    echo "Europe/Vienna" > /etc/timezone && \
    apt-get clean && rm -rf /var/lib/apt/lists/*
ENV TZ=Europe/Vienna

USER app
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
ENV DOCKER_BUILD=true
WORKDIR /src

# Root MSBuild + NuGet config must be present BEFORE restore so the custom
# ANcpLua.NET.Sdk.Web resolver and CPM versions resolve.
COPY ["global.json", "nuget.config", "Directory.Packages.props", "Version.props", "./"]

COPY ["PaperlessUI.Blazor/PaperlessUI.Blazor.csproj", "PaperlessUI.Blazor/"]
RUN dotnet restore "PaperlessUI.Blazor/PaperlessUI.Blazor.csproj"

COPY ["PaperlessUI.Blazor/", "PaperlessUI.Blazor/"]
WORKDIR "/src/PaperlessUI.Blazor"
RUN dotnet build "PaperlessUI.Blazor.csproj" -c $BUILD_CONFIGURATION -o /app/build -p:DOCKER_BUILD=true

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "PaperlessUI.Blazor.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false -p:DOCKER_BUILD=true

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "PaperlessUI.Blazor.dll"]
