FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Install build dependencies for Native AOT
RUN apt-get update && apt-get install -y --no-install-recommends \
    clang \
    zlib1g-dev \
    libicu-dev

# Copy everything
COPY . ./
# Restore as distinct layers
RUN dotnet restore "antifraud.csproj"
# Build and publish a release with AOT
RUN dotnet publish "antifraud.csproj" -c Release -o out /p:PublishAot=true

# Build runtime image (AOT uses a minimal base)
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
WORKDIR /app
COPY --from=build /app/out .
ENTRYPOINT ["./antifraud"]