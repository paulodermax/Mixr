using System.Diagnostics;
namespace Mixr.Services;
public class ProcessWatcher {
    public event Action? OnWhitelistChanged;
    private HashSet<string> _known = new();
    private HashSet<string> _wl = new();
    public void SetWhitelist(List<string> l) => _wl = l.Select(x => x.ToLower()).ToHashSet();
    public void Start() => Task.Run(async () => {
        while (true) {
            var curr = Process.GetProcesses().Select(p => p.ProcessName.ToLower()).Where(n => _wl.Contains(n)).ToHashSet();
            if (!curr.SetEquals(_known)) { _known = curr; OnWhitelistChanged?.Invoke(); }
            await Task.Delay(5000);
        }
    });
}