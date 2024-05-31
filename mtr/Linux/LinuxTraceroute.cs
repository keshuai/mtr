using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;

namespace mtr;

public class LinuxTraceroute
{
    private string _target;
    private string _targetAddress;
    private readonly List<LinuxHost> _hosts = new List<LinuxHost>(31);
    private readonly List<LinuxHost> _hostsToAdd = new List<LinuxHost>(31);
    
    private readonly List<string> _consolePreLines = new List<string>();
    
    public async void Start(string target)
    {
        _target = target;
        var targetAddress = await LinuxPing.DnsAddress(target);
        if (string.IsNullOrEmpty(targetAddress))
        {
            Console.WriteLine($"Host not found: {target}");
            return;
        }

        _targetAddress = targetAddress;
        _consolePreLines.Add($"mtr to {target} with max 30 hops:");
        _consolePreLines.Add($"  Target: {targetAddress}");
        _consolePreLines.Add($"  Registration: {GeoIP.Ins.Registration(targetAddress)}");
        _consolePreLines.Add($"  Location: {GeoIP.Ins.IPLocation(targetAddress)}");
        
        this.ShowLoop();

        var hosts = new string[31];
        while (true)
        {
            for (int i = 1; i < 31; ++i)
            {
                if (hosts[i] != null)
                {
                    continue;
                }

                var host = await LinuxPing.GetHost(target, i);
                if (string.IsNullOrEmpty(host))
                {
                    continue;
                }

                hosts[i] = host;
                if (!IPAddress.TryParse(host, out var hostAddress))
                {
                    Console.WriteLine($"IPAddress parse error: {host}");
                    Environment.Exit(1);
                    return;
                }

                var hostObj = new LinuxHost(i, hostAddress, host);
                lock (_hostsToAdd)
                {
                    _hostsToAdd.Add(hostObj);
                }

                // 目标节点已经找到
                if (host == targetAddress)
                {
                    return;
                }
            }
        }

    }
    
    private async void ShowLoop()
    {
        while (true)
        {
            // 加入新发现的主机
            {
                LinuxHost[] array = null;

                lock (_hostsToAdd)
                {
                    if (_hostsToAdd.Count > 0)
                    {
                        array = _hostsToAdd.ToArray();
                        _hostsToAdd.Clear();
                    }
                }

                if (array != null)
                {
                    foreach (var host in array)
                    {
                        this.AddHost(host);
                    }
                }
            }
            
            // 展示
            this.Show();

            await Task.Delay(1000);
        }
    }

    private void Show()
    {
        // pre lines
        for (int i = 0; i < _consolePreLines.Count; i++)
        {
            ConsoleLineEx.Write(i, _consolePreLines[i]);
        }
        
        // title
        ConsoleLineEx.Write(_consolePreLines.Count, $"No.\tSent\tRtt\tJitter\tLoss\t{GetFormattedIpStr("Address")} Location");
        
        // hosts
        for (int i = 0; i < _hosts.Count; i++)
        {
            var host = _hosts[i];
            var line = _consolePreLines.Count + 1 + i;
            if (host == null)
            {
                ConsoleLineEx.Write(line, $"{i + 1}\t*");
            }
            else
            {
                ConsoleLineEx.Write(line, $"{i + 1}\t{host.TotalCount}\t{host.AvgRtt}ms\t{host.Jitter}ms\t{host.LossPercentStr}\t{GetFormattedIpStr(host.IpString)} {host.Location}"); 
            }
        }

        var targetNotFound = _hosts.Count == 0 || _hosts[^1].IpString != _targetAddress;
        if (targetNotFound)
        {
            ConsoleLineEx.Write(_consolePreLines.Count + 1 + _hosts.Count, $"*\t*");
        }

        // Clear Bottom
        var bottomStart = _consolePreLines.Count + 1 + _hosts.Count;
        if (targetNotFound) ++bottomStart;
        for (int line = bottomStart; line < Console.BufferHeight; line++)
        {
            ConsoleLineEx.ClearLine(line);
        }
        
        // set cursor
        ConsoleLineEx.SetCursor(0, bottomStart);
    }
    
    private int GetIpStrMaxLength()
    {
        int max = "address".Length;
        foreach (var host in _hosts)
        {
            if (host != null)
            {
                var ipStr = host.IpString;
                if (ipStr != null)
                {
                    if (ipStr.Length > max)
                    {
                        max = ipStr.Length;
                    }
                }
            }
        }

        return max;
    }
    
    private string GetFormattedIpStr(string ipStr)
    {
        var totalLength = GetIpStrMaxLength() + 1;
        if (ipStr == null) ipStr = "";

        var spaceCount = totalLength - ipStr.Length;
        if (spaceCount <= 0)
        {
            return ipStr;
        }

        return ipStr + new string(' ', spaceCount);
    }

    private void AddHost(LinuxHost host)
    {
        var ttl = host.Ttl;
        while (_hosts.Count < ttl)
        {
            _hosts.Add(null);
        }

        _hosts[ttl - 1] = host;
    }
}

public class LinuxHost
{
    private readonly int _ttl;
    private readonly IPAddress _ipAddress;
    private readonly string _ipString;
    private readonly string _location;
    private readonly Queue<int> _rtts = new Queue<int>();
    
    private readonly Ping _pingSender = new Ping();
    private readonly int _timeout = 1000;
    private readonly PingOptions _pingOptions = new PingOptions(64, true);
    
    private const string PingMessage = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly byte[] SharedPingBuffer = Encoding.ASCII.GetBytes(PingMessage);

    private int _totalCount = 0;
    private int _lossCount = 0;

    private int _avgRtt = -1;
    private int _jitter = -1;

    public int Ttl => _ttl;
    public IPAddress Address => _ipAddress;
    public string IpString => _ipString;
    public string Location => _location;

    public int AvgRtt => _avgRtt;
    public int Jitter => _jitter;

    public int TotalCount => _totalCount;
    public float LossPercent => _totalCount == 0 ? 0 : (float)(_lossCount / (float)_totalCount);
    public string LossPercentStr => $"{LossPercent * 100:F2}%";

    public LinuxHost(int ttl, IPAddress ipAddress, string ipString)
    {
        _ttl = ttl;
        _ipAddress = ipAddress;
        _ipString = ipString;
        _location = GeoIP.Ins.IPLocation(ipString);
        
        this.PingLoop();
    }

    private async void PingLoop()
    {
        await Task.Delay(1);
        var stopwatch = new Stopwatch();

        while (true)
        {
            try
            {
                stopwatch.Restart();
                var pingReply = await _pingSender.SendPingAsync(_ipAddress, _timeout, SharedPingBuffer, _pingOptions);
                stopwatch.Stop();
                
                var rtt = (int)stopwatch.ElapsedMilliseconds;
                ++_totalCount;
            
                if (pingReply.Status == IPStatus.Success)
                {
                    this.AddRtt(rtt);
                }
                else
                {
                    ++_lossCount;
                }
            
                var sleep = 1000 - rtt;
                if (sleep > 0)
                    await Task.Delay(sleep);
            }
            catch (Exception e)
            {
                // 网络不可达
                await Task.Delay(1000);
            }
        }
    }

    private void AddRtt(int rtt)
    {
        // add
        _rtts.Enqueue(rtt);
        while (_rtts.Count > 10)
        {
            _rtts.Dequeue();
        }

        // avg rtt
        {
            var rttTotal = 0;
            foreach (var r in _rtts)
            {
                rttTotal += r;
            }
            _avgRtt = rttTotal / _rtts.Count;
        }

        // jitter
        {
            var jitter = 0;
            foreach (var r in _rtts)
            {
                var d = Math.Abs(_avgRtt - r);
                if (d > jitter)
                {
                    jitter = d;
                }
            }

            _jitter = jitter;
        }
    }
    
}

