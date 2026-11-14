FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
ENV ASPNETCORE_URLS=http://+:80

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/Alsav.ApiGateway/Alsav.ApiGateway.csproj", "src/Alsav.ApiGateway/"]
RUN dotnet restore "src/Alsav.ApiGateway/Alsav.ApiGateway.csproj"
COPY . .
WORKDIR "/src/src/Alsav.ApiGateway"
RUN dotnet build "Alsav.ApiGateway.csproj" -c Release -o /app/build --no-restore

FROM build AS publish
RUN dotnet publish "Alsav.ApiGateway.csproj" -c Release -o /app/publish /p:UseAppHost=false --no-restore

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Alsav.ApiGateway.dll"]
