# OmgekeerdeStemWijzer.Api

Deze kleine API is mijn poging om PDF-bestanden om te zetten in gevectoriseerde politieke fragmenten, zodat je vragen kunt stellen zoals: "Wat zegt partij X over onderwerp Y?"

Het is simpel, robuust en gemaakt om mee te experimenteren:

- Importeer PDF's vanaf `Data/Manifesten/`.
- Verdeel de tekst in hapklare `PoliticalChunk`s.
- Genereer embeddings met OpenAI's text-embedding-3-small model via de OpenAI API.
- Sla vectoren op in een in-memory Chroma-achtige collectie voor snelle experimenten.

Waarom ik dit maakte:
- Ik wilde een snelle manier om politieke manifesten te indexeren en te spelen met semantische zoekopdrachten.
- Ik was het zat dat elke stemwijzer hetzelfde was; je krijgt een aantal stellingen en je moet maar antwoord geven.

Snelle start (development)

1. Zorg dat .NET 9 geïnstalleerd is.
2. Stel je OpenAI API key in via user secrets of `appsettings.json` onder `OpenAI:ApiKey`:
   ```powershell
   dotnet user-secrets set "OpenAI:ApiKey" "sk-your-api-key-here"
   ```
3. Plaats PDF's in `Data/Manifesten/`.
4. Vanuit de projectroot voer je uit:

```powershell
dotnet build
dotnet run --project OmgekeerdeStemWijzer.Api.csproj
```

Roep daarna de ingest-endpoint aan (bijvoorbeeld met curl of Postman):

```powershell
curl -X POST http://localhost:5000/api/ingestion/start-indexing
```

Notities & tips
- De `EmbeddingService` gebruikt de OpenAI .NET SDK om embeddings te genereren met het `text-embedding-3-small` model.
- Je kunt het embedding model aanpassen in `appsettings.json` onder `OpenAI:EmbeddingModel`.
 
## Geheimen en API-keys

Het project verwacht de volgende API keys in de configuratie:
- **OpenAI API Key** voor embeddings (verplicht)
- **Groq API Key** voor LLM responses (indien gebruikt)

Bewaar deze veilig via user secrets of omgevingsvariabelen, niet in de repository.

## Chroma v2 configuratie

Deze API gebruikt Chroma v2 als vector store. Stel de volgende variabelen in (via `.env`, user secrets of het hosting platform):

- ServiceUrls__ChromaDb = jouw Chroma basis-URL (bijv. https://omgekeerdestemwijzer-chromadb.onrender.com)
- Chroma__ApiKeyHeader = x-chroma-token
- Chroma__ApiKey = jouw Chroma token (zonder "Bearer ")
- Chroma__Tenant = jouw tenant (bijv. default_tenant)
- Chroma__Database = jouw database (bijv. default_database)

Opmerking:
- De client spreekt de v2 endpoints aan (base path /api/v2)
- Upsert en Query gebruiken het collection UUID (geen naam) en sturen POST verzoeken naar respectievelijk `/upsert` en `/query`.
