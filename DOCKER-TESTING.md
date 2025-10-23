# Quick Docker Testing Guide

## ✅ What We Set Up

1. **`.env` file** - Contains all your API keys (already filled with your keys)
2. **`docker-compose.yml`** - Automatically loads the `.env` file
3. **`.gitignore`** - Updated to prevent committing `.env` to Git

## 🚀 Commands

### Start the application with automatic API key loading:
```powershell
docker-compose up
```

### Start with rebuild (if you changed code):
```powershell
docker-compose up --build
```

### Run in background (detached mode):
```powershell
docker-compose up -d
```

### Stop the application:
```powershell
docker-compose down
```

### View logs:
```powershell
docker-compose logs -f
```

### View running containers:
```powershell
docker-compose ps
```

## 🧪 Test the Application

Once running, test these endpoints:

```powershell
# Health check
curl http://localhost:8080/health

# Swagger UI (in browser)
http://localhost:8080/swagger

# Start indexing PDFs
curl -X POST http://localhost:8080/api/ingestion/start-indexing

# Test matching
curl -X POST http://localhost:8080/api/matching/match `
  -H "Content-Type: application/json" `
  -d '{"messages":[{"content":"Ik wil lagere belastingen"}]}'
```

## 📝 How It Works

1. Docker Compose reads the `.env` file
2. Environment variables are passed to the container
3. ASP.NET Core automatically maps them to configuration:
   - `OpenAI__ApiKey` → `OpenAI:ApiKey`
   - `Groq__ApiKey` → `Groq:ApiKey`
4. Your application starts with all secrets loaded! ✨

## 🔒 Security Notes

- ✅ `.env` is in `.gitignore` - won't be committed to Git
- ✅ API keys are loaded from `.env` automatically
- ✅ No need to manually type API keys each time
- ⚠️ Never commit `.env` to version control!

## 🎯 Production Deployment

For Azure deployment, you won't use `.env`. Instead:
- Use Azure Container Apps secrets
- Or Azure Key Vault
- Or App Service application settings

See `DEPLOYMENT.md` for details.
