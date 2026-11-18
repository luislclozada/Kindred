using Kindred.SharedKernel.Domain;
using Kindred.SharedKernel.Mediator;
using Kindred.SharedKernel.Messaging;
using Kindred.SharedKernel.Messaging.Events;
using Kindred.StudentService.Domain;

namespace Kindred.StudentService.Application.Commands;

public sealed record DropCourseCommand(Guid StudentId, string? CourseCode) : ICommand<Result<StudentDto>>;

public sealed class DropCourseHandler(IStudentRepository students, IEventBus eventBus, TimeProvider clock)
    : ICommandHandler<DropCourseCommand, Result<StudentDto>>
{
    public async Task<Result<StudentDto>> Handle(DropCourseCommand command, CancellationToken cancellationToken)
    {
        var courseCode = CourseCode.Create(command.CourseCode);
        if (courseCode.IsFailure) return courseCode.Error!;

        var student = await students.GetByIdAsync(command.StudentId, cancellationToken);
        if (student is null) return StudentErrors.StudentNotFound(command.StudentId);

        var drop = student.Drop(courseCode.Value, clock.GetUtcNow());
        if (drop.IsFailure) return drop.Error!;

        await students.SaveChangesAsync(cancellationToken);

        foreach (var dropped in student.DequeueDomainEvents().OfType<StudentDroppedCourse>())
        {
            await eventBus.PublishAsync(
                new StudentDroppedCourseIntegrationEvent(
                    Guid.NewGuid(), dropped.OccurredAt, dropped.StudentId, dropped.CourseCode.Value),
                cancellationToken);
        }

        return student.ToDto();
    }
}
