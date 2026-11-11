# Career profile project references

No schema migration or new table is required. CareerProfile.ProjectsJson now accepts nullable projectId. Existing manual projects without projectId remain supported.

## Select projects

GET /api/profile/portfolio/me/eligible-projects (Student authentication).
Returns completed projects created by the current student or having their Active ProjectMember record. Every active member can select the same project independently. Left/Kicked members are excluded.

## Save, reorder, remove

Use existing PUT /api/profile/portfolio/me or PUT /api/profile/portfolio/editor/me. The projects array replaces the current list; include all items to retain. Array order is display order. Remove an item from this array to unlink it.

Example portfolio payload:

```json
{
  "projects": [
    {
      "projectId": "00000000-0000-0000-0000-000000000001",
      "personalContribution": "Developed recruitment APIs",
      "featured": true
    }
  ]
}
```

Only projectId, personalContribution and featured are retained for linked entries. Server resolves title, description, type, technologies, role and completedAt from source tables on each read. Duplicate, empty, missing, non-completed and unauthorized references are rejected. No verification pass is required. verification is null until a finalized verification exists; otherwise it contains id, status and finalizedAt of the latest finalized case (including failed outcomes). No internal evidence or reports are exposed.

Owner, visitor and job application snapshots all resolve references before applying existing portfolio visibility settings. References whose project is no longer completed/eligible are omitted from responses. Existing project visibility is not modified; publishing a career profile exposes only the project summary fields above.

Frontend must retain projectId when round-tripping an existing linked entry. Do not render a verification badge when verification is null; distinguish Verified from other returned statuses.