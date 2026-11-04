using System;
using System.Collections.Generic;
using System.Text;

namespace MediPassPlus.Application.Dtos.Auth;

public  record RegisterDto(string FullName, string Email, string Password,string Role);

