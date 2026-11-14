# External project drafts

Students enter the details of a completed external project manually. The project is private and is created only after confirmation.

1. POST /api/external-project-drafts with no request body. Returns 201 with a Ready draft and a Location header.
2. GET /api/external-project-drafts/{id} to resume the draft. Unconfirmed drafts expire after seven days.
3. PATCH /api/external-project-drafts/{id} with { "version": 1, "data": { ... } } to save the form. Use the version returned by the latest successful request.
4. POST /api/external-project-drafts/{id}/confirm with { "version": 2, "confirmSharingRights": true }. Confirmation is idempotent.

Required fields are title, description, projectField, specializationId, startDate, endDate, submitterRole, submitterContribution, submitterIsLeader and skillIds. Enter leaderName when the submitter is not the leader. Repository and demo URLs, features and declared members remain optional. Read missingFields before confirming.

The legacy files and evidenceFileIds arrays are empty and extraction is null. Existing drafts can still show previously saved or extracted project details in data.

Apply ArchiveAndRemoveExternalProjectFiles during the API rollout after stopping old worker instances. It archives the two attachment tables before dropping them and releases unexpired drafts waiting on extraction for manual editing. UploadedFiles remains shared by the general upload endpoint. The migration does not delete objects from Cloudinary.
