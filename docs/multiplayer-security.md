# Multiplayer security model

Applies to protocol 2 (`Wire.Version`). Tested by `burger sec-test <project>` and `burger mp-test <project>`.

## Assets and attackers

| Attacker | Goal |
|---|---|
| Hostile client | Run code on the host or other clients; cheat (move other players' units, spend their gold); crash or stall the host; spoof names; read the disk |
| Hostile host | Run code on clients; feed bad saves; crash or stall clients |
| Network attacker (MITM, scanner) | Read or alter traffic; join without the code; flood the port |
| Hostile mod package | Run native code, read files, open sockets, change engine state outside the rules |

Out of scope: a hostile OS user on the same machine, a hostile project backend (it is the developer's own locked code and is trusted), side channels.

## Rules

1. **No code travels.** The wire carries ids, hashes and typed data. Mods are installed locally; the join check compares hashes of `scripts/`, the project `backend/` and the set of mod message keys. A mismatch refuses the join.
2. **Host-authoritative.** A client sends requests. The host runs `CommandValidator` (shape, range, ownership, "can it happen now"), stamps the tick and the country itself, and broadcasts. A client cannot forge the country, the tick or system commands (`claim`, `release`).
3. **Data only, typed, versioned.** `Wire` holds a table of every message: direction, field count, size, whether it needs a finished handshake. Unknown, wrongly shaped, oversized, wrong-direction or out-of-state lines are refused. Numbers go through `Sanitize.Int` with a range. No arbitrary type is ever deserialized. The save file of a resync is the one format that is parsed from the host (see risks).
4. **Strikes and budgets.** Each peer has a budget of 400 messages (refilled at 200 per second). A refused line is a strike; five strikes drop the peer, and its country goes to the AI. A handler that throws counts as a strike and never takes the host down. Clients do the same to a host (five bad messages and they leave).
5. **Encrypted and authenticated.** Handshake: both sides send an ephemeral ECDH P-256 key and a nonce; each proves it knows the session secret (77 bits in the join code) with an HMAC over the full transcript; keys come from HKDF over the shared point, the secret and the transcript, one per direction. Each line is sealed with AES-GCM; the nonce is a strict counter, so replay, reorder, drop and alteration end the link. Handshake time limit 5 s, at most 16 handshakes at once, lines capped at 96 KB (bounded reader), saves capped at 96 MB compressed and 512 MB expanded.
6. **Sanitize everything shown or used.** Names and chat lose control, zero-width and bidi characters and are length-capped; tags and project names match a strict pattern; relative paths (`Sanitize.RelativePath`) refuse `..`, rooted paths, `\`, `:`, hidden segments, control characters, over-long paths and links that leave the root. Fields are percent-escaped, so `|`, `,`, `;` and newlines cannot inject protocol.
7. **Mods are sandboxed, not disabled.** See `docs/modding-api.md`.

## Flow of a mod message

`Network.Send` (mod, on a client) -> typed encode against the schema -> `CMD mod_message` -> host `Wire.Check` -> `CommandValidator` (schema + the mod's own `validate`, with the sender's country set by the host) -> stamped, broadcast -> every machine applies it at the same tick through the mod's `apply` -> next monthly checksum proves all machines agree.

## Remaining risks

- The sandbox is a verifier of the compiled image, not an OS sandbox. A bug in the verifier, or a hole in an allowed API, is an escape. Defense in depth (a separate process, WASM) is not in this prototype.
- The host is trusted for game state. A hostile host can send a malicious save (the parser is plain text but large), a huge horizon, or false state. Join only hosts you trust.
- One shared secret per session: anybody who has the code is a peer, and a peer who leaks it lets others in. The host sees every IP.
- TCP, not UDP/DTLS; no NAT traversal; the host must expose a port. A flood below the handshake limit still costs CPU.
- Determinism across CPU architectures is not guaranteed (floating point); the checksum and resync recover from drift.
- Mod `validate` code runs on the host with the host's permissions, inside the same sandbox. A slow `validate` can stall the host (no time limit yet).
