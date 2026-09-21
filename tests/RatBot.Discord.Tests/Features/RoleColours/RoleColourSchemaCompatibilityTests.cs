using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using RatBot.Infrastructure.Data;
using Shouldly;

namespace RatBot.Discord.Tests.Features.RoleColours;

[TestFixture]
public sealed class RoleColourSchemaCompatibilityTests
{
    [Test]
    public void RelocatedEntities_PreserveTheLatestMigrationSchema()
    {
        using BotDbContext db = new BotDbContext(
            new DbContextOptionsBuilder<BotDbContext>().UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused").Options
        );

        IMigrationsAssembly migrations = db.GetService<IMigrationsAssembly>();

        Migration latestMigration = migrations.CreateMigration(migrations.Migrations.Last().Value, db.Database.ProviderName!);

        IModel migrationModel = db.GetService<IModelRuntimeInitializer>().Initialize(latestMigration.TargetModel, designTime: true);

        IModel currentModel = db.GetService<IDesignTimeModel>().Model;

        db.GetService<IMigrationsModelDiffer>()
            .GetDifferences(migrationModel.GetRelationalModel(), currentModel.GetRelationalModel())
            .ShouldBeEmpty();

        db.Database.HasPendingModelChanges().ShouldBeFalse();
    }
}
