FROM node:22-bookworm-slim AS web
WORKDIR /src/web
COPY app-temp/web/package*.json ./
RUN npm ci
COPY app-temp/web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src/api
COPY app-temp/api/Playback.Api.csproj ./
RUN dotnet restore
COPY app-temp/api/ ./
RUN dotnet publish -c Release --no-restore -o /publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production \
    PORT=8080
COPY --from=api /publish/ ./
COPY --from=web /src/web/dist/ ./wwwroot/
RUN mkdir -p /data/audio /data/local-capture /data/keys && chown -R $APP_UID:$APP_UID /data
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Playback.Api.dll"]
