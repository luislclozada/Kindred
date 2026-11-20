using Kindred.SharedKernel.Domain;
using Kindred.StudentService.Domain;

namespace Kindred.StudentService.UnitTests;

public class StudentProfileTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddDays(1);

    private const string OriginalName = "Valeria Hernández";
    private const string OriginalProgram = "Ciencias de la Computación";

    private static Student NewStudent() =>
        Student.Register(OriginalName, "valeria@kindred.example", OriginalProgram, Now).Value;

    private static CourseCode Code(string value) => CourseCode.From(value);

    // ── Editing the profile ──────────────────────────────────────────────────

    [Fact]
    public void Editing_the_profile_replaces_name_and_program_trimmed()
    {
        var student = NewStudent();

        var result = student.UpdateProfile("  Valeria H. Ruiz ", "  Matemáticas  ", Later);

        Assert.True(result.IsSuccess);
        Assert.Equal("Valeria H. Ruiz", student.DisplayName);
        Assert.Equal("Matemáticas", student.Program);
    }

    [Fact]
    public void Editing_the_profile_leaves_email_and_enrollments_intact()
    {
        var student = NewStudent();
        student.Enroll(Code("COMP-248"), Now);
        student.Enroll(Code("MATH-203"), Now);

        student.UpdateProfile("Valeria Ruiz", "Matemáticas", Later);

        Assert.Equal("valeria@kindred.example", student.Email);
        Assert.Equal(Now, student.RegisteredAt);
        Assert.Equal(2, student.Enrollments.Count);
        Assert.True(student.IsEnrolledIn(Code("COMP-248")));
        Assert.True(student.IsEnrolledIn(Code("MATH-203")));
    }

    // ── Announcing a rename ──────────────────────────────────────────────────

    [Fact]
    public void Renaming_a_student_announces_the_new_name()
    {
        var student = NewStudent();

        student.UpdateProfile("  Valeria Ruiz ", OriginalProgram, Later);

        var raised = Assert.IsType<StudentRenamed>(Assert.Single(student.DomainEvents));
        Assert.Equal(student.Id, raised.StudentId);
        Assert.Equal("Valeria Ruiz", raised.DisplayName);
        Assert.Equal(Later, raised.OccurredAt);
    }

    [Fact]
    public void Changing_only_the_program_announces_nothing()
    {
        var student = NewStudent();

        var result = student.UpdateProfile(OriginalName, "Matemáticas", Later);

        Assert.True(result.IsSuccess);
        Assert.Equal("Matemáticas", student.Program);
        Assert.Equal(OriginalName, student.DisplayName);
        Assert.Empty(student.DomainEvents);
    }

    [Fact]
    public void A_name_differing_only_by_surrounding_spaces_is_not_a_rename()
    {
        var student = NewStudent();

        var result = student.UpdateProfile($"  {OriginalName}  ", OriginalProgram, Later);

        Assert.True(result.IsSuccess);
        Assert.Equal(OriginalName, student.DisplayName);
        Assert.Empty(student.DomainEvents);
    }

    [Fact]
    public void Saving_an_unchanged_profile_announces_nothing()
    {
        var student = NewStudent();

        var result = student.UpdateProfile(OriginalName, OriginalProgram, Later);

        Assert.True(result.IsSuccess);
        Assert.Empty(student.DomainEvents);
    }

    // ── Rejections ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, OriginalProgram, "student.display_name_required")]
    [InlineData("", OriginalProgram, "student.display_name_required")]
    [InlineData("   ", OriginalProgram, "student.display_name_required")]
    [InlineData("Valeria Ruiz", null, "student.program_required")]
    [InlineData("Valeria Ruiz", "", "student.program_required")]
    [InlineData("Valeria Ruiz", "   ", "student.program_required")]
    public void Editing_the_profile_rejects_missing_details_and_changes_nothing(
        string? name, string? program, string expectedCode)
    {
        var student = NewStudent();

        var result = student.UpdateProfile(name, program, Later);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal(OriginalName, student.DisplayName);
        Assert.Equal(OriginalProgram, student.Program);
        Assert.Empty(student.DomainEvents);
    }

    [Fact]
    public void A_missing_name_is_reported_before_a_missing_program()
    {
        var student = NewStudent();

        var result = student.UpdateProfile("", "", Later);

        Assert.Equal("student.display_name_required", result.Error!.Code);
    }
}
