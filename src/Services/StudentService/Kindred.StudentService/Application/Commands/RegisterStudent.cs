using Kindred.SharedKernel.Domain;
using Kindred.SharedKernel.Mediator;
using Kindred.StudentService.Domain;

namespace Kindred.StudentService.Application.Commands;

public sealed record RegisterStudentCommand(string? DisplayName, string? Email, string? Program)
    : ICommand<Result<StudentDto>>;

public sealed class RegisterStudentHandler(IStudentRepository students, TimeProvider clock)
    : ICommandHandler<RegisterStudentCommand, Result<StudentDto>>
{
    public async Task<Result<StudentDto>> Handle(RegisterStudentCommand command, CancellationToken cancellationToken)
    {
        var registration = Student.Register(command.DisplayName, command.Email, command.Program, clock.GetUtcNow());
        if (registration.IsFailure) return registration.Error!;

        var student = registration.Value;
        if (await students.EmailExistsAsync(student.Email, cancellationToken))
            return StudentErrors.EmailTaken(student.Email);

        students.Add(student);
        await students.SaveChangesAsync(cancellationToken);
        return student.ToDto();
    }
}
