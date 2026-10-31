# Multi-stage Dockerfile for Logi-Track (.NET 8 + SQLite)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

# Copy csproj and restore dependencies
COPY logirack.csproj ./
RUN dotnet restore

# Copy remaining source code and publish
COPY . ./
RUN dotnet publish logirack.csproj -c Release -o /app --no-restore

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Ensure SQLite directory exists for persistent volume mounting
RUN mkdir -p /app/data

# Copy build artifacts
COPY --from=build /app ./

# Default ASPNETCORE URL
ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 5000

ENTRYPOINT ["dotnet", "logirack.dll"]
