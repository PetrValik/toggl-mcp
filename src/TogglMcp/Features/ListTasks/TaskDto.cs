namespace TogglMcp.Features.ListTasks;

/// <summary>
/// Slice-local projection of a Toggl task returned to the MCP client. Trimmed to the
/// fields the model needs to pick a "Project • Task" target, decoupling the tool's
/// output contract from the raw Toggl API model.
/// </summary>
/// <param name="Name">Task label (the "Task" half of a "Project • Task" entry).</param>
/// <param name="ProjectId">Owning project — echoed back so the client can pair task_id with its project_id.</param>
/// <param name="Active">False for archived/inactive tasks that can't take new time entries.</param>
public sealed record TaskDto(long Id, string Name, long ProjectId, bool Active);
