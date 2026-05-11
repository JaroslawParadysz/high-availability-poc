# Stage 1: build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Connector/Connector.csproj Connector/
RUN dotnet restore Connector/Connector.csproj

COPY Connector/ Connector/
RUN dotnet publish Connector/Connector.csproj -c Release -o /app/publish --no-restore

# Stage 2: runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Run as non-root for least-privilege.
RUN groupadd --system connector && useradd --system --gid connector connector
USER connector

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Connector.dll"]
