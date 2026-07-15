using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YoutubeBulkUploader.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaylistFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlaylistError",
                table: "UploadJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlaylistId",
                table: "UploadJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlaylistItemId",
                table: "UploadJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlaylistOrder",
                table: "UploadJobs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlaylistTitle",
                table: "UploadJobs",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlaylistError",
                table: "UploadJobs");

            migrationBuilder.DropColumn(
                name: "PlaylistId",
                table: "UploadJobs");

            migrationBuilder.DropColumn(
                name: "PlaylistItemId",
                table: "UploadJobs");

            migrationBuilder.DropColumn(
                name: "PlaylistOrder",
                table: "UploadJobs");

            migrationBuilder.DropColumn(
                name: "PlaylistTitle",
                table: "UploadJobs");
        }
    }
}
