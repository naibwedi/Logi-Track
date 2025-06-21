# ----- Build Stage -----
    FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
    WORKDIR /app
    COPY . .
    RUN dotnet restore
    RUN dotnet publish -c Release -o out
    
    # ----- Runtime Stage -----
    FROM mcr.microsoft.com/dotnet/aspnet:8.0
    WORKDIR /app
    
    # Only copy published output (includes appsettings.json)
    COPY --from=build /app/out .
    
    ENTRYPOINT ["dotnet", "logirack.dll"]
    