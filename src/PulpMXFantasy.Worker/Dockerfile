# PulpMX Fantasy Worker - Multi-stage Dockerfile
# Builds and runs the MassTransit consumer worker service

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Copy solution and project files first (for layer caching)
COPY *.sln ./
COPY src/PulpMXFantasy.Domain/*.csproj src/PulpMXFantasy.Domain/
COPY src/PulpMXFantasy.Contracts/*.csproj src/PulpMXFantasy.Contracts/
COPY src/PulpMXFantasy.Application/*.csproj src/PulpMXFantasy.Application/
COPY src/PulpMXFantasy.Infrastructure/*.csproj src/PulpMXFantasy.Infrastructure/
COPY src/PulpMXFantasy.ReadModel/*.csproj src/PulpMXFantasy.ReadModel/
COPY src/PulpMXFantasy.Messaging/*.csproj src/PulpMXFantasy.Messaging/
COPY src/PulpMXFantasy.Worker/*.csproj src/PulpMXFantasy.Worker/

# Restore dependencies
RUN dotnet restore src/PulpMXFantasy.Worker/PulpMXFantasy.Worker.csproj

# Copy source code
COPY src/ src/

# Build and publish
WORKDIR /src/src/PulpMXFantasy.Worker
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app

# Create non-root user for security
RUN useradd --create-home --no-log-init appuser

# Copy published app
COPY --from=build /app/publish .

# Create directory for trained models
RUN mkdir -p /app/TrainedModels && chown -R appuser:appuser /app

USER appuser

ENTRYPOINT ["dotnet", "PulpMXFantasy.Worker.dll"]
