# BMMDL Registry API - Runtime-only image
# Uses pre-published self-contained .NET 10 app
FROM ubuntu:24.04

# Install minimal dependencies
RUN apt-get update && apt-get install -y \
    libicu74 \
    libssl3 \
    curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# Copy pre-published self-contained app
COPY publish/ .

# Make executable
RUN chmod +x BMMDL.Registry.Api

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health/live || exit 1

# Run as non-root
RUN useradd -m appuser && chown -R appuser /app
USER appuser

ENTRYPOINT ["./BMMDL.Registry.Api"]
