from cryptography.fernet import Fernet

from nosai.mcp.secrets import SecretStore


def test_secret_store_returns_metadata_not_plaintext(tmp_path):
    store = SecretStore(tmp_path / "secrets.enc", Fernet.generate_key())
    metadata = store.upsert("provider", "super-secret")
    assert metadata.fingerprint != "super-secret"
    assert "super-secret" not in str(store.metadata())
    assert store.resolve_for_internal_call("provider") == "super-secret"


