using Kindred.StudentService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kindred.StudentService.Data;

internal sealed class StudentConfiguration : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> builder)
    {
        builder.ToTable("students");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.DisplayName).HasMaxLength(120).IsRequired();
        builder.Property(s => s.Email).HasMaxLength(254).IsRequired();
        builder.HasIndex(s => s.Email).IsUnique();
        builder.Property(s => s.Program).HasMaxLength(120).IsRequired();
        builder.Ignore(s => s.DomainEvents);

        // Enrollments are part of the Student aggregate: loaded, saved and deleted with it.
        builder.OwnsMany(s => s.Enrollments, enrollment =>
        {
            enrollment.ToTable("enrollments");
            enrollment.WithOwner().HasForeignKey("StudentId");
            enrollment.Property(e => e.CourseCode)
                .HasConversion(code => code.Value, value => CourseCode.From(value))
                .HasMaxLength(8);
            enrollment.HasKey("StudentId", nameof(Enrollment.CourseCode));
            enrollment.HasIndex(e => e.CourseCode);
        });
        builder.Navigation(s => s.Enrollments).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class CourseConfiguration : IEntityTypeConfiguration<Course>
{
    public void Configure(EntityTypeBuilder<Course> builder)
    {
        builder.ToTable("courses");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("Code")
            .HasConversion(code => code.Value, value => CourseCode.From(value))
            .HasMaxLength(8)
            .ValueGeneratedNever();
        builder.Property(c => c.Title).HasMaxLength(160).IsRequired();
        builder.Property(c => c.Department).HasMaxLength(120).IsRequired();
        builder.Ignore(c => c.Code);
        builder.Ignore(c => c.DomainEvents);
    }
}
