# Docker Deployment Guide

This guide explains how to build, deploy, and run the Mocku.Web application using Docker and GitHub Container Registry.

## Quick Start

### For Development (from project root)

If you're in the project root directory (where the `src` folder is):

1. **Build and run locally:**
   ```bash
   docker-compose up --build
   ```
   The application will be available at `http://localhost:8080`

2. **Run with production setup (including nginx):**
   ```bash
   docker-compose --profile production up --build
   ```
   The application will be available at `http://localhost:80`

### For Quick Testing (from any directory)

If you want to run from any directory or just test with pre-built images:

1. **Create a local mocks directory:**
   ```bash
   mkdir mocks
   # Add your mock JSON files to this directory
   ```

2. **Use the simple compose file:**
   ```bash
   docker-compose -f docker-compose.simple.yml up
   ```

3. **Or run directly with Docker:**
   ```bash
   docker run -p 8080:8080 \
     -v $(pwd)/mocks:/app/mocks:ro \
     ghcr.io/[your-username]/mocku:latest
   ```

### Using Pre-built Images from GHCR

1. **Pull the latest image:**
   ```bash
   docker pull ghcr.io/[your-username]/mocku:latest
   ```

2. **Run with local mocks:**
   ```bash
   # Windows PowerShell
   docker run -p 8080:8080 -v "${PWD}/mocks:/app/mocks:ro" ghcr.io/[your-username]/mocku:latest
   
   # Linux/Mac
   docker run -p 8080:8080 -v "$(pwd)/mocks:/app/mocks:ro" ghcr.io/[your-username]/mocku:latest
   ```

## Troubleshooting Volume Mount Issues

If you encounter the error: `"not a directory: unknown: Are you trying to mount a directory onto a file"`

### Solution 1: Ensure you're in the correct directory
```bash
# Navigate to the project root (where src/ folder exists)
cd /path/to/your/project
docker-compose up --build
```

### Solution 2: Use the simple compose file
```bash
# This works from any directory
mkdir -p mocks
docker-compose -f docker-compose.simple.yml up
```

### Solution 3: Run without volume mount (uses built-in mocks)
```bash
docker run -p 8080:8080 ghcr.io/[your-username]/mocku:latest
```

### Solution 4: Create mocks directory manually
```bash
# Windows
mkdir mocks
echo {} > mocks/example.json

# Linux/Mac  
mkdir -p mocks
echo '{}' > mocks/example.json

docker-compose up
```

## GitHub Actions CI/CD Pipeline

The repository includes a GitHub Actions workflow that automatically:

1. **Builds** the Docker image on every push to main/master
2. **Pushes** to GitHub Container Registry (ghcr.io)
3. **Scans** for security vulnerabilities using Trivy
4. **Tags** images based on branch/tag names

### Automatic Tagging Strategy

- `latest` - Latest commit on the default branch
- `main` or `master` - Latest commit on the respective branch
- `v1.2.3` - Semantic version tags
- `v1.2` - Major.minor version tags
- `v1` - Major version tags
- `main-abc1234` - Branch name with commit SHA

### Required Permissions

The workflow requires the following permissions (automatically granted):
- `contents: read` - To checkout the repository
- `packages: write` - To push to GitHub Container Registry
- `security-events: write` - To upload security scan results

## Production Deployment

### Environment Variables

The application supports the following environment variables:

```bash
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:8080
MockApi__Directory=/app/mocks  # Path to mock files directory
```

### Docker Run Example

```bash
docker run -d \
  --name mocku-web \
  -p 8080:8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e ASPNETCORE_URLS=http://+:8080 \
  -v /path/to/your/mocks:/app/mocks:ro \
  --restart unless-stopped \
  ghcr.io/[your-username]/mocku:latest
```

### Available Docker Compose Files

1. **docker-compose.yml** - For development from project root
2. **docker-compose.simple.yml** - For running from any directory with pre-built images
3. **docker-compose.override.yml** - Development overrides (auto-loaded with docker-compose.yml)

### Kubernetes Deployment

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: mocku-web
spec:
  replicas: 3
  selector:
    matchLabels:
      app: mocku-web
  template:
    metadata:
      labels:
        app: mocku-web
    spec:
      containers:
      - name: mocku-web
        image: ghcr.io/[your-username]/mocku:latest
        ports:
        - containerPort: 8080
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: "Production"
        - name: ASPNETCORE_URLS
          value: "http://+:8080"
        volumeMounts:
        - name: mocks-volume
          mountPath: /app/mocks
          readOnly: true
      volumes:
      - name: mocks-volume
        configMap:
          name: mocku-mocks
---
apiVersion: v1
kind: Service
metadata:
  name: mocku-web-service
spec:
  selector:
    app: mocku-web
  ports:
  - protocol: TCP
    port: 80
    targetPort: 8080
  type: LoadBalancer
```

## Building Images Manually

### Build for Single Platform
```bash
docker build -t mocku-web .
```

### Build for Multiple Platforms
```bash
docker buildx build --platform linux/amd64,linux/arm64 -t mocku-web .
```

### Build and Push to GHCR
```bash
# Login to GHCR
echo $GITHUB_TOKEN | docker login ghcr.io -u USERNAME --password-stdin

# Build and push
docker buildx build --platform linux/amd64,linux/arm64 \
  -t ghcr.io/[your-username]/mocku:latest \
  --push .
```

## Troubleshooting

### Common Issues

1. **"not a directory" mount error:**
   - Ensure the mocks directory exists: `mkdir -p mocks`
   - Check you're in the project root directory
   - Use `docker-compose.simple.yml` for external directories

2. **Permission denied when pushing to GHCR:**
   - Ensure `GITHUB_TOKEN` has `packages: write` permission
   - Make sure the repository visibility allows package creation

3. **Mock files not loading:**
   - Verify the volume mount path: `-v /host/path:/app/mocks:ro`
   - Check file permissions on the host system
   - Ensure JSON files are valid

4. **Application not accessible:**
   - Verify port mapping: `-p 8080:8080`
   - Check if the container is running: `docker ps`
   - Review container logs: `docker logs [container-name]`

### Useful Commands

```bash
# View container logs
docker logs -f mocku-web

# Execute shell in running container
docker exec -it mocku-web /bin/bash

# List files in mocks directory inside container
docker exec -it mocku-web ls -la /app/mocks

# Inspect image details
docker inspect ghcr.io/[your-username]/mocku:latest

# Remove all containers and images
docker system prune -a

# Test if mocks directory is properly mounted
docker exec -it mocku-web cat /app/mocks/sample.json
```

## Security Considerations

1. **Image Scanning:** The CI/CD pipeline includes Trivy security scanning
2. **Minimal Base Image:** Uses official Microsoft ASP.NET Core runtime image
3. **Non-root User:** Consider adding a non-root user in the Dockerfile for production
4. **Secrets Management:** Use environment variables or secret management systems for sensitive data

## Performance Optimization

1. **Multi-stage Build:** The Dockerfile uses multi-stage builds to minimize image size
2. **Docker Layer Caching:** The GitHub Actions workflow includes build cache optimization
3. **Multi-platform Support:** Images are built for both AMD64 and ARM64 architectures
4. **Built-in Mocks:** Default mocks are included in the image, external mounting is optional