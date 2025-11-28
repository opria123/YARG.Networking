# Dockerfile for YARG Introducer Service
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

# Copy solution and project files for better layer caching
COPY ["src/YARG.Introducer/YARG.Introducer.csproj", "src/YARG.Introducer/"]
COPY ["src/YARG.Net/YARG.Net.csproj", "src/YARG.Net/"]

RUN dotnet restore "src/YARG.Introducer/YARG.Introducer.csproj"

# Copy all source code
COPY . .

WORKDIR "/src/src/YARG.Introducer"
RUN dotnet build "YARG.Introducer.csproj" -c Release -o /app/build --no-restore

FROM build AS publish
RUN dotnet publish "YARG.Introducer.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "YARG.Introducer.dll"]
