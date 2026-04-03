// To add the initial EF Core migration, run from the solution root:
//
//   dotnet ef migrations add InitialCreate \
//       --project HgermanContentFactory.Infrastructure \
//       --startup-project HgermanContentFactory.Web \
//       --output-dir Migrations
//
// Then apply it:
//   dotnet ef database update \
//       --project HgermanContentFactory.Infrastructure \
//       --startup-project HgermanContentFactory.Web
//
// The app also auto-migrates at startup (Program.cs → db.Database.MigrateAsync()).
//
// NOTE: If you prefer to manage schema manually, run CF_Schema.sql directly
// against HgermanAppsDB and skip EF migrations.
