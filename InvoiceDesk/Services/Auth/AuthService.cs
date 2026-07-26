using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using InvoiceDesk.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvoiceDesk.Services.Auth;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(string email, string password, string deviceName, CancellationToken cancellationToken = default);
    Task<ValidateResponse?> ValidateTokenAsync(string token, CancellationToken cancellationToken = default);
    Task LogoutAsync(string token, CancellationToken cancellationToken = default);
}

public sealed class AuthService : IAuthService
{
    private sealed class WpErrorResponse
    {
        [JsonPropertyName("code")]
        public string? Code { get; init; }

        [JsonPropertyName("message")]
        public string? Message { get; init; }
    }

    private readonly HttpClient _httpClient;
    private readonly ILogger<AuthService> _logger;
    private readonly AuthOptions _options;

    public AuthService(HttpClient httpClient, IOptions<AuthOptions> options, ILogger<AuthService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new InvalidOperationException("AuthApi:BaseUrl must be configured");
        }

        _httpClient.BaseAddress = new Uri(_options.BaseUrl, UriKind.Absolute);
        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(5, _options.RequestTimeoutSeconds));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("InvoiceDeskDesktop/1.0 (Windows)");
    }

    public async Task<LoginResponse?> LoginAsync(string email, string password, string deviceName, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new { email, password, device_name = deviceName };
            var response = await _httpClient.PostAsJsonAsync("login", payload, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Login failed with status {Status}", response.StatusCode);
                try
                {
                    var err = await response.Content.ReadFromJsonAsync<WpErrorResponse>(cancellationToken: cancellationToken);
                    var msg = !string.IsNullOrWhiteSpace(err?.Message)
                        ? err.Message
                        : $"Server returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase})";
                    return new LoginResponse { Success = false, ErrorMessage = msg, ErrorCode = err?.Code };
                }
                catch
                {
                    return new LoginResponse
                    {
                        Success = false,
                        ErrorMessage = $"Server returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase})"
                    };
                }
            }

            var result = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: cancellationToken);
            return result ?? new LoginResponse { Success = false, ErrorMessage = "Empty response from server." };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login request failed");
            return new LoginResponse
            {
                Success = false,
                ErrorMessage = $"Connection failed: {ex.Message}"
            };
        }
    }

    public async Task<ValidateResponse?> ValidateTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "validate");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Token validation failed with status {Status}", response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<ValidateResponse>(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Validate request failed");
            return null;
        }
    }

    public async Task LogoutAsync(string token, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "logout");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Logout returned status {Status}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Logout request failed");
        }
    }
}
