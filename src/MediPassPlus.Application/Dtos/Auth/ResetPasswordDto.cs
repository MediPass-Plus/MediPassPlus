using System;
using System.Collections.Generic;
using System.Text;

namespace MediPassPlus.Application.Dtos.Auth;

public record ResetPasswordDto(string Email, string Token, string NewPassword);

