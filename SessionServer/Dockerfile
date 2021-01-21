#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:5.0-buster-slim AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:5.0-buster-slim AS build
ENV BuildConfiguration=Release
WORKDIR /src
COPY ["nuget.config", "."]
COPY ["SessionServer/SessionServer.csproj", "SessionServer/"]
COPY ["Common/Common.csproj", "Common/"]
RUN dotnet restore "SessionServer/SessionServer.csproj"
COPY . .
WORKDIR "/src/SessionServer"
RUN dotnet build "SessionServer.csproj" -c ${BuildConfiguration} -o /app/build

FROM build AS publish
ENV BuildConfiguration=Release
RUN dotnet publish "SessionServer.csproj" -c ${BuildConfiguration} -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SessionServer.dll"]