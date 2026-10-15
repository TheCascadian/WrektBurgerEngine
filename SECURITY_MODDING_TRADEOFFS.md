# Security and modding trade-offs

Rule: C# modding is never disabled or crippled for security. Where security and modding conflicted, the network layer, authority model or sandbox changed instead. Real conflicts and the choices made:

| Conflict | Choice | Cost | Options not taken |
|---|---|---|---|
| Mods are code; running a peer's code is RCE | Code never travels. Mods are local; hashes are compared. | Players must install the same mods by hand; no auto-download | Auto-download of mods (needs signing and a trust store) |
| Mods want the full .NET API; the full API is an escape | Allow-list verifier on the compiled image, plus permissions in `mod.json` | Some APIs are missing (IO, net, reflection, threads, clock, `Random`). Mods needing them must use the engine API or `content` | Out-of-process mod host or WASM: stronger, much slower and a rewrite of the mod API. Not in this prototype |
| Trusted developer code (`backend/`) needs IO and environment | Backend tier is not sandboxed; it is locked project code, hashed at join | A malicious *project* is a malicious program. Install projects you trust | Sandbox the backend too (breaks existing backends) |
| Mod messages need to be flexible; flexible = injectable | Registered, typed schemas (Int range, Text length, Bool, Country); host validates, sender set by host | No free-form payloads | Raw byte payloads (fuzz surface) |
| Lockstep needs deterministic mods; `DateTime.Now`, `Random` break it | Those members are blocked in scripts; `Game.Rngs` is the supported source | Mod authors must follow the determinism rules | Trusting authors; the monthly checksum then resyncs |
| Mod `validate` runs host-side | It runs inside the same sandbox, exceptions are caught | No time limit yet; a slow check can stall the host | Watchdog thread (OPEN) |
| DTLS/UDP vs the existing TCP lockstep | Own authenticated encryption on TCP (ECDH + session secret, AES-GCM) | No NAT traversal, head-of-line blocking; custom handshake needs review | DTLS via a native library (not in .NET), QUIC (needs TLS certs and relay) |
| Peer authentication without accounts | Session secret inside the join code | Anyone with the code is a peer; code is the credential | Per-player invites or public keys (needs identity service) |
| Host-authoritative vs cheating hosts | Clients validate shape and size, not game truth | A hostile host can send false state or a hostile save | Server-side dedicated hosting; signed saves |
| Float math across CPUs | Checksum + full-save resync | Cross-architecture sessions can resync often | Fixed-point sim (large rewrite) |
