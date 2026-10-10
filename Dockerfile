FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/DietFlow.Web/DietFlow.Web.csproj src/DietFlow.Web/
RUN dotnet restore src/DietFlow.Web/DietFlow.Web.csproj

COPY src/ src/
RUN dotnet publish src/DietFlow.Web/DietFlow.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "DietFlow.Web.dll"]
