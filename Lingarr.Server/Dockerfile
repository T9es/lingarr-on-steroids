# Set default build configuration
ARG BUILD_CONFIGURATION=Debug
ARG BUILDPLATFORM=linux/amd64
ARG TARGETARCH=amd64
FROM node:24-slim AS client

# Prepare Client
COPY ./Lingarr.Client/ /Client
WORKDIR /Client
# Install dependencies
RUN if [ "$BUILD_CONFIGURATION" = "Release" ] ; then npm ci --only=production ; else npm install ; fi
RUN npm run build

# Step 2: Prepare the base image for the server
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app

# Step 3: Build the server
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Debug
ARG TARGETARCH=amd64
# Optional override pushed by .github/workflows/docker-build.yml when a
# release tag is being packaged. When unset, the build resolves the
# version from git describe via Directory.Build.props (or falls back to
# the in-repo default).
ARG VERSION
RUN echo "Current BUILD_CONFIGURATION: $BUILD_CONFIGURATION"
RUN echo "Current VERSION: ${VERSION:-<from-git-or-default>}"
WORKDIR /src
COPY ["Lingarr.Server/Lingarr.Server.csproj", "Lingarr.Server/"]
RUN dotnet restore "./Lingarr.Server/./Lingarr.Server.csproj" --arch $TARGETARCH
COPY . .
WORKDIR "/src/Lingarr.Server"
COPY --from=client /Client/dist/ ./wwwroot
RUN dotnet build "./Lingarr.Server.csproj" -c $BUILD_CONFIGURATION -o /app/build --arch $TARGETARCH \
        ${VERSION:+"/p:Version=$VERSION"}

# Step 4: Publish the application
FROM --platform=$BUILDPLATFORM build AS publish
ARG BUILD_CONFIGURATION=Debug
ARG TARGETARCH=amd64
# Re-declared: ARGs don't carry across FROM boundaries.
ARG VERSION
RUN echo "Current BUILD_CONFIGURATION: $BUILD_CONFIGURATION"
RUN dotnet publish "./Lingarr.Server.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --arch $TARGETARCH \
        ${VERSION:+"/p:Version=$VERSION"}

# Step 4b: Build PGS-to-SRT OCR helper
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS pgstosrt
RUN apt-get update && apt-get install -y --no-install-recommends git ca-certificates \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /src
RUN git clone --depth 1 https://github.com/Tentacule/PgsToSrt.git /src/PgsToSrt
WORKDIR /src/PgsToSrt/src/PgsToSrt
RUN dotnet publish PgsToSrt.csproj -c Release -o /opt/pgstosrt

# Step 5: Final image
FROM base AS final

# Install FFmpeg and OCR runtime dependencies for embedded subtitle extraction
RUN apt-get update && apt-get install -y --no-install-recommends \
        ffmpeg \
        mkvtoolnix \
        libtesseract5 \
        libleptonica-dev \
        tesseract-ocr-eng \
        tesseract-ocr-jpn \
        tesseract-ocr-fra \
        tesseract-ocr-spa \
        tesseract-ocr-deu \
        tesseract-ocr-pol \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=pgstosrt /opt/pgstosrt /opt/pgstosrt
ENV DOTNET_ROLL_FORWARD=Major

VOLUME /app/config
# Start the application
ENTRYPOINT ["dotnet", "Lingarr.Server.dll"]
