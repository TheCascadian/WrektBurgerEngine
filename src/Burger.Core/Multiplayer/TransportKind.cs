using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Burger.Core.Multiplayer;

/// <summary>Which path carries a multiplayer game. Lan is a direct connection (same network, or a router-opened port).</summary>
public enum TransportKind { Lan, SteamSdr, Relay }

public static class TransportSelector
{
    /// <summary>The internet path a host offers: Steam when it is running, else the relay when one is configured, else same-network only.</summary>
    public static TransportKind ForHost(bool steamAvailable, bool relayConfigured) =>
        steamAvailable ? TransportKind.SteamSdr : relayConfigured ? TransportKind.Relay : TransportKind.Lan;

    /// <summary>The path a join code needs. A Steam code needs Steam, a relay code the relay, a direct code a plain connection (a host on the same subnet needs no outside help).</summary>
    public static TransportKind ForCode(string code)
    {
        if (SteamCode.TryParse(code, out _, out _)) return TransportKind.SteamSdr;
        if (JoinCode.TryParseAny(code, out _, out _, out long room, out _) && room != 0) return TransportKind.Relay;
        return TransportKind.Lan;
    }

    /// <summary>True when <paramref name="host"/> lies inside a subnet this machine is attached to.</summary>
    public static bool SameSubnet(IPAddress host)
    {
        if (host.AddressFamily != AddressFamily.InterNetwork) return false;
        byte[] h = host.GetAddressBytes();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            foreach (var u in nic.GetIPProperties().UnicastAddresses)
            {
                if (u.Address.AddressFamily != AddressFamily.InterNetwork || u.IPv4Mask == null) continue;
                byte[] a = u.Address.GetAddressBytes(), m = u.IPv4Mask.GetAddressBytes();
                bool same = true;
                for (int i = 0; i < 4 && same; i++) same = (a[i] & m[i]) == (h[i] & m[i]);
                if (same) return true;
            }
        }
        return false;
    }
}
