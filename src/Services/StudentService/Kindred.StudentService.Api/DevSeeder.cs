using Kindred.SharedKernel.Mediator;
using Kindred.StudentService.Application.Commands;
using Kindred.StudentService.Domain;

namespace Kindred.StudentService.Api;

/// <summary>
/// Development-only demo data, written once into an empty database. Students go through the real
/// commands, so every enrollment publishes its integration event and matching-service builds its
/// rosters the same way it would in production. (Start matching-service first.)
/// </summary>
internal static class DevSeeder
{
    private static readonly (string Code, string Title, string Department)[] Catalog =
    [
        ("COMP-248", "Object-Oriented Programming I", "Computer Science"),
        ("COMP-249", "Object-Oriented Programming II", "Computer Science"),
        ("COMP-352", "Data Structures and Algorithms", "Computer Science"),
        ("MATH-203", "Differential and Integral Calculus I", "Mathematics"),
        ("MATH-205", "Differential and Integral Calculus II", "Mathematics"),
        ("PHYS-204", "Mechanics", "Physics"),
        ("SOEN-287", "Web Programming", "Software Engineering"),
        ("ENGL-212", "English Composition", "English"),
    ];

    private static readonly (string Name, string Email, string Program, string[] Courses)[] Students =
    [
        ("Amira Haddad", "amira@kindred.example", "Computer Science", ["COMP-248", "MATH-203", "SOEN-287", "ENGL-212"]),
        ("Lucas Tremblay", "lucas@kindred.example", "Software Engineering", ["COMP-248", "SOEN-287", "MATH-203"]),
        ("Mei Chen", "mei@kindred.example", "Computer Science", ["COMP-249", "COMP-352", "MATH-205"]),
        ("Diego Morales", "diego@kindred.example", "Software Engineering", ["COMP-248", "COMP-249", "SOEN-287", "PHYS-204"]),
        ("Priya Nair", "priya@kindred.example", "Mathematics", ["MATH-203", "MATH-205", "PHYS-204"]),
        ("Noah Bergeron", "noah@kindred.example", "Physics", ["PHYS-204", "MATH-203", "ENGL-212"]),
        ("Fatou Diallo", "fatou@kindred.example", "Computer Science", ["COMP-352", "COMP-249", "MATH-205", "SOEN-287"]),
        ("Ethan Wright", "ethan@kindred.example", "Software Engineering", ["SOEN-287", "COMP-248", "ENGL-212"]),
        ("Sofia Rossi", "sofia@kindred.example", "Mathematics", ["MATH-205", "COMP-248", "PHYS-204"]),
        ("Kenji Watanabe", "kenji@kindred.example", "Computer Science", ["COMP-352", "COMP-249"]),
        ("Olivia Martin", "olivia@kindred.example", "English", ["ENGL-212", "MATH-203"]),
        ("Samuel Okafor", "samuel@kindred.example", "Software Engineering", ["COMP-248", "COMP-249", "COMP-352", "SOEN-287", "MATH-203"]),
    ];

    public static async Task SeedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var courses = scope.ServiceProvider.GetRequiredService<ICourseRepository>();
        if ((await courses.ListAsync(CancellationToken.None)).Count > 0) return;

        foreach (var (code, title, department) in Catalog)
            courses.Add(Course.Create(CourseCode.From(code), title, department));
        await courses.SaveChangesAsync(CancellationToken.None);

        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        foreach (var (name, email, program, enrollments) in Students)
        {
            var student = await mediator.Send(new RegisterStudentCommand(name, email, program));
            if (student.IsFailure) throw new InvalidOperationException($"Seed failed for {name}: {student.Error!.Message}");

            foreach (var code in enrollments)
            {
                var enrolled = await mediator.Send(new EnrollInCourseCommand(student.Value.Id, code));
                if (enrolled.IsFailure) throw new InvalidOperationException($"Seed failed for {name}/{code}: {enrolled.Error!.Message}");
            }
        }

        scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DevSeeder")
            .LogInformation("Seeded {Courses} courses and {Students} demo students", Catalog.Length, Students.Length);
    }
}
