using MediPassPlus.Api.Extensions;
using MediPassPlus.Application.Validator;
using MediPassPlus.Infrastructure.Extensions;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// ((( because we are using a separate Extensions class to configure the services, we can call it here)))
//builder.Services.AddDbContext<MediPassDbContext>(options =>
//  options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// Seeding: each module adds one line here, for example:
// builder.Services.AddScoped<ISeeder, ClinicsSeeder>();
builder.Services.AddScoped<DatabaseSeeder>();

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
  

// DbContext + Identity + Roles + JWT (all registered in Infrastructure/Extensions)
builder.Services.AddInfrastructure(builder.Configuration);
//  Swagger Services
builder.Services.AddSwaggerDocumentation();
var app = builder.Build();

await app.SeedDefaultRolesAsync();
// Run the seeders in development only.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().RunAsync();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
  
}

app.UseHttpsRedirection();
        // Add authentication and authorization middleware
 app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
