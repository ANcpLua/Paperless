# .NET 8.0 SDK for building the application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-env
ARG BUILD_CONFIG=Release
WORKDIR /App

# Copy the project files and restore dependencies
COPY ["PaperlessServices/PaperlessServices.csproj", "PaperlessServices/"]
COPY ["Contract/Contract.csproj", "Contract/"]
COPY . .
RUN dotnet restore "PaperlessServices/PaperlessServices.csproj"

# Build and publish the application
RUN dotnet publish -c $BUILD_CONFIG -r linux-x64 --no-self-contained -o out "PaperlessServices/PaperlessServices.csproj"

# .NET 8.0 ASP.NET runtime as the base image for the final stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /App
COPY --from=build-env /App/out .

# Install necessary dependencies
RUN apt-get update -y && \
    apt-get install -y \
    tesseract-ocr \
    tesseract-ocr-eng \
    libtesseract-dev \
    libleptonica-dev \
    ghostscript \
    imagemagick \
    libmagickwand-dev \
    libmagickcore-6.q16-6-extra \
    pkg-config \
    wget \
    curl

# Configure ImageMagick policy to allow PDF processing
RUN sed -i 's/rights="none" pattern="PDF"/rights="read|write" pattern="PDF"/' /etc/ImageMagick-6/policy.xml

# Debug: Find Tesseract data directory
RUN find /usr -name "eng.traineddata" -type f

# Set up Tesseract data directories
RUN mkdir -p /App/tessdata && \
    find /usr -name "eng.traineddata" -type f -exec cp {} /App/tessdata/ \; && \
    chmod -R 755 /App/tessdata

# Verify installations
RUN tesseract --version && \
    gs --version && \
    convert --version && \
    ls -la /App/tessdata

# Create necessary symbolic links with proper paths - with version detection
RUN mkdir -p /App/runtimes/linux-x64/native/ && \
    mkdir -p /App/x64 && \
    TESSERACT_LIB=$(find /usr/lib/x86_64-linux-gnu -name "libtesseract.so*" | head -n 1) && \
    LEPT_LIB=$(find /usr/lib/x86_64-linux-gnu -name "liblept.so*" | head -n 1) && \
    DL_LIB=$(find /usr/lib/x86_64-linux-gnu -name "libdl.so*" | head -n 1) && \
    echo "Found libraries:" && \
    echo "Tesseract: $TESSERACT_LIB" && \
    echo "Leptonica: $LEPT_LIB" && \
    echo "DL: $DL_LIB" && \
    ln -sf $LEPT_LIB /App/x64/libleptonica-1.82.0.so && \
    ln -sf $TESSERACT_LIB /App/x64/libtesseract50.so && \
    ln -sf $DL_LIB /usr/lib/libdl.so && \
    ln -sf $DL_LIB /usr/lib/x86_64-linux-gnu/libdl.so && \
    mkdir -p /lib/x86_64-linux-gnu && \
    ln -sf $DL_LIB /lib/x86_64-linux-gnu/libdl.so && \
    ln -sf $DL_LIB /App/runtimes/linux-x64/native/libdl.so

# Set environment variables in separate commands
ENV LD_LIBRARY_PATH="/usr/lib/x86_64-linux-gnu:/usr/lib:/lib:/App/runtimes/linux-x64/native:/App/x64"
ENV TESSDATA_PREFIX=/App/tessdata/
ENV PATH="/usr/local/bin:/usr/bin:/bin:/usr/local/games:/usr/games"
ENV MAGICK_HOME="/usr"
    
# Copy the application configuration file
COPY PaperlessServices/service-appsettings.json /app/service-appsettings.json

# Expose the application port
EXPOSE 8082

# Define the entry point for the application
ENTRYPOINT ["dotnet", "PaperlessServices.dll"]