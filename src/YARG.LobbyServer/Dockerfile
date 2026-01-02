# Dockerfile for YARG LobbyServer Service
# A lightweight lobby directory service that enables clients to discover public game lobbies.

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# Use non-root user for security
RUN adduser -D -u 1000 appuser
USER appuser

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

# Copy all source code first
COPY . .

# Restore and build
WORKDIR "/src/src/YARG.LobbyServer"
RUN dotnet restore "YARG.LobbyServer.csproj"
RUN dotnet build "YARG.LobbyServer.csproj" -c Release -o /app/build --no-restore

FROM build AS publish
RUN dotnet publish "YARG.LobbyServer.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "YARG.LobbyServer.dll"]
