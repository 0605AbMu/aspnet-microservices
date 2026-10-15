FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Packages.props", "./"]
COPY ["Common/Microservices.Common.csproj", "Common/"]
COPY ["WebCore/Microservices.WebCore.csproj", "WebCore/"]
COPY ["Gateway/Microservices.Gateway/Microservices.Gateway.csproj", "Gateway/Microservices.Gateway/"]
RUN dotnet restore "Gateway/Microservices.Gateway/Microservices.Gateway.csproj"
COPY . .
WORKDIR "/src/Gateway/Microservices.Gateway"
RUN dotnet build "Microservices.Gateway.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Microservices.Gateway.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Microservices.Gateway.dll"]
