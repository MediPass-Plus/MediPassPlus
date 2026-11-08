using MediPassPlus.Application.Common;

namespace MediPassPlus.Application.Clinical;

public class ClinicSearchQuery : PageRequest
{
    // Part of the clinic name. Case does not matter.
    public string? Search { get; set; }
}

