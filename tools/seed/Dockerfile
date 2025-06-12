# syntax=docker/dockerfile:1.7
# build context is the repo root: docker build -f tools/seed/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:9.0.301-bookworm-slim AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/shared/ src/shared/
COPY tools/seed/RemoteHealthWatch.Seed/ tools/seed/RemoteHealthWatch.Seed/
RUN --mount=type=cache,id=rhw-nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet restore tools/seed/RemoteHealthWatch.Seed --locked-mode
RUN --mount=type=cache,id=rhw-nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet publish tools/seed/RemoteHealthWatch.Seed -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0.6-bookworm-slim
WORKDIR /app
COPY --from=build /app .
RUN mkdir /seed-out && chown $APP_UID /seed-out
ENV DOTNET_gcServer=0
USER $APP_UID
ENTRYPOINT ["dotnet", "RemoteHealthWatch.Seed.dll"]
