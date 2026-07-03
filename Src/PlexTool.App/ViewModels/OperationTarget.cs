namespace PlexTool.App.ViewModels;

/// <summary>Where an operation runs: against the remote storage box, or the local filesystem.</summary>
public enum OperationTarget
{
    Server,
    Local,
}
