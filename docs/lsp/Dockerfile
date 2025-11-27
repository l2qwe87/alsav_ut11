FROM eclipse-temurin:17-jre-alpine AS base

# Install required tools
RUN apk add --no-cache curl wget ca-certificates bash

WORKDIR /app

# BSL Language Server setup
ARG BSL_VERSION=v0.24.2
ARG BSL_JAR_PATH=/opt/bsl/bsl-language-server.jar

RUN mkdir -p /opt/bsl && \
    DOWNLOAD_URL="https://github.com/1c-syntax/bsl-language-server/releases/download/v0.24.2/bsl-language-server-0.24.2-exec.jar"; \
    echo "Downloading from: $DOWNLOAD_URL" && \
    wget -O "$BSL_JAR_PATH" "$DOWNLOAD_URL" && \
    chmod +x "$BSL_JAR_PATH"

# Copy application JAR and scripts
COPY run-stdio.sh /app/run-stdio.sh
RUN chmod +x /app/run-stdio.sh

# Create temporary directory for BSL reports
RUN mkdir -p /tmp/bsl-reports

# Volumes
VOLUME ["/workspaces"]

# Environment variables
ENV WEB_UI_PORT=9090
ENV MCP_TRANSPORT=http
ENV MCP_PORT=9090
ENV LOGGING_ENABLED=true
ENV BSL_JAR_PATH=/opt/bsl/bsl-language-server.jar
ENV BSL_MAX_HEAP=4g
ENV SPRING_PROFILES_ACTIVE=default
ENV MOUNT_HOST_ROOT=/workspaces

# Expose ports
EXPOSE 9090

# Use stdio script for stdio mode, regular jar for other modes
ENTRYPOINT ["sh", "-c", "java -jar /opt/bsl/bsl-language-server.jar websocket --server.port=9090"]

