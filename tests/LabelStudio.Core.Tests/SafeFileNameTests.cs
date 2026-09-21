namespace LabelStudio.Core.Tests;

public class SafeFileNameTests
{
    [Theory]
    [InlineData("ABC123456789", "ABC123456789")]
    [InlineData("D5J/213", "D5J_213")]
    [InlineData("a b:c", "a_b_c")]
    public void Keeps_letters_and_digits_only(string input, string expected) => Assert.Equal(expected, SafeFileName.From(input));
}
