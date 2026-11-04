using System.Text;
using Api;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// Refuse to start with a JWT key that would let anyone forge a login
const string placeholderJwtKey = "CHANGE_ME_TO_A_LONG_RANDOM_SECRET_KEY_32+_CHARS";
var jwtKey = config["Jwt:Key"];
var jwtKeyProblem =
    string.IsNullOrWhiteSpace(jwtKey) ? "is not set" :
    jwtKey == placeholderJwtKey ? "is still the placeholder" :
    jwtKey.Length < 32 ? $"is only {jwtKey.Length} characters long (minimum 32)" :
    null;
if (jwtKeyProblem != null)
{
    Console.Error.WriteLine($"Cannot start: Jwt:Key {jwtKeyProblem}.");
    Console.Error.WriteLine("Set a long random key in api/appsettings.Development.json. See section 2 of the README.");
    Environment.Exit(1);
}

// Database
builder.Services.AddSingleton<Db>();

// Controllers (validation errors are returned as { message })
builder.Services.AddControllers().ConfigureApiBehaviorOptions(o =>
    o.InvalidModelStateResponseFactory = ctx => new BadRequestObjectResult(new
    {
        message = string.Join(" ", ctx.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage))
    }));

// CORS for the React app
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(config["ClientUrl"]!).AllowAnyHeader().AllowAnyMethod()));

// JWT
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false; // keep the short claim names: sub, name, email, role
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = config["Jwt:Issuer"],
        ValidAudience = config["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)),
        NameClaimType = "name",
        RoleClaimType = "role"
    };
});

// Every endpoint requires a logged-in user unless marked [AllowAnonymous]
builder.Services.AddAuthorization(o =>
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// Swagger with a "Bearer token" button
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Paste the token from POST /api/auth/login"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = []
    });
});

var app = builder.Build();

// Unhandled errors -> { message }
app.UseExceptionHandler(e => e.Run(async ctx =>
{
    var error = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, message) = error switch
    {
        SqlException { Number: 2601 or 2627 } => (409, "A record with the same value already exists."),
        SqlException { Number: 547 } => (409, "This record is used by other data and cannot be deleted."),
        _ => (500, "Something went wrong. Please try again.")
    };
    ctx.Response.StatusCode = status;
    await ctx.Response.WriteAsJsonAsync(new { message });
}));

// Empty 401 / 403 / 404 responses -> { message }
app.UseStatusCodePages(async ctx =>
{
    var res = ctx.HttpContext.Response;
    var message = res.StatusCode switch
    {
        401 => "Please log in.",
        403 => "You do not have permission to do this.",
        404 => "Not found.",
        _ => "Request failed."
    };
    await res.WriteAsJsonAsync(new { message });
});

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
