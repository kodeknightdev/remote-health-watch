# syntax=docker/dockerfile:1.7
# build context is the repo root: docker build -f src/services/realtime/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:9.0.301-bookworm-slim AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/shared/ src/shared/
COPY src/services/realtime/RemoteHealthWatch.Realtime/ src/services/realtime/RemoteHealthWatch.Realtime/
RUN --mount=type=cache,id=rhw-nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet restore src/services/realtime/RemoteHealthWatch.Realtime --locked-mode
RUN --mount=type=cache,id=rhw-nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet publish src/services/realtime/RemoteHealthWatch.Realtime -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0.6-bookworm-slim
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "RemoteHealthWatch.Realtime.dll"]
