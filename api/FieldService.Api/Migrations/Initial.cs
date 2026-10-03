using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace FieldService.Api.Migrations;
[DbContext(typeof(AppDb))]
[Migration("202610030001_Initial")]
public class Initial:Migration
{
 protected override void Up(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
CREATE TABLE "Users" ("Id" uuid PRIMARY KEY, "Email" varchar(254) NOT NULL UNIQUE, "Name" text NOT NULL, "PasswordHash" text NOT NULL, "Role" text NOT NULL CHECK ("Role" IN ('Supervisor','Technician')));
CREATE TABLE "Sites" ("Id" uuid PRIMARY KEY, "Name" text NOT NULL, "Address" text NOT NULL);
CREATE TABLE "Assets" ("Id" uuid PRIMARY KEY, "SiteId" uuid NOT NULL REFERENCES "Sites"("Id") ON DELETE RESTRICT, "Identifier" text NOT NULL UNIQUE, "Name" text NOT NULL, "Category" text NOT NULL, "Location" text NOT NULL, "Status" text NOT NULL, "ServiceIntervalDays" integer NOT NULL CHECK ("ServiceIntervalDays">0));
CREATE TABLE "Schedules" ("Id" uuid PRIMARY KEY, "AssetId" uuid NOT NULL REFERENCES "Assets"("Id") ON DELETE RESTRICT, "Name" text NOT NULL, "IntervalDays" integer NOT NULL CHECK ("IntervalDays">0), "NextDueAt" timestamptz NOT NULL, "Priority" text NOT NULL, "TechnicianId" uuid REFERENCES "Users"("Id") ON DELETE RESTRICT, "ChecklistJson" text NOT NULL, "Active" boolean NOT NULL, "Version" bigint NOT NULL);
CREATE TABLE "WorkOrders" ("Id" uuid PRIMARY KEY, "AssetId" uuid NOT NULL REFERENCES "Assets"("Id") ON DELETE RESTRICT, "ScheduleId" uuid REFERENCES "Schedules"("Id") ON DELETE RESTRICT, "FollowUpFaultId" uuid, "Title" text NOT NULL, "DueAt" timestamptz NOT NULL, "Priority" text NOT NULL, "TechnicianId" uuid REFERENCES "Users"("Id") ON DELETE RESTRICT, "Status" text NOT NULL CHECK ("Status" IN ('Scheduled','Assigned','In Progress','Submitted','Completed')), "Version" bigint NOT NULL, "ChecklistJson" text NOT NULL, "InspectionJson" text NOT NULL, "ReviewNotes" text NOT NULL, "CompletedAt" timestamptz, UNIQUE ("ScheduleId","DueAt"));
CREATE TABLE "Parts" ("Id" uuid PRIMARY KEY, "Name" text NOT NULL, "Sku" text NOT NULL UNIQUE, "Stock" numeric(18,3) NOT NULL CHECK ("Stock">=0), "Version" bigint NOT NULL);
CREATE TABLE "Faults" ("Id" uuid PRIMARY KEY, "AssetId" uuid NOT NULL REFERENCES "Assets"("Id") ON DELETE RESTRICT, "WorkOrderId" uuid NOT NULL REFERENCES "WorkOrders"("Id") ON DELETE RESTRICT, "Description" text NOT NULL, "Severity" text NOT NULL, "ReportedAt" timestamptz NOT NULL);
CREATE TABLE "Attachments" ("Id" uuid PRIMARY KEY, "WorkOrderId" uuid NOT NULL REFERENCES "WorkOrders"("Id") ON DELETE RESTRICT, "UserId" uuid NOT NULL REFERENCES "Users"("Id") ON DELETE RESTRICT, "ContentType" text NOT NULL, "Hash" text NOT NULL, "Data" bytea NOT NULL);
CREATE TABLE "Receipts" ("Id" uuid PRIMARY KEY, "UserId" uuid NOT NULL REFERENCES "Users"("Id") ON DELETE RESTRICT, "RequestHash" text NOT NULL, "ResponseJson" text NOT NULL, "CreatedAt" timestamptz NOT NULL);
CREATE TABLE "Audits" ("Id" uuid PRIMARY KEY, "UserId" uuid NOT NULL, "EntityId" uuid NOT NULL, "Action" text NOT NULL, "Detail" text NOT NULL, "At" timestamptz NOT NULL);
CREATE TABLE "Consumptions" ("Id" uuid PRIMARY KEY, "WorkOrderId" uuid NOT NULL REFERENCES "WorkOrders"("Id") ON DELETE RESTRICT, "PartId" uuid NOT NULL REFERENCES "Parts"("Id") ON DELETE RESTRICT, "Quantity" numeric(18,3) NOT NULL CHECK ("Quantity">0), UNIQUE ("WorkOrderId","PartId"));
CREATE INDEX "IX_WorkOrders_Technician_Status_Due" ON "WorkOrders" ("TechnicianId","Status","DueAt");
CREATE INDEX "IX_Audits_Entity_At" ON "Audits" ("EntityId","At");
""");
 protected override void Down(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
DROP TABLE "Consumptions";
DROP TABLE "Audits";
DROP TABLE "Receipts";
DROP TABLE "Attachments";
DROP TABLE "Faults";
DROP TABLE "Parts";
DROP TABLE "WorkOrders";
DROP TABLE "Schedules";
DROP TABLE "Assets";
DROP TABLE "Sites";
DROP TABLE "Users";
""");
}
