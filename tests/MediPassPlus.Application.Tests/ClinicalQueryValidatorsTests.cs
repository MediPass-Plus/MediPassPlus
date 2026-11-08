using MediPassPlus.Application.Clinical;

namespace MediPassPlus.Application.Tests;

public class ClinicalQueryValidatorsTests
{
    private readonly DoctorSearchQueryValidator _doctors = new();
    private readonly ClinicSearchQueryValidator _clinics = new();
    private readonly SlotSearchQueryValidator _slots = new();

    [Fact]
    public void Doctor_query_with_defaults_passes()
    {
        var result = _doctors.Validate(new DoctorSearchQuery());

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Doctor_query_rejects_a_page_below_1(int page)
    {
        var result = _doctors.Validate(new DoctorSearchQuery { Page = page });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(DoctorSearchQuery.Page));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Doctor_query_rejects_a_page_size_below_1(int pageSize)
    {
        var result = _doctors.Validate(new DoctorSearchQuery { PageSize = pageSize });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(DoctorSearchQuery.PageSize));
    }

    [Fact]
    public void Doctor_query_accepts_a_huge_page_size_because_it_is_cut_down_later()
    {
        var result = _doctors.Validate(new DoctorSearchQuery { PageSize = 5000 });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Doctor_query_rejects_a_long_specialty()
    {
        var result = _doctors.Validate(new DoctorSearchQuery { Specialty = new string('a', 101) });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(DoctorSearchQuery.Specialty));
    }

    [Fact]
    public void Doctor_query_rejects_a_long_search_text()
    {
        var result = _doctors.Validate(new DoctorSearchQuery { Search = new string('a', 101) });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(DoctorSearchQuery.Search));
    }

    [Fact]
    public void Clinic_query_with_defaults_passes()
    {
        var result = _clinics.Validate(new ClinicSearchQuery());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Clinic_query_rejects_a_page_below_1()
    {
        var result = _clinics.Validate(new ClinicSearchQuery { Page = 0 });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(ClinicSearchQuery.Page));
    }

    [Fact]
    public void Slot_query_with_only_from_passes()
    {
        var result = _slots.Validate(new SlotSearchQuery { From = DateTimeOffset.UtcNow });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Slot_query_with_a_valid_window_passes()
    {
        var from = DateTimeOffset.UtcNow;
        var result = _slots.Validate(new SlotSearchQuery { From = from, To = from.AddDays(1) });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Slot_query_rejects_a_window_where_to_is_before_from()
    {
        var from = DateTimeOffset.UtcNow;
        var result = _slots.Validate(new SlotSearchQuery { From = from, To = from.AddDays(-1) });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(SlotSearchQuery.To));
    }
}