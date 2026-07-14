# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["GodmorgenBotFSharp.fsproj", "./"]
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore "GodmorgenBotFSharp.fsproj"
COPY . .
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish "GodmorgenBotFSharp.fsproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "GodmorgenBotFSharp.dll"]
