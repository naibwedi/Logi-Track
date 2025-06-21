# ---- Build Stage ----
    FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
    WORKDIR /app
    COPY . .
    RUN dotnet restore
    RUN dotnet publish -c Release -o out
    
    # ---- Runtime Stage ----
    FROM mcr.microsoft.com/dotnet/aspnet:8.0
    WORKDIR /app
    
    # Copy only necessary files
    COPY --from=build /app/out .
    COPY appsettings.json .
    
    ENTRYPOINT ["dotnet", "logirack.dll"]
    