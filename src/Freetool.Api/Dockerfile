# Build .NET App
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Copy project files and restore as distinct layers
COPY ./Directory.Build.props ./
COPY ./src/Freetool.Domain/Freetool.Domain.fsproj ./src/Freetool.Domain/
COPY ./src/Freetool.Application/Freetool.Application.fsproj ./src/Freetool.Application/
COPY ./src/Freetool.Infrastructure/Freetool.Infrastructure.fsproj ./src/Freetool.Infrastructure/
COPY ./src/Freetool.Api/Freetool.Api.fsproj ./src/Freetool.Api/
RUN dotnet restore src/Freetool.Api/Freetool.Api.fsproj

# Copy and publish app
COPY . .
RUN dotnet publish src/Freetool.Api/Freetool.Api.fsproj -c Release -o /app

# Final runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

EXPOSE 8080
ENV HTTP_PORTS=8080

ENTRYPOINT ["dotnet", "Freetool.Api.dll"]
