# Build Stage
FROM mcr.microsoft.com/mssql/server:2019-latest as sqlserver
FROM mcr.microsoft.com/dotnet/sdk:6.0-focal AS build
WORKDIR /source
COPY ./ ./
RUN dotnet restore "./Shuffull.Site/Shuffull.Site.csproj"
RUN dotnet publish "./Shuffull.Site/Shuffull.Site.csproj" -c release -o /app --no-restore

# Serve Stage
FROM mcr.microsoft.com/dotnet/aspnet:6.0-focal
WORKDIR /app
COPY --from=build /app ./
EXPOSE 1433
ENTRYPOINT ["dotnet", "Shuffull.Site.dll"]
