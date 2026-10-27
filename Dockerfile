FROM node:22-bookworm-slim AS web
WORKDIR /src/web
COPY app-temp/web/package*.json ./
RUN npm ci
COPY app-temp/web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src/app-temp
COPY app-temp/domain/Playback.Domain.csproj domain/
COPY app-temp/application/Playback.Application.csproj application/
COPY app-temp/infrastructure/Playback.Infrastructure.csproj infrastructure/
COPY app-temp/api/Playback.Api.csproj api/
RUN dotnet restore api/Playback.Api.csproj
COPY app-temp/domain/ domain/
COPY app-temp/application/ application/
COPY app-temp/infrastructure/ infrastructure/
COPY app-temp/api/ api/
WORKDIR /src/app-temp/api
RUN dotnet publish -c Release --no-restore -o /publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production \
    ENVIRONMENT=prod \
    PORT=8080
COPY --from=api /publish/ ./
COPY --from=web /src/web/dist/ ./wwwroot/
RUN mkdir -p /data/audio /data/local-capture /data/keys && chown -R $APP_UID:$APP_UID /data
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Playback.Api.dll"]
