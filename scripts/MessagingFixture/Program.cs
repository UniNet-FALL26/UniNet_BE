using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Infrastructure.Data;

// Explicit local-only data for real browser/SignalR tests. Never reads the application's .env.
var raw = Environment.GetEnvironmentVariable("UNINET_TEST_POSTGRES") ?? throw new InvalidOperationException("UNINET_TEST_POSTGRES is required.");
var connection = new NpgsqlConnectionStringBuilder(raw);
if (connection.Host is not ("127.0.0.1" or "localhost") || !connection.Database!.StartsWith("uninet_chat_")) throw new InvalidOperationException("Only a local uninet_chat_* database is allowed.");
var schema = "chat_browser_" + Guid.NewGuid().ToString("N");
await using var admin = new NpgsqlConnection(raw); await admin.OpenAsync(); await new NpgsqlCommand($"CREATE SCHEMA {schema}", admin).ExecuteNonQueryAsync();
connection.SearchPath = schema;
await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(connection.ConnectionString).Options);
await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript());
var password = "ChatTest123!";
Account User(string name) { var account = new Account { Email = name.ToLowerInvariant() + "@chat.test", EmailVerified = true, Profile = new() { FullName = name, Nickname = name, Bio = "Messaging browser fixture", UniversityName = "UniNet Test" } }; account.PasswordHash = new PasswordHasher<Account>().HashPassword(account, password); return account; }
var alice = User("Alice"); var bob = User("Bob"); var carol = User("Carol"); var dan = User("Dan"); db.AddRange(alice, bob, carol, dan); await db.SaveChangesAsync();
var friends = new FriendshipService(db);
foreach (var peer in new[] { bob, carol }) { var request = await friends.SendAsync(alice.Id, peer.Id, default); await friends.DecideAsync(peer.Id, request.FriendshipId!.Value, "accept", request.FriendshipVersion!.Value, default); }
var chat = new MessagingService(db); var group = await chat.CreateGroup(alice.Id, new("UniNet Development", null, [bob.Id, carol.Id]), default);
var fixture = new { connectionString = connection.ConnectionString, schema, password, groupId = group.Id, users = new[] { alice, bob, carol, dan }.Select(a => new { a.Id, a.Email, profileId = a.Profile!.Id, name = a.Profile.FullName }) };
var output = Environment.GetEnvironmentVariable("UNINET_CHAT_FIXTURE") ?? throw new InvalidOperationException("UNINET_CHAT_FIXTURE output path is required.");
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(fixture, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
Console.WriteLine("Created local messaging fixture: " + schema);
