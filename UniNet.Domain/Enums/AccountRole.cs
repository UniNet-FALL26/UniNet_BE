namespace UniNet.Domain;

// Persisted values are shared by authorization, API contracts and the mobile client.
public enum AccountRole : short { Student = 0, Partner = 1, Admin = 2 }
