using System.Net;

namespace mtr;

public class Program
{
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

        var maxHops = 30; // 最大跳数
        var timeout = 1000; // 超时时间，毫秒
        
        var targetAddress = GetIpAddress(target);
        if (targetAddress == null)
        {
            Console.WriteLine($"Host not found: {target}");
            return;
        }

        var myTraceroute = new MyTraceroute(target, targetAddress, maxHops);
        myTraceroute.Start();

        Thread.CurrentThread.Join();
    }
    
    static string GetTarget(string[] args)
    {
        if (args.Length == 0)
        {
            var baseDir = AppContext.BaseDirectory.Replace("\\", "/");
            if (baseDir.Contains("/bin/Debug/"))
            {
                return "google.com";
            }
        }
        
        if (args.Length == 1)
        {
            return args[0].Trim();
        }

        return null;
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
}