
using EmployeeApi.Data;
using CloudinaryDotNet;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Net.Http.Headers;
using EmployeeApi.Services;

var builder = WebApplication.CreateBuilder(args);

var cloudName = builder.Configuration["Cloudinary:CloudName"];
var apiKey = builder.Configuration["Cloudinary:ApiKey"];
var apiSecret = builder.Configuration["Cloudinary:ApiSecret"];

if (string.IsNullOrWhiteSpace(cloudName) ||
    string.IsNullOrWhiteSpace(apiKey) ||
    string.IsNullOrWhiteSpace(apiSecret))
{
    throw new InvalidOperationException(
        "Cloudinary configuration is missing.");
}

var account = new Account(cloudName, apiKey, apiSecret);
builder.Services.AddSingleton(new Cloudinary(account));

var connectionName = builder.Environment.IsDevelopment()
    ? "DefaultConnection"
    : "ProductionConnection";

var connectionString =
    builder.Configuration.GetConnectionString(connectionName)
    ?? throw new InvalidOperationException(
        $"Connection string '{connectionName}' is not configured.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// JWT configuration
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException(
        "JWT key is not configured. Set Jwt:Key using an environment variable.");

if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException(
        "JWT key must be at least 32 bytes long.");
}

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "EmployeeApi";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "EmployeeApp";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = AccessScope.ValidateToken
        };
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,

            ValidateAudience = true,
            ValidAudience = jwtAudience,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey)),

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddScoped<IDevicePushRegistry, DevicePushRegistry>();
builder.Services.AddScoped<ITaskPushService, TaskPushService>();
builder.Services.AddSingleton<PushReceiptTracker>();
builder.Services.AddHostedService<PushReceiptWorker>();
builder.Services.AddHttpClient("ExpoPush", client =>
{
    client.BaseAddress = new Uri("https://exp.host/--/api/v2/push/");
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    var token = builder.Configuration["Expo:AccessToken"];
    if (!string.IsNullOrWhiteSpace(token))
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

var port = Environment.GetEnvironmentVariable("PORT") ?? "5299";
app.Run($"http://0.0.0.0:{port}");
