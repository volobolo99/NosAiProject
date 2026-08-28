import pytest

from cryptography.hazmat.primitives.asymmetric.x25519 import X25519PrivateKey
from nosai.security.ephemeral_session import EphemeralSession, PROLOGO


def test_due_parti_derivano_la_stessa_chiave_di_sessione():
    server = X25519PrivateKey.generate()
    client = X25519PrivateKey.generate()
    server_session = EphemeralSession.from_x25519(server, client.public_key().public_bytes_raw())
    client_session = EphemeralSession.from_x25519(client, server.public_key().public_bytes_raw())
    plaintext = b"messaggio NosAi"
    packet = client_session.encrypt(plaintext, PROLOGO)
    assert server_session.decrypt(packet, PROLOGO) == plaintext


def test_dati_associati_errati_fanno_fallire_la_decrittazione():
    a = X25519PrivateKey.generate()
    b = X25519PrivateKey.generate()
    session = EphemeralSession.from_x25519(a, b.public_key().public_bytes_raw())
    packet = session.encrypt(b"dato", b"corretto")
    with pytest.raises(Exception):
        session.decrypt(packet, b"errato")


def test_pacchetto_manomesso_fallisce():
    a = X25519PrivateKey.generate()
    b = X25519PrivateKey.generate()
    sender = EphemeralSession.from_x25519(a, b.public_key().public_bytes_raw())
    receiver = EphemeralSession.from_x25519(b, a.public_key().public_bytes_raw())
    packet = bytearray(sender.encrypt(b"dato"))
    packet[-1] ^= 1
    with pytest.raises(Exception):
        receiver.decrypt(bytes(packet))


def test_chiave_effimera_e_diversa_a_ogni_sessione():
    first = X25519PrivateKey.generate().public_key().public_bytes_raw()
    second = X25519PrivateKey.generate().public_key().public_bytes_raw()
    assert first != second
