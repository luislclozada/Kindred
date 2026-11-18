using Kindred.SharedKernel.Domain;
using Kindred.StudentService.Domain;

namespace Kindred.StudentService.UnitTests;

public class StudentTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);

    private static Student NewStudent() =>
        Student.Register("Amira Haddad", "amira@kindred.example", "Computer Science", Now).Value;

    private static CourseCode Code(string value) => CourseCode.From(value);

    // ── Registering ──────────────────────────────────────────────────────────

    [Fact]
    public void Registering_normalizes_the_email_and_trims_the_name()
    {
        var student = Student.Register("  Amira Haddad ", " Amira@Kindred.Example ", "Computer Science", Now).Value;

        Assert.Equal("Amira Haddad", student.DisplayName);
        Assert.Equal("amira@kindred.example", student.Email);
        Assert.Empty(student.Enrollments);
    }

    [Theory]
    [InlineData("", "amira@kindred.example", "Computer Science", "student.display_name_required")]
    [InlineData("Amira", "not-an-email", "Computer Science", "student.invalid_email")]
    [InlineData("Amira", "amira@nowhere", "Computer Science", "student.invalid_email")]
    [InlineData("Amira", "amira@kindred.example", " ", "student.program_required")]
    public void Registering_rejects_incomplete_details(string name, string email, string program, string expectedCode)
    {
        var result = Student.Register(name, email, program, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    // ── Enrolling ────────────────────────────────────────────────────────────

    [Fact]
    public void Enrolling_adds_the_course_and_raises_an_event()
    {
        var student = NewStudent();

        var result = student.Enroll(Code("COMP-248"), Now);

        Assert.True(result.IsSuccess);
        Assert.True(student.IsEnrolledIn(Code("COMP-248")));
        var raised = Assert.IsType<StudentEnrolledInCourse>(Assert.Single(student.DomainEvents));
        Assert.Equal(student.Id, raised.StudentId);
        Assert.Equal("Amira Haddad", raised.StudentName);
        Assert.Equal(Code("COMP-248"), raised.CourseCode);
    }

    [Fact]
    public void Enrolling_twice_in_the_same_course_is_a_conflict()
    {
        var student = NewStudent();
        student.Enroll(Code("COMP-248"), Now);

        var result = student.Enroll(Code("COMP-248"), Now);

        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal("enrollment.already_enrolled", result.Error.Code);
        Assert.Single(student.Enrollments);
    }

    [Fact]
    public void A_student_cannot_take_more_than_six_courses()
    {
        var student = NewStudent();
        foreach (var code in new[] { "COMP-248", "COMP-249", "COMP-352", "MATH-203", "MATH-205", "PHYS-204" })
            Assert.True(student.Enroll(Code(code), Now).IsSuccess);

        var result = student.Enroll(Code("SOEN-287"), Now);

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal("enrollment.course_limit_reached", result.Error.Code);
        Assert.Equal(Student.MaxCourses, student.Enrollments.Count);
    }

    // ── Dropping ─────────────────────────────────────────────────────────────

    [Fact]
    public void Dropping_removes_the_course_and_raises_an_event()
    {
        var student = NewStudent();
        student.Enroll(Code("COMP-248"), Now);
        student.DequeueDomainEvents();

        var result = student.Drop(Code("COMP-248"), Now);

        Assert.True(result.IsSuccess);
        Assert.Empty(student.Enrollments);
        var raised = Assert.IsType<StudentDroppedCourse>(Assert.Single(student.DomainEvents));
        Assert.Equal(Code("COMP-248"), raised.CourseCode);
    }

    [Fact]
    public void Dropping_a_course_the_student_does_not_take_is_not_found()
    {
        var student = NewStudent();

        var result = student.Drop(Code("COMP-248"), Now);

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal("enrollment.not_enrolled", result.Error.Code);
        Assert.Empty(student.DomainEvents);
    }

    [Fact]
    public void Dropping_frees_a_slot_under_the_course_limit()
    {
        var student = NewStudent();
        foreach (var code in new[] { "COMP-248", "COMP-249", "COMP-352", "MATH-203", "MATH-205", "PHYS-204" })
            student.Enroll(Code(code), Now);

        student.Drop(Code("PHYS-204"), Now);

        Assert.True(student.Enroll(Code("SOEN-287"), Now).IsSuccess);
    }
}
