using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RatBot.Features.RoleColours;

public sealed class RoleColourOptionConfiguration : IEntityTypeConfiguration<RoleColourOption>
{
    public void Configure(EntityTypeBuilder<RoleColourOption> builder)
    {
        builder.ToTable(
            "RoleColourOptions",
            table =>
            {
                table.HasCheckConstraint("CK_RoleColourOptions_Key_NotBlank", "length(btrim(\"Key\")) > 0");
                table.HasCheckConstraint("CK_RoleColourOptions_Label_NotBlank", "length(btrim(\"Label\")) > 0");

                table.HasCheckConstraint("CK_RoleColourOptions_DifferentRoles", "\"SourceRoleId\" <> \"DisplayRoleId\"");
            }
        );

        builder.HasKey(x => x.OptionId);
        builder.HasAlternateKey(x => new { x.GuildId, x.OptionId });

        builder.Property(x => x.OptionId).HasConversion(id => id.Value, value => new RoleColourOption.Id(value));

        builder.Property(x => x.GuildId).IsRequired().HasConversion<long>().HasColumnType("bigint");
        builder.Property(x => x.Key).IsRequired().HasMaxLength(64);
        builder.Property(x => x.NormalisedKey).IsRequired().HasMaxLength(64);
        builder.Property(x => x.Label).IsRequired().HasMaxLength(64);
        builder.Property(x => x.SourceRoleId).IsRequired().HasConversion<long>().HasColumnType("bigint");
        builder.Property(x => x.DisplayRoleId).IsRequired().HasConversion<long>().HasColumnType("bigint");
        builder.Property(x => x.IsEnabled).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired().HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).IsRequired().HasColumnType("timestamp with time zone");

        builder.HasIndex(x => new { x.GuildId, x.NormalisedKey }).IsUnique();
        builder.HasIndex(x => new { x.GuildId, x.DisplayRoleId }).IsUnique();
        builder.HasIndex(x => new { x.GuildId, x.SourceRoleId }).IsUnique();
        builder
            .HasIndex(x => new
            {
                x.GuildId,
                x.SourceRoleId,
                x.DisplayRoleId,
            })
            .IsUnique();
    }
}
