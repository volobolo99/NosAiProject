"""Timeout deterministici per i blocchi sincroni del percorso critico."""
from __future__ import annotations
from concurrent.futures import ThreadPoolExecutor, TimeoutError
from typing import Callable, TypeVar

T = TypeVar("T")

class RuntimeTimeout(TimeoutError):
    """Un blocco sincrono ha superato il proprio budget temporale."""

def run_with_timeout(operation: Callable[[], T], timeout_seconds: float = 0.2) -> T:
    if timeout_seconds <= 0:
        raise ValueError("timeout_seconds deve essere positivo")
    with ThreadPoolExecutor(max_workers=1) as executor:
        future = executor.submit(operation)
        try:
            return future.result(timeout=timeout_seconds)
        except TimeoutError as exc:
            future.cancel()
            raise RuntimeTimeout(f"timeout del blocco: {timeout_seconds:.3f}s") from exc
