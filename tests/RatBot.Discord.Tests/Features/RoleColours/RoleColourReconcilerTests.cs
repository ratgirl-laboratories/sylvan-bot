using System.Collections.Immutable;
using RatBot.Features.RoleColours;
using RatBot.Handlers;
using Shouldly;

namespace RatBot.Discord.Tests.Features.RoleColours;

[TestFixture]
public sealed class RoleColourReconcilerTests
{
    [Test]
    public void SelectTargetDisplayRole_ExplicitNoColourSuppressesFallback()
    {
        RoleColourOption option = CreateOption("blue", 10, 20);
        MemberColourPreference preference = MemberColourPreference.CreateNoColour(1, 1);

        ulong? target = RoleColourReconciler.SelectTargetDisplayRole(new ulong[] { 10 }, preference, new[] { option }, _ => 5);

        target.ShouldBeNull();
    }

    [Test]
    public void SelectTargetDisplayRole_ValidExplicitSelectionWins()
    {
        RoleColourOption lower = CreateOption("blue", 10, 20);
        RoleColourOption higher = CreateOption("red", 11, 21);
        MemberColourPreference preference = MemberColourPreference.CreateForOption(1, 1, lower.OptionId);

        ulong? target = RoleColourReconciler.SelectTargetDisplayRole(
            new ulong[] { 10, 11 },
            preference,
            new[] { lower, higher },
            roleId => roleId == 10 ? 1 : 2
        );

        target.ShouldBe(20UL);
    }

    [Test]
    public void SelectTargetDisplayRole_InvalidExplicitSelectionFallsBackToHighestSourceRole()
    {
        RoleColourOption unavailable = CreateOption("blue", 10, 20);
        RoleColourOption lower = CreateOption("green", 11, 21);
        RoleColourOption higher = CreateOption("red", 12, 22);
        MemberColourPreference preference = MemberColourPreference.CreateForOption(1, 1, unavailable.OptionId);

        ulong? target = RoleColourReconciler.SelectTargetDisplayRole(
            new ulong[] { 11, 12 },
            preference,
            new[] { unavailable, lower, higher },
            roleId => roleId == 11 ? 1 : 2
        );

        target.ShouldBe(22UL);
    }

    [Test]
    public void CalculateRoleDiff_RemovesEveryConfiguredDisplayRoleExceptMissingTarget()
    {
        (ImmutableArray<ulong> add, ImmutableArray<ulong> remove) = RoleColourReconciler.CalculateRoleDiff(
            new ulong[] { 20, 21, 99 },
            new ulong[] { 20, 21, 22 },
            22
        );

        add.ShouldBe(new ulong[] { 22 });
        remove.ShouldBe(new ulong[] { 20, 21 });
    }

    [Test]
    public void CalculateRoleDiff_WhenTargetAlreadyPresent_IsIdempotent()
    {
        (ImmutableArray<ulong> add, ImmutableArray<ulong> remove) = RoleColourReconciler.CalculateRoleDiff(
            new ulong[] { 22, 99 },
            new ulong[] { 20, 21, 22 },
            22
        );

        add.ShouldBeEmpty();
        remove.ShouldBeEmpty();
    }

    private static RoleColourOption CreateOption(string key, ulong sourceRoleId, ulong displayRoleId) =>
        RoleColourOption.Create(1, key, key, sourceRoleId, displayRoleId).Value;
}
