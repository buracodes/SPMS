using System;
using System.Collections.Generic;
using Infrastructure.Persistence.Services;
using Xunit;

namespace Application.Tests.AccessControl;

/// <summary>The pure HR position-level → permission rule, independent of the HR database.</summary>
public class PositionPermissionMapperTests
{
    private static IReadOnlyDictionary<string, string[]> Mapping(params (string Level, string[] Rights)[] entries)
    {
        var map = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (level, rights) in entries)
        {
            map[level] = rights;
        }

        return map;
    }

    [Fact]
    public void MatchesLevelsCaseInsensitivelyAndIgnoresWhitespace()
    {
        var grants = PositionPermissionMapper.Map(
            new[] { " Manager ", "branch head" },
            Mapping(("manager", new[] { "dashboard:view", "scorecard:view" }),
                    ("Branch Head", new[] { "reports:view" })));

        Assert.Equal(
            new[] { "dashboard:view", "reports:view", "scorecard:view" },
            grants.OrderBy(g => g, StringComparer.Ordinal));
    }

    [Fact]
    public void UnknownOrBlankLevelsGrantNothing()
    {
        var grants = PositionPermissionMapper.Map(
            new string?[] { null, "", "  ", "Director" },
            Mapping(("Manager", new[] { "scorecard:view" })));

        Assert.Empty(grants);
    }

    [Fact]
    public void EmptyMappingGrantsNothing()
    {
        var grants = PositionPermissionMapper.Map(
            new[] { "Manager" },
            new Dictionary<string, string[]>());

        Assert.Empty(grants);
    }

    [Fact]
    public void DuplicatePermissionsAcrossLevelsAreDeduplicated()
    {
        var grants = PositionPermissionMapper.Map(
            new[] { "Manager", "Director" },
            Mapping(("Manager", new[] { "scorecard:view" }),
                    ("Director", new[] { " Scorecard:View ", "reports:view" })));

        Assert.Equal(2, grants.Count);
        Assert.Contains("reports:view", grants);
    }

    [Fact]
    public void NullOrBlankPermissionNamesAreSkipped()
    {
        var grants = PositionPermissionMapper.Map(
            new[] { "Manager" },
            Mapping(("Manager", new[] { "scorecard:view", "", "  " })));

        Assert.Single(grants);
        Assert.Contains("scorecard:view", grants);
    }
}
