namespace FileRoutingAgent.App.Services;

public sealed class AutomationPromptOptions
{
    public bool Enabled { get; set; }
    public string DefaultAction { get; set; } = "move";
    public string DefaultPdfCategory { get; set; } = "progress_print";
}

