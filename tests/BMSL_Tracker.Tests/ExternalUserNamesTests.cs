using BMSL_Tracker.Services;
using Xunit;

namespace BMSL_Tracker.Tests;

public class ExternalUserNamesTests
{
    [Theory]
    [InlineData("John.Doe+work@Example.com", null, "john.doe")]
    [InlineData("jane@gmail.com", "Jane Smith", "jane")]
    [InlineData(null, "Jane  O'Hara!", "jane.ohara")]
    [InlineData(null, "Ünïcode Öwner", "ncode.wner")]
    [InlineData("###@example.com", null, "user")]
    [InlineData(null, null, "user")]
    public void BuildBaseUserName_DerivesSafeLowerCaseNames(string? email, string? displayName, string expected)
    {
        Assert.Equal(expected, ExternalUserNames.BuildBaseUserName(email, displayName));
    }

    [Fact]
    public void BuildBaseUserName_TruncatesLongCandidates()
    {
        var email = new string('a', 60) + "@example.com";

        var result = ExternalUserNames.BuildBaseUserName(email, null);

        Assert.Equal(32, result.Length);
        Assert.All(result, c => Assert.True(char.IsLetterOrDigit(c) || c is '.' or '_' or '-'));
    }

    [Theory]
    [MemberData(nameof(TakenSets))]
    public async Task ReserveUniqueUserName_AppendsSmallestFreeNumber(
        string baseName, HashSet<string> taken, string expected)
    {
        var result = await ExternalUserNames.ReserveUniqueUserNameAsync(
            baseName, name => Task.FromResult(taken.Contains(name)));

        Assert.Equal(expected, result);
    }

    public static TheoryData<string, HashSet<string>, string> TakenSets => new()
    {
        { "jsmith", new HashSet<string>(), "jsmith" },
        { "jsmith", new HashSet<string> { "jsmith" }, "jsmith1" },
        { "jsmith", new HashSet<string> { "jsmith", "jsmith1", "jsmith2" }, "jsmith3" },
    };

    [Fact]
    public async Task ReserveUniqueUserName_FallsBackToRandomSuffix()
    {
        // Simulate the deterministic range being taken (base..base999) -> random fallback still succeeds.
        var taken = new HashSet<string>(Enumerable.Range(0, 1000).Select(i => i == 0 ? "bob" : $"bob{i}"));

        var result = await ExternalUserNames.ReserveUniqueUserNameAsync(
            "bob", name => Task.FromResult(taken.Contains(name)));

        Assert.StartsWith("bob", result);
        Assert.NotEqual("bob", result);
    }
}
