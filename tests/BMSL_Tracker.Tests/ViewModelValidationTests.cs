using System.ComponentModel.DataAnnotations;
using BMSL_Tracker.Models;
using Xunit;

namespace BMSL_Tracker.Tests;

public class ViewModelValidationTests
{
    private static IList<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void RegisterViewModel_AcceptsValidInput()
    {
        var model = new RegisterViewModel
        {
            Username = "field.agent-1",
            Password = "Passw0rd!",
            ConfirmPassword = "Passw0rd!",
            Email = "agent1@example.com",
        };

        Assert.Empty(Validate(model));
    }

    [Fact]
    public void RegisterViewModel_AllowsMissingOptionalEmail()
    {
        var model = new RegisterViewModel
        {
            Username = "runner",
            Password = "Passw0rd!",
            ConfirmPassword = "Passw0rd!",
        };

        Assert.Empty(Validate(model));
    }

    [Theory]
    [InlineData("ab")]            // too short
    [InlineData("bad name!!")]    // illegal characters
    public void RegisterViewModel_RejectsBadUserNames(string username)
    {
        var model = new RegisterViewModel
        {
            Username = username,
            Password = "Passw0rd!",
            ConfirmPassword = "Passw0rd!",
        };

        Assert.NotEmpty(Validate(model));
    }

    [Fact]
    public void RegisterViewModel_RejectsShortPassword()
    {
        var model = new RegisterViewModel
        {
            Username = "validname",
            Password = "short",
            ConfirmPassword = "short",
        };

        Assert.NotEmpty(Validate(model));
    }

    [Fact]
    public void RegisterViewModel_RejectsMismatchedConfirmation()
    {
        var model = new RegisterViewModel
        {
            Username = "validname",
            Password = "Passw0rd!",
            ConfirmPassword = "Passw0rd?",
        };

        Assert.NotEmpty(Validate(model));
    }

    [Fact]
    public void LoginViewModel_RequiresIdentifierAndPassword()
    {
        Assert.NotEmpty(Validate(new LoginViewModel()));
        Assert.Empty(Validate(new LoginViewModel { Username = "anyone", Password = "whatever" }));
    }

    [Fact]
    public void LoginViewModel_AcceptsLongIdentifier()
    {
        var model = new LoginViewModel { Username = "someone@example.com", Password = "whatever" };
        Assert.Empty(Validate(model));
    }
}
