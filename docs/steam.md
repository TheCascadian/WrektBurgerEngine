# Multiplayer over Steam

Internet play goes through Steam Networking (Valve's relay network). The host opens no router port. Steam hides both players' addresses and encrypts the traffic.
The join code holds the host's Steam account id plus a session secret. The host proves nothing to Steam; the joiner proves the secret (HMAC challenge) before the game accepts them.

## App ID
- Dev builds use Valve's test app 480 (Spacewar), set automatically when no `steam_appid.txt` sits next to the exe.
- When the real App ID exists: put it in `steam_appid.txt` for local runs. Shipped builds launched by Steam need no file. Change `SteamService.TestAppId` too.

## Native library (NOT in the repo)
Steamworks.NET is managed only. Copy from the Steamworks SDK `redistributable_bin` next to the exe:
`steam_api64.dll` (Windows), `libsteam_api.so` (Linux), `libsteam_api.dylib` (macOS).
Without it the game runs and shows "The Steam library is missing"; same-network play still works.

## Two-account test (OPEN, needs a person)
1. Two machines (or one machine and a second Steam account), both signed in to Steam, both with the library beside the exe.
2. Host: Multiplayer, Host a game, copy the internet code.
3. Joiner: Multiplayer, Join, paste, Connect.
Offline check that needs no Steam: `burger link-test`.

## Fallbacks
No Steam: LAN code works. Router mapping (UPnP) and `relay.txt` relay remain as dev paths only (see relay.md).
