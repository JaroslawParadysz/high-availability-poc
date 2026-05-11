# Stage 1: build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Connector/Connector.csproj src/Connector/
RUN dotnet restore src/Connector/Connector.csproj

COPY src/Connector/ src/Connector/
RUN dotnet publish src/Connector/Connector.csproj -c Release -o /app/publish --no-restore

# Stage 2: runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Run as non-root for least-privilege.
RUN addgroup --system connector && adduser --system --ingroup connector connector
USER connector

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Connector.dll"]
