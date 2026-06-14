# ---- Build stage ----
# Shuffull.Api targets net8.0, so build on the .NET 8 SDK (the old 6.0 image could not build it).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

# Copy only the project files first so `restore` is cached unless a .csproj changes.
# Shuffull.Api references Core, Shared and Metadata (Core in turn references Shared + Metadata);
# the WPF Shuffull.Windows project is never referenced here and is therefore never restored/built.
COPY Shuffull.Api/Shuffull.Api.csproj         Shuffull.Api/
COPY Shuffull.Core/Shuffull.Core.csproj       Shuffull.Core/
COPY Shuffull.Shared/Shuffull.Shared.csproj   Shuffull.Shared/
COPY Shuffull.Metadata/Shuffull.Metadata.csproj Shuffull.Metadata/
RUN dotnet restore "Shuffull.Api/Shuffull.Api.csproj"

# Copy the rest of the source and publish.
COPY ./ ./
RUN dotnet publish "Shuffull.Api/Shuffull.Api.csproj" -c Release -o /app --no-restore

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app ./

# Clients connect over plain HTTP on 7117 (see Shuffull.Windows SiteInfo.cs). 5117 is exposed but
# reserved for a future HTTPS endpoint (appsettings.Production.json keeps a commented-out Kestrel
# HTTPS endpoint on 5117 + cert). NOTE: a machine-local appsettings.Production.json Kestrel:Endpoints
# section overrides this ASPNETCORE_URLS default; this value only applies when that file is absent.
ENV ASPNETCORE_URLS=http://+:7117
EXPOSE 7117 5117

ENTRYPOINT ["dotnet", "Shuffull.Api.dll"]
