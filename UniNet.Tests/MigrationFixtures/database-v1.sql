-- Run only in an isolated test database after the original authentication migration.
INSERT INTO "Accounts" ("Id", "Email", "PasswordHash", "Role", "Status", "EmailVerified", "CreatedAt", "UpdatedAt") VALUES
('00000000-0000-0000-0000-000000000001', 'student@migration.test', 'preserved-password-hash', 0, 1, false, now(), now()),
('00000000-0000-0000-0000-000000000002', 'partner@migration.test', 'preserved-partner-hash', 1, 1, false, now(), now());
INSERT INTO "UserProfiles" ("Id", "AccountId", "DisplayName", "UniversityName", "TaxCode", "PartnerType", "IsVerified", "CreatedAt", "UpdatedAt") VALUES
('00000000-0000-0000-0000-000000000011', '00000000-0000-0000-0000-000000000001', repeat('A', 150), 'UIT', null, null, false, now(), now()),
('00000000-0000-0000-0000-000000000012', '00000000-0000-0000-0000-000000000002', 'ABC Company', null, '0312345678', 0, true, now(), now());
INSERT INTO "RefreshTokens" ("Id", "AccountId", "TokenHash", "ExpiresAt", "CreatedAt") VALUES
('00000000-0000-0000-0000-000000000021', '00000000-0000-0000-0000-000000000002', 'preserved-token-hash', now() + interval '30 days', now());
