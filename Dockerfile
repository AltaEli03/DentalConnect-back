FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:3000
EXPOSE 3000

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["DentalConnect.Domain/DentalConnect.Domain.csproj", "DentalConnect.Domain/"]
COPY ["DentalConnect.Application/DentalConnect.Application.csproj", "DentalConnect.Application/"]
COPY ["DentalConnect.Infrastructure/DentalConnect.Infrastructure.csproj", "DentalConnect.Infrastructure/"]
COPY ["DentalConnect/DentalConnect.csproj", "DentalConnect/"]
RUN dotnet restore "./DentalConnect/DentalConnect.csproj"
COPY . .
WORKDIR "/src/DentalConnect"
RUN dotnet build "./DentalConnect.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./DentalConnect.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
USER root
WORKDIR /app
ADD https://truststore.pki.rds.amazonaws.com/global/global-bundle.pem /tmp/rds-global-bundle.pem
RUN cat /tmp/rds-global-bundle.pem >> /etc/ssl/certs/ca-certificates.crt && rm /tmp/rds-global-bundle.pem
USER $APP_UID
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DentalConnect.dll"]
