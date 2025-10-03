# Use the official .NET 9 SDK image for building
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy csproj and restore dependencies
COPY src/Mocku.Web/*.csproj src/Mocku.Web/
RUN dotnet restore src/Mocku.Web/Mocku.Web.csproj

# Copy everything else and build
COPY . .
WORKDIR /app/src/Mocku.Web
RUN dotnet publish -c Release -o /app/publish --no-restore

# Use the official ASP.NET Core runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Copy the published app
COPY --from=build /app/publish .

# Expose port 8080 (default for ASP.NET Core in containers)
EXPOSE 8080

# Set the entry point
ENTRYPOINT ["dotnet", "Mocku.Web.dll"]