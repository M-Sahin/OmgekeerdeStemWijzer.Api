# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["OmgekeerdeStemWijzer.Api.csproj", "./"]
RUN dotnet restore "OmgekeerdeStemWijzer.Api.csproj"

# Copy the rest of the sources (respecting .dockerignore)
COPY . ./
RUN dotnet build "OmgekeerdeStemWijzer.Api.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "OmgekeerdeStemWijzer.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Copy published app
COPY --from=publish /app/publish .

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "OmgekeerdeStemWijzer.Api.dll"]
