FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app
EXPOSE 8080

# Copy everything
COPY . ./
# Restore as distinct layers
RUN dotnet restore "antifraud.csproj"
# Build and publish a release
RUN dotnet publish "antifraud.csproj" -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/out .
ENTRYPOINT ["dotnet", "antifraud.dll"]