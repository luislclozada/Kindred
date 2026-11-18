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
        ("COMP-248", "Programación Orientada a Objetos I", "Ciencias de la Computación"),
        ("COMP-249", "Programación Orientada a Objetos II", "Ciencias de la Computación"),
        ("COMP-352", "Estructuras de Datos y Algoritmos", "Ciencias de la Computación"),
        ("MATH-203", "Cálculo Diferencial e Integral I", "Matemáticas"),
        ("MATH-205", "Cálculo Diferencial e Integral II", "Matemáticas"),
        ("PHYS-204", "Mecánica", "Física"),
        ("SOEN-287", "Programación Web", "Ingeniería de Software"),
        ("ENGL-212", "Inglés Técnico", "Lenguas"),
    ];

    private static readonly (string Name, string Email, string Program, string[] Courses)[] Students =
    [
        ("Valeria Hernández", "valeria@kindred.example", "Ciencias de la Computación", ["COMP-248", "MATH-203", "SOEN-287", "ENGL-212"]),
        ("Santiago Ramírez", "santiago@kindred.example", "Ingeniería de Software", ["COMP-248", "SOEN-287", "MATH-203"]),
        ("Ximena Torres", "ximena@kindred.example", "Ciencias de la Computación", ["COMP-249", "COMP-352", "MATH-205"]),
        ("Diego Morales", "diego@kindred.example", "Ingeniería de Software", ["COMP-248", "COMP-249", "SOEN-287", "PHYS-204"]),
        ("Regina Castillo", "regina@kindred.example", "Matemáticas", ["MATH-203", "MATH-205", "PHYS-204"]),
        ("Emiliano Flores", "emiliano@kindred.example", "Física", ["PHYS-204", "MATH-203", "ENGL-212"]),
        ("Fernanda Guzmán", "fernanda@kindred.example", "Ciencias de la Computación", ["COMP-352", "COMP-249", "MATH-205", "SOEN-287"]),
        ("Mateo Sánchez", "mateo@kindred.example", "Ingeniería de Software", ["SOEN-287", "COMP-248", "ENGL-212"]),
        ("Sofía Mendoza", "sofia@kindred.example", "Matemáticas", ["MATH-205", "COMP-248", "PHYS-204"]),
        ("Leonardo Cruz", "leonardo@kindred.example", "Ciencias de la Computación", ["COMP-352", "COMP-249"]),
        ("Camila Ortega", "camila@kindred.example", "Lenguas Modernas", ["ENGL-212", "MATH-203"]),
        ("Sebastián Aguilar", "sebastian@kindred.example", "Ingeniería de Software", ["COMP-248", "COMP-249", "COMP-352", "SOEN-287", "MATH-203"]),
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
