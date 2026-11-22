# ---------- Build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached until api.csproj changes
COPY api.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app --no-restore

# ---------- Run ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# appsettings.json binds to localhost only, which is unreachable from outside the container
ENV Urls=http://+:5000
EXPOSE 5000

# Non-root user that ships with the .NET images
USER $APP_UID
ENTRYPOINT ["dotnet", "api.dll"]
