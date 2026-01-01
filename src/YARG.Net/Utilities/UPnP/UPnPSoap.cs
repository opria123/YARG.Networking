using System;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace YARG.Net.Utilities.UPnP;

/// <summary>
/// Handles SOAP requests for UPnP port mapping operations.
/// </summary>
internal static class UPnPSoap
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    /// <summary>
    /// Adds a port mapping on the UPnP device.
    /// </summary>
    public static async Task AddPortMappingAsync(
        UPnPDevice device,
        PortMapping mapping,
        CancellationToken cancellationToken = default)
    {
        var action = "AddPortMapping";
        var body = $@"<?xml version=""1.0""?>
<s:Envelope xmlns:s=""http://schemas.xmlsoap.org/soap/envelope/"" s:encodingStyle=""http://schemas.xmlsoap.org/soap/encoding/"">
  <s:Body>
    <u:{action} xmlns:u=""{device.ServiceType}"">
      <NewRemoteHost></NewRemoteHost>
      <NewExternalPort>{mapping.ExternalPort}</NewExternalPort>
      <NewProtocol>{mapping.Protocol}</NewProtocol>
      <NewInternalPort>{mapping.InternalPort}</NewInternalPort>
      <NewInternalClient>{mapping.InternalClient}</NewInternalClient>
      <NewEnabled>{(mapping.Enabled ? "1" : "0")}</NewEnabled>
      <NewPortMappingDescription>{EscapeXml(mapping.Description)}</NewPortMappingDescription>
      <NewLeaseDuration>{mapping.LeaseDuration}</NewLeaseDuration>
    </u:{action}>
  </s:Body>
</s:Envelope>";

        await SendSoapRequestAsync(device, action, body, cancellationToken);
    }

    /// <summary>
    /// Deletes a port mapping from the UPnP device.
    /// </summary>
    public static async Task DeletePortMappingAsync(
        UPnPDevice device,
        int externalPort,
        PortMappingProtocol protocol,
        CancellationToken cancellationToken = default)
    {
        var action = "DeletePortMapping";
        var body = $@"<?xml version=""1.0""?>
<s:Envelope xmlns:s=""http://schemas.xmlsoap.org/soap/envelope/"" s:encodingStyle=""http://schemas.xmlsoap.org/soap/encoding/"">
  <s:Body>
    <u:{action} xmlns:u=""{device.ServiceType}"">
      <NewRemoteHost></NewRemoteHost>
      <NewExternalPort>{externalPort}</NewExternalPort>
      <NewProtocol>{protocol}</NewProtocol>
    </u:{action}>
  </s:Body>
</s:Envelope>";

        await SendSoapRequestAsync(device, action, body, cancellationToken);
    }

    /// <summary>
    /// Gets the external IP address from the UPnP device.
    /// </summary>
    public static async Task<string?> GetExternalIPAddressAsync(
        UPnPDevice device,
        CancellationToken cancellationToken = default)
    {
        var action = "GetExternalIPAddress";
        var body = $@"<?xml version=""1.0""?>
<s:Envelope xmlns:s=""http://schemas.xmlsoap.org/soap/envelope/"" s:encodingStyle=""http://schemas.xmlsoap.org/soap/encoding/"">
  <s:Body>
    <u:{action} xmlns:u=""{device.ServiceType}"">
    </u:{action}>
  </s:Body>
</s:Envelope>";

        var response = await SendSoapRequestAsync(device, action, body, cancellationToken);
        
        var match = Regex.Match(response, @"<NewExternalIPAddress>(.+?)</NewExternalIPAddress>", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Gets a specific port mapping entry.
    /// </summary>
    public static async Task<PortMapping?> GetSpecificPortMappingAsync(
        UPnPDevice device,
        int externalPort,
        PortMappingProtocol protocol,
        CancellationToken cancellationToken = default)
    {
        var action = "GetSpecificPortMappingEntry";
        var body = $@"<?xml version=""1.0""?>
<s:Envelope xmlns:s=""http://schemas.xmlsoap.org/soap/envelope/"" s:encodingStyle=""http://schemas.xmlsoap.org/soap/encoding/"">
  <s:Body>
    <u:{action} xmlns:u=""{device.ServiceType}"">
      <NewRemoteHost></NewRemoteHost>
      <NewExternalPort>{externalPort}</NewExternalPort>
      <NewProtocol>{protocol}</NewProtocol>
    </u:{action}>
  </s:Body>
</s:Envelope>";

        try
        {
            var response = await SendSoapRequestAsync(device, action, body, cancellationToken);
            return ParsePortMappingResponse(response, externalPort, protocol);
        }
        catch (UPnPException ex) when (ex.ErrorCode == 714) // NoSuchEntryInArray
        {
            return null;
        }
    }

    private static PortMapping? ParsePortMappingResponse(string response, int externalPort, PortMappingProtocol protocol)
    {
        var internalPortMatch = Regex.Match(response, @"<NewInternalPort>(\d+)</NewInternalPort>", RegexOptions.IgnoreCase);
        var internalClientMatch = Regex.Match(response, @"<NewInternalClient>(.+?)</NewInternalClient>", RegexOptions.IgnoreCase);
        var descriptionMatch = Regex.Match(response, @"<NewPortMappingDescription>(.+?)</NewPortMappingDescription>", RegexOptions.IgnoreCase);
        var enabledMatch = Regex.Match(response, @"<NewEnabled>(\d+)</NewEnabled>", RegexOptions.IgnoreCase);
        var leaseMatch = Regex.Match(response, @"<NewLeaseDuration>(\d+)</NewLeaseDuration>", RegexOptions.IgnoreCase);

        if (!internalPortMatch.Success || !internalClientMatch.Success)
            return null;

        return new PortMapping(
            ExternalPort: externalPort,
            InternalPort: int.Parse(internalPortMatch.Groups[1].Value),
            Protocol: protocol,
            Description: descriptionMatch.Success ? descriptionMatch.Groups[1].Value : "",
            InternalClient: internalClientMatch.Groups[1].Value,
            LeaseDuration: leaseMatch.Success ? int.Parse(leaseMatch.Groups[1].Value) : 0,
            Enabled: !enabledMatch.Success || enabledMatch.Groups[1].Value == "1"
        );
    }

    private static async Task<string> SendSoapRequestAsync(
        UPnPDevice device,
        string action,
        string body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, device.ControlUrl);
        request.Headers.Add("SOAPAction", $"\"{device.ServiceType}#{action}\"");
        request.Content = new StringContent(body, Encoding.UTF8, "text/xml");

        using var response = await SharedHttpClient.SendAsync(request, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            // Try to parse UPnP error
            var errorCode = ParseErrorCode(responseContent);
            var errorDesc = ParseErrorDescription(responseContent);
            
            throw new UPnPException(
                errorDesc ?? $"UPnP request failed: HTTP {(int)response.StatusCode}",
                errorCode);
        }

        return responseContent;
    }

    private static int ParseErrorCode(string xml)
    {
        var match = Regex.Match(xml, @"<errorCode>(\d+)</errorCode>", RegexOptions.IgnoreCase);
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    private static string? ParseErrorDescription(string xml)
    {
        var match = Regex.Match(xml, @"<errorDescription>(.+?)</errorDescription>", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string EscapeXml(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}
