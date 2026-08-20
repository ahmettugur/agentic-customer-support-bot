using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CustomerSupportBot.Adapters.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerAccountUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_users_linked_customer_id",
                schema: "auth",
                table: "users",
                column: "linked_customer_id",
                unique: true,
                filter: "linked_customer_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_users_linked_customer_id",
                schema: "auth",
                table: "users");
        }
    }
}
