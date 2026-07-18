FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY src/S3VideoUploadApi/S3VideoUploadApi.csproj src/S3VideoUploadApi/
RUN dotnet restore src/S3VideoUploadApi/S3VideoUploadApi.csproj

COPY src/S3VideoUploadApi/ src/S3VideoUploadApi/
RUN dotnet publish src/S3VideoUploadApi/S3VideoUploadApi.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .
USER app
ENTRYPOINT ["dotnet", "S3VideoUploadApi.dll"]
