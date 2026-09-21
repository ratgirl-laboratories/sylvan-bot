namespace RatBot.Features.RoleColours;

public sealed class MemberColourPreference
{
    // EF Core private ctor
    private MemberColourPreference() { }

    public ulong GuildId { get; private set; }

    public bool IsNoColourSelected => Kind == MemberColourPreferenceKind.NoColour;

    public MemberColourPreferenceKind Kind { get; private set; }

    public Id PreferenceId { get; private set; } = Id.Empty;

    public RoleColourOption.Id? SelectedOptionId { get; private set; }

    public ulong UserId { get; private set; }

    public static MemberColourPreference CreateForOption(ulong guildId, ulong userId, RoleColourOption.Id selectedId) =>
        new MemberColourPreference
        {
            PreferenceId = Id.NewId(),
            GuildId = guildId,
            UserId = userId,
            Kind = MemberColourPreferenceKind.ConfiguredOption,
            SelectedOptionId = selectedId,
        };

    public static MemberColourPreference CreateNoColour(ulong guildId, ulong userId) =>
        new MemberColourPreference
        {
            PreferenceId = Id.NewId(),
            GuildId = guildId,
            UserId = userId,
            Kind = MemberColourPreferenceKind.NoColour,
            SelectedOptionId = null,
        };

    public void SelectNoColour()
    {
        Kind = MemberColourPreferenceKind.NoColour;
        SelectedOptionId = null;
    }

    public void SelectOption(RoleColourOption.Id id)
    {
        if (id.Equals(RoleColourOption.Id.Empty))
            throw new ArgumentException("Selected option id must be a real id.");

        Kind = MemberColourPreferenceKind.ConfiguredOption;
        SelectedOptionId = id;
    }

    public readonly record struct Id(Guid Value)
    {
        public static Id Empty { get; } = new Id(Guid.Empty);

        public static Id NewId() => new Id(Guid.NewGuid());
    }
}
