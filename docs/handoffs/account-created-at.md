# Account creation date in auth responses

`AccountResponse` now exposes `createdAt` from `Account.CreatedAt` through `AuthMapping.ToResponse`. This is an additive response property; no migration, request contract or authentication behavior changes. Login, refresh and current-account responses share this mapping. The frontend uses it for the profile verification reminder, counting Vietnam calendar days; missing timestamps keep the reminder hidden.

Validation: backend build and `AccountRegistrationDateTests` mapping/JSON assertions pass. Broader auth run: 10 passed, 3 failed in existing partner registration name/nickname expectations, unrelated to the added response field.
