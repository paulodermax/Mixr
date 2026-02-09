using System.Diagnostics;
namespace Mixr.Services;

public class ProcessWatcher {
    public event Action? OnWhitelistChanged;
    private HashSet<string> _known = new();
    private HashSet<string> _wl = new();

    // Whitelist wird beim Setzen komplett kleingeschrieben
    public void SetWhitelist(List<string> l) => _wl = l.Select(x => x.ToLower()).ToHashSet();

    public void Start() => Task.Run(async () => {
        bool debugListPrinted = true; 

        while (true) {
            var processes = Process.GetProcesses();
            
            if (!debugListPrinted)
            {
                LoggerService.Info("--- DEBUG: LISTE ALLER PROZESSE ---");
                foreach (var p in processes)
                {
                    if (!string.IsNullOrEmpty(p.MainWindowTitle)) 
                    {
                        LoggerService.Info($"Name: '{p.ProcessName}' | Titel: '{p.MainWindowTitle}'");
                    }
                }
                LoggerService.Info("--- DEBUG ENDE ---");
                debugListPrinted = true;
            }

            var curr = processes
                .Select(p => p.ProcessName.ToLower())
                .Where(n => _wl.Any(w => n.Contains(w))) 
                .ToHashSet();

            if (!curr.SetEquals(_known)) { _known = curr; OnWhitelistChanged?.Invoke(); }
            await Task.Delay(5000);
        }
    });
}