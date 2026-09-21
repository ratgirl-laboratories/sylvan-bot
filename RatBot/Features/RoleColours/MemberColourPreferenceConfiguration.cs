using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RatBot.Features.RoleColours;

public sealed class MemberColourPreferenceConfiguration : IEntityTypeConfiguration<MemberColourPreference>
{
    public void Configure(EntityTypeBuilder<MemberColourPreference> builder)
    {
        builder.ToTable(
            "MemberColourPreferences",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_MemberColourPreferences_ConfiguredOption_SelectedOption_NotNull",
                    "(\"Kind\" <> 1) OR (\"SelectedOptionId\" IS NOT NULL)"
                );

                table.HasCheckConstraint(
                    "CK_MemberColourPreferences_NoColour_SelectedOption_Null",
                    "(\"Kind\" <> 2) OR (\"SelectedOptionId\" IS NULL)"
                );
            }
        );

        builder.HasKey(x => x.PreferenceId);

        builder.Property(x => x.PreferenceId).HasConversion(id => id.Value, value => new MemberColourPreference.Id(value));

        builder.Property(x => x.GuildId).IsRequired().HasConversion<long>().HasColumnType("bigint");
        builder.Property(x => x.UserId).IsRequired().HasConversion<long>().HasColumnType("bigint");
        builder.Property(x => x.Kind).IsRequired();

        builder
            .Property(x => x.SelectedOptionId)
            .HasConversion(id => id.HasValue ? id.Value.Value : (Guid?)null, value => value.HasValue ? new RoleColourOption.Id(value.Value) : null);

        builder.HasIndex(x => new { x.GuildId, x.UserId }).IsUnique();

        builder
            .HasOne<RoleColourOption>()
            .WithMany()
            .HasForeignKey(x => new { x.GuildId, x.SelectedOptionId })
            .HasPrincipalKey(x => new { x.GuildId, x.OptionId })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
