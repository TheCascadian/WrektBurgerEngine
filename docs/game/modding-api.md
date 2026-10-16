# Modding API (C#)

C# mods stay a first-class feature. A mod is a `GameScript` class in `game/scripts/*.cs`; the game compiles it at load. API version: the engine protocol (`Wire.Version`, now 2).

## Permissions

Declared in `game/scripts/mod.json`:

```json
{ "permissions": ["network", "content"] }
```

| Permission | Gives |
|---|---|
| none | All sim hooks (`On*`), `Game.*` APIs (scripting, combat, commands, interface) |
| `network` | `Network.Register`, `Network.Send` (typed messages, host-validated) |
| `content` | `Content.Exists`, `Content.ReadText` for files under `scripts/content/` (1 MB each) |

Calling a gated API without its permission throws `ModPermissionException`. The locked project `backend/` always has every permission.

## Network messages

```csharp
public override void OnLoad()
{
    Network.Register("gift",
        ModMessageSchema.Create().Int("amount", 1, 100),          // Int, Text, Bool, Country (max 8 fields)
        m => m.Sender.Gold >= m.GetInt("amount") ? null : "not enough gold",   // host only: return null or a reason
        m => m.Sender.Gold -= m.GetInt("amount"));                // every machine, same tick
}
// later:  Network.Send("gift", 25);
```

- Register in `OnLoad`, the same on every machine (the join check compares the message keys).
- The schema is checked on the host and again on every machine. Values outside it never reach `apply`.
- `Sender` is set by the host. The sender cannot choose it.
- `apply` must be deterministic: use `Game.Rngs`, not clock or `System.Random`; change only sim state.
- Outside multiplayer, `Send` runs the same checks and applies at once.

## Blocked in player scripts

Checked on the compiled image (also for cached images). A violation stops the scripts from loading and the log says which call.

- Assemblies other than core library, collections, LINQ, numerics, memory and the engine.
- Namespaces: `System.IO`, `System.Net`, `System.Reflection`, `System.Runtime.InteropServices`, `System.Diagnostics` (except compiler attributes), `System.Threading`, `System.Security`, `System.Runtime.Loader` and anything else not on the allow list (new namespaces start blocked).
- Types: `Activator`, `Environment`, `AppDomain`, `GC`, `Random`, `Console`, `Unsafe`, `TimeProvider`, `IntPtr`/`UIntPtr` (`nint`), `WeakReference`, `Uri`, `Vector`/`Vector<T>` (SIMD width differs per CPU), `System.Buffers` (pools), `Memory<T>` ... Members: `DateTime.Now/UtcNow/Today`, `Guid.NewGuid/CreateVersion7`, `String.Intern`, `CultureInfo.CurrentCulture` (read or write), `Type` members other than names and equality, `Delegate` members other than combine and equality, `MemberInfo` other than `Name`.
- Allowed and tested: collections, LINQ, `Math`, strings and `StringBuilder`, `Convert`, formatting with `CultureInfo.InvariantCulture`, `BigInteger`, `ImmutableList` and friends, `Span<T>` on the stack, tuples, records, pattern matching, exceptions. The test suite compiles a mod that uses all of them (`sec-test`).
- `validate` has a hard deadline of 100 ms (`ModLimits.ValidateDeadlineMs`). If it does not return, the message is rejected, that message type is switched off for the session and a log line is written. Keep `validate` short and free of loops over big data. `apply` has no deadline (it must run on every machine); keep it bounded.
- Native code: P/Invoke, explicit struct layout, `unsafe` (compile fails).
- Engine internals: `ModManager`, `ModSandbox`, `Game.Load`, `Burger.Core.Multiplayer`, `Burger.Core.Save`, loaders, writers and compilers.

## For mod authors

- Your mod only changes the game through the engine API; that is also what keeps all players in sync.
- Every player must install the same mod files; the join check refuses a different set.
- Never trust a message: validate in `validate`. A cheating client can send any value your schema allows.
