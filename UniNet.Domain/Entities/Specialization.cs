using UniNet.Domain;

namespace UniNet.Domain.Entities;

public sealed class Specialization
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed class AssessorSpecialization
{
    public Guid AssessorUserId { get; set; }
    public UserProfile Assessor { get; set; } = null!;
    public Guid SpecializationId { get; set; }
    public Specialization Specialization { get; set; } = null!;
}
