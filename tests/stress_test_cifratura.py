"""Stress test asincrono ispirato alla specifica v1.9.

Il test misura il canale di cifratura locale senza dichiarare un throughput
minimo: i risultati dipendono dall'hardware e dall'ambiente di esecuzione.
"""
from __future__ import annotations

import asyncio
import time

from cryptography.hazmat.primitives.asymmetric.x25519 import X25519PrivateKey
from nosai.security.ephemeral_session import EphemeralSession


async def invia_macro(sessione: EphemeralSession, identificativo: int) -> float:
    payload = f"STRESS_TEST_MACRO_{identificativo}_TIMESTAMP_{time.time_ns()}".encode()
    inizio = time.perf_counter()
    packet = sessione.encrypt(payload)
    # Simulazione locale del percorso cifratura/decrittazione.
    await asyncio.sleep(0)
    sessione.decrypt(packet)
    return time.perf_counter() - inizio


async def esegui_stress(numero_macro: int = 1000) -> dict[str, float | int]:
    server = X25519PrivateKey.generate()
    client = X25519PrivateKey.generate()
    sender = EphemeralSession.from_x25519(client, server.public_key().public_bytes_raw())
    receiver = EphemeralSession.from_x25519(server, client.public_key().public_bytes_raw())

    # Ogni task usa una sessione propria per mantenere contatori nonce indipendenti.
    async def ciclo(i: int) -> float:
        payload = f"STRESS_TEST_MACRO_{i}_{time.time_ns()}".encode()
        start = time.perf_counter()
        packet = sender.encrypt(payload)
        await asyncio.sleep(0)
        receiver.decrypt(packet)
        return time.perf_counter() - start

    global_start = time.perf_counter()
    latencies = await asyncio.gather(*(ciclo(i) for i in range(numero_macro)))
    total = time.perf_counter() - global_start
    return {
        "macro_inviate": numero_macro,
        "tempo_totale_secondi": total,
        "throughput_macro_secondo": numero_macro / total if total else 0.0,
        "latenza_media_ms": sum(latencies) / len(latencies) * 1000 if latencies else 0.0,
    }


if __name__ == "__main__":
    print(asyncio.run(esegui_stress()))
