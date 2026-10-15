FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Packages.props", "./"]
COPY ["Common/Microservices.Common.csproj", "Common/"]
COPY ["WebCore/Microservices.WebCore.csproj", "WebCore/"]
COPY ["Services/Template/Template.Contracts/Microservices.Template.Contracts.csproj", "Services/Template/Template.Contracts/"]
COPY ["Services/Template/Template.Service/Microservices.Template.Service.csproj", "Services/Template/Template.Service/"]
RUN dotnet restore "Services/Template/Template.Service/Microservices.Template.Service.csproj"
COPY . .
WORKDIR "/src/Services/Template/Template.Service"
RUN dotnet build "Microservices.Template.Service.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Microservices.Template.Service.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Microservices.Template.Service.dll"]
