using Kindred.StudentService.Domain;

namespace Kindred.StudentService.UnitTests;

public class CourseCodeTests
{
    [Theory]
    [InlineData("COMP-248", "COMP-248")]
    [InlineData("comp-248", "COMP-248")]
    [InlineData("  soen-287 ", "SOEN-287")]
    [InlineData("ENG-101", "ENG-101")]
    public void A_valid_code_is_normalized_to_upper_case(string raw, string expected)
    {
        var result = CourseCode.Create(raw);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("COMP248")]
    [InlineData("CO-248")]
    [InlineData("COMPS-248")]
    [InlineData("COMP-24")]
    [InlineData("COMP-2488")]
    [InlineData("C0MP-248")]
    public void Anything_else_is_rejected(string? raw)
    {
        var result = CourseCode.Create(raw);

        Assert.True(result.IsFailure);
        Assert.Equal("course.invalid_code", result.Error!.Code);
    }

    [Fact]
    public void Two_codes_with_the_same_value_are_equal()
    {
        Assert.Equal(CourseCode.From("COMP-248"), CourseCode.From("comp-248"));
    }
}
