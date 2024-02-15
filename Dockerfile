FROM mcr.microsoft.com/dotnet/runtime-deps:8.0-jammy-chiseled AS base
USER app
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
RUN apt-get update && apt-get install -y --no-install-recommends clang zlib1g-dev
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY rinha2-dotnet.csproj .
RUN dotnet restore 
COPY Program.cs .
RUN dotnet build -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish -c $BUILD_CONFIGURATION -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["./rinha2-dotnet"]