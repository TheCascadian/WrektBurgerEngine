# Steam native libraries (not committed)
Copy from the Steamworks SDK `redistributable_bin` (matching Steamworks.NET 2024.8.0, SDK 1.60):
- `win-x64/steam_api64.dll`
- `linux-x64/libsteam_api.so`
- `osx/libsteam_api.dylib` (macOS, OPEN: not available yet)
The build copies whatever exists here next to the game. The binaries are git-ignored (Valve's license: get them from your own Steamworks account for release builds).
Current local copies are borrowed from other installed Steam games, for local testing only.
