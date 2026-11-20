using Kindred.SharedKernel.Domain;
using Kindred.SharedKernel.Messaging;
using Kindred.SharedKernel.Messaging.Events;
using Kindred.StudentService.Application;
using Kindred.StudentService.Application.Commands;
using Kindred.StudentService.Domain;

namespace Kindred.StudentService.UnitTests;

public class UpdateStudentProfileHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddDays(1);

    private const string OriginalName = "Valeria Hernández";
    private const string OriginalProgram = "Ciencias de la Computación";

    private readonly List<string> _log = [];
    private readonly FakeStudentRepository _students;
    private readonly FakeEventBus _bus;
    private readonly UpdateStudentProfileHandler _handler;

    public UpdateStudentProfileHandlerTests()
    {
        _students = new FakeStudentRepository(_log);
        _bus = new FakeEventBus(_log);
        _handler = new UpdateStudentProfileHandler(_students, _bus, new FixedClock(Later));
    }

    private Student ExistingStudent()
    {
        var student = Student.Register(OriginalName, "valeria@kindred.example", OriginalProgram, Now).Value;
        student.Enroll(CourseCode.From("COMP-248"), Now);
        student.DequeueDomainEvents();
        _students.Add(student);
        return student;
    }

    private Task<Result<StudentDto>> Update(Guid id, string? name, string? program) =>
        _handler.Handle(new UpdateStudentProfileCommand(id, name, program), CancellationToken.None);

    // ── Editing ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Editing_a_profile_saves_it_and_returns_the_updated_student()
    {
        var student = ExistingStudent();

        var result = await Update(student.Id, "  Valeria Ruiz ", " Matemáticas ");

        Assert.True(result.IsSuccess);
        Assert.Equal("Valeria Ruiz", result.Value.DisplayName);
        Assert.Equal("Matemáticas", result.Value.Program);
        Assert.Equal("valeria@kindred.example", result.Value.Email);
        Assert.Single(result.Value.Enrollments);
        Assert.Equal("Valeria Ruiz", student.DisplayName);
        Assert.Equal(1, _students.SaveCount);
    }

    // ── Announcing a rename ──────────────────────────────────────────────────

    [Fact]
    public async Task Renaming_a_student_publishes_the_new_name_after_saving()
    {
        var student = ExistingStudent();

        await Update(student.Id, "Valeria Ruiz", OriginalProgram);

        var published = Assert.IsType<StudentRenamedIntegrationEvent>(Assert.Single(_bus.Published));
        Assert.Equal(student.Id, published.StudentId);
        Assert.Equal("Valeria Ruiz", published.DisplayName);
        Assert.Equal(Later, published.OccurredAt);
        Assert.Equal(new[] { "save", "publish" }, _log);
    }

    [Fact]
    public async Task Changing_only_the_program_publishes_nothing()
    {
        var student = ExistingStudent();

        var result = await Update(student.Id, OriginalName, "Matemáticas");

        Assert.True(result.IsSuccess);
        Assert.Equal("Matemáticas", result.Value.Program);
        Assert.Equal(1, _students.SaveCount);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task A_name_differing_only_by_surrounding_spaces_publishes_nothing()
    {
        var student = ExistingStudent();

        await Update(student.Id, $" {OriginalName} ", OriginalProgram);

        Assert.Empty(_bus.Published);
    }

    // ── Rejections ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Editing_an_unknown_student_is_not_found_and_publishes_nothing()
    {
        var result = await Update(Guid.NewGuid(), "Valeria Ruiz", "Matemáticas");

        Assert.True(result.IsFailure);
        Assert.Equal("student.not_found", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(0, _students.SaveCount);
        Assert.Empty(_bus.Published);
    }

    [Theory]
    [InlineData("   ", "Matemáticas", "student.display_name_required")]
    [InlineData("Valeria Ruiz", "   ", "student.program_required")]
    public async Task A_rejected_edit_changes_nothing_saves_nothing_and_publishes_nothing(
        string name, string program, string expectedCode)
    {
        var student = ExistingStudent();

        var result = await Update(student.Id, name, program);

        Assert.Equal(expectedCode, result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal(OriginalName, student.DisplayName);
        Assert.Equal(OriginalProgram, student.Program);
        Assert.Equal(0, _students.SaveCount);
        Assert.Empty(_bus.Published);
    }

    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeEventBus(List<string> log) : IEventBus
    {
        public List<IIntegrationEvent> Published { get; } = [];

        public Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        {
            log.Add("publish");
            Published.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStudentRepository(List<string> log) : IStudentRepository
    {
        private readonly List<Student> _students = [];

        public int SaveCount { get; private set; }

        public void Add(Student student) => _students.Add(student);

        public Task<Student?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_students.FirstOrDefault(s => s.Id == id));

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            log.Add("save");
            SaveCount++;
            return Task.CompletedTask;
        }

        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken) =>
            Task.FromResult(_students.Any(s => s.Email == email));

        public Task<IReadOnlyList<Student>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Student>>(_students);

        public Task<IReadOnlyList<Student>> ListSharingAnyCourseAsync(
            Guid studentId, IReadOnlyCollection<CourseCode> courses, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<CourseCode, int>> CountEnrollmentsByCourseAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
