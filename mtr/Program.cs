using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;

namespace mtr;

public class Program
{
    static string GetTarget(string[] args)
    {
        if (args.Length == 0)
        {
            var baseDir = AppContext.BaseDirectory.Replace("\\", "/");
            if (baseDir.Contains("/bin/Debug/"))
            {
                return "bing.com";
            }
        }
        
        if (args.Length == 1)
        {
            return args[0].Trim();
        }

        return null;
    }

    public static void Main(string[] args)
    {
        // ip数据库 GeoLite2
        {
            var baseDir = AppContext.BaseDirectory;
            var cityDb = Path.Combine(baseDir, "GeoLite2-City.mmdb");
            var asnDb = Path.Combine(baseDir, "GeoLite2-ASN.mmdb");
            GeoIP.Ins.Reload(cityDb, asnDb);
        }
        
        var target = GetTarget(args); // 目标域名或IP地址
        if (target == null)
        {
            Console.WriteLine("Invalid target.\nuse mtr xxx");
            return;
        }

        int maxHops = 60; // 最大跳数
        int timeout = 1000; // 超时时间，毫秒
        
        var targetAddress = GetIpAddress(target);
        if (targetAddress == null)
        {
            Console.WriteLine($"Host not found: {target}");
            return;
        }

        var pingBuffer = Encoding.ASCII.GetBytes("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var mtr = new MtrResult();
        
        mtr.AddPreLine($"mtr to {target} with max {maxHops} hops:");
        mtr.AddPreLine($"Target: {targetAddress}");
        mtr.AddPreLine($"Registration: {GeoIP.Ins.Registration(targetAddress)}");
        mtr.AddPreLine($"Location: {GeoIP.Ins.IPLocation(targetAddress)}");
        
        try
        {
            using (Ping pingSender = new Ping())
            {
                var pingOptions = new PingOptions(1, true);
                PingReply reply;

                var roundStopWatch = new Stopwatch();
                var rttStopWatch = new Stopwatch();

                long round = 0;
                while (true)
                {
                    roundStopWatch.Restart();
                    mtr.SetRound(++round);
                    for (var i = 1; i <= maxHops; i++)
                    {
                        pingOptions.Ttl = i;
                        
                        rttStopWatch.Restart();
                        reply = pingSender.Send(targetAddress, timeout, pingBuffer, pingOptions);
                        rttStopWatch.Stop();
                        var rtt = (int)rttStopWatch.ElapsedMilliseconds;

                        if (reply.Status == IPStatus.Success)
                        {
                            mtr.AddPingResult(i - 1, new PingNodeResult(reply.Address, rtt, GeoIP.Ins.IPLocation(reply.Address)));
                            break;
                        }
                        else if (reply.Status == IPStatus.TtlExpired)
                        {
                            mtr.AddPingResult(i - 1, new PingNodeResult(reply.Address, rtt, GeoIP.Ins.IPLocation(reply.Address)));
                        }
                        else
                        {
                            mtr.AddPingResult(i - 1, new PingNodeResult(null, rtt, GeoIP.Ins.IPLocation(reply.Address)));
                        }
                    }
                    
                    roundStopWatch.Stop();
                    var sleep = 1000 - (int)rttStopWatch.ElapsedMilliseconds;
                    if (sleep > 0)
                        Thread.Sleep(sleep);
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"An error occurred: {e}");
        }

        Console.ReadLine();
    }

    static IPAddress GetIpAddress(string ipOrHost)
    {
        if (string.IsNullOrEmpty(ipOrHost))
        {
            return null;
        }

        if (IPAddress.TryParse(ipOrHost, out var address))
        {
            return address;
        }

        try
        {
            var addresses = Dns.GetHostAddresses(ipOrHost);
            if (addresses.Length > 0)
            {
                return addresses[0];
            }
        }
        catch (Exception e)
        {
            return null;
        }
        
        return null;
    }


    public struct PingNodeResult
    {
        public readonly IPAddress Ip;
        public readonly int RTT;
        public readonly string Location;

        public PingNodeResult(IPAddress ip, int rtt, string location)
        {
            this.Ip = ip;
            this.RTT = rtt;
            this.Location = location;
        }
    }

    public class PingNode
    {
        private readonly Queue<PingNodeResult> _results = new Queue<PingNodeResult>();
        private long _totalCount = 0;
        private long _lossCount = 0;
        
        public int ResultCount => _results.Count;
        public long TotalCount => _totalCount;
        public long LossCount => _lossCount;

        public float LossPercent => _totalCount == 0 ? 0 : (float)(_lossCount / (float)_totalCount);
        public string LossPercentStr => $"{LossPercent * 100:F2}%";

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
                    if (i.Ip != null)
                    {
                        return i.Ip.ToString();
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
                foreach (var i in _results)
                {
                    rtt += i.RTT;
                }

                return rtt / _results.Count;
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

        public void AddPingNodeResult(PingNodeResult pingNodeResult)
        {
            _totalCount += 1;
            
            if (pingNodeResult.Ip == null)
            {
                _lossCount += 1;
                return;
            }

            _results.Enqueue(pingNodeResult);
            if (_results.Count > 10)
            {
                _results.Dequeue();
            }
        }
    }
    
    public class MtrResult
    {
        private readonly List<string> _preLines = new List<string>();
        private readonly List<PingNode> _nodes = new List<PingNode>();
        private long _currentRound;
        private int _currentNodeIndex = 0;

        public void SetRound(long round) => _currentRound = round;

        private int GetIpStrMaxLength()
        {
            int max = 4;
            foreach (var node in _nodes)
            {
                var ipStr = node.IpString;
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
            return ipStr + new string(' ', totalLength - ipStr.Length);
        }

        public void AddPreLine(string str)
        {
            _preLines.Add(str);
            this.ShowInConsole();
        }

        public void AddPingResult(int nodeIndex, PingNodeResult pingNodeResult)
        {
            _currentNodeIndex = nodeIndex;
            
            if (nodeIndex > _nodes.Count)
            {
                throw new Exception("nodeIndex out of range");
            }
            
            if (nodeIndex == _nodes.Count)
            {
                _nodes.Add(new PingNode());
            }
            
            // 添加结果
            _nodes[nodeIndex].AddPingNodeResult(pingNodeResult);
            this.ShowInConsole();
        }
        
        public void ShowInConsole()
        {
            // pre lines
            for (int i = 0; i < _preLines.Count; i++)
            {
                ConsoleLineEx.Write(i, _preLines[i]);
            }
            
            // SendTimes
            {
                ConsoleLineEx.Write(_preLines.Count, $"SendTimes: {_currentRound} - {_currentNodeIndex + 1}");
            }
            
            // title
            ConsoleLineEx.Write(_preLines.Count + 1, $"index\tround\trtt\tloss\t{GetFormattedIpStr("ip")} location");
            
            // nodes
            for (int i = 0; i < _nodes.Count; i++)
            {
                var node = _nodes[i];
                var line = _preLines.Count + 2 + i;
                if (node.ResultCount == 0)
                {
                    ConsoleLineEx.Write(line, $"{i + 1}\t{node.TotalCount}\t*");
                }
                else
                {
                    ConsoleLineEx.Write(line, $"{i + 1}\t{node.TotalCount}\t{node.AvgRtt}ms\t{node.LossPercentStr}\t{GetFormattedIpStr(node.IpString)} {node.Location}"); 
                }
            }
            
            // Clear Bottom
            for (int line = _preLines.Count + 2 + _nodes.Count; line < Console.BufferHeight; line++)
            {
                ConsoleLineEx.ClearLine(line);
            }
        }
    }
}