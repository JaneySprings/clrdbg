namespace DotNet.Debugging.Engine.Models;

public class ThreadInfo {
    public int Id { get; }
    // The managed 'Thread.Name', null when the thread has none
    public string? Name { get; }
    public bool IsMain { get; }

    public ThreadInfo(int id, string? name, bool isMain) {
        Id = id;
        Name = name;
        IsMain = isMain;
    }
}
