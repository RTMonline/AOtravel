using System;
using System.IO;
using TravelPlugin;

class Program
{
    static int Main()
    {
        string path = Path.Combine(Path.GetTempPath(), "travel-config-test", "123", "Config.json");
        if (File.Exists(path)) File.Delete(path);
        var a = Config.Load(path, 123, 5, out bool created1);
        a.CharSettings[123].Slots[4].Kind = SlotKind.Item;
        a.CharSettings[123].Slots[4].Name = "Portable Gate";
        a.CharSettings[123].JoinIn = false;
        a.Save(out string err);
        var b = Config.Load(path, 123, 5, out bool created2);
        var s = b.CharSettings[123];
        bool ok = created1 && !created2 && s.Slots[4].Name == "Portable Gate" && s.Slots[4].Kind == SlotKind.Item && !s.JoinIn && s.Slots.Count == 5;
        Console.WriteLine(File.ReadAllText(path));
        Console.WriteLine("Default folder: " + Config.PathFor(123));
        Console.WriteLine(ok ? "PASS" : "FAIL " + err);
        return ok ? 0 : 1;
    }
}
