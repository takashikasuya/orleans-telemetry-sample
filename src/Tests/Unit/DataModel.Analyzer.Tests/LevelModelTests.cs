using System.Threading.Tasks;
using DataModel.Analyzer.Models;
using FluentAssertions;
using Xunit;

namespace DataModel.Analyzer.Tests;

public class LevelModelTests
{
    [Theory]
    [InlineData("B1F", -1)]
    [InlineData("B2F", -2)]
    [InlineData("1F", 1)]
    [InlineData("12F", 12)]
    [InlineData("12階", 12)]
    [InlineData("１２階", 12)]
    [InlineData("Level 7", 7)]
    [InlineData("Penthouse", null)]
    public void ExtractLevelNumberFromName_Works(string name, int? expected)
    {
        var result = Level.ExtractLevelNumberFromName(name);
        result.Should().Be(expected);
    }
}
