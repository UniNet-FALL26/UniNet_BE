# Remove project classification and show recent update

Remove persisted Projects.Origin, its check/default, ProjectOrigin enum, origin fields from live API contracts and computed Hub mode. Keep historical migrations unchanged. Keep field/specialization, status, creator, membership, draft import data and assessment history.

Unified policy: new Hub projects start Active and create an owner member; completion remains explicit. Pending/Active projects can recruit. Pending/Active/Completed projects can invite accepted active student friends; rejected projects cannot. Completion expires applications and preserves pending direct invitations. Viewing never joins; only invite acceptance creates membership.

Migrate leadership before dropping Origin: existing internal creators retain leader identity using the existing SubmitterIsLeader field; imported leader/submission data remains intact. Do not encode origin under another name. No replacement classification column.

Remove creation type chooser, personal/community badges, information type/source rows and all runtime origin checks in FE/assessor/dashboard consumers. Show Cập nhật gần đây from UpdatedAt, formatted in Vietnam time, including time; edits refresh that value from API.

Order: schema/contract tests and policy tests → domain/API migration and mapping consumers → unified FE and timestamp → EF migration → isolated PostgreSQL migration/data checks → backend/frontend/dashboard checks and browser validation. Create a reviewable migration before applying to any non-isolated database.
