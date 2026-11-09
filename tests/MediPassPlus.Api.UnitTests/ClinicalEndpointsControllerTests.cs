using MediPassPlus.Api.Controllers;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace MediPassPlus.Api.UnitTests;

public sealed class ClinicalEndpointsControllerTests
{
    private static readonly DateTime Future = DateTime.UtcNow.AddHours(2);

    [Fact]
    public async Task Clinics_applies_search_and_paging_without_exposing_extra_data()
    {
        await using var database = await CreateDatabaseAsync();
        database.Context.Clinics.AddRange(
            new Clinic { Id = 1, Name = "Central Clinic", Address = "Central address", Phone = "01000000000" },
            new Clinic { Id = 2, Name = "West Clinic", Address = "West address", Phone = "01111111111" });
        await database.Context.SaveChangesAsync();

        var controller = new ClinicsController(database.Context);
        var result = await controller.Get(
            new MediPassPlus.Application.Clinical.ClinicSearchQuery { Search = "central", PageSize = 1 },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var page = Assert.IsType<MediPassPlus.Application.Common.PagedResult<MediPassPlus.Application.Clinical.ClinicDto>>(ok.Value);
        var clinic = Assert.Single(page.Items);
        Assert.Equal("Central Clinic", clinic.Name);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(["Address", "Id", "Name", "Phone"], PropertyNames(clinic));
    }

    [Fact]
    public async Task Clinics_use_default_paging_and_accept_page_size_50()
    {
        await using var database = await CreateDatabaseAsync();
        database.Context.Clinics.AddRange(Enumerable.Range(1, 55)
            .Select(id => new Clinic { Id = id, Name = $"Clinic {id}", Address = "Address" }));
        await database.Context.SaveChangesAsync();

        var controller = new ClinicsController(database.Context);

        var defaultResult = await controller.Get(new MediPassPlus.Application.Clinical.ClinicSearchQuery(), CancellationToken.None);
        var defaultPage = Assert.IsType<MediPassPlus.Application.Common.PagedResult<MediPassPlus.Application.Clinical.ClinicDto>>(
            Assert.IsType<OkObjectResult>(defaultResult.Result).Value);
        Assert.Equal(1, defaultPage.Page);
        Assert.Equal(20, defaultPage.PageSize);
        Assert.Equal(55, defaultPage.TotalCount);
        Assert.Equal(20, defaultPage.Items.Count);

        var maximumResult = await controller.Get(
            new MediPassPlus.Application.Clinical.ClinicSearchQuery { PageSize = 50 },
            CancellationToken.None);
        var maximumPage = Assert.IsType<MediPassPlus.Application.Common.PagedResult<MediPassPlus.Application.Clinical.ClinicDto>>(
            Assert.IsType<OkObjectResult>(maximumResult.Result).Value);
        Assert.Equal(50, maximumPage.PageSize);
        Assert.Equal(50, maximumPage.Items.Count);
    }

    [Fact]
    public async Task ClinicDoctors_unknown_clinic_returns_not_found()
    {
        await using var database = await CreateDatabaseAsync();
        var controller = new ClinicDoctorsController(database.Context);

        var result = await controller.Get(999, new MediPassPlus.Application.Clinical.DoctorSearchQuery(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task ClinicDoctors_returns_only_active_doctors_and_applies_filters()
    {
        await using var database = await CreateDatabaseAsync();
        var activeUser = User("active@test", "Active Doctor", true);
        var inactiveUser = User("inactive@test", "Inactive Doctor", false);
        database.Context.Users.AddRange(activeUser, inactiveUser);
        database.Context.Clinics.Add(new Clinic { Id = 1, Name = "Central Clinic", Address = "Address" });
        database.Context.Doctors.AddRange(
            new Doctor { Id = 1, UserId = activeUser.Id, ClinicId = 1, Specialty = "Cardiology" },
            new Doctor { Id = 2, UserId = inactiveUser.Id, ClinicId = 1, Specialty = "Cardiology" });
        await database.Context.SaveChangesAsync();

        var controller = new ClinicDoctorsController(database.Context);
        var result = await controller.Get(1, new MediPassPlus.Application.Clinical.DoctorSearchQuery
        {
            Search = "active",
            Specialty = "cardiology"
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var page = Assert.IsType<MediPassPlus.Application.Common.PagedResult<MediPassPlus.Application.Clinical.DoctorDto>>(ok.Value);
        var doctor = Assert.Single(page.Items);
        Assert.Equal("Active Doctor", doctor.FullName);
        Assert.DoesNotContain(page.Items, item => item.FullName == "Inactive Doctor");
        Assert.Equal(["ClinicId", "ClinicName", "FullName", "Id", "Specialty"], PropertyNames(doctor));
    }

    [Fact]
    public async Task DoctorSlots_excludes_past_and_booked_slots_but_keeps_cancelled_slots()
    {
        await using var database = await CreateDatabaseAsync();
        var doctorUser = User("doctor@test", "Doctor", true);
        var patient = User("patient@test", "Patient", true);
        database.Context.Users.AddRange(doctorUser, patient);
        database.Context.Doctors.Add(new Doctor { Id = 1, UserId = doctorUser.Id, ClinicId = 1, Specialty = "General" });
        database.Context.Clinics.Add(new Clinic { Id = 1, Name = "Clinic", Address = "Address" });
        var past = new DoctorSlot { Id = 1, DoctorId = 1, StartTimeUtc = DateTime.UtcNow.AddMinutes(-30), EndTimeUtc = DateTime.UtcNow.AddMinutes(-1) };
        var booked = new DoctorSlot { Id = 2, DoctorId = 1, StartTimeUtc = Future, EndTimeUtc = Future.AddMinutes(30) };
        var cancelled = new DoctorSlot { Id = 3, DoctorId = 1, StartTimeUtc = Future.AddHours(1), EndTimeUtc = Future.AddHours(1).AddMinutes(30) };
        database.Context.DoctorSlots.AddRange(past, booked, cancelled);
        database.Context.Appointments.AddRange(
            new Appointment { Id = 1, PatientId = patient.Id, DoctorId = 1, DoctorSlotId = 2, Status = AppointmentStatus.Booked },
            new Appointment { Id = 2, PatientId = patient.Id, DoctorId = 1, DoctorSlotId = 3, Status = AppointmentStatus.Cancelled });
        await database.Context.SaveChangesAsync();

        var controller = new DoctorSlotsController(database.Context);
        var result = await controller.Get(1, new MediPassPlus.Application.Clinical.SlotSearchQuery(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var slots = Assert.IsAssignableFrom<IReadOnlyList<MediPassPlus.Application.Clinical.SlotDto>>(ok.Value);
        var slot = Assert.Single(slots);
        Assert.Equal(cancelled.Id, slot.Id);
        Assert.Equal(["DoctorId", "EndTimeUtc", "Id", "StartTimeUtc"], PropertyNames(slot));
    }

    [Fact]
    public async Task DoctorSlots_apply_from_and_to_window()
    {
        await using var database = await CreateDatabaseAsync();
        var doctorUser = User("window-doctor@test", "Window Doctor", true);
        database.Context.Users.Add(doctorUser);
        database.Context.Clinics.Add(new Clinic { Id = 1, Name = "Clinic", Address = "Address" });
        database.Context.Doctors.Add(new Doctor { Id = 1, UserId = doctorUser.Id, ClinicId = 1, Specialty = "General" });
        var first = new DoctorSlot { Id = 1, DoctorId = 1, StartTimeUtc = Future, EndTimeUtc = Future.AddMinutes(30) };
        var second = new DoctorSlot { Id = 2, DoctorId = 1, StartTimeUtc = Future.AddHours(1), EndTimeUtc = Future.AddHours(1).AddMinutes(30) };
        database.Context.DoctorSlots.AddRange(first, second);
        await database.Context.SaveChangesAsync();

        var controller = new DoctorSlotsController(database.Context);
        var result = await controller.Get(1, new MediPassPlus.Application.Clinical.SlotSearchQuery
        {
            From = Future.AddMinutes(30),
            To = Future.AddHours(1).AddMinutes(30)
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var slots = Assert.IsAssignableFrom<IReadOnlyList<MediPassPlus.Application.Clinical.SlotDto>>(ok.Value);
        var slot = Assert.Single(slots);
        Assert.Equal(second.Id, slot.Id);
    }

    [Fact]
    public async Task DoctorSlots_unknown_or_inactive_doctor_returns_not_found()
    {
        await using var database = await CreateDatabaseAsync();
        var inactiveUser = User("inactive-slot-doctor@test", "Inactive Slot Doctor", false);
        database.Context.Users.Add(inactiveUser);
        database.Context.Clinics.Add(new Clinic { Id = 1, Name = "Clinic", Address = "Address" });
        database.Context.Doctors.Add(new Doctor { Id = 1, UserId = inactiveUser.Id, ClinicId = 1, Specialty = "General" });
        await database.Context.SaveChangesAsync();
        var controller = new DoctorSlotsController(database.Context);

        var unknown = await controller.Get(999, new MediPassPlus.Application.Clinical.SlotSearchQuery(), CancellationToken.None);
        var inactive = await controller.Get(1, new MediPassPlus.Application.Clinical.SlotSearchQuery(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(unknown.Result);
        Assert.IsType<NotFoundResult>(inactive.Result);
    }

    private static string[] PropertyNames(object value) => JsonSerializer.SerializeToElement(value)
        .EnumerateObject()
        .Select(property => property.Name)
        .OrderBy(name => name)
        .ToArray();

    private static ApplicationUser User(string email, string fullName, bool active) => new()
    {
        Id = Guid.NewGuid(),
        Email = email,
        UserName = email,
        FullName = fullName,
        IsActive = active
    };

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private TestDatabase(SqliteConnection connection, MediPassDbContext context)
        {
            _connection = connection;
            Context = context;
        }

        public MediPassDbContext Context { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<MediPassDbContext>().UseSqlite(connection).Options;
            var context = new MediPassDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private static Task<TestDatabase> CreateDatabaseAsync() => TestDatabase.CreateAsync();
}
