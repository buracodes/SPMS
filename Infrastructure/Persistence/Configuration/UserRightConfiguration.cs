using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configuration;

public class UserRightConfiguration : IEntityTypeConfiguration<UserRight>
{
    public void Configure(EntityTypeBuilder<UserRight> builder)
    {
        builder.Property(ur => ur.Effect).HasDefaultValue(Domain.Enums.PermissionEffect.Grant);

        builder.Property(ur => ur.Reason).HasMaxLength(500);

        builder.HasIndex(ur => new { ur.UserId, ur.PermissionId })
            .HasDatabaseName("IX_UserRights_UserId_PermissionId")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        builder.HasOne(ur => ur.User)
            .WithMany(u => u.UserRights)
            .HasForeignKey(ur => ur.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(ur => ur.Permission)
            .WithMany()
            .HasForeignKey(ur => ur.PermissionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
