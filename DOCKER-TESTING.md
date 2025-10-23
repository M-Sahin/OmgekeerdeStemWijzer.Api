# Quick Docker Testing Guide

1. **`.env` file** - Contains all your API keys (already filled with your keys)
2. **`docker-compose.yml`** - Automatically loads the `.env` file
3. **`.gitignore`** - Updated to prevent committing `.env` to Git

##  Commands

### Start the application with automatic API key loading:
```powershell
docker-compose up
```

### Start with rebuild 
```powershell
docker-compose up --build
```

### Run in background:
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

### Test the Application

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