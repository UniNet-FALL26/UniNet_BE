namespace UniNet.Domain;

// Persisted contract; old staff values are remapped by the role migration.
public enum AccountRole : short
{
    Student = 0, Partner = 1, Assessor = 2, Verifier = 3, Moderator = 4, Accountant = 5, Admin = 6
}
