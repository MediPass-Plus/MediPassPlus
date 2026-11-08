using System.Collections;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Infrastructure.Persistence;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPassPlus.Infrastructure.Tests.Seeding;

public class DemoDataSeederTests
{
    private static int ExpectedSlots =>
        DemoSeedData.Doctors.Count * DemoSeedData.SlotDays * DemoSeedData.SlotStartTimesUtc.Count;

    [Fact]
    public async Task Running_the_seeders_twice_creates_no_duplicates()
    {
        await using var host = await SeederTestHost.CreateAsync();

        await host.RunSeedersAsync();
        var first = await host.CountAsync();

        await host.RunSeedersAsync();
        var second = await host.CountAsync();

        Assert.Equal(first, second);
        Assert.Equal(1, second.Clinics);
        Assert.Equal(2, second.Doctors);
        Assert.Equal(ExpectedSlots, second.Slots);
        Assert.Equal(5, second.Users);
        Assert.Equal(1, second.Staff);
        Assert.Equal(1, second.Admins);
    }

    [Fact]
    public async Task Seeding_creates_usable_demo_data()
    {
        await using var host = await SeederTestHost.CreateAsync();
        await host.RunSeedersAsync();

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediPassDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var clinic = await db.Clinics.SingleAsync();
        Assert.Equal(DemoSeedData.ClinicName, clinic.Name);
        Assert.Equal(DemoSeedData.ClinicAddress, clinic.Address);

        var doctors = await db.Doctors.Include(doctor => doctor.Slots).ToListAsync();
        Assert.Equal(DemoSeedData.Doctors.Count, doctors.Count);
        Assert.All(doctors, doctor =>
        {
            Assert.Equal(clinic.Id, doctor.ClinicId);
            Assert.False(string.IsNullOrWhiteSpace(doctor.Specialty));
            Assert.Equal(ExpectedSlots / DemoSeedData.Doctors.Count, doctor.Slots.Count);
            Assert.All(doctor.Slots, slot =>
            {
                Assert.True(slot.StartTimeUtc > DateTime.UtcNow);
                Assert.True(slot.EndTimeUtc > slot.StartTimeUtc);
                Assert.True(slot.IsAvailable);
            });
        });

        var staff = await db.ClinicStaff.SingleAsync();
        Assert.Equal(clinic.Id, staff.ClinicId);
        Assert.Equal(1, await db.Admins.CountAsync());

        foreach (var account in DemoSeedData.Accounts)
        {
            var user = await users.FindByEmailAsync(account.Email);

            Assert.NotNull(user);
            Assert.Equal(account.FullName, user.FullName);
            Assert.Equal(account.Role, user.Role);
            Assert.True(user.IsActive);
            Assert.True(user.EmailConfirmed);
            Assert.True(await users.IsInRoleAsync(user, account.Role));
        }
    }

    [Fact]
    public async Task Seeded_values_are_synthetic_and_contain_no_secret()
    {
        await using var host = await SeederTestHost.CreateAsync();
        await host.RunSeedersAsync();

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediPassDbContext>();

        var values = ReadAllStringValues(db).ToList();
        Assert.NotEmpty(values);

        // The configured password must never reach a column: Identity stores only a salted hash (NFR-SEC-02).
        Assert.All(values, value => Assert.False(value.Contains(host.Password, StringComparison.Ordinal)));

        // A connection string is the other secret that must never be seeded into a table.
        Assert.All(values, value => Assert.False(value.Contains("Server=", StringComparison.OrdinalIgnoreCase)));
        Assert.All(values, value => Assert.False(value.Contains("Data Source=", StringComparison.OrdinalIgnoreCase)));

        var emails = await db.Users.Select(user => user.Email).ToListAsync();
        Assert.All(emails, email => Assert.EndsWith(DemoSeedData.EmailDomain, email));
    }

    [Fact]
    public async Task Missing_password_creates_no_demo_data()
    {
        await using var host = await SeederTestHost.CreateAsync(string.Empty);

        await host.RunSeedersAsync();
        var counts = await host.CountAsync();

        Assert.Equal(0, counts.Users);
        Assert.Equal(0, counts.Clinics);
        Assert.Equal(0, counts.Doctors);
        Assert.Equal(0, counts.Slots);
        Assert.Equal(0, counts.Staff);
        Assert.Equal(0, counts.Admins);
    }

    private static IEnumerable<string> ReadAllStringValues(MediPassDbContext db)
    {
        var dbSets = typeof(MediPassDbContext)
            .GetProperties()
            .Where(property => property.PropertyType.IsGenericType
                && property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .Where(property => db.Model.FindEntityType(property.PropertyType.GetGenericArguments()[0]) is not null);

        foreach (var dbSet in dbSets)
        {
            if (dbSet.GetValue(db) is not IEnumerable entities)
            {
                continue;
            }

            foreach (var entity in entities)
            {
                if (entity is null)
                {
                    continue;
                }

                foreach (var property in entity.GetType().GetProperties())
                {
                    if (property.PropertyType != typeof(string))
                    {
                        continue;
                    }

                    if (property.GetValue(entity) is string value && value.Length > 0)
                    {
                        yield return value;
                    }
                }
            }
        }
    }
}
