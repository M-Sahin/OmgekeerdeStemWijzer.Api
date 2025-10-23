# OmgekeerdeStemWijzer.Api — Architecture Overview

This document describes the high-level architecture, major components, data flows, and runtime configuration for the OmgekeerdeStemWijzer API project.

## Goals
- Explain how PDF ingestion, embedding generation, and vector storage are composed.
- Show the dependencies and where to change configuration (OpenAI, Chroma, Groq).
- Provide a concise diagram and component responsibilities for maintainers.

## High-level diagram

```
+---------------------------+        +----------------------+        +-----------------------+
| Clients / Frontend        |  --->  | ASP.NET Core Web API |  --->  | Vector Store (Chroma) |
| (browser / CLI / tests)   |        | (Controllers)        |        | In-Memory / Service   |
+---------------------------+        +----------------------+        +-----------------------+
                                          |       ^   ^
                                          |       |   |
                                          v       |   |
                                   +------------------------+
                                   | Services Layer         |  |
                                   | - IngestionController  |  |
                                   | - EmbeddingService     |--+
                                   | - DocumentProcessor    |
                                   | - VectorStoreService   |
                                   +------------------------+
                                          |
                                          v
                                   +------------------------+
                                   | External APIs          |
                                   | - OpenAI Embeddings API|
                                   |   (text-embedding-3-   |
                                   |    small model)        |
                                   +------------------------+

```

## Components

- Program.cs / Startup
  - Registers services via dependency injection.
  - Configures logging, Swagger, and controller endpoints.
  - Ensures `VectorStoreService.InitializeAsync()` runs at startup.

- Controllers
  - `IngestionController` handles ingestion endpoints. It orchestrates reading PDFs, chunking text, requesting embeddings, and storing vectors.
  - `MatchingController` (if present) exposes search/query endpoints against the vector store.

- Services
  - `DocumentProcessor`
    - Reads PDF files (UglyToad.PdfPig), extracts text, and splits into chunks (configurable chunk size/overlap).
    - Produces `PoliticalChunk` models.

  - `EmbeddingService`
    - Responsible for generating vector embeddings for text.
    - Uses the OpenAI .NET SDK to call the OpenAI Embeddings API.
    - Uses the `text-embedding-3-small` model by default (configurable in appsettings.json).
    - Returns a float[] embedding vector.
    - Includes logging for API calls and error handling.

  - `VectorStoreService`
    - Wraps an in-memory Chroma-like collection (for local dev). Stores embeddings and associated metadata (PoliticalChunk).
    - Provides initialization and query APIs.

- Models
  - `PoliticalChunk` — small piece of document with metadata (party, theme, page, text chunk, id).

## Data flow (ingestion)
1. Client triggers ingestion (POST /api/ingestion/start-indexing).
2. `IngestionController` enumerates PDF files in `Data/Manifesten/`.
3. For each PDF, `DocumentProcessor` extracts and chunks text.
4. For each chunk, `EmbeddingService.GenerateEmbeddingAsync(text)` is called:
   - Calls OpenAI Embeddings API using the configured model (text-embedding-3-small).
   - Returns the embedding vector as float[].
5. `VectorStoreService.AddChunkAsync(chunk, embedding)` stores the embedding and metadata.
6. In the future, `MatchingController` queries the vector store and returns nearest chunks.

## Configuration
- `appsettings.json` contains keys and URLs for:
  - `OpenAI:ApiKey` (OpenAI API key for embeddings)
  - `OpenAI:EmbeddingModel` (embedding model to use, defaults to text-embedding-3-small)
  - `ServiceUrls:ChromaDb` (if using an external Chroma service)
  - `Groq:ApiKey` (for LLM responses if used)

Prefer to store secrets (API keys) in user secrets or environment variables rather than in repo files.

## Error handling and fallbacks
- `EmbeddingService` handles OpenAI API errors gracefully with proper exception logging.
- API calls include timeout configuration and error messages for debugging.
- Vector store operations are resilient for local dev; consider using an external persistent store for production.

## Extension points / TODOs
- Add batching/parallel embedding calls to speed up ingestion.
- Consider using OpenAI's batch API for cost optimization on large datasets.
- Add unit/integration tests for embedding API calls and vector-store queries.
- Secure configuration using user secrets / environment variables.

## Files of interest

```mermaid
flowchart LR
  Client["Client / Frontend\n(browser / CLI / tests)"] --> API["ASP.NET Core Web API\n(Controllers)"]
  API --> Vector["Vector Store\n(Chroma / In-Memory)"]
  API --> Services["Services Layer\n(Ingestion, Embedding, DocumentProcessor, VectorStore)"]
  Services --> OpenAI["OpenAI Embeddings API\n(text-embedding-3-small)"]
  Services --> Vector
  style API fill:#eef,stroke:#333,stroke-width:1px
  style OpenAI fill:#efe,stroke:#333,stroke-width:1px
  style Vector fill:#fee,stroke:#333,stroke-width:1px

![Architecture diagram](assets/architecture.svg)

