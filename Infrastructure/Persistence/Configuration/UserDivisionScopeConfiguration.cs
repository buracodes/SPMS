using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public class UserDivisionScopeConfiguration : IEntityTypeConfiguration<UserDivisionScope>
{
    public void Configure(EntityTypeBuilder<UserDivisionScope> builder)
    {
        builder.HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Division)
            .WithMany()
            .HasForeignKey(s => s.DivisionId)
            .OnDelete(DeleteBehavior.Restrict);

        // A revoked grant is soft-deleted, so the uniqueness rule must only cover live rows;
        // otherwise re-granting a revoked division would collide with the retained row.
        builder.HasIndex(s => new { s.UserId, s.DivisionId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [Status] <> 0");
    }
}
