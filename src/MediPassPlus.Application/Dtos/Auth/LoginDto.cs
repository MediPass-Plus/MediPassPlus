using System;
using System.Collections.Generic;
using System.Text;

namespace MediPassPlus.Application.Dtos.Auth;

public  record LoginDto(string Email, string Password);
