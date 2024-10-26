FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app
COPY . .
RUN dotnet publish -c Release -o /app/build

FROM nginx:alpine
COPY --from=build /app/build /usr/share/nginx/html
EXPOSE 80