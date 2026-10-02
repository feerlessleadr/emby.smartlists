using Emby.Plugin.SmartLists.Core.Models;
using Emby.Plugin.SmartLists.Core.QueryEngine;
using Emby.Plugin.SmartLists.Utilities;

namespace Emby.Plugin.SmartLists.Tests.Utilities;

/// <summary>
/// Pins <see cref="PlaylistUserResolver.HasRulePinnedToAnyUser"/>. A user-data change by user B
/// normally refreshes only B's copy of an all-users playlist - but a rule pinned to B (e.g.
/// "Is Favorite for user B") reads B's data for EVERY user's copy, so all of them must refresh.
/// </summary>
public class PlaylistUserResolverTests
{
    private static readonly Guid UserB = Guid.Parse("7b24d53e-5007-4667-b8c5-376e00983e9e");
    private static readonly Guid UserC = Guid.Parse("3a8091f3-4d99-4ed4-99a6-2b1fe8fe0000");

    private static SmartPlaylistDto Playlist(Expression? rule = null, Expression? bumperRule = null) => new()
    {
        Name = "Test",
        ExpressionSets = rule == null ? [] : [new ExpressionSet { Expressions = [rule] }],
        Bumpers = bumperRule == null ? null : new BumperConfigDto { ExpressionSets = [new ExpressionSet { Expressions = [bumperRule] }] },
    };

    private static Expression PinnedTo(Guid userId, bool dashed = true)
        => new("IsFavorite", "Equal", "true") { UserId = dashed ? userId.ToString("D") : userId.ToString("N"), IncludeParentFavorite = true };

    [Fact]
    public void RulePinnedToATriggeringUser_IsPinned_WhateverTheIdFormat()
    {
        var triggering = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { UserB.ToString("N") };

        Assert.True(PlaylistUserResolver.HasRulePinnedToAnyUser(Playlist(PinnedTo(UserB, dashed: true)), triggering));
        Assert.True(PlaylistUserResolver.HasRulePinnedToAnyUser(Playlist(PinnedTo(UserB, dashed: false)), triggering));
    }

    [Fact]
    public void BumperRulePinnedToATriggeringUser_IsPinned()
    {
        Assert.True(PlaylistUserResolver.HasRulePinnedToAnyUser(Playlist(bumperRule: PinnedTo(UserB)), [UserB.ToString("N")]));
    }

    [Fact]
    public void RulesWithoutAUserOrPinnedToAnotherUser_AreNotPinned()
    {
        var triggering = new List<string> { UserB.ToString("N") };

        // No UserId: each copy reads its own user's data, so narrowing to B's copy is correct.
        Assert.False(PlaylistUserResolver.HasRulePinnedToAnyUser(Playlist(new Expression("IsFavorite", "Equal", "true")), triggering));
        Assert.False(PlaylistUserResolver.HasRulePinnedToAnyUser(Playlist(PinnedTo(UserC)), triggering));
        Assert.False(PlaylistUserResolver.HasRulePinnedToAnyUser(Playlist(), triggering));
    }
}
