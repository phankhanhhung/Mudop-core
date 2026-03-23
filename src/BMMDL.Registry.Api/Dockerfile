# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Copy solution and project files
COPY ["BMMDL.sln", "./"]
COPY ["src/BMMDL.Registry.Api/BMMDL.Registry.Api.csproj", "src/BMMDL.Registry.Api/"]
COPY ["src/BMMDL.Registry/BMMDL.Registry.csproj", "src/BMMDL.Registry/"]
COPY ["src/BMMDL.Infrastructure/BMMDL.Infrastructure.csproj", "src/BMMDL.Infrastructure/"]
COPY ["src/BMMDL.Compiler/BMMDL.Compiler.csproj", "src/BMMDL.Compiler/"]
COPY ["src/BMMDL.CodeGen/BMMDL.CodeGen.csproj", "src/BMMDL.CodeGen/"]
COPY ["src/BMMDL.MetaModel/BMMDL.MetaModel.csproj", "src/BMMDL.MetaModel/"]

# Restore dependencies
RUN dotnet restore "src/BMMDL.Registry.Api/BMMDL.Registry.Api.csproj"

# Copy source code
COPY . .

# Build
WORKDIR "/src/src/BMMDL.Registry.Api"
RUN dotnet build "BMMDL.Registry.Api.csproj" -c Release -o /app/build

# Publish
FROM build AS publish
WORKDIR "/src/src/BMMDL.Registry.Api"
RUN dotnet publish "BMMDL.Registry.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS final
WORKDIR /app
EXPOSE 8080

# Copy published files
COPY --from=publish /app/publish .

# Set environment
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Development

ENTRYPOINT ["dotnet", "BMMDL.Registry.Api.dll"]
