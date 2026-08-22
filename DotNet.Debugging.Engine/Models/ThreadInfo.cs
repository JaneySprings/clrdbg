namespace DotNet.Debugging.Engine.Models;

public class ThreadInfo {
    public int Id { get; }
    // The managed 'Thread.Name', or the OS-level thread name for threads other than the main one; null when there is neither
    public string? Name { get; }
    public bool IsMain { get; }

    public ThreadInfo(int id, string? name, bool isMain) {
        Id = id;
        Name = name;
        IsMain = isMain;
    }
}
