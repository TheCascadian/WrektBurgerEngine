# Multiplayer (prototype)

Code: `src/Burger.Core/Multiplayer/`. Test: `burger mp-test <project> [--days N] [--seed S]`.

## Model
- Every machine runs the same sim from the same seed. Only commands travel.
- `Command = {tick, country, type, args}`. The host stamps `hostTick + InputDelay (2)` and broadcasts. A command runs just before tick number `tick`.
- The host sends `HORIZON n` (ticks it has run). A client runs ticks below the horizon only. Any command still to come is stamped above it, so a client always has what it needs.
- The host waits when the slowest client is 48 ticks behind (`MaxLead`).
- Host owns pause and speed. Clients send `VOTE`; a change needs more than half of all humans (the host counts for the current state).
- Checksum on the 1st of each month. Mismatch: host toasts, sends `SYNC` (gzip save, human countries) plus its waiting commands; the client loads a fresh game from it.
- Join: `HELLO` > `WELCOME` (project, seed, scripts hash) > client loads and compares hash > `READY` > `SYNC`. A late joiner gets the same save a desynced client gets.
- Leaving: `release` command at a tick; the country is AI again; the player stays in the list with an AI tag.

## Rules for sim code
- No wall clock, no `Environment.TickCount` (outside the seed), no unordered iteration that affects state, only `Game.Rngs`.
- Anything a human decides must be a `GameCommand`, or it cannot be replayed.
- Anything that affects the sim must be in the save, or a resync drifts. The checksum is only as strong as what it hashes.

## Not done
- No UI: start screen (host, seed, join code, free-country pick), player list panel, chat box, toasts. The session has the data and events for all of them (`Players`, `Chat`, `Toast`, `GameReplaced`).
- Hud actions still call the sim directly: they must call `Session.Submit` while a session runs. Only 8 command types exist.
- `World.Player` and fog of war are per machine; `Clock.Paused` set by events (pause on event) is local and the session ignores it.
- Join codes carry an IPv4 address: LAN or a forwarded port. No relay, no NAT punch.
- Resync sends one base64 line. Large worlds need chunking.
- Mid-game player takeover of a country with a pending human proposal is untested.
- Host-side `Notify` and AI notifications are per machine and not part of the checksum.
