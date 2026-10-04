using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using SanaCash.GoldCredit.Infrastructure;
using SanaCash.GoldCredit.Persistence.Migrations;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddGoldCreditInfrastructure(builder.Configuration);

var authority = builder.Configuration["Jwt:Authority"];
var signingKey = builder.Configuration["Jwt:SigningKey"];
var issuer = builder.Configuration["Jwt:Issuer"];
var audience = builder.Configuration["Jwt:Audience"];
builder.Services
	.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
	.AddJwtBearer(options =>
	{
		options.MapInboundClaims = false;
		if (!string.IsNullOrWhiteSpace(authority))
		{
			options.Authority = authority;
			options.Audience = audience;
		}
		else
		{
			if (string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
			{
				throw new InvalidOperationException("Configure Jwt:Authority or a Jwt:SigningKey of at least 32 bytes.");
			}

			options.TokenValidationParameters = new TokenValidationParameters
			{
				ValidateIssuer = true,
				ValidIssuer = issuer,
				ValidateAudience = true,
				ValidAudience = audience,
				ValidateIssuerSigningKey = true,
				IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
				NameClaimType = "client_id",
				RoleClaimType = "role"
			};
		}
	});
builder.Services.AddAuthorization();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
	await scope.ServiceProvider.GetRequiredService<MigrationRunner>().ApplyAsync();
}

if (args.Contains("--migrate-only", StringComparer.Ordinal))
{
	return;
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program;
