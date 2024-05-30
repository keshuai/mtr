using System.Diagnostics;
using System.Net;

namespace mtr;

public class MyHostPingResult
{
    private readonly Queue<MyPingResult> _results = new Queue<MyPingResult>();
    private long _totalCount = 0;
    private long _lossCount = 0;
    
    public int ResultCount => _results.Count;
    public long TotalCount => _totalCount;
    public long LossCount => _lossCount;

    public float LossPercent => _totalCount == 0 ? 0 : (float)(_lossCount / (float)_totalCount);
    public string LossPercentStr => $"{LossPercent * 100:F2}%";

    public bool HasAddress
    {
        get
        {
            if (_results.Count == 0)
            {
                return false;
            }

            foreach (var i in _results)
            {
                if (i.Address != null)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public string IpString
    {
        get
        {
            if (_results.Count == 0)
            {
                return "*";
            }

            foreach (var i in _results)
            {
                if (i.Address != null)
                {
                    return i.Address.ToString();
                }
            }

            return "*";
        }
    }

    public int AvgRtt
    {
        get
        {
            if (_results.Count == 0)
            {
                return -1;
            }

            var rtt = 0;
            var count = 0;
            foreach (var i in _results)
            {
                if (i.Address != null)
                {
                    rtt += i.Rtt;
                    ++count;
                }
            }

            if (count == 0)
            {
                return 0;
            }

            return rtt / count;
        }
    }

    public int Jitter
    {
        get
        {
            var avg = this.AvgRtt;
            if (avg == -1)
            {
                return 0;
            }

            var jitter = 0;
            foreach (var i in _results)
            {
                if (i.Address != null)
                {
                    var iJitter = Math.Abs(i.Rtt - avg);
                    if (iJitter > jitter)
                    {
                        jitter = iJitter;
                    }
                }
            }

            return jitter;
        }
    }

    public string Location
    {
        get
        {
            if (_results.Count == 0)
            {
                return "*";
            }
            
            foreach (var i in _results)
            {
                return i.Location;
            }

            return "*";
        }
    }

    public void AddPingHostResult(MyPingResult pingResult)
    {
        _totalCount += 1;
        
        if (pingResult.Address == null)
        {
            _lossCount += 1;
            return;
        }

        _results.Enqueue(pingResult);
        while (_results.Count > 10)
        {
            _results.Dequeue();
        }
    }
}

public class MyTracerouteResult
{
    private readonly List<string> _consolePreLines = new List<string>();
    private readonly List<MyHostPingResult> _hosts = new List<MyHostPingResult>();
    
    public void AddConsolePreLine(string str)
    {
        _consolePreLines.Add(str);
    }
    
    private void CheckRange(int hostCount)
    {
        if (hostCount > _hosts.Count)
        {
            for (int i = _hosts.Count; i < hostCount; i++)
            {
                _hosts.Add(new MyHostPingResult());
            }
        }
        else if (hostCount < _hosts.Count)
        {
            _hosts.RemoveRange(hostCount, _hosts.Count - hostCount);
        }
    }

    public void AddPingResults(MyPingResult[] pingResults)
    {
        this.CheckRange(pingResults.Length);
        
        for (int i = 0; i < pingResults.Length; i++)
        {
            _hosts[i].AddPingHostResult(pingResults[i]);
        }
    }

    private int GetValidHostCount()
    {
        for (int i = _hosts.Count - 1; i >= 0; --i)
        {
            if (_hosts[i].HasAddress)
            {
                return i + 1;
            }
        }

        return 0;
    }

    private int GetShowHostCount()
    {
        var showHostCount = this.GetValidHostCount() + 1;
        return Math.Min(showHostCount, _hosts.Count);
    }

    private long GetSentCount()
    {
        if (_hosts.Count == 0)
        {
            return 0;
        }

        return _hosts[0].TotalCount;
    }

    public void ShowInConsole()
    {
        // pre lines
        for (int i = 0; i < _consolePreLines.Count; i++)
        {
            ConsoleLineEx.Write(i, _consolePreLines[i]);
        }
        
        // sent count
        ConsoleLineEx.Write(_consolePreLines.Count, $"  Sent: {this.GetSentCount()}");
        
        // title
        ConsoleLineEx.Write(_consolePreLines.Count + 1, $"No.\tRtt\tJitter\tLoss\t{GetFormattedIpStr("Address")} Location");
        
        // hosts
        var showHostCount = this.GetShowHostCount();
        for (int i = 0; i < showHostCount; i++)
        {
            var host = _hosts[i];
            var line = _consolePreLines.Count + 2 + i;
            if (host.ResultCount == 0)
            {
                ConsoleLineEx.Write(line, $"{i + 1}\t*");
            }
            else
            {
                ConsoleLineEx.Write(line, $"{i + 1}\t{host.AvgRtt}ms\t{host.Jitter}ms\t{host.LossPercentStr}\t{GetFormattedIpStr(host.IpString)} {host.Location}"); 
            }
        }
        
        // Clear Bottom
        for (int line = _consolePreLines.Count + 2 + showHostCount; line < Console.BufferHeight; line++)
        {
            ConsoleLineEx.ClearLine(line);
        }
        
        // set cursor
        ConsoleLineEx.SetCursor(0, _consolePreLines.Count + 2 + showHostCount);
    }
    
    private int GetIpStrMaxLength()
    {
        int max = "address".Length;
        foreach (var host in _hosts)
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
}

public class MyTraceroute
{
    private readonly IPAddress _targetAddress;
    private int _maxHops;
    private readonly MyTracerouteResult _result = new MyTracerouteResult();
    
    private readonly List<string> _preLines = new List<string>();
    
    public MyTraceroute(string targetName, IPAddress targetAddress, int maxHops)
    {
        _targetAddress = targetAddress;
        _maxHops = maxHops;
        
        _result.AddConsolePreLine($"mtr to {targetName} with max {maxHops} hops:");
        _result.AddConsolePreLine($"  Target: {targetAddress}");
        _result.AddConsolePreLine($"  Registration: {GeoIP.Ins.Registration(targetAddress)}");
        _result.AddConsolePreLine($"  Location: {GeoIP.Ins.IPLocation(targetAddress)}");
        _result.ShowInConsole();
    }

    public async void Start()
    {
        await this.PingAsync(_targetAddress, _maxHops);
    }

    public async Task PingAsync(IPAddress targetAddress, int maxHops)
    {
        // 并发 ping 列表
        var pingList = new List<MyPing>(maxHops);
        for (int i = 0; i < maxHops; i++)
        {
            pingList.Add(new MyPing());
        }

        var pingTasks = new List<Task<MyPingResult>>(maxHops);
        var targetTtl = -1;

        var stopWatch = new Stopwatch();
        while (true)
        {
            stopWatch.Restart();
            {
                // 全部并发 ping
                pingTasks.Clear();
                for (int i = 0; i < pingList.Count; i++)
                {
                    var task = pingList[i].PingAsync(targetAddress, i + 1);
                    pingTasks.Add(task);
                }
                var results = await Task.WhenAll(pingTasks);
            
                // 获取目标节点ttl, 移除多不必要的ping
                if (targetTtl == -1)
                {
                    for (int i = 0; i < results.Length; i++)
                    {
                        if (targetAddress.Equals(results[i].Address))
                        {
                            targetTtl = results[i].Ttl;
                            break;
                        }
                    }

                    if (targetTtl != -1)
                    {
                        pingList.RemoveRange(targetTtl, pingList.Count - targetTtl);
                        results = CopyResults(results, targetTtl);
                    }
                }

                _result.AddPingResults(results);
                _result.ShowInConsole();
            }

            
            stopWatch.Stop();
            
            var sleep = 1000 - (int)stopWatch.ElapsedMilliseconds;
            if (sleep > 0)
                Thread.Sleep(sleep);
        }
    }

    static MyPingResult[] CopyResults(MyPingResult[] results, int count)
    {
        var newResults = new MyPingResult[count];
        for (int i = 0; i < newResults.Length; i++)
        {
            newResults[i] = results[i];
        }
                    
        return newResults;
    }
}