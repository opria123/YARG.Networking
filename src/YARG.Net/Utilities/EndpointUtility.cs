using System;
using System.Net;

namespace YARG.Net.Utilities;

/// <summary>
/// Helper methods for parsing and formatting network endpoints.
/// </summary>
public static class EndpointUtility
{
    /// <summary>
    /// Default UDP port for YARG networking.
    /// </summary>
    public const int DefaultPort = 7777;

    /// <summary>
    /// Attempts to parse an endpoint string into address and port components.
    /// </summary>
    /// <param name="input">Endpoint string (hostname, IPv4/6, optional port).</param>
    /// <param name="fallbackPort">Port used when the endpoint omits an explicit port.</param>
    /// <param name="address">Resulting normalized address without brackets.</param>
    /// <param name="port">Resulting port number.</param>
    /// <param name="error">Validation error message when parsing fails.</param>
    /// <returns>True if parsing succeeded, false otherwise.</returns>
    public static bool TryParseEndpoint(string input, int fallbackPort, out string address, out int port, out string error)
    {
        address = string.Empty;
        port = fallbackPort > 0 ? fallbackPort : DefaultPort;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Please enter a valid host or IP address.";
            return false;
        }

        string endpoint = input.Trim();
        string host = endpoint;
        int parsedPort = port;

        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            host = uri.Host;
            if (uri.Port > 0)
                parsedPort = uri.Port;
        }
        else if (!endpoint.Contains(" ", StringComparison.Ordinal))
        {
            if (endpoint.StartsWith("[", StringComparison.Ordinal))
            {
                // IPv6 with brackets
                int closing = endpoint.IndexOf(']');
                if (closing <= 0)
                {
                    error = "IPv6 address is missing the closing ']'";
                    return false;
                }

                host = endpoint.Substring(1, closing - 1);

                if (closing + 1 < endpoint.Length)
                {
                    if (endpoint[closing + 1] != ':' || !TryParsePort(endpoint[(closing + 2)..], out parsedPort, out error))
                    {
                        if (string.IsNullOrEmpty(error))
                            error = "Invalid port specified.";
                        return false;
                    }
                }
            }
            else
            {
                // IPv4 or hostname with optional port
                int colon = endpoint.LastIndexOf(':');
                if (colon > -1 && endpoint.IndexOf(':') == colon)
                {
                    host = endpoint[..colon];
                    if (!TryParsePort(endpoint[(colon + 1)..], out parsedPort, out error))
                    {
                        if (string.IsNullOrEmpty(error))
                            error = "Invalid port specified.";
                        return false;
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            error = "Host cannot be empty.";
            return false;
        }

        host = host.Trim();

        bool isIpAddress = IPAddress.TryParse(host, out _);
        if (!isIpAddress && LooksLikeIpv4(host))
        {
            error = "IPv4 address appears to be malformed. Please double-check for extra digits or dots.";
            return false;
        }

        if (!isIpAddress && Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            error = "Host must be a valid IP address or domain.";
            return false;
        }

        address = host;
        port = Math.Clamp(parsedPort, 1, ushort.MaxValue);
        return true;
    }

    /// <summary>
    /// Formats an address/port pair into an endpoint string.
    /// </summary>
    public static string FormatEndpoint(string address, int port)
    {
        if (string.IsNullOrWhiteSpace(address))
            return string.Empty;

        string trimmed = address.Trim();
        int normalizedPort = Math.Clamp(port, 1, ushort.MaxValue);

        // Wrap IPv6 addresses in brackets
        if (trimmed.Contains(':', StringComparison.Ordinal) && !trimmed.StartsWith("[", StringComparison.Ordinal))
            return $"[{trimmed}]:{normalizedPort}";

        return $"{trimmed}:{normalizedPort}";
    }

    /// <summary>
    /// Parses an endpoint string and returns the address component.
    /// </summary>
    public static string GetAddress(string endpoint, int fallbackPort = DefaultPort)
    {
        TryParseEndpoint(endpoint, fallbackPort, out string address, out _, out _);
        return address;
    }

    /// <summary>
    /// Parses an endpoint string and returns the port component.
    /// </summary>
    public static int GetPort(string endpoint, int fallbackPort = DefaultPort)
    {
        TryParseEndpoint(endpoint, fallbackPort, out _, out int port, out _);
        return port;
    }

    private static bool LooksLikeIpv4(string host)
    {
        if (string.IsNullOrEmpty(host))
            return false;

        bool hasDigit = false;
        foreach (char c in host)
        {
            if (c >= '0' && c <= '9')
            {
                hasDigit = true;
                continue;
            }

            if (c != '.')
                return false;
        }

        return hasDigit;
    }

    private static bool TryParsePort(string text, out int port, out string error)
    {
        error = string.Empty;
        if (!int.TryParse(text.Trim(), out port) || port <= 0 || port > 65535)
        {
            error = "Port must be between 1 and 65535.";
            return false;
        }

        return true;
    }
}
