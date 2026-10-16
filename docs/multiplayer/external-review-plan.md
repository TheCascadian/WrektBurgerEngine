# Plan for an external review

Goal: independent eyes on the two parts where a mistake means remote code execution or a broken trust model: the channel and the mod sandbox.

## Scope (in priority order)

1. **Key confirmation and channel, first.** The channel is marked UNREVIEWED until this is done. (`docs/multiplayer/channel-spec.md`, `Net.cs`): the key confirmation bound to the certificate hash; listener limits; token handling (`Issue`, `Revoke`, `Claim`); the use of `SslStream` options (TLS 1.3 only, accept-any-certificate callback, why it is safe here).
2. **Sandbox** (`ModSandbox.cs`, `ModApi.cs`, `ModManager.cs`, `FuelRewriter.cs`, `Fuel.cs`): the step meter (completeness of the instrumentation, the uncatchable stop), the allow-lists (assemblies, namespaces, denied types and members), the array-init and `MemberInfo` special cases, verifier coverage of method bodies (IL, not only references), cached image path, `IModNetwork` and `IModContent`.
3. **Parsers on the host and client**: `Wire`, `GameCommand.Decode`, `SaveGuard`, `ScriptParser.ParseStrict`.

## Material to hand over

This branch at a tagged commit; the four documents `docs/multiplayer/multiplayer-security.md`, `docs/multiplayer/channel-spec.md`, `docs/multiplayer/os-sandbox-evaluation.md`, `SECURITY_MODDING_TRADEOFFS.md`; `burger sec-test` output; list of known gaps (the "Remaining risks" section).

## Questions to put to the reviewers

- Is certificate-hash channel binding sufficient without a TLS exporter, given the 77-bit secret?
- Can any allowed API in `AllowedNamespaces` reach files, processes, sockets, reflection, native code, the clock or threads? Please try to break `ModSandbox.Verify` with hand-written IL.
- Can a hostile host cause unbounded memory or CPU on a client with a save that passes `SaveGuard`?
- Can a mod's `validate` or `apply` be driven by a hostile peer into a hang that the step meter does not stop? Please look for mod code the `FuelRewriter` does not instrument (constructs that loop or call without a function body, loop or label of their own) and for base-library calls that run unbounded on mod-chosen input.

## Steps and rough effort

| Step | Who | Effort |
|---|---|---|
| Freeze and tag the commit; prepare the hand-over pack | Maintainer | 1 day |
| Cryptographic review of the channel (spec plus code) | Applied cryptographer | 2 to 3 days |
| Sandbox review and escape attempts (source review plus fuzzing of the verifier with generated IL) | .NET security engineer | 5 to 8 days |
| Parser fuzzing (coverage-guided, `SharpFuzz` or libFuzzer on `ParseStrict`, `Wire.Check`) | Reviewer or maintainer | 2 to 3 days |
| Fix findings, add a regression test per finding to `sec-test` | Maintainer | 3 to 5 days |
| Retest by the reviewers | Reviewers | 1 to 2 days |

Findings are tracked as issues with severity; every fix gets a `sec-test` case before it is closed. Repeat before any release that adds auto-download of mods or public matchmaking.
