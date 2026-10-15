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

## UI and routing (done)
- Main menu: "Host game" (selected country, generated seed, join code shown in the chat log, port 27960) and "Join game" (name and code).
- In game: a player panel (flag, name, ready tick, ping, AI tag; Ctrl+click kicks as host), chat box, toasts as status line and chat lines, and a country list for joiners without a country.
- Routed through `Session.Submit`: clock keys and buttons (host sets, clients vote), unit moves (also shift-queued; army moves go out as per-division moves), event options, every proposal answer (`respond` for non-peace kinds), production lines. Event and proposal dialogs only show the local country's items.
- Multiplayer games ignore the local historical bias and AI toggle (they must match on all machines).

## Not done
- Still direct (they change only the local sim and the monthly checksum will revert them): navy and air orders and lines, declare war from the Hud (needs a casus belli in the command), sending peace offers from the Hud (`white_peace` exists, terms do not), decisions, focus, research, laws, designer, production changes of existing lines, recruiting.
- `World.Player` and fog of war are per machine; `Clock.Paused` set by events (pause on event) is local and the session ignores it.
- Join codes carry an IPv4 address, a port and a 77-bit session secret (25 characters): LAN or a forwarded port. No relay, no NAT punch. The secret keys the encrypted channel; share the code only with the people who should join. See docs/multiplayer-security.md.
- Resync sends one base64 line; the reader has no line limit so it works, but there is no progress display.
- Mid-game player takeover of a country with a pending human proposal is untested.
- Host-side `Notify` and AI notifications are per machine and not part of the checksum.
