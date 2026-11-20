using Kindred.SharedKernel.Domain;
using Kindred.SharedKernel.Mediator;
using Kindred.SharedKernel.Messaging;
using Kindred.SharedKernel.Messaging.Events;
using Kindred.StudentService.Domain;

namespace Kindred.StudentService.Application.Commands;

public sealed record UpdateStudentProfileCommand(Guid StudentId, string? DisplayName, string? Program)
    : ICommand<Result<StudentDto>>;

public sealed class UpdateStudentProfileHandler(
    IStudentRepository students,
    IEventBus eventBus,
    TimeProvider clock) : ICommandHandler<UpdateStudentProfileCommand, Result<StudentDto>>
{
    public async Task<Result<StudentDto>> Handle(UpdateStudentProfileCommand command, CancellationToken cancellationToken)
    {
        var student = await students.GetByIdAsync(command.StudentId, cancellationToken);
        if (student is null) return StudentErrors.StudentNotFound(command.StudentId);

        var update = student.UpdateProfile(command.DisplayName, command.Program, clock.GetUtcNow());
        if (update.IsFailure) return update.Error!;

        await students.SaveChangesAsync(cancellationToken);

        // Saved first, then announced: matching-service only hears about committed renames.
        foreach (var renamed in student.DequeueDomainEvents().OfType<StudentRenamed>())
        {
            await eventBus.PublishAsync(
                new StudentRenamedIntegrationEvent(
                    Guid.NewGuid(), renamed.OccurredAt, renamed.StudentId, renamed.DisplayName),
                cancellationToken);
        }

        return student.ToDto();
    }
}
