using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using TorreClou.Core.Entities;
using TorreClou.Core.Interfaces;

namespace TorreClou.Infrastructure.Services
{
    public class TokenService(IConfiguration config) : ITokenService
    {
        public string CreateToken(User user)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.GivenName, user.FullName),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), // UserId
            };

            // 2. Key & Signature
            var jwtKey = config["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key configuration is missing");
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature);

            // 3. Create Token
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.Now.AddDays(7), // التوكن صالح لمدة أسبوع
                SigningCredentials = creds,
                Issuer = config["Jwt:Issuer"],
                Audience = config["Jwt:Audience"]
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);

            return tokenHandler.WriteToken(token);
        }

        public async Task<GoogleJsonWebSignature.Payload> VerifyGoogleTokenAsync(string idToken)
        {
           
                var settings = new GoogleJsonWebSignature.ValidationSettings()
                {
                    Audience = [config["Google:ClientId"]] // عشان نتأكد إن التوكن ده لـ App بتاعنا
                };

                var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
                return payload;
           
        }
    }
}