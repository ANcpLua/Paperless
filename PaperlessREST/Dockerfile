FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["PaperlessREST/PaperlessREST.csproj", "PaperlessREST/"]
RUN dotnet restore "PaperlessREST/PaperlessREST.csproj"
COPY . .
WORKDIR "/src/PaperlessREST"
RUN dotnet build "PaperlessREST.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "PaperlessREST.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
# Ensure we're running in Production mode
ENV ASPNETCORE_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "PaperlessREST.dll"]