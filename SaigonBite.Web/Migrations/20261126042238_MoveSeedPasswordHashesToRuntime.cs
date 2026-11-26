using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaigonBite.Web.Migrations
{
    /// <inheritdoc />
    public partial class MoveSeedPasswordHashesToRuntime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "PasswordHash",
                value: "!SEED-PENDING!");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 2,
                column: "PasswordHash",
                value: "!SEED-PENDING!");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 3,
                column: "PasswordHash",
                value: "!SEED-PENDING!");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 4,
                column: "PasswordHash",
                value: "!SEED-PENDING!");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 5,
                column: "PasswordHash",
                value: "!SEED-PENDING!");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 6,
                column: "PasswordHash",
                value: "!SEED-PENDING!");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 7,
                column: "PasswordHash",
                value: "!SEED-PENDING!");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 8,
                column: "PasswordHash",
                value: "!SEED-PENDING!");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "PasswordHash",
                value: "PBKDF2-SHA256$100000$seed-admin$MO1NujUgcgRbOaabCQqIe6cQHYLhmGVITJh5MkyGreQ=");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 2,
                column: "PasswordHash",
                value: "PBKDF2-SHA256$100000$seed-kitchen-d1$JAZsZSo16HXKKEZAvm1Wd7ils02wS90ZxOOZfkb4YWY=");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 3,
                column: "PasswordHash",
                value: "PBKDF2-SHA256$100000$seed-kitchen-bt$aJ5mn/pItyttA3qPIQVjPEPq6d8M3Syf9CH63Hx1M9Q=");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 4,
                column: "PasswordHash",
                value: "PBKDF2-SHA256$100000$seed-kitchen-td$rp/4+TVDXKHJ0bSx/3WwCgWRf6X7gDVRQLJo1uLEEaw=");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 5,
                column: "PasswordHash",
                value: "PBKDF2-SHA256$100000$seed-shipper1$bZkRM65l9vBHh1K/lP5oktpIWRsq0075eV10JCFcbrE=");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 6,
                column: "PasswordHash",
                value: "PBKDF2-SHA256$100000$seed-shipper2$EhI3lQUOWDi2ak29N5H9rv1YYGTJ4XNareA9lO771cQ=");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 7,
                column: "PasswordHash",
                value: "PBKDF2-SHA256$100000$seed-local$gV43amXn3D4yShVy+XaxlHNXP4dl6Cu3EwpWdU5jO3c=");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 8,
                column: "PasswordHash",
                value: "PBKDF2-SHA256$100000$seed-tourist$gaAA/ldWzQZj8JoieFTLAyF4wECXFNVlxqvJln/1FN8=");
        }
    }
}
