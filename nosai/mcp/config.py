from __future__ import annotations

import json
from pathlib import Path
from typing import Any


DEFAULT_CONFIG: dict[str, Any] = {
    "schema_version": "mcp.config.v1",
    "network_enabled": False,
    "audit_path": "data/mcp/audit.jsonl",
    "learning_path": "data/mcp/learning",
    "secret_path": "data/mcp/secrets.enc.json",
    "role_bindings_path": "data/mcp/role_bindings.json",
    "state_path": "data/mcp/state.sqlite3",
    "providers": [
        {"provider_id": "ollama-local", "model_id": "qwen2.5-coder:7b", "aliases": [], "tier": 0, "cost_class": "local", "enabled": True, "network_required": False, "capabilities": ["documentation", "scaffold", "offline"], "sec_medi": 14, "bench": "a+b", "misurato_il": "2026-09-09", "routable": True, "quota": "nessun tetto: gira in locale"},
        {"provider_id": "groq-free", "model_id": "qwen/qwen3.8-27b", "aliases": ["qwen3.8-27b"], "tier": 1, "cost_class": "free", "enabled": True, "network_required": True, "capabilities": ["coding", "tactical"], "sec_medi": 0.7, "bench": "a+b", "misurato_il": "2026-09-10", "routable": True, "quota": "tier gratuito Groq"},
        {"provider_id": "groq-free", "model_id": "openai/gpt-oss-120b", "aliases": ["gpt-oss-120b"], "tier": 1, "cost_class": "free", "enabled": True, "network_required": True, "capabilities": ["coding", "tactical", "review"], "sec_medi": 1.7, "bench": "a+b", "misurato_il": "2026-09-10", "routable": True, "quota": "tier gratuito Groq; servono almeno 300 token di budget, spende i primi in ragionamento"},
        {"provider_id": "groq-free", "model_id": "openai/gpt-oss-20b", "aliases": ["gpt-oss-20b"], "tier": 1, "cost_class": "free", "enabled": True, "network_required": True, "capabilities": ["coding", "tactical"], "sec_medi": 1.5, "bench": "a+b", "misurato_il": "2026-09-10", "routable": True, "quota": "tier gratuito Groq; servono almeno 300 token di budget"},
        {"provider_id": "groq-free", "model_id": "groq/compound-mini", "aliases": ["compound-mini"], "tier": 1, "cost_class": "free", "enabled": True, "network_required": True, "capabilities": ["coding", "general"], "sec_medi": 3.1, "bench": "a+b", "misurato_il": "2026-09-09", "routable": True, "quota": "tier gratuito Groq"},
        {"provider_id": "openrouter-free", "model_id": "nex-agi/nex-n2.5-mini:free", "aliases": ["nex-n2.5-mini"], "tier": 2, "cost_class": "free", "enabled": True, "network_required": True, "capabilities": ["coding", "general"], "sec_medi": 8, "bench": "a+b", "misurato_il": "2026-09-09", "routable": True, "quota": "20 richieste al minuto, 1000 al giorno"},
        {"provider_id": "openrouter-free", "model_id": "dots-studio/dots-3-note-preview:free", "aliases": ["dots-3-note-preview"], "tier": 2, "cost_class": "free", "enabled": True, "network_required": True, "capabilities": ["coding", "vision", "general"], "sec_medi": 13, "bench": "a+b,vision", "misurato_il": "2026-09-09", "routable": True, "quota": "20 richieste al minuto, 1000 al giorno"},
        {"provider_id": "openrouter-free", "model_id": "nvidia/nemotron-3-nano-omni-30b-a3b-reasoning:free", "aliases": ["nemotron-3-nano-omni"], "tier": 2, "cost_class": "free", "enabled": True, "network_required": True, "capabilities": ["vision", "coding"], "sec_medi": 3.9, "bench": "a+b,vision", "misurato_il": "2026-09-09", "routable": True, "quota": "20 richieste al minuto, 1000 al giorno; 3.9 s sulla visione, 53 s sul codice"},
        {"provider_id": "openrouter-free", "model_id": "nvidia/nemotron-3-super-120b-a12b:free", "aliases": ["nemotron-3-super-120b"], "tier": 2, "cost_class": "free", "enabled": True, "network_required": True, "capabilities": ["coding", "general"], "sec_medi": 22, "bench": "a+b", "misurato_il": "2026-09-09", "routable": True, "quota": "20 al minuto, 1000 al giorno; servono almeno 1200 token di budget"},
        {"provider_id": "openrouter-free", "model_id": "cohere/north-mini-code:free", "aliases": ["north-mini-code"], "tier": 2, "cost_class": "free", "enabled": True, "network_required": True, "capabilities": ["coding"], "sec_medi": 27, "bench": "a+b", "misurato_il": "2026-09-09", "routable": True, "quota": "20 richieste al minuto, 1000 al giorno"},
        {"provider_id": "openrouter-free", "model_id": "nex-agi/nex-n2.5-pro:free", "aliases": ["nex-n2.5-pro"], "tier": 2, "cost_class": "free", "enabled": True, "network_required": True, "capabilities": ["coding", "vision", "general"], "sec_medi": 61, "bench": "a+b,vision", "misurato_il": "2026-09-09", "routable": True, "quota": "20 richieste al minuto, 1000 al giorno"},
        {"provider_id": "openrouter-free", "model_id": "nvidia/nemotron-3.5-lightning:free", "aliases": ["nemotron-3.5-lightning"], "tier": 3, "cost_class": "free", "enabled": True, "network_required": True, "capabilities": ["coding"], "sec_medi": 229, "bench": "a+b", "misurato_il": "2026-09-09", "routable": True, "quota": "solo lavori in differita: troppo lento per l'interattivo"},
        {"provider_id": "openrouter-free", "model_id": "nvidia/nemotron-3-ultra-550b-a55b:free", "aliases": ["nemotron-3-ultra-550b"], "tier": 3, "cost_class": "free", "enabled": True, "network_required": True, "capabilities": ["coding", "architecture"], "sec_medi": 422, "bench": "a+b", "misurato_il": "2026-09-09", "routable": True, "quota": "solo lavori in differita"},
        {"provider_id": "openrouter-paid", "model_id": "qwen/qwen3-coder-30b-a3b-instruct", "aliases": ["qwen3-coder-30b"], "tier": 4, "cost_class": "paid", "enabled": True, "network_required": True, "capabilities": ["coding", "tactical", "review", "architecture"], "sec_medi": 20, "bench": "a+b", "misurato_il": "2026-09-09", "routable": True, "quota": "a pagamento: prompt 0.07 e completion 0.28 per milione di token"},
        {"provider_id": "deepseek-native", "model_id": "deepseek-v4-flash", "aliases": [], "tier": 4, "cost_class": "paid", "enabled": True, "network_required": True, "capabilities": ["coding", "general", "review"], "sec_medi": 148, "bench": "a+b", "misurato_il": "2026-09-09", "routable": True, "quota": "solo api.deepseek.com: passarlo da OpenRouter e' vietato"},
        {"provider_id": "openrouter-paid", "model_id": "google/gemini-2.5-flash-lite", "aliases": ["gemini-2.5-flash-lite"], "tier": 4, "cost_class": "paid", "enabled": True, "network_required": True, "capabilities": ["vision", "general"], "sec_medi": 2.0, "bench": "vision", "misurato_il": "2026-09-09", "routable": True, "quota": "a pagamento, costo minimo del roster"},
        {"provider_id": "claude-session", "model_id": "claude", "aliases": [], "tier": 5, "cost_class": "session", "enabled": True, "network_required": False, "capabilities": ["architecture", "review", "coordination", "routing"], "sec_medi": None, "bench": "non_instradabile", "misurato_il": "2026-09-10", "routable": False, "quota": "non chiamabile dal gateway: Claude agisce nella sessione, non come fornitore"},
    ],
}


def load_config(path: Path | str | None = None) -> dict[str, Any]:
    target = Path(path) if path else Path("config/mcp.default.json")
    if not target.is_file():
        return json.loads(json.dumps(DEFAULT_CONFIG))
    loaded = json.loads(target.read_text(encoding="utf-8"))
    if not isinstance(loaded, dict):
        raise ValueError("MCP config must be a JSON object")
    merged = json.loads(json.dumps(DEFAULT_CONFIG))
    merged.update(loaded)
    merged["network_enabled"] = bool(merged.get("network_enabled", False))
    return merged


