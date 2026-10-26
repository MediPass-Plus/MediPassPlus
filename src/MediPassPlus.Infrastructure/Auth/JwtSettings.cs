using System;
using System.Collections.Generic;
using System.Text;

namespace MediPassPlus.Infrastructure.Auth;

public class JwtSettings
{
    public const string SectionName = "Jwt";
              // information or setting s related to JWT authentication


    public string Issuer { get; set; } = string.Empty; // The issuer of the token (typically your application)
       public string Audience { get; set; } = string.Empty; 
    public string SecretKey { get; set; } = string.Empty;  //32-character secret key for HMACSHA256
    public int AccessTokenMinutes { get; set; } = 15;    // Access token expiration time in minutes
    public int RefreshTokenDays { get; set; } = 7;  // Refresh token expiration time in days( (7 days) )
}
