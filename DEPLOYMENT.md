# Azure Deployment Guide

## Quick Steps to Deploy to Azure

### 1. Build and Test Docker Image Locally

```powershell
# Build the Docker image
docker build -t omgekeerdestemwijzer:latest .

# Test the image locally
docker run -p 8080:8080 `
  -e OpenAI__ApiKey="your-openai-key" `
  -e Groq__ApiKey="your-groq-key" `
  omgekeerdestemwijzer:latest

# Test the health endpoint
curl http://localhost:8080/health
```

### 2. Push to Azure Container Registry (ACR)

```powershell
# Login to Azure
az login

# Create a resource group (if not exists)
az group create --name omgekeerde-stemwijzer-rg --location westeurope

# Create Azure Container Registry
az acr create --resource-group omgekeerde-stemwijzer-rg `
  --name omgekeerdestemwijzer --sku Basic

# Login to ACR
az acr login --name omgekeerdestemwijzer

# Tag your image
docker tag omgekeerdestemwijzer:latest omgekeerdestemwijzer.azurecr.io/omgekeerdestemwijzer:latest

# Push to ACR
docker push omgekeerdestemwijzer.azurecr.io/omgekeerdestemwijzer:latest
```

### 3. Deploy to Azure Container Apps

```powershell
# Install Container Apps extension
az extension add --name containerapp --upgrade

# Create Container Apps environment
az containerapp env create `
  --name omgekeerde-env `
  --resource-group omgekeerde-stemwijzer-rg `
  --location westeurope

# Create Container App
az containerapp create `
  --name omgekeerde-stemwijzer-api `
  --resource-group omgekeerde-stemwijzer-rg `
  --environment omgekeerde-env `
  --image omgekeerdestemwijzer.azurecr.io/omgekeerdestemwijzer:latest `
  --target-port 8080 `
  --ingress external `
  --registry-server omgekeerdestemwijzer.azurecr.io `
  --query properties.configuration.ingress.fqdn

# Set environment variables (secrets)
az containerapp update `
  --name omgekeerde-stemwijzer-api `
  --resource-group omgekeerde-stemwijzer-rg `
  --set-env-vars "OpenAI__ApiKey=secretref:openai-key" "Groq__ApiKey=secretref:groq-key" `
  --secrets "openai-key=your-openai-api-key" "groq-key=your-groq-api-key"
```

### 4. Alternative: Deploy to Azure Web App for Containers

```powershell
# Create App Service Plan
az appservice plan create `
  --name omgekeerde-plan `
  --resource-group omgekeerde-stemwijzer-rg `
  --is-linux `
  --sku B1

# Create Web App
az webapp create `
  --resource-group omgekeerde-stemwijzer-rg `
  --plan omgekeerde-plan `
  --name omgekeerde-stemwijzer `
  --deployment-container-image-name omgekeerdestemwijzer.azurecr.io/omgekeerdestemwijzer:latest

# Configure ACR credentials
az webapp config container set `
  --name omgekeerde-stemwijzer `
  --resource-group omgekeerde-stemwijzer-rg `
  --docker-custom-image-name omgekeerdestemwijzer.azurecr.io/omgekeerdestemwijzer:latest `
  --docker-registry-server-url https://omgekeerdestemwijzer.azurecr.io

# Set application settings (environment variables)
az webapp config appsettings set `
  --resource-group omgekeerde-stemwijzer-rg `
  --name omgekeerde-stemwijzer `
  --settings OpenAI__ApiKey="your-openai-key" Groq__ApiKey="your-groq-key"
```

### 5. Verify Deployment

```powershell
# Get the application URL
az containerapp show `
  --name omgekeerde-stemwijzer-api `
  --resource-group omgekeerde-stemwijzer-rg `
  --query properties.configuration.ingress.fqdn

# Or for Web App
az webapp show `
  --name omgekeerde-stemwijzer `
  --resource-group omgekeerde-stemwijzer-rg `
  --query defaultHostName

# Test the deployment
curl https://your-app-url/health
```

## Environment Variables for Production

Set these in Azure:
- `OpenAI__ApiKey` - Your OpenAI API key
- `Groq__ApiKey` - Your Groq API key
- `ServiceUrls__ChromaDb` - ChromaDB URL (if using external service)
- `ASPNETCORE_ENVIRONMENT` - Set to "Production"

## Important Notes

1. **API Keys**: Never commit API keys to your repository. Use Azure Key Vault or Container Apps secrets.
2. **Costs**: Basic SKU for ACR and B1 App Service Plan have associated costs.
3. **Data Persistence**: The in-memory vector store will reset on restart. Consider using persistent storage.
4. **Scaling**: Container Apps auto-scales based on HTTP traffic.
5. **Monitoring**: Enable Application Insights for production monitoring.

## CI/CD with GitHub Actions (Optional)

Create `.github/workflows/azure-deploy.yml`:

```yaml
name: Deploy to Azure

on:
  push:
    branches: [ main ]

jobs:
  build-and-deploy:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v3
    
    - name: Login to Azure
      uses: azure/login@v1
      with:
        creds: ${{ secrets.AZURE_CREDENTIALS }}
    
    - name: Build and push image
      run: |
        az acr build --registry omgekeerdestemwijzer \
          --image omgekeerdestemwijzer:${{ github.sha }} \
          --image omgekeerdestemwijzer:latest .
    
    - name: Deploy to Container App
      run: |
        az containerapp update \
          --name omgekeerde-stemwijzer-api \
          --resource-group omgekeerde-stemwijzer-rg \
          --image omgekeerdestemwijzer.azurecr.io/omgekeerdestemwijzer:${{ github.sha }}
```
