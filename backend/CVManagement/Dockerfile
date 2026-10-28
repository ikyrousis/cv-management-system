FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY backend/CVManagement/CVManagement.csproj backend/CVManagement/
RUN dotnet restore backend/CVManagement/CVManagement.csproj
COPY backend/CVManagement/ backend/CVManagement/
RUN dotnet publish backend/CVManagement/CVManagement.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "CVManagement.dll"]
