using System.Windows.Media;

namespace Monolith.Models;

public sealed class AppItem
{
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required string TargetPath { get; init; }
    public string Arguments { get; init; } = string.Empty;
    public ImageSource? Icon { get; set; }
    public string Category { get; init; } = "Application";
    public bool IsEngineCommand { get; init; }
    public string EngineCommand { get; init; } = string.Empty;
    public object? EngineArgs { get; init; }
}

