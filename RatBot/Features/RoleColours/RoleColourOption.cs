namespace RatBot.Features.RoleColours;

public sealed class RoleColourOption
{
    // EF Core private ctor
    private RoleColourOption() { }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public ulong DisplayRoleId { get; private set; }

    public ulong GuildId { get; private set; }

    public bool IsEnabled { get; private set; }

    // Stable identifier used by admins and users
    public string Key { get; private set; } = null!;

    // Human-friendly label for display
    public string Label { get; private set; } = null!;

    // Uppercase/normalized form for uniqueness checks
    public string NormalisedKey { get; private set; } = null!;

    public Id OptionId { get; private set; } = Id.Empty;

    public ulong SourceRoleId { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static ErrorOr<RoleColourOption> Create(ulong guildId, string key, string label, ulong sourceRoleId, ulong displayRoleId)
    {
        if (guildId == 0)
            return Error.Validation("RoleColourOption.GuildRequired", "Guild is required.");

        if (sourceRoleId == displayRoleId)
            return Error.Validation("RoleColourOption.RolesMustDiffer", "Source and display role IDs must be different.");

        if (string.IsNullOrWhiteSpace(key))
            return Error.Validation("RoleColourOption.KeyRequired", "Key is required.");

        if (string.IsNullOrWhiteSpace(label))
            return Error.Validation("RoleColourOption.LabelRequired", "Label is required.");

        DateTimeOffset now = DateTimeOffset.UtcNow;

        string trimmedKey = key.Trim();
        string normalized = trimmedKey.ToUpperInvariant();

        return new RoleColourOption
        {
            OptionId = Id.NewId(),
            GuildId = guildId,
            Key = trimmedKey,
            NormalisedKey = normalized,
            Label = label.Trim(),
            SourceRoleId = sourceRoleId,
            DisplayRoleId = displayRoleId,
            IsEnabled = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    public void Disable()
    {
        if (!IsEnabled)
            return;

        IsEnabled = false;
        Touch();
    }

    public ErrorOr<Success> Update(string key, string label, ulong displayRoleId)
    {
        if (SourceRoleId == displayRoleId)
            return Error.Validation("RoleColourOption.RolesMustDiffer", "Source and display role IDs must be different.");

        if (string.IsNullOrWhiteSpace(key))
            return Error.Validation("RoleColourOption.KeyRequired", "Key is required.");

        if (string.IsNullOrWhiteSpace(label))
            return Error.Validation("RoleColourOption.LabelRequired", "Label is required.");

        string trimmedKey = key.Trim();

        Key = trimmedKey;
        NormalisedKey = trimmedKey.ToUpperInvariant();
        Label = label.Trim();
        DisplayRoleId = displayRoleId;
        Touch();

        return Result.Success;
    }

    private void Touch() => UpdatedAtUtc = DateTimeOffset.UtcNow;

    // Real SCR/DCR mappings only. The built-in "no colour" preference is not configured here.
    public readonly record struct Id(Guid Value)
    {
        public static Id Empty { get; } = new Id(Guid.Empty);

        public static Id NewId() => new Id(Guid.NewGuid());
    }
}
