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
5. **Encrypted and authenticated.** TLS 1.3 only (vetted protocol from the runtime) with a throw-away certificate per hosting session, then a key confirmation: both sides prove they know the 77-bit session secret from the join code, with an HMAC that covers the role, both nonces and the hash of the host certificate (so a man in the middle with its own certificate cannot pass). Handshake limits: 5 s, 16 at once, 3 per address, a one-minute refusal after 5 failures from one address, 1 KB lines. Game lines capped at 96 KB (bounded reader). Full specification and the property table: `docs/multiplayer/channel-spec.md`.
6. **Sanitize everything shown or used.** Names and chat lose control, zero-width and bidi characters and are length-capped; tags and project names match a strict pattern; relative paths (`Sanitize.RelativePath`) refuse `..`, rooted paths, `\`, `:`, hidden segments, control characters, over-long paths and links that leave the root. Fields are percent-escaped, so `|`, `,`, `;` and newlines cannot inject protocol.
7. **Mods are sandboxed, not disabled.** See `docs/game/modding-api.md`. The allowed API surface was audited: everything not needed was removed (clock and time sources, native handles, SIMD, pools, URLs, weak references, culture state, reflection through `Type`, `Delegate` and `MemberInfo`). `sec-test` tries about 70 escapes through files, processes, network, reflection, native code, the clock, threads and unsafe code, and checks that ordinary mod code still compiles.
8. **Host-sent saves are untrusted input.** A resync save is expanded with a hard cap (256 MB), decoded as strict UTF-8, parsed with limits (nesting 40, 4096 characters per string, and 2 GB of memory allocated by the parse, measured as it goes: no count of tokens or entries stands in for memory, so no text of any shape can make the reader exceed it; the parser keeps no token list and shares repeated keys and values), and checked against a schema (`SaveGuard`: only known top-level blocks, no duplicates, `meta` with the right version and game name, tag-shaped country keys, bounded keys, no control characters, finite numbers) before anything is loaded. It loads into a game object that is thrown away on failure, and the client leaves. No type is instantiated from the file; the loader reads values by known names.
9. **A false checksum costs the sender.** A full save is sent for a mismatch at most once per 30 s per peer; a repeat inside that time is a strike (five strikes remove the peer). The only resync in `mp-test` is the one injected on purpose, and the test fails on any other.
10. **Mod code that network data reaches is metered.** The mod compiler rewrites every sandboxed script before compiling it (`FuelRewriter`): a call to `Fuel.Tick()` goes at the start of every function body (methods, accessors, operators, lambdas, local functions; expression bodies become blocks), every loop body, every catch and finally block and after every label. A mod cannot opt out: the source it ships is not the code that runs. `validate` (host) and `apply` (every machine) run under `Fuel.Run` with a budget of steps (2 million and 10 million) and of allocated bytes (64 MB); deeper recursion than the stack allows is stopped everywhere, budget or not. Once the budget is used up every further step throws `ModBudgetException`, including the first step of any catch or finally block, so the mod cannot swallow the stop. The stop happens on the calling thread: no thread is abandoned, nothing keeps running. Steps are counted, not timed, so an `apply` stops at the same point on every machine and lockstep stays in sync; the message is then switched off for the session and the host refuses it. A `validate` over 100 ms of wall clock (time inside base-library calls is not counted in steps) is also switched off afterwards. The old thread-and-deadline watchdog is gone.
11. **Per-player tokens.** For public or large sessions the host can issue one secret per player with expiry, single live use and revocation, switch the shared session secret off, or rotate it (`docs/multiplayer/channel-spec.md`).

## Flow of a mod message

`Network.Send` (mod, on a client) -> typed encode against the schema -> `CMD mod_message` -> host `Wire.Check` -> `CommandValidator` (schema + the mod's own `validate`, with the sender's country set by the host) -> stamped, broadcast -> every machine applies it at the same tick through the mod's `apply` -> next monthly checksum proves all machines agree.

## What was found and fixed in the second pass

- The resync in `mp-test` was the drift injected on purpose by the test (`Gold += 1` on one client). Evidence: the test now asserts zero resyncs before the injection, exactly one after it, only for the drifted client, and none by the end (4 to 60 thousand ticks of a 4-player game). A hostile client could have asked for full saves repeatedly with false checksums; that is now limited (rule 9) and tested (`sec-test`, "False checksum flood").
- The save parser had unbounded recursion (a deeply nested file crashed the process with an uncatchable stack overflow) and unbounded memory. Fixed with the limits in rule 8; tested with ten malformed saves, a gzip bomb, and a fake host that sends five kinds of hostile saves to a real client.
- The first validation of a resync save ran before the multiplayer save section was registered and rejected every legitimate resync; found by `mp-test`, fixed.
- The compiler's constant-array blobs (`new[] {1,2,3}`) use explicit layout, which the verifier refused; ordinary mods that use array literals now pass (tested with a positive control).

## Test map

| Test | Command |
|---|---|
| Play-through with 4 players, votes, commands, one injected desync, a leaver, equal checksums | `burger mp-test <project>` |
| Sandbox escapes (about 70), allowed-API positive control, paths, fuzz, channel attacks, hostile saves, false checksums, per-player tokens, host authority, a legitimate mod over the network, runaway mod code (eight `validate` attacks that ignore the stop: plain loop, loop that catches, exception filter, `goto`, endless recursion, lambdas, iterators, allocation; one `apply` that never returns on host and client), the Game app path without a window | `burger sec-test <project>` |
| Save size of a fresh game and after N years of AI play, against the limits | `burger save-limits <project> [--years N]` |

**Out of scope: the Game window.** Drawing and input of the window are not tested by a script, on purpose: a window test needs a display and a GPU context, which the test machines and CI do not guarantee, and it would test the renderer, not security. Everything security-relevant behind the buttons is in `MpController` (`Burger.Core`): `App.Multiplayer.cs` only forwards to it, and `sec-test` ("Game app path") drives the same calls over real TCP (host, join by code, wrong code, wrong secret, pause and speed, chat, disconnect). A manual check of the window before a release stays on the checklist.

## Remaining risks

- The sandbox is a verifier of the compiled image, not an OS sandbox. A bug in the verifier, or a hole in an allowed API, is an escape. Why an OS or process sandbox is not used, and the compensating controls: `docs/multiplayer/os-sandbox-evaluation.md`. External review plan: `docs/multiplayer/external-review-plan.md`.
- Metering counts steps of mod code, not time inside one base-library call. A single call such as `Enumerable.Range(0, int.MaxValue).ToArray()` runs to its end (seconds at most, bounded by its arguments; the allocation budget stops the mod at its next step). For `validate` the 100 ms wall-clock check switches such a rule off after the first time; for `apply` the cost is paid once on every machine, then the message is off.
- "Recursion too deep" depends on the stack size of each machine, so an `apply` stopped by it (not by steps) can stop at different points; the monthly checksum and resync recover. Step and memory stops are the same everywhere.
- Mod code outside `validate` and `apply` (`OnLoad`, hourly and daily hooks, events) is not under a budget; only the stack check applies. That code is the player's own local install and network data does not choose what it runs, beyond the game state every machine shares.
- The host is trusted for game state. A hostile host cannot crash a client with a save (limits and schema) but can send false state, a huge horizon (capped) or a world that is valid but unfair. Join only hosts you trust.
- Save limits (6 million tokens, 4 million nodes) were measured with `burger save-limits <project>` on fresh games: SampleGame 6 thousand nodes, World500BCE 34 thousand, Maxi_100 67 thousand, Maxi_800 134 thousand (2.5 MB), i.e. at least 30 times below the limit. A save from the late game of the largest project was not measured: OPEN; raise `ParseLimits.Save` if a legitimate save is refused.
- **The channel is UNREVIEWED.** The key confirmation is custom (TLS binding through the certificate hash, because `SslStream` exposes neither TLS-PSK nor a keying-material exporter in the target .NET version; checked 2026-10-15, OPEN for .NET releases after that). It is small, specified and attacked by `sec-test`, but no cryptographer has reviewed it; it is item 1 of `docs/multiplayer/external-review-plan.md`. If an exporter (RFC 5705 / RFC 8446 7.5), TLS-PSK or a vetted Noise library becomes usable, replace the confirmation with it.
- The join code holds the host IP. Anyone with the code (or token) is a peer until it is rotated or revoked; per-player identity is a label chosen by the host, not an account. Public sessions are not built; see the sketch in `SECURITY_MODDING_TRADEOFFS.md`.
- No UDP, NAT traversal or relay; the host exposes a port. A flood is limited per address; a distributed flood still costs CPU for accept and TLS.
- Determinism across CPU architectures is not guaranteed (floating point); the checksum and resync recover from drift.
