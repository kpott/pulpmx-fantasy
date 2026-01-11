# PulpMX Fantasy Web - Multi-stage Dockerfile
# Builds and runs the ASP.NET MVC web application

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Copy solution and project files first (for layer caching)
COPY *.sln ./
COPY src/PulpMXFantasy.Domain/*.csproj src/PulpMXFantasy.Domain/
COPY src/PulpMXFantasy.Contracts/*.csproj src/PulpMXFantasy.Contracts/
COPY src/PulpMXFantasy.ReadModel/*.csproj src/PulpMXFantasy.ReadModel/
COPY src/PulpMXFantasy.Messaging/*.csproj src/PulpMXFantasy.Messaging/
COPY src/PulpMXFantasy.Web/*.csproj src/PulpMXFantasy.Web/

# Restore dependencies
RUN dotnet restore src/PulpMXFantasy.Web/PulpMXFantasy.Web.csproj

# Copy source code
COPY src/ src/

# Build and publish
WORKDIR /src/src/PulpMXFantasy.Web
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

# Expose port
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "PulpMXFantasy.Web.dll"]
