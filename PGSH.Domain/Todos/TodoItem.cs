using PGSH.SharedKernel;

namespace PGSH.Domain.Todos;

public sealed class TodoItem : Entity
{
    public Guid Id { get; set; }
    //public Guid UserId { get; set; }
    public string Description { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? StartDate { get; set; }
    public List<string> Labels { get; set; } = [];
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Priority Priority { get; set; }
}
