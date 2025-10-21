# OmgekeerdeStemWijzer.Api

Hoi! 👋 Deze kleine API is mijn speelse poging om PDF-bestanden om te zetten in gevectoriseerde politieke fragmenten, zodat je vragen kunt stellen zoals: "Wat zegt partij X over onderwerp Y?"

Het is simpel, vriendelijk en gemaakt om mee te experimenteren:

- Importeer PDF's vanaf `Data/Manifesten/`.
- Verdeel de tekst in hapklare `PoliticalChunk`s.
- Genereer embeddings met een lokale Ollama-modelserver (of via `OllamaSharp` als die beschikbaar is).
- Sla vectoren op in een in-memory Chroma-achtige collectie voor snelle experimenten.

Waarom ik dit maakte
- Ik wilde een snelle manier om politieke manifesten te indexeren en te spelen met semantische zoekopdrachten.
- Ik was het zat dat elke stemwijzer hetzelfde was; je krijgt een aantal stellingen en je moet maar antwoord geven.

Snelle start (development)

1. Zorg dat .NET 9 geïnstalleerd is.
2. Stel de Ollama-server-URL in `appsettings.json` onder `Ollama:Url` (of via een omgevingsvariabele).
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
- De `EmbeddingService` probeert eerst de `OllamaSharp` client via reflection te gebruiken voor compatibiliteit met meerdere versies. Als dat niet lukt, doet hij een HTTP POST naar je Ollama-server en probeert een aantal endpoint-vormen, en cachet de werkende endpoint.
- 
## Geheimen en API-keys

Het project verwacht een Groq API key in de configuratie als je die gebruikt. Je moet deze lokaal of in CI veilig instellen.
