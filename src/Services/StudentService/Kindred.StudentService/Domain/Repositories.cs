namespace Kindred.StudentService.Domain;

public interface IStudentRepository
{
    Task<Student?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);
    Task<IReadOnlyList<Student>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Every other student enrolled in at least one of the given courses.</summary>
    Task<IReadOnlyList<Student>> ListSharingAnyCourseAsync(
        Guid studentId, IReadOnlyCollection<CourseCode> courses, CancellationToken cancellationToken);

    /// <summary>Course code → number of students enrolled.</summary>
    Task<IReadOnlyDictionary<CourseCode, int>> CountEnrollmentsByCourseAsync(CancellationToken cancellationToken);

    void Add(Student student);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface ICourseRepository
{
    Task<IReadOnlyList<Course>> ListAsync(CancellationToken cancellationToken);
    Task<bool> ExistsAsync(CourseCode code, CancellationToken cancellationToken);
    void Add(Course course);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
