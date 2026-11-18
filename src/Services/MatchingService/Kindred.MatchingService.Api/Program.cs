using Kindred.MatchingService.Api.Endpoints;
using Kindred.MatchingService.Application;
using Kindred.MatchingService.Data;
using Kindred.SharedKernel.Api;
using Kindred.SharedKernel.Mediator;
using Kindred.SharedKernel.Messaging;
using Kindred.SharedKernel.Messaging.Events;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKindredJson();
builder.Services.AddKindredMediator(typeof(GetCourseRosterHandler).Assembly);
builder.Services.AddMatchingData(builder.Configuration);

// What this service listens to. Its queue (matching-service.events) is bound to exactly these.
builder.Services.AddKindredMessaging(builder.Configuration);
builder.Services.AddIntegrationEventHandler<StudentEnrolledInCourseIntegrationEvent, StudentEnrolledInCourseHandler>();
builder.Services.AddIntegrationEventHandler<StudentDroppedCourseIntegrationEvent, StudentDroppedCourseHandler>();

var app = builder.Build();

await app.Services.InitializeMatchingDatabaseAsync();

app.MapGroup("/api/v1/matching").MapRosterEndpoints();

app.Run();
