FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props global.json ./
COPY shared/FieldService.Contracts/FieldService.Contracts.csproj shared/FieldService.Contracts/
COPY api/FieldService.Api/FieldService.Api.csproj api/FieldService.Api/
RUN dotnet restore api/FieldService.Api
COPY shared/ shared/
COPY api/ api/
RUN dotnet publish api/FieldService.Api -c Release --no-restore -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "FieldService.Api.dll"]
