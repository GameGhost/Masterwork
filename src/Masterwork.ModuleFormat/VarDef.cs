namespace Masterwork.ModuleFormat;

public class VarDef
{
    public string Name { get; set; } = "";
    public string VarType { get; set; } = "string";  // int | string | array
    public object? Default { get; set; }
    public bool IsStandard { get; set; }  // from GLOBALS.cs (nameA-E, townname, playerCount...)
}
