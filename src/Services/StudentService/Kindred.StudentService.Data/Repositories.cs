using Kindred.StudentService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kindred.StudentService.Data;

internal sealed class StudentRepository(StudentsDbContext db) : IStudentRepository
{
    public Task<Student?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Students.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken) =>
        db.Students.AnyAsync(s => s.Email == email, cancellationToken);

    public async Task<IReadOnlyList<Student>> ListAsync(CancellationToken cancellationToken) =>
        await db.Students.AsNoTracking().ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Student>> ListSharingAnyCourseAsync(
        Guid studentId, IReadOnlyCollection<CourseCode> courses, CancellationToken cancellationToken)
    {
        var codes = courses.ToList();
        return await db.Students.AsNoTracking()
            .Where(s => s.Id != studentId && s.Enrollments.Any(e => codes.Contains(e.CourseCode)))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<CourseCode, int>> CountEnrollmentsByCourseAsync(CancellationToken cancellationToken) =>
        await db.Students.AsNoTracking()
            .SelectMany(s => s.Enrollments)
            .GroupBy(e => e.CourseCode)
            .Select(g => new { Course = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Course, x => x.Count, cancellationToken);

    public void Add(Student student) => db.Students.Add(student);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

internal sealed class CourseRepository(StudentsDbContext db) : ICourseRepository
{
    public async Task<IReadOnlyList<Course>> ListAsync(CancellationToken cancellationToken) =>
        await db.Courses.AsNoTracking().ToListAsync(cancellationToken);

    public Task<bool> ExistsAsync(CourseCode code, CancellationToken cancellationToken) =>
        db.Courses.AnyAsync(c => c.Id == code, cancellationToken);

    public void Add(Course course) => db.Courses.Add(course);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
