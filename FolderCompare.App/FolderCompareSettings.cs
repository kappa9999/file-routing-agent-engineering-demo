namespace FolderCompare.App;

public sealed record FolderCompareSettings
{
    public string PrimaryFolderPath { get; init; } = @"P:\1000_Software";
    public string ReferenceFolderPath { get; init; } = @"C:\Users\akiswani\Documents\SoftwareFolderCompare";
    public string OutputFolderPath { get; init; } = string.Empty;
    public DateTime CutoffDateLocal { get; init; } = new(2025, 4, 1);
    public bool OpenOutputOnSuccess { get; init; } = true;
}
