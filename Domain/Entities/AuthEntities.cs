using System.Text.Json.Serialization;

namespace SigefClient.Domain.Entities;

/// <summary>
/// Status de autenticação retornado pela API
/// </summary>
public sealed class AuthStatus
{
    [JsonPropertyName("authenticated")]
    public bool Authenticated { get; set; }

    [JsonPropertyName("session")]
    public SessionInfo? Session { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    // Considera autenticado apenas se tem sessão válida com dados do usuário ou cookies SIGEF
    public bool IsFullyAuthenticated => 
        Authenticated && 
        Session is not null && 
        Session.IsValid &&
        Session.IsSigefAuthenticated;
}

/// <summary>
/// Informações da sessão
/// </summary>
public sealed class SessionInfo
{
    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("cpf")]
    public string? Cpf { get; set; }

    [JsonPropertyName("nome")]
    public string? Nome { get; set; }

    [JsonPropertyName("is_valid")]
    public bool IsValid { get; set; }

    [JsonPropertyName("is_govbr_authenticated")]
    public bool IsGovBrAuthenticated { get; set; }

    [JsonPropertyName("is_sigef_authenticated")]
    public bool IsSigefAuthenticated { get; set; }
}

/// <summary>
/// Resposta do endpoint browser-login
/// </summary>
public sealed class BrowserLoginResponse
{
    [JsonPropertyName("auth_token")]
    public string AuthToken { get; set; } = string.Empty;

    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("login_url")]
    public string LoginUrl { get; set; } = string.Empty;
}

/// <summary>
/// Dados para enviar no callback de autenticação
/// </summary>
public sealed class AuthCallbackData
{
    [JsonPropertyName("auth_token")]
    public string AuthToken { get; set; } = string.Empty;

    [JsonPropertyName("govbr_cookies")]
    public List<CookieData> GovBrCookies { get; set; } = new();

    [JsonPropertyName("sigef_cookies")]
    public List<CookieData>? SigefCookies { get; set; }

    [JsonPropertyName("jwt_payload")]
    public Dictionary<string, object>? JwtPayload { get; set; }
}

/// <summary>
/// Dados de um cookie capturado
/// </summary>
public sealed class CookieData
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("domain")]
    public string Domain { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = "/";

    [JsonPropertyName("secure")]
    public bool Secure { get; set; }

    [JsonPropertyName("httpOnly")]
    public bool HttpOnly { get; set; }
}

/// <summary>
/// Resposta do callback de autenticação
/// </summary>
public sealed class CallbackResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Resposta do logout
/// </summary>
public sealed class LogoutResponse
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Resultado de uma operação
/// </summary>
public sealed class OperationResult<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? ErrorMessage { get; init; }

    public static OperationResult<T> Ok(T data) => new() { Success = true, Data = data };
    public static OperationResult<T> Fail(string error) => new() { Success = false, ErrorMessage = error };
}
