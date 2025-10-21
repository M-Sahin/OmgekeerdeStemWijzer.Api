# OmgekeerdeStemWijzer.Api — Architecture Overview

This document describes the high-level architecture, major components, data flows, and runtime configuration for the OmgekeerdeStemWijzer API project.

## Goals
- Explain how PDF ingestion, embedding generation, and vector storage are composed.
- Show the dependencies and where to change configuration (Ollama, Chroma, Groq).
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
                                   +------------------------+  |
                                   | Services Layer         |  |
                                   | - IngestionController  |  |
                                   | - EmbeddingService     |--+
                                   | - DocumentProcessor    |
                                   | - VectorStoreService   |
                                   +------------------------+
                                          |
                                          v
                                   +------------------------+
                                   | External Models/Engines|
                                   | - Ollama (local server)|
                                   |   accessed via HTTP or |
                                   |   OllamaSharp reflection|
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
    - Attempts to use `OllamaSharp` (if available) via reflection for compatibility across versions.
    - Falls back to HTTP requests against the configured Ollama server endpoints (tries `/api/embeddings`, `/api/embed`, `/embed` and caches the working one).
    - Returns a float[] embedding or an empty array when unavailable.
    - Includes logging for decision points (reflection found/failed, HTTP endpoint chosen, errors).

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
   - Try reflection-based call into OllamaSharp client if available.
   - If failing or unavailable, perform HTTP POST to the Ollama server and parse the JSON response.
   - Cache the working HTTP endpoint for future requests.
5. `VectorStoreService.AddChunkAsync(chunk, embedding)` stores the embedding and metadata.
6. In the future, `MatchingController` queries the vector store and returns nearest chunks.

## Configuration
- `appsettings.json` contains keys and URLs for:
  - `Ollama:Url` (base URL for local Ollama server)
  - `Chroma:Url` (if using an external Chroma service)
  - `Groq:ApiKey` (if used elsewhere)

Prefer to store secrets (API keys) in user secrets or environment variables rather than in repo files.

## Error handling and fallbacks
- `EmbeddingService` uses reflection-first strategy and marks an internal flag `_httpFallbackOnly` to avoid repeated reflection attempts if not available.
- HTTP fallback tries multiple endpoints and caches the one that works for faster subsequent calls.
- Vector store operations are resilient for local dev; consider using an external persistent store for production.

## Extension points / TODOs
- Add batching/parallel embedding calls to speed up ingestion.
- Replace reflection with direct `OllamaSharp` usage after choosing a version; the reflection approach is safer for cross-version compatibility.
- Add unit/integration tests for embedding fallback behaviors and vector-store queries.
- Secure configuration using user secrets / environment variables.

## Files of interest

```mermaid
flowchart LR
  Client["Client / Frontend\n(browser / CLI / tests)"] --> API["ASP.NET Core Web API\n(Controllers)"]
  API --> Vector["Vector Store\n(Chroma / In-Memory)"]
  API --> Services["Services Layer\n(Ingestion, Embedding, DocumentProcessor, VectorStore)"]
  Services --> Ollama["Ollama (Model Server)\nvia HTTP or OllamaSharp"]
  Services --> Vector
  style API fill:#eef,stroke:#333,stroke-width:1px
  style Ollama fill:#efe,stroke:#333,stroke-width:1px
  style Vector fill:#fee,stroke:#333,stroke-width:1px

![Architecture diagram](assets/architecture.svg)

