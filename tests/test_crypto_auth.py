import base64

from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding, rsa

from nosai.network.crypto_auth import NosAiCryptoAuthManager


def test_rsa_session_auth_is_one_shot(tmp_path):
    private = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    public_path = tmp_path / "phone_guard.pem"
    public_path.write_bytes(
        private.public_key().public_bytes(
            serialization.Encoding.PEM,
            serialization.PublicFormat.SubjectPublicKeyInfo,
        )
    )
    manager = NosAiCryptoAuthManager(public_path)
    challenge = manager.generate_secure_challenge()
    signature = private.sign(
        challenge.encode("ascii"), padding.PKCS1v15(), hashes.SHA256()
    )
    encoded = base64.b64encode(signature).decode("ascii")

    assert manager.verify_phone_signature(encoded) is True
    assert manager.verify_phone_signature(encoded, challenge) is False


def test_invalid_signature_fails_closed(tmp_path):
    private = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    public_path = tmp_path / "phone_guard.pem"
    public_path.write_bytes(
        private.public_key().public_bytes(
            serialization.Encoding.PEM,
            serialization.PublicFormat.SubjectPublicKeyInfo,
        )
    )
    manager = NosAiCryptoAuthManager(public_path)
    manager.generate_secure_challenge()
    assert manager.verify_phone_signature("not-base64") is False
    assert manager._last_generated_challenge is None
