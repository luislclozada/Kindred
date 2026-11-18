using Kindred.SharedKernel.Api;
using Kindred.SharedKernel.Mediator;
using Kindred.SharedKernel.Messaging;
using Kindred.StudentService.Api;
using Kindred.StudentService.Api.Endpoints;
using Kindred.StudentService.Application.Commands;
using Kindred.StudentService.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddKindredJson();
builder.Services.AddKindredMediator(typeof(RegisterStudentHandler).Assembly);
builder.Services.AddKindredMessaging(builder.Configuration);
builder.Services.AddStudentsData(builder.Configuration);

var app = builder.Build();

await app.Services.InitializeStudentsDatabaseAsync();
if (app.Environment.IsDevelopment()) await DevSeeder.SeedAsync(app.Services);

var api = app.MapGroup("/api/v1");
api.MapStudentEndpoints();
api.MapCourseEndpoints();

app.Run();
