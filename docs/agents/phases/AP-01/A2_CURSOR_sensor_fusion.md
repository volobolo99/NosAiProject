# AP-01 / A2 — Cursor — Sensor Fusion

Implement the fusion layer that converts existing Network/Memory/Screen/local observations into the AP-01 World Model.

READ: world observation contracts, Gate1 observation channel, navigation observation/evidence, DataClassification.
OWN: only sensor-fusion adapters/normalizers and direct tests.
REQUIRE: deterministic precedence, disagreement tracking, provenance/confidence/freshness, UNKNOWN preservation, bounded latency/allocations. Never fabricate missing values. Complete files/tests.
