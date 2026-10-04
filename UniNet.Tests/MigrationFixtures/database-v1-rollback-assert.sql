DO $$ BEGIN
    IF (SELECT count(*) FROM "UserProfiles") <> 2
        OR (SELECT "DisplayName" FROM "UserProfiles" WHERE "Id" = '00000000-0000-0000-0000-000000000011') <> repeat('A', 150)
        OR (SELECT "DisplayName" FROM "UserProfiles" WHERE "Id" = '00000000-0000-0000-0000-000000000012') <> 'ABC Company'
        OR (SELECT "TaxCode" FROM "UserProfiles" WHERE "Id" = '00000000-0000-0000-0000-000000000012') <> '0312345678'
        OR NOT (SELECT "IsVerified" FROM "UserProfiles" WHERE "Id" = '00000000-0000-0000-0000-000000000012')
        OR (SELECT "TokenHash" FROM "RefreshTokens") <> 'preserved-token-hash'
    THEN RAISE EXCEPTION 'Rollback did not restore legacy data'; END IF;
END $$;
