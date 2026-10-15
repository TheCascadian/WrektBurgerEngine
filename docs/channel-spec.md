# Multiplayer channel specification (protocol 2, channel version 3)

Code: `src/Burger.Core/Multiplayer/Net.cs` (`SecureChannel`, `TcpTransport`, `TcpHostListener`, `JoinCode`). Tests: `burger sec-test <project>`, sections "Encrypted channel", "Channel attacks" and "Per-peer tokens".

## Decision: vetted protocol underneath, a small binding on top

The first version had its own key exchange (ECDH + HKDF + AES-GCM). It is gone. The channel is now **TLS 1.3** from the .NET runtime (`SslStream`: OpenSSL on Linux, SChannel on Windows, Secure Transport on macOS). TLS supplies the key exchange, the AEAD record layer, replay and reorder protection, truncation detection, and downgrade protection. Only TLS 1.3 is enabled; there is no fallback.

Why a layer on top: a join has no certificate authority and no account. The host makes a throw-away self-signed ECDSA P-256 certificate each time it starts hosting, and the client cannot tell from the certificate who it is talking to. Who the peers are is decided by a **session secret** (77 random bits) that travels only in the join code. TLS-PSK would be the textbook answer, but .NET does not expose it. So after the TLS handshake the two sides run a short key confirmation that ties the secret to the exact TLS session (through the SHA-256 of the host certificate).

## Join code

`code = base32( IPv4 (32 bits) | port (16) | secret (77) )`, 25 characters, shown in groups of 5. The secret never goes on the wire in clear: TLS first, then only HMAC proofs.

## Key confirmation (after the TLS handshake, one text line per message, at most 1024 bytes each)

```
psk        = SHA-256( "burger-mp-psk-v3" || secret )
certHash   = SHA-256( DER of the host certificate )         both sides compute it from what TLS gave them
proof(role, a, b) = HMAC-SHA256( psk, "burger-mp-v3|" || role || "|" || certHash || a || b )

host   -> client   A1 | ns                          ns: 16 random bytes
client -> host     A2 | nc | proof("client", ns, nc)
host   -> client   A3 | proof("server", nc, ns)
```

The host accepts if `A2` matches the proof of any currently valid secret (the session secret, or an unexpired, unrevoked, not-in-use per-player token). All candidates are checked so that timing does not show which one matched. The client accepts if `A3` verifies. Any failure closes the socket.

Properties and how each is met:

| Property | How |
|---|---|
| Confidentiality, integrity, ordering | TLS 1.3 AEAD records. Nothing is sent before the confirmation succeeds; the game protocol starts after it |
| Secret never exposed | Only HMAC outputs are sent, inside TLS. Offline guessing of 77 bits is not feasible; there is nothing to guess against without the secret anyway |
| Man in the middle | A MITM must terminate TLS with its own certificate. The client then computes `certHash` of the MITM certificate, the host of its own. The proofs cannot match, even if the MITM forwards every line unchanged |
| Reflection | `role` is part of the proof, and the two proofs use swapped nonces. Echoing a peer's own proof or challenge back does not verify |
| Replay of a recorded session | New TLS keys and a new `ns` per connection. A recorded handshake and records are useless |
| Downgrade | Only TLS 1.3 is offered and accepted on both sides, and the application checks `SslProtocol` again. The confirmation has no versions or options to negotiate |
| Unknown key share | The proof covers the certificate of this very TLS session and both nonces. A peer cannot be made to confirm a session it did not join under that certificate |
| Counter reuse | No application-level counters exist any more (TLS handles record numbers and keys per connection) |
| Truncation | TLS close semantics; the game protocol is line based and a cut line is never delivered (lines end with `\n` inside a record) |
| Handshake flood | 16 handshakes at once, 3 per address, 5 failed handshakes per address per minute lead to a one-minute refusal, 5 s time limit per handshake, 1 KB limit for handshake lines, a connection is queued for the game only after the confirmation |
| Line flood after the handshake | Bounded `LineReader` (96 KB), per-peer token bucket and strikes in `LockstepSession` |

## Per-player tokens (public or large sessions)

`TcpHostListener.Issue(label, lifetime)` returns a secret just for one person; it is used in a join code like the session secret. The host sees which label joined (`TcpTransport.Identity`). One token supports one live connection at a time, expires after its lifetime, can be revoked (`Revoke(label)` also closes that player's connection), and the session secret can be switched off (`SessionSecretEnabled = false`) or replaced (`RotateSessionSecret()`). Labels are sanitized names.

## Not covered

- The host IP is in the join code. Anyone who has a code is a peer until it is rotated or revoked; a leaked per-player token reveals one identity only.
- Binding uses the certificate hash, not a TLS exporter secret (the .NET API does not offer one). Equivalent for this threat model, but it is the part that deserves a cryptographer's look (see `docs/external-review-plan.md`).
- Traffic analysis, a malicious host's own behavior (handled by client-side checks, not by the channel), and denial of service by a host's network provider.
