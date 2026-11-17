using MailManager.Data.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace MailManager.Data.Migrations;

[DbContext(typeof(MailDbContext))]
internal sealed class MailDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder model)
    {
        model.HasAnnotation("ProductVersion", "10.0.14");

        model.Entity("MailManager.Core.Models.EmailAccount", entity =>
        {
            entity.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            entity.Property<string>("Email").IsRequired().HasColumnType("TEXT");
            entity.Property<int>("Provider").HasColumnType("INTEGER");
            entity.Property<DateTime>("CreatedUtc").HasColumnType("TEXT");
            entity.Property<int>("Status").HasColumnType("INTEGER");
            entity.Property<int>("PhoneCheck").HasColumnType("INTEGER");
            entity.Property<string>("Note").IsRequired().HasColumnType("TEXT");
            entity.Property<byte[]>("ProtectedPassword").HasColumnType("BLOB");
            entity.HasKey("Id");
            entity.HasIndex("Email").IsUnique();
            entity.ToTable("Accounts");
        });
        model.Entity("MailManager.Core.Models.RegistrationTask", entity =>
        {
            entity.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            entity.Property<int>("Provider").HasColumnType("INTEGER");
            entity.Property<int>("Mode").HasColumnType("INTEGER");
            entity.Property<string>("Login").IsRequired().HasColumnType("TEXT");
            entity.Property<string>("FirstName").HasColumnType("TEXT");
            entity.Property<string>("LastName").HasColumnType("TEXT");
            entity.Property<int>("Status").HasColumnType("INTEGER");
            entity.Property<int>("PhoneCheck").HasColumnType("INTEGER");
            entity.Property<DateTime>("CreatedUtc").HasColumnType("TEXT");
            entity.Property<DateTime?>("FinishedUtc").HasColumnType("TEXT");
            entity.Property<string>("Note").IsRequired().HasColumnType("TEXT");
            entity.Property<byte[]>("ProtectedPassword").HasColumnType("BLOB");
            entity.HasKey("Id");
            entity.HasIndex("Status", "Provider");
            entity.ToTable("Tasks");
        });
        model.Entity("MailManager.Core.Models.HistoryEntry", entity =>
        {
            entity.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            entity.Property<DateTime>("OccurredUtc").HasColumnType("TEXT");
            entity.Property<int>("Action").HasColumnType("INTEGER");
            entity.Property<string>("Subject").IsRequired().HasColumnType("TEXT");
            entity.HasKey("Id");
            entity.HasIndex("OccurredUtc");
            entity.ToTable("History");
        });
        model.Entity("MailManager.Core.Models.AppSettings", entity =>
        {
            entity.Property<int>("Id").ValueGeneratedNever().HasColumnType("INTEGER");
            entity.Property<int>("LoginLength").HasColumnType("INTEGER");
            entity.Property<int>("PasswordLength").HasColumnType("INTEGER");
            entity.Property<int>("ClipboardSeconds").HasColumnType("INTEGER");
            entity.HasKey("Id");
            entity.ToTable("Settings");
        });
    }
}
