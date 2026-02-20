namespace FileRoutingAgent.Core.Domain;

public enum FileCategory
{
    Unknown = 0,
    Pdf = 1,
    Cad = 2,
    PlotSet = 3
}

public enum DetectionSource
{
    WatcherHint = 0,
    ReconciliationScan = 1
}

public enum ProposedAction
{
    None = 0,
    Move = 1,
    Copy = 2,
    PublishCopy = 3,
    Leave = 4
}

public enum ConflictChoice
{
    KeepBothVersioned = 0,
    Overwrite = 1,
    Cancel = 2
}

public enum PendingStatus
{
    Pending = 0,
    Processing = 1,
    Done = 2,
    Dismissed = 3,
    Error = 4
}

public enum RootAvailabilityState
{
    Available = 0,
    Unavailable = 1,
    Recovering = 2
}

public enum StructurePathStatus
{
    Exists = 0,
    Missing = 1,
    OutsideProjectRoot = 2,
    AccessDenied = 3,
    Invalid = 4
}
