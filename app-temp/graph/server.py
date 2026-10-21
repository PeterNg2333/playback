"""Playback concept graph: a loopback-only LightRAG sidecar with one workspace per session.

The .NET API sends one-minute lecture documents (document ID = "{session}-concept-{source}-{minute}") and asks for the
entities, relations and chunks related to a question. A changed document is deleted and inserted again, so LightRAG
rebuilds only the entities it supported. Gemini is reached through Vertex AI express mode with the same API key the API
uses (GOOGLE_AI_STUDIO_API_KEY, read from the process environment only); LightRAG's own Gemini binding needs a Google
Cloud project for Vertex, so this module supplies its own completion and embedding functions.

Run: python -I app-temp/graph/server.py   (binds 127.0.0.1:PLAYBACK_GRAPH_PORT, default 5090)
"""

from __future__ import annotations

import asyncio
import json
import os
import random
import re
import time
from pathlib import Path
from typing import Any

import numpy as np
import uvicorn
from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import JSONResponse
from google import genai
from google.genai import errors, types
from lightrag import LightRAG, QueryParam
from lightrag.utils import wrap_embedding_func_with_attrs
from pydantic import BaseModel, Field

ROOT = Path(os.environ.get("PLAYBACK_GRAPH_DIR") or Path(__file__).resolve().parent.parent / "data" / "graph")
PORT = int(os.environ.get("PLAYBACK_GRAPH_PORT") or "5090")
MODEL = os.environ.get("PLAYBACK_GRAPH_MODEL") or "gemini-3.1-flash-lite"
EMBED_MODEL = os.environ.get("PLAYBACK_GRAPH_EMBED_MODEL") or "gemini-embedding-001"
EMBED_DIM = 1536
MAX_BODY_BYTES = 2_000_000
IDLE_SECONDS = 15 * 60
ALLOWED_MODELS = {"gemini-3.1-flash-lite", "gemini-3.5-flash-lite"}
SESSION_ID = re.compile(r"^[a-f0-9]{32}$")
DOC_ID = re.compile(r"^[a-f0-9]{32}-concept-[A-Za-z0-9_]{1,40}-\d{1,7}$")

if MODEL not in ALLOWED_MODELS:
    raise SystemExit("PLAYBACK_GRAPH_MODEL is not allowlisted")
_key = os.environ.get("GOOGLE_AI_STUDIO_API_KEY")
if not _key:
    raise SystemExit("GOOGLE_AI_STUDIO_API_KEY is not in the process environment")
client = genai.Client(vertexai=True, api_key=_key, http_options=types.HttpOptions(timeout=120_000))
del _key


async def retrying(call):
    """Vertex express answers bursts with 429; wait 2, 4, 8, 16 s (or longer under load) before giving up."""
    for attempt in range(5):
        try:
            return await call()
        except (errors.ClientError, errors.ServerError) as error:
            if attempt == 4 or not (getattr(error, "code", 0) == 429 or getattr(error, "code", 0) >= 500):
                raise
            await asyncio.sleep(2 ** (attempt + 1) + random.random())


async def complete(prompt: str, system_prompt: str | None = None, history_messages: list[dict[str, Any]] | None = None,
                   keyword_extraction: bool = False, **_: Any) -> str:
    contents = [types.Content(role="model" if m.get("role") == "assistant" else "user", parts=[types.Part(text=str(m.get("content", "")))])
                for m in (history_messages or [])]
    contents.append(types.Content(role="user", parts=[types.Part(text=prompt)]))
    config = types.GenerateContentConfig(system_instruction=system_prompt, max_output_tokens=8192,
                                         response_mime_type="application/json" if keyword_extraction else None)
    response = await retrying(lambda: client.aio.models.generate_content(model=MODEL, contents=contents, config=config))
    return response.text or ""


_embed_slots = asyncio.Semaphore(2)


@wrap_embedding_func_with_attrs(embedding_dim=EMBED_DIM, max_token_size=2048, model_name=EMBED_MODEL)
async def embed(texts: list[str], **_: Any) -> np.ndarray:
    async def one(text: str) -> list[float]:
        async with _embed_slots:
            result = await retrying(lambda: client.aio.models.embed_content(
                model=EMBED_MODEL, contents=text, config=types.EmbedContentConfig(output_dimensionality=EMBED_DIM)))
            return list(result.embeddings[0].values)
    # One text per request: Vertex embedding models accept a single input per call.
    return np.array(await asyncio.gather(*(one(text) for text in texts)), dtype=np.float32)


class Workspace:
    def __init__(self, session_id: str, language: str):
        self.session_id = session_id
        self.language = language
        self.used = time.monotonic()
        self.lock = asyncio.Lock()
        self.index_path = ROOT / session_id / "playback-index.json"
        self.rag = LightRAG(working_dir=str(ROOT), workspace=session_id, llm_model_func=complete, llm_model_name=MODEL,
                            embedding_func=embed, addon_params={"language": language},
                            llm_model_max_async=2, embedding_func_max_async=2)

    async def start(self) -> None:
        await self.rag.initialize_storages()
        try:  # Older releases need the shared pipeline status initialized separately.
            from lightrag.kg.shared_storage import initialize_pipeline_status
            await initialize_pipeline_status()
        except ImportError:
            pass

    def hashes(self) -> dict[str, str]:
        try:
            return json.loads(self.index_path.read_text(encoding="utf-8"))
        except (FileNotFoundError, json.JSONDecodeError):
            return {}

    def save(self, hashes: dict[str, str]) -> None:
        self.index_path.parent.mkdir(parents=True, exist_ok=True)
        temporary = self.index_path.with_suffix(".part")
        temporary.write_text(json.dumps(hashes), encoding="utf-8")
        temporary.replace(self.index_path)


workspaces: dict[str, Workspace] = {}
workspaces_lock = asyncio.Lock()


async def workspace(session_id: str, language: str | None = None) -> Workspace:
    if not SESSION_ID.match(session_id):
        raise HTTPException(400, "Invalid session identity")
    async with workspaces_lock:
        now = time.monotonic()
        for key, idle in list(workspaces.items()):
            if now - idle.used > IDLE_SECONDS and not idle.lock.locked():
                await idle.rag.finalize_storages()
                del workspaces[key]
        current = workspaces.get(session_id)
        if current is None or (language and current.language != language):
            if current is not None:
                async with current.lock:
                    await current.rag.finalize_storages()
            current = Workspace(session_id, language or (current.language if current else "English"))
            await current.start()
            workspaces[session_id] = current
        current.used = now
        return current


class Document(BaseModel):
    id: str = Field(max_length=120)
    text: str = Field(max_length=20_000)
    hash: str = Field(max_length=128)


class Upsert(BaseModel):
    language: str = Field(max_length=60)
    docs: list[Document] = Field(max_length=16)


class Delete(BaseModel):
    ids: list[str] = Field(max_length=500)


class Question(BaseModel):
    question: str = Field(min_length=1, max_length=1000)


app = FastAPI(docs_url=None, redoc_url=None, openapi_url=None)


@app.middleware("http")
async def bounded(request: Request, call_next):
    if request.client is None or request.client.host not in ("127.0.0.1", "::1"):
        return JSONResponse({"error": "Loopback only"}, status_code=403)
    length = request.headers.get("content-length")
    if length is not None and int(length) > MAX_BODY_BYTES:
        return JSONResponse({"error": "Request too large"}, status_code=413)
    return await call_next(request)


def documents(field: str) -> list[str]:
    return [x for x in str(field or "").split("<SEP>") if DOC_ID.match(x)]


@app.get("/health")
async def health():
    return {"status": "ok", "model": MODEL, "embedModel": EMBED_MODEL, "workspaces": len(workspaces)}


@app.put("/sessions/{session_id}/docs")
async def upsert(session_id: str, body: Upsert):
    if any(not DOC_ID.match(d.id) or not d.id.startswith(session_id) for d in body.docs):
        raise HTTPException(400, "Invalid document identity")
    space = await workspace(session_id, body.language)
    inserted = replaced = unchanged = 0
    failed: list[str] = []
    async with space.lock:
        hashes = space.hashes()
        for doc in body.docs:
            if hashes.get(doc.id) == doc.hash:
                unchanged += 1
                continue
            try:
                if doc.id in hashes:
                    result = await space.rag.adelete_by_doc_id(doc.id)
                    if getattr(result, "status", "success") not in ("success", "not_found"):
                        raise RuntimeError(getattr(result, "message", "delete failed"))
                    replaced += 1
                else:
                    inserted += 1
                await space.rag.ainsert(doc.text, ids=[doc.id], file_paths=[doc.id])
                # ainsert records an extraction failure in doc status instead of raising.
                status = await space.rag.doc_status.get_by_id(doc.id) or {}
                state = str(status.get("status", "") if isinstance(status, dict) else getattr(status, "status", "")).lower()
                if not state.endswith("processed"):
                    await space.rag.adelete_by_doc_id(doc.id)
                    hashes.pop(doc.id, None)
                    space.save(hashes)
                    raise RuntimeError(f"document status {state or 'unknown'}")
                hashes[doc.id] = doc.hash
                space.save(hashes)
            except Exception as error:  # Reported per document; the API retries later.
                failed.append(doc.id)
                print(f"concept graph: {doc.id} failed: {type(error).__name__}", flush=True)
    return {"inserted": inserted, "replaced": replaced, "unchanged": unchanged, "failed": failed}


@app.post("/sessions/{session_id}/docs/delete")
async def delete(session_id: str, body: Delete):
    space = await workspace(session_id)
    removed = 0
    async with space.lock:
        hashes = space.hashes()
        for doc_id in body.ids:
            if DOC_ID.match(doc_id) and doc_id in hashes:
                await space.rag.adelete_by_doc_id(doc_id)
                del hashes[doc_id]
                removed += 1
        space.save(hashes)
    return {"removed": removed}


@app.post("/sessions/{session_id}/query")
async def query(session_id: str, body: Question):
    space = await workspace(session_id)
    result = await space.rag.aquery_data(body.question, param=QueryParam(mode="mix", top_k=20, chunk_top_k=10))
    data = result.get("data") or {}
    return {
        "entities": [{"name": e.get("entity_name", ""), "type": e.get("entity_type", ""), "description": e.get("description", ""),
                      "documentIds": documents(e.get("file_path"))} for e in data.get("entities", [])],
        "relations": [{"source": r.get("src_id", ""), "target": r.get("tgt_id", ""), "description": r.get("description", ""),
                       "keywords": r.get("keywords", ""), "documentIds": documents(r.get("file_path"))} for r in data.get("relationships", [])],
        "chunks": [{"documentId": (documents(c.get("file_path")) or [""])[0], "content": c.get("content", "")} for c in data.get("chunks", [])],
    }


@app.get("/sessions/{session_id}/graph")
async def graph(session_id: str, limit: int = 200):
    space = await workspace(session_id)
    knowledge = await space.rag.get_knowledge_graph(node_label="*", max_depth=2, max_nodes=max(1, min(limit, 500)))
    names = {node.id: (node.properties or {}).get("entity_id", node.id) for node in knowledge.nodes}
    return {
        "entities": [{"name": names[n.id], "type": (n.properties or {}).get("entity_type", ""), "description": (n.properties or {}).get("description", ""),
                      "documentIds": documents((n.properties or {}).get("file_path"))} for n in knowledge.nodes],
        "relations": [{"source": names.get(e.source, e.source), "target": names.get(e.target, e.target),
                       "description": (e.properties or {}).get("description", ""), "keywords": (e.properties or {}).get("keywords", ""),
                       "documentIds": documents((e.properties or {}).get("file_path"))} for e in knowledge.edges],
        "chunks": [],
    }


if __name__ == "__main__":
    ROOT.mkdir(parents=True, exist_ok=True)
    uvicorn.run(app, host="127.0.0.1", port=PORT, log_level="warning")
