using Kindred.SharedKernel.Domain;
using Kindred.SharedKernel.Mediator;
using Kindred.SharedKernel.Messaging;
using Kindred.SharedKernel.Messaging.Events;
using Kindred.StudentService.Domain;

namespace Kindred.StudentService.Application.Commands;

public sealed record EnrollInCourseCommand(Guid StudentId, string? CourseCode) : ICommand<Result<StudentDto>>;

public sealed class EnrollInCourseHandler(
    IStudentRepository students,
    ICourseRepository courses,
    IEventBus eventBus,
    TimeProvider clock) : ICommandHandler<EnrollInCourseCommand, Result<StudentDto>>
{
    public async Task<Result<StudentDto>> Handle(EnrollInCourseCommand command, CancellationToken cancellationToken)
    {
        var courseCode = CourseCode.Create(command.CourseCode);
        if (courseCode.IsFailure) return courseCode.Error!;

        var student = await students.GetByIdAsync(command.StudentId, cancellationToken);
        if (student is null) return StudentErrors.StudentNotFound(command.StudentId);

        if (!await courses.ExistsAsync(courseCode.Value, cancellationToken))
            return StudentErrors.CourseNotFound(courseCode.Value);

        var enrollment = student.Enroll(courseCode.Value, clock.GetUtcNow());
        if (enrollment.IsFailure) return enrollment.Error!;

        await students.SaveChangesAsync(cancellationToken);

        // Saved first, then announced: other services only ever hear about facts that are committed.
        foreach (var enrolled in student.DequeueDomainEvents().OfType<StudentEnrolledInCourse>())
        {
            await eventBus.PublishAsync(
                new StudentEnrolledInCourseIntegrationEvent(
                    Guid.NewGuid(), enrolled.OccurredAt, enrolled.StudentId, enrolled.StudentName, enrolled.CourseCode.Value),
                cancellationToken);
        }

        return student.ToDto();
    }
}
