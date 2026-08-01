using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustomerSupportBot.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddComplaintSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ComplaintRepository.Create, complaints.code için catalog.complaint_seq'ten
            // nextval() ile ID üretiyor (ComplaintConfiguration.Code = ValueGeneratedNever,
            // yani EF kendi identity/sequence'ini oluşturmuyor — orders.code'un aksine).
            // Bu sequence hiçbir migration'da oluşturulmamıştı, sadece test fixture'ında
            // (PostgresCatalogFixture) ad-hoc yaratılıyordu — bu yüzden gerçek bir
            // deployment'ta "relation catalog.complaint_seq does not exist" (42P01) hatası
            // alınıyordu. START WITH 1006: PersistenceHydrator.SeedDefaultComplaintsAsync
            // seed verisi 1001-1005 arası kodlarla explicit insert yapıyor; çakışmayı
            // önlemek için sequence bir sonraki değerden (1006) başlıyor.
            migrationBuilder.Sql(
                "CREATE SEQUENCE IF NOT EXISTS catalog.complaint_seq START WITH 1006;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS catalog.complaint_seq;");
        }
    }
}
