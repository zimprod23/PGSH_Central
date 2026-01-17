namespace PGSH.Domain.Registrations;

public sealed record FailureReasons(string Description,List<string> Notes,bool Cheat = false);

