using ReTAC.Shell;

namespace ReTAC.Domain.Tests;

/// <summary>R-01-7 / Q20: 拡張子が OS に登録されているかの判定。</summary>
public class RegisteredExtensionsTests
{
    [Theory]
    [InlineData(".txt", true)]
    [InlineData(".TXT", true)]
    [InlineData(".retac-unregistered-ext-test", false)]
    [InlineData("", false)]
    public void 登録の判定(string extension, bool expected) =>
        Assert.Equal(expected, RegisteredExtensions.IsRegistered(extension));
}
