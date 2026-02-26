using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IISPSupdate.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScanSource = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MaxConcurrency = table.Column<int>(type: "int", nullable: false),
                    AllowReboot = table.Column<bool>(type: "bit", nullable: false),
                    OnlineOnly = table.Column<bool>(type: "bit", nullable: false),
                    ErrorPolicy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ScheduledForUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HangfireJobId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectRuns_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectServers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Fqdn = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Environment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PreFlightStatus = table.Column<int>(type: "int", nullable: false),
                    PreFlightError = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastScanAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectServers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectServers_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectRunServers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectRunId = table.Column<int>(type: "int", nullable: false),
                    ProjectServerId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    LastUpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LogPath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LogTail = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectRunServers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectRunServers_ProjectRuns_ProjectRunId",
                        column: x => x.ProjectRunId,
                        principalTable: "ProjectRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectRunServers_ProjectServers_ProjectServerId",
                        column: x => x.ProjectServerId,
                        principalTable: "ProjectServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServerScanResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    ProjectServerId = table.Column<int>(type: "int", nullable: false),
                    ScanTimeUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ScanSource = table.Column<int>(type: "int", nullable: false),
                    RawOutput = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerScanResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServerScanResults_ProjectServers_ProjectServerId",
                        column: x => x.ProjectServerId,
                        principalTable: "ProjectServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ServerScanResults_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServerSelections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    ProjectServerId = table.Column<int>(type: "int", nullable: false),
                    KbNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerSelections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServerSelections_ProjectServers_ProjectServerId",
                        column: x => x.ProjectServerId,
                        principalTable: "ProjectServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ServerSelections_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UpdateCandidates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ServerScanResultId = table.Column<int>(type: "int", nullable: false),
                    KbNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Severity = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsSelectedByDefault = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UpdateCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UpdateCandidates_ServerScanResults_ServerScanResultId",
                        column: x => x.ServerScanResultId,
                        principalTable: "ServerScanResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectRuns_ProjectId",
                table: "ProjectRuns",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectRunServers_ProjectRunId_ProjectServerId",
                table: "ProjectRunServers",
                columns: new[] { "ProjectRunId", "ProjectServerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectRunServers_ProjectServerId",
                table: "ProjectRunServers",
                column: "ProjectServerId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectServers_ProjectId",
                table: "ProjectServers",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ServerScanResults_ProjectId",
                table: "ServerScanResults",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ServerScanResults_ProjectServerId",
                table: "ServerScanResults",
                column: "ProjectServerId");

            migrationBuilder.CreateIndex(
                name: "IX_ServerSelections_ProjectId",
                table: "ServerSelections",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ServerSelections_ProjectServerId",
                table: "ServerSelections",
                column: "ProjectServerId");

            migrationBuilder.CreateIndex(
                name: "IX_UpdateCandidates_ServerScanResultId",
                table: "UpdateCandidates",
                column: "ServerScanResultId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectRunServers");

            migrationBuilder.DropTable(
                name: "ServerSelections");

            migrationBuilder.DropTable(
                name: "UpdateCandidates");

            migrationBuilder.DropTable(
                name: "ProjectRuns");

            migrationBuilder.DropTable(
                name: "ServerScanResults");

            migrationBuilder.DropTable(
                name: "ProjectServers");

            migrationBuilder.DropTable(
                name: "Projects");
        }
    }
}
