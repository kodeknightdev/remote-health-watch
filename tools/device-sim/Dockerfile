# syntax=docker/dockerfile:1.7
# build context is the repo root: docker build -f tools/device-sim/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:9.0.301-bookworm-slim AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/shared/ src/shared/
COPY tools/device-sim/RemoteHealthWatch.DeviceSim/ tools/device-sim/RemoteHealthWatch.DeviceSim/
RUN --mount=type=cache,id=rhw-nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet restore tools/device-sim/RemoteHealthWatch.DeviceSim --locked-mode
RUN --mount=type=cache,id=rhw-nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet publish tools/device-sim/RemoteHealthWatch.DeviceSim -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0.6-bookworm-slim
WORKDIR /app
COPY --from=build /app .
ENV DOTNET_gcServer=0
USER $APP_UID
ENTRYPOINT ["dotnet", "RemoteHealthWatch.DeviceSim.dll"]
