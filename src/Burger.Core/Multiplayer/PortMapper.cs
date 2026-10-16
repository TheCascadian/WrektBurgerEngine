using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace Burger.Core.Multiplayer;

/// <summary>
/// Opens the host's port on the home router by itself (UPnP, the standard every desktop OS can speak over plain sockets), so the host never touches
/// router settings. Finds the router, asks it for its public address (no third-party "what is my IP" service is contacted), maps the port, renews
/// the lease and removes the mapping on dispose. Only a router on the local network is ever talked to. Runs on its own thread; poll <see cref="State"/>.
/// </summary>
public sealed class PortMapper : IDisposable
{
    public enum Phase { Searching, Ready, Failed }

    private readonly int _port;
    private readonly CancellationTokenSource _stop = new();
    private volatile Phase _phase = Phase.Searching;
    private volatile string _status = "Looking for your router...";
    private volatile string _publicIp = "";
    private int _externalPort;
    private string _control = "", _service = "";

    public Phase State => _phase;
    /// <summary>Plain text for the player.</summary>
    public string Status => _status;
    /// <summary>The router's public IPv4 address once mapped; empty otherwise. Never shown to the player, only put into the join code.</summary>
    public string PublicAddress => _publicIp;
    public int ExternalPort => _externalPort;

    public PortMapper(int port)
    {
        _port = port;
        new Thread(Run) { IsBackground = true, Name = "mp-upnp" }.Start();
    }

    private void Fail(string why) { _status = why; _phase = Phase.Failed; }

    private void Run()
    {
        try
        {
            var found = Discover(_stop.Token);
            if (found == null) { Fail("No router answered (UPnP is off or unsupported). Only players on your own network can join."); return; }
            var (control, service, local) = found.Value;
            _control = control; _service = service;
            string ip = Soap(control, service, "GetExternalIPAddress", "")?.Descendants().FirstOrDefault(e => e.Name.LocalName == "NewExternalIPAddress")?.Value ?? "";
            if (!IPAddress.TryParse(ip, out var ext) || !IsPublic(ext)) { Fail("Your internet provider shares one address between customers, so direct internet play is not possible. Players on your own network can still join."); return; }
            int chosen = 0;
            for (int i = 0; i < 10 && chosen == 0; i++)
            {
                int tryPort = _port + i;
                string err = Map(tryPort, local, 7200);
                if (err == "725") err = Map(tryPort, local, 0);
                if (err == "") chosen = tryPort;
                else if (err != "718" && err != "") { Fail("Your router refused to open the port (error " + err + "). Only players on your own network can join."); return; }
            }
            if (chosen == 0) { Fail("The router has no free port to open. Only players on your own network can join."); return; }
            _externalPort = chosen; _publicIp = ext.ToString();
            _status = "Open to the internet";
            _phase = Phase.Ready;
            while (!_stop.Token.WaitHandle.WaitOne(TimeSpan.FromMinutes(50))) Map(chosen, local, 7200);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Log.Warn($"Port mapping stopped ({e.GetType().Name})", "multiplayer"); Fail("Could not reach your router. Only players on your own network can join."); }
    }

    private string Map(int externalPort, string local, int lease)
    {
        var r = Soap(_control, _service, "AddPortMapping",
            $"<NewRemoteHost></NewRemoteHost><NewExternalPort>{externalPort}</NewExternalPort><NewProtocol>TCP</NewProtocol><NewInternalPort>{_port}</NewInternalPort>" +
            $"<NewInternalClient>{local}</NewInternalClient><NewEnabled>1</NewEnabled><NewPortMappingDescription>BurgerEngine</NewPortMappingDescription><NewLeaseDuration>{lease}</NewLeaseDuration>", out string code);
        return r != null ? "" : code;
    }

    public void Dispose()
    {
        _stop.Cancel();
        if (_phase == Phase.Ready && _externalPort > 0)
        {
            int p = _externalPort; _phase = Phase.Failed; _externalPort = 0;
            string c = _control, s = _service;
            var t = new Thread(() => { try { Soap(c, s, "DeletePortMapping", $"<NewRemoteHost></NewRemoteHost><NewExternalPort>{p}</NewExternalPort><NewProtocol>TCP</NewProtocol>"); } catch { } }) { IsBackground = true };
            t.Start(); t.Join(2000);
        }
    }

    // ---- UPnP ----

    private static (string Control, string Service, string Local)? Discover(CancellationToken ct)
    {
        var locals = new List<IPAddress>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var u in nic.GetIPProperties().UnicastAddresses)
                if (u.Address.AddressFamily == AddressFamily.InterNetwork) locals.Add(u.Address);
        }
        foreach (var local in locals)
        {
            foreach (string st in new[] { "urn:schemas-upnp-org:device:InternetGatewayDevice:1", "upnp:rootdevice" })
            {
                if (ct.IsCancellationRequested) return null;
                foreach (var location in Search(local, st))
                {
                    var r = Describe(location, local.ToString());
                    if (r != null) return r;
                }
            }
        }
        return null;
    }

    private static List<string> Search(IPAddress local, string st)
    {
        var found = new List<string>();
        try
        {
            using var udp = new UdpClient(new IPEndPoint(local, 0));
            udp.Client.ReceiveTimeout = 700;
            byte[] msg = Encoding.ASCII.GetBytes($"M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 1\r\nST: {st}\r\n\r\n");
            var group = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
            udp.Send(msg, msg.Length, group);
            udp.Send(msg, msg.Length, group);
            var end = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < end)
            {
                try
                {
                    IPEndPoint? from = null;
                    string text = Encoding.ASCII.GetString(udp.Receive(ref from));
                    foreach (var line in text.Split("\r\n"))
                        if (line.StartsWith("LOCATION:", StringComparison.OrdinalIgnoreCase)) found.Add(line[9..].Trim());
                }
                catch (SocketException) { break; }
            }
        }
        catch { }
        return found.Distinct().ToList();
    }

    private static (string Control, string Service, string Local)? Describe(string location, string local)
    {
        if (!Uri.TryCreate(location, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp || !IPAddress.TryParse(uri.Host, out var host) || !IsLocal(host)) return null;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var doc = XDocument.Parse(http.GetStringAsync(uri).GetAwaiter().GetResult());
            foreach (var svc in doc.Descendants().Where(e => e.Name.LocalName == "service"))
            {
                string type = svc.Elements().FirstOrDefault(e => e.Name.LocalName == "serviceType")?.Value ?? "";
                string ctl = svc.Elements().FirstOrDefault(e => e.Name.LocalName == "controlURL")?.Value ?? "";
                if (!(type.Contains(":WANIPConnection:") || type.Contains(":WANPPPConnection:")) || ctl.Length == 0) continue;
                if (!Uri.TryCreate(uri, ctl, out var full) || full.Scheme != Uri.UriSchemeHttp || !IPAddress.TryParse(full.Host, out var ch) || !IsLocal(ch)) continue;
                return (full.ToString(), type, local);
            }
        }
        catch { }
        return null;
    }

    private static XElement? Soap(string control, string service, string action, string body) => Soap(control, service, action, body, out _);

    private static XElement? Soap(string control, string service, string action, string body, out string errorCode)
    {
        errorCode = "";
        string xml = $"<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body><u:{action} xmlns:u=\"{service}\">{body}</u:{action}></s:Body></s:Envelope>";
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        using var req = new HttpRequestMessage(HttpMethod.Post, control) { Content = new StringContent(xml, Encoding.UTF8, "text/xml") };
        req.Headers.TryAddWithoutValidation("SOAPAction", $"\"{service}#{action}\"");
        using var res = http.Send(req);
        string text = new StreamReader(res.Content.ReadAsStream()).ReadToEnd();
        if (!res.IsSuccessStatusCode)
        {
            try { errorCode = XDocument.Parse(text).Descendants().FirstOrDefault(e => e.Name.LocalName == "errorCode")?.Value ?? res.StatusCode.ToString(); }
            catch { errorCode = res.StatusCode.ToString(); }
            return null;
        }
        return XDocument.Parse(text).Root ?? new XElement("empty");
    }

    // ---- address classes ----

    /// <summary>Loopback, private, link-local: where a home router lives.</summary>
    private static bool IsLocal(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
    }

    /// <summary>A routable address another home can reach (not private, shared-carrier 100.64/10, loopback, multicast or reserved).</summary>
    public static bool IsPublic(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        if (IsLocal(ip) || b[0] == 0 || b[0] == 127 || b[0] >= 224) return false;
        if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return false;
        return true;
    }
}
