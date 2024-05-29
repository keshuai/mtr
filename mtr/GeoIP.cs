using System.Net;
using MaxMind.GeoIP2;

namespace mtr;

public class GeoIP
{
    public static readonly GeoIP Ins = new GeoIP();

    private DatabaseReader _cityDb;
    private DatabaseReader _asnDb;
    
    private GeoIP()
    {
    }

    public void Reload(string cityDbFile, string asnDbFile)
    {
        // 置空旧内容
        {
            _cityDb?.Dispose();
            _cityDb = null;
            _asnDb?.Dispose();
            _asnDb = null;
        }

        _cityDb = new DatabaseReader(cityDbFile);
        _asnDb = new DatabaseReader(asnDbFile);
    }

    public string IPLocation(IPAddress ip)
    {
        if (ip == null)
        {
            return "";
        }

        return IPLocation(ip.ToString());
    }

    public string IPLocation(string ip)
    {
        var cityStr = "";
        var asnStr= "";
        
        if (_cityDb.TryCity(ip, out var city))
        {
            var countryName = city?.Country?.Name?.Trim();
            var cityName = city?.City?.Name?.Trim();

            if (string.IsNullOrEmpty(countryName))
            {
                cityStr = cityName;
            }
            else if (string.IsNullOrEmpty(cityName))
            {
                cityStr = countryName;
            }
            else if (countryName == cityName)
            {
                cityStr = cityName;
            }
            else 
            {
                cityStr = countryName + " " + cityName;
            }
        }

        if (_asnDb.TryAsn(ip, out var asn))
        {
            asnStr = $"{asn.AutonomousSystemOrganization}(AS{asn.AutonomousSystemNumber})";
        }

        if (string.IsNullOrEmpty(cityStr))
        {
            return asnStr;
        }

        return $"{cityStr} {asnStr}";
    }

    public string Registration(IPAddress ip)
    {
        if (ip == null)
        {
            return "";
        }

        return Registration(ip.ToString());
    }

    public string Registration(string ip)
    {
        if (_cityDb.TryCity(ip, out var city))
        {
            return city.RegisteredCountry.ToString();
        }
        
        return "";
    }
}