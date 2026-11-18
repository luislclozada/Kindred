const string FrontendCors = "frontend";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
builder.Services.AddCors(options => options.AddPolicy(FrontendCors, policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseCors(FrontendCors);
app.MapGet("/", () => Results.Ok(new { service = "kindred-gateway" }));
app.MapReverseProxy();

app.Run();
