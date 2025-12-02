
namespace TorreClou.Core.DTOs.Auth;

public class GoogleLoginDto
{
    public string IdToken { get; set; }
    public string Provider { get; set; } = "Google";
}

public class AuthResponseDto
{
    public string AccessToken { get; set; }
    public string Email { get; set; }
    public string FullName { get; set; }
    public decimal CurrentBalance { get; set; }
    public string Role { get; set; }
}