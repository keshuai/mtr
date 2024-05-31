using System.Diagnostics;
using System.Net;

namespace mtr;

public class LinuxPing
{
    // ping -c 1 -t ttl host
    
    // ping -c COUNT -t ttl -W timeout_s domain
    // ping -c 1 -t 1 -W 1 google.com
    
    // timeout: From 10.100.1.1 (10.100.1.1) icmp_seq=1 Time to live exceeded
    // success: 64 bytes from hkg07s55-in-f14.1e100.net (142.251.222.206): icmp_seq=1 ttl=117 time=1.54 ms

    public static async Task<string> DnsAddress(string target)
    {
        var result = await Ping(target, 1, 0);
        return result.targetAddress;
    }

    public static async Task<string> GetHost(string target, int ttl)
    {
        var result = await Ping(target, ttl, 1);
        return result.host;
    }

    public static async Task<(string targetAddress, List<string> hosts)> GetHosts(string target)
    {
        var targetAddress = "";

        // 解析目标ip
        {
            var pingResult = await Ping(target, 1, 0);
            if (string.IsNullOrEmpty(pingResult.targetAddress))
            {
                return ("", null);
            }

            targetAddress = pingResult.targetAddress;
        }

        // 进行一系列ping拿到主机列表
        {
            var tasks= new List<Task<(string targetAddress, string host)>>();
        
            for (int i = 1; i < 30; i++)
            {
                var task = Ping(target, i, 1);
                tasks.Add(task);
            }
        
            var results = await Task.WhenAll(tasks);
            // foreach (var result in results)
            // {
            //     Console.WriteLine(result);
            // }
            
            return (targetAddress, ClearHosts(targetAddress, results));
        }
    }

    static List<string> ClearHosts(string targetAddress, (string targetAddress, string host)[] results)
    {
        var list = new List<string>(results.Length);
        foreach (var result in results)
        {
            list.Add(result.host);
        }

        // 找到第一个
        var first = list.IndexOf(targetAddress);
        if (first != -1)
        {
            var removeCount = list.Count - (first + 1);
            if (removeCount > 0)
            {
                list.RemoveRange(first + 1, removeCount);
            }

            return list;
        }

        // 移除后面所有为空的
        for (int i = list.Count - 1; i >= 0; --i)
        {
            if (string.IsNullOrEmpty(list[i]))
            {
                list.RemoveAt(i);
            }
            else
            {
                break;
            }
        }
        
        return list;
    }

    private static async Task<(string targetAddress, string host)> Ping(string target, int ttl, float timeout)
    {
        var result = await ExecuteLinuxPing(target, ttl, timeout);
        if (!string.IsNullOrEmpty(result.error))
        {
            return ("", "");
            //throw new Exception($"Error: {result.error}");
        }

        return ParseIp(result.output);
    }
    
    private static (string targetAddress, string host) ParseIp(string output)
    {
        //Console.WriteLine(output);
        var targetAddress = "";
        var host = "";
        
        if (string.IsNullOrEmpty(output))
        {
            return (targetAddress, host);
        }

        var lines = output.Split('\n');
        if (lines.Length > 0)
        {
            targetAddress = GetIp(lines[0]);
        }
        
        if (lines.Length > 1)
        {
            host = GetIp(lines[1]);
        }

        return (targetAddress, host);
    }
    
    private static string GetIp(string line, int startIndex = 0)
    {
        // PING google.com(tsa03s06-in-x0e.1e100.net (2404:6800:4012:1::200e)) 56 data bytes
        // PING eiivi.com (103.95.207.112) 56(84) bytes of data.
        
        var index0 = line.IndexOf('(', startIndex);
        if (index0 == -1)
        {
            return "";
        }

        var index1 = line.IndexOf(')', index0);
        if (index1 == -1)
        {
            return "";
        }
        
        var length = index1 - index0 - 1; //  // (1)
        if (length > 0)
        {
            var ipString = line.Substring(index0 + 1, length);
            if (IPAddress.TryParse(ipString, out _))
            {
                return ipString;
            }

            return GetIp(line, index0 + 1);
        }

        return "";
    }

    private static async Task<(string output, string error)> ExecuteLinuxPing(string target, int ttl, float timeout)
    {
        using var process = new Process();
        process.StartInfo.FileName = "/bin/bash";
        process.StartInfo.Arguments = $"-c \"ping -c 1 -t {ttl} -W {timeout} {target}\"";
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;

        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        
        return (output, error);
    }
}