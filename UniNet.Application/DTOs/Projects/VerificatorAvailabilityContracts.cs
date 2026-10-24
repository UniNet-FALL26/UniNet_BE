namespace UniNet.Application;

public record SaveVerificatorAvailabilityRequest(DateTimeOffset StartAt, DateTimeOffset EndAt);
public record VerificatorAvailabilityResponse(Guid Id, DateTimeOffset StartAt, DateTimeOffset EndAt);
