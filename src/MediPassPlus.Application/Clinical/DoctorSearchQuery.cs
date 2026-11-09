using MediPassPlus.Application.Common;

namespace MediPassPlus.Application.Clinical;

public class DoctorSearchQuery : PageRequest
{
    // Part of the doctor name. Case does not matter.
    public string? Search { get; set; }

    // The specialty. Case does not matter.
    public string? Specialty { get; set; }
}

