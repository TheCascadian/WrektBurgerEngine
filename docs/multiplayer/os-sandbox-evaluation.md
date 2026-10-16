# OS-level sandbox for mods: evaluation

Question: should mod C# run in a separate, OS-restricted process instead of inside the game process?

## What it would take

| Platform | Mechanism |
|---|---|
| Linux | child process with seccomp-bpf (deny open for write, socket, execve, ptrace), user and network namespaces, rlimits, cgroups for CPU and memory |
| Windows | AppContainer or a restricted token, job object limits (memory, CPU, no child processes) |
| macOS | `sandbox-exec` / App Sandbox profile |

The mod host would be a child process with its own copy of `Burger.Core` and the world; the game would talk to it over a pipe.

## Why it is not in this branch

1. **Mods run inside the simulation.** A `GameScript` reads and writes `Game` objects directly (countries, divisions, events) on the simulation thread, in the same tick order on every machine. Out of process, every property read is a round trip (microseconds to milliseconds; lockstep runs thousands of calls per tick), or the whole world must be mirrored and diffed. That is a rewrite of the mod API into a message API, not a hardening step. It would end source compatibility of the existing C# mods (`ExampleMod`).
2. **Determinism.** A second process with its own scheduler and state mirror adds ordering risk to exactly the thing the checksum guards.
3. **Three different sandboxes** to build and test (Linux, Windows, macOS), where the game currently ships one managed code path.
4. **The gain is partly there already.** The verifier removes the APIs an escape needs (files, processes, sockets, reflection, native code, threads, clock). What an OS sandbox adds is protection against a bug in that verifier or in an allowed API.

Verdict: not feasible as a hardening step without breaking the Principled Modding Rule's intent (C# mods that work as they do now). Recorded as a trade-off, with compensating controls.

## Compensating controls (all in place)

- Post-compile allow-list verifier over assembly references, type references, member references, explicit layout, P/Invoke; applies to the compiled image and to cached images.
- Audit of the allowed surface, with tests (`sec-test`, "Sandbox escapes": about 70 escape attempts, plus a positive control that ordinary mod code still compiles).
- Mods get no files, network or engine internals without a permission in `mod.json` (`network`, `content`), and even then only through typed engine APIs.
- Code never travels over the network; mods are installed by hand and compared by hash at join. A hostile peer cannot get code run on another machine; only a mod the player chose to install runs.
- Host-side `validate` has a 100 ms deadline and a circuit breaker (see `docs/multiplayer/multiplayer-security.md`).
- Saves from a host go through a strict, limited parser before any state is touched.
- Recommended for players: run the game as a normal user, not an administrator; on Linux, optional wrapper `bwrap` or `firejail` around the game process works without code changes (no network except the game port, read-only home).

## Revisit when

A mod needs capabilities the verifier cannot grant safely, the mod ecosystem grows to untrusted third-party packages with automatic install, or the mod API is moved to a message API anyway (then a WASM or process host becomes cheap).
