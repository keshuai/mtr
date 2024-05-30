using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;

namespace mtr;

public struct MyPingResult
{
    public readonly int Ttl;
    public readonly IPAddress Address;
    public readonly int Rtt;
    public readonly string Location;

    public MyPingResult(int ttl, IPAddress address, int rtt, string location)
    {
        this.Ttl = ttl;
        this.Address = address;
        this.Rtt = rtt;
        this.Location = location;
    }
}

public class MyPing : IDisposable
{
    private const string PingNumberX = "12345678901234567890123456789012"; // 32
    private const string PingMessage = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly byte[] SharedPingBuffer = Encoding.ASCII.GetBytes(PingMessage);

    private readonly Ping _pingSender = new Ping();
    private readonly int _timeout = 1000;
    private readonly PingOptions _pingOptions = new PingOptions(64, true);
    private readonly Stopwatch _stopwatch = new Stopwatch();

    private bool _disposed = false;

    void IDisposable.Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _pingSender.Dispose();
        _disposed = true;
    }

    public MyPing()
    {
    }

    public MyPing(int timeout)
    {
        _timeout = timeout;
    }

    public async Task<MyPingResult> PingAsync(IPAddress target, int ttl)
    {
        await Task.Delay(1); // for rtt jitter
        _pingOptions.Ttl = ttl;
        _stopwatch.Restart();
        var pingReply = await _pingSender.SendPingAsync(target, _timeout, SharedPingBuffer, _pingOptions);
        _stopwatch.Stop();
        var rtt = (int)_stopwatch.ElapsedMilliseconds;
        
        IPAddress address = null;
        
        if (pingReply.Status == IPStatus.Success) // local address ipv6%number
        {
            address = target;
        }
        else if (pingReply.Status == IPStatus.TtlExpired)
        {
            address = pingReply.Address;
        }
        
        return new MyPingResult(ttl, address, rtt, GeoIP.Ins.IPLocation(address));
    }
}