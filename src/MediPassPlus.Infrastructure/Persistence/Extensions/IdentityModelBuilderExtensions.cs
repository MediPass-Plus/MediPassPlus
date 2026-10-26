using System;
using System.Collections.Generic;
using System.Text;
using MediPassPlus.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Persistence.Extensions;

public static class IdentityModelBuilderExtensions
{     // this is the schema name for the Identity tables in the database
    public const string Schema = "identity";

    public static ModelBuilder ConfigureIdentitySchema(this ModelBuilder builder)
    {
        builder.Entity<ApplicationUser>().ToTable("Users", Schema);
        builder.Entity<IdentityRole<Guid>>().ToTable("Roles", Schema);
        builder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles", Schema);
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims", Schema);
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins", Schema);
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims", Schema);
        builder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens", Schema);

        return builder;
    }
}
