# Relay (internet play without opening a router)

Players behind a home router, CGNAT or a gateway without UPnP cannot accept incoming connections. The relay fixes that: the host dials out to it and joiners meet the host there. The host does nothing by hand.

- Run `burger-relay [port]` (default TCP 27961) on any machine with a public address. Build: `dotnet publish src/Burger.Relay -c Release -r linux-x64 --self-contained`. Open that one TCP port on the relay machine only.
- Each game copy finds the relay in `relay.txt` next to the exe (first line `host:port`) or in the environment variable `BURGER_RELAY`. Ship the same `relay.txt` to every player.
- With a relay set, hosting uses it instead of UPnP. The internet join code then holds a room number and the session secret, no IP address. The host sees joiners only as an opaque key; joiners see only the relay's address.
- The relay copies bytes. The TLS channel and the key confirmation run between host and joiner, and the session secret is never sent to the relay, so it cannot read or alter game data.
- Limits: 24 connections per address, 2 rooms per address, 8 waiting joiners per room, 5 s for the first line, 10 s for a joiner to be claimed, 30 min idle.
- The relay operator can see player addresses and traffic volume. Players' addresses are not shown to each other.
- OPEN: no bandwidth cap per connection; no relay-side TLS (not needed for secrecy, but a network observer sees that a relay is used).
- Test: `burger relay-test`.
