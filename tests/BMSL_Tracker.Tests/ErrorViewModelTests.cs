using BMSL_Tracker.Models;
using Xunit;

namespace BMSL_Tracker.Tests;

public class ErrorViewModelTests
{
    [Fact]
    public void Defaults_ToServerError()
    {
        var model = new ErrorViewModel();

        Assert.Equal(500, model.StatusCode);
        Assert.False(model.ShowRequestId);
        Assert.Contains("error occurred", model.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(404, "Page not found.")]
    [InlineData(401, "You need to sign in for that.")]
    [InlineData(403, "Access denied.")]
    [InlineData(429, "Too many requests.")]
    public void Titles_MapToStatusCode(int code, string expectedTitle)
    {
        var model = new ErrorViewModel { StatusCode = code };

        Assert.Equal(expectedTitle, model.Title);
    }

    [Fact]
    public void ShowRequestId_WhenSet()
    {
        var model = new ErrorViewModel { RequestId = "abc-123" };

        Assert.True(model.ShowRequestId);
    }
}
