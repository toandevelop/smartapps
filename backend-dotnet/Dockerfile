FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY SmartApps.Api.csproj ./
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
RUN mkdir -p /app/uploads
EXPOSE 5200
ENV ASPNETCORE_URLS=http://+:5200
ENTRYPOINT ["dotnet", "SmartApps.Api.dll"]
