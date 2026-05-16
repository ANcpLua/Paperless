FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
ARG APP_UID=0

USER root
RUN apt-get update && apt-get install -y tzdata && \
    ln -sf /usr/share/zoneinfo/Europe/Vienna /etc/localtime && \
    echo "Europe/Vienna" > /etc/timezone && \
    apt-get clean && rm -rf /var/lib/apt/lists/*
ENV TZ=Europe/Vienna

USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
ENV DOCKER_BUILD=true
WORKDIR /src

# Copy project files for restore
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
