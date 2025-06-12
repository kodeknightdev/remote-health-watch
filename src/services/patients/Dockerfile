# syntax=docker/dockerfile:1.7
# build context is the repo root: docker build -f src/services/patients/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:9.0.301-bookworm-slim AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/shared/ src/shared/
COPY src/services/patients/RemoteHealthWatch.Patients/ src/services/patients/RemoteHealthWatch.Patients/
RUN --mount=type=cache,id=rhw-nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet restore src/services/patients/RemoteHealthWatch.Patients --locked-mode
RUN --mount=type=cache,id=rhw-nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet publish src/services/patients/RemoteHealthWatch.Patients -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0.6-bookworm-slim
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0
EXPOSE 8080 8081
USER $APP_UID
ENTRYPOINT ["dotnet", "RemoteHealthWatch.Patients.dll"]
