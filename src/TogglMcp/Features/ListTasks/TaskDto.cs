namespace TogglMcp.Features.ListTasks;

public sealed record TaskDto(long Id, string Name, long ProjectId, bool Active);
