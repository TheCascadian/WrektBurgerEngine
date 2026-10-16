using Burger.Core.Multiplayer;

// burger-relay [port]  (default 27961). Run it on any machine with a public address and an open TCP port; players never need to open theirs.
int port = args.Length > 0 && int.TryParse(args[0], out int p) ? p : RelayConfig.DefaultPort;
using var relay = new RelayServer(port) { Log = m => Console.WriteLine($"{DateTime.UtcNow:u} {m}") };
Console.WriteLine($"Relay listening on TCP {relay.Port}. Ctrl+C stops it.");
var done = new ManualResetEventSlim();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.Set(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => done.Set();
while (!done.Wait(60000)) Console.WriteLine($"{DateTime.UtcNow:u} rooms {relay.RoomCount}, connections spliced {relay.Spliced}");
