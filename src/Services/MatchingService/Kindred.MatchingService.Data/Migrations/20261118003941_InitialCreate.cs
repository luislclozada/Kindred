using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kindred.MatchingService.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "course_rosters",
                columns: table => new
                {
                    CourseCode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_course_rosters", x => x.CourseCode);
                });

            migrationBuilder.CreateTable(
                name: "roster_members",
                columns: table => new
                {
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseCode = table.Column<string>(type: "character varying(8)", nullable: false),
                    StudentName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roster_members", x => new { x.CourseCode, x.StudentId });
                    table.ForeignKey(
                        name: "FK_roster_members_course_rosters_CourseCode",
                        column: x => x.CourseCode,
                        principalTable: "course_rosters",
                        principalColumn: "CourseCode",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "roster_members");

            migrationBuilder.DropTable(
                name: "course_rosters");
        }
    }
}
