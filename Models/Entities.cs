using System.ComponentModel.DataAnnotations;

namespace IISPSupdate.Models;

public enum ScanSource
{
    [Display(Name = "WSUS Server")]
    Wsus = 0,

    [Display(Name = "Scan and Install Windows Updates")]
    MicrosoftUpdate = 1,

    [Display(Name = "Offline Catalog")]
    OfflineCatalog = 2
}

public enum PreFlightStatus
{
    NotStarted = 0,
    Pending = 1,
    Success = 2,
    Failed = 3
}

public enum RunStatus
{
    Pending = 0,
    Queued = 1,
    Running = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5
}

public enum RunServerStatus
{
    Queued = 0,
    Downloading = 1,
    Installing = 2,
    RebootPending = 3,
    WaitingForWinRm = 4,
    Verifying = 5,
    Success = 6,
    Failed = 7
}

/// <summary>
/// High-level orchestration project grouping a set of servers and one or more runs.
/// </summary>
public class Project
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;

    public ScanSource ScanSource { get; set; } = ScanSource.MicrosoftUpdate;

    public ICollection<ProjectServer> Servers { get; set; } = new List<ProjectServer>();
    public ICollection<ProjectRun> Runs { get; set; } = new List<ProjectRun>();
}

/// <summary>
/// A target Windows server participating in a project.
/// </summary>
public class ProjectServer
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Fqdn { get; set; }
    public string? Environment { get; set; }

    public PreFlightStatus PreFlightStatus { get; set; } = PreFlightStatus.NotStarted;
    public string? PreFlightError { get; set; }

    public DateTime? LastScanAtUtc { get; set; }

    public Project? Project { get; set; }
    public ICollection<ServerScanResult> ScanResults { get; set; } = new List<ServerScanResult>();
}

/// <summary>
/// Result of a scan operation for a particular server within a project.
/// </summary>
public class ServerScanResult
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int ProjectServerId { get; set; }

    public DateTime ScanTimeUtc { get; set; } = DateTime.UtcNow;
    public ScanSource ScanSource { get; set; }
    public string? RawOutput { get; set; }

    public Project? Project { get; set; }
    public ProjectServer? ProjectServer { get; set; }
    public ICollection<UpdateCandidate> Candidates { get; set; } = new List<UpdateCandidate>();
}

/// <summary>
/// Individual update candidate discovered during a scan.
/// </summary>
public class UpdateCandidate
{
    public int Id { get; set; }
    public int ServerScanResultId { get; set; }

    public string KbNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Severity { get; set; }

    public bool IsSelectedByDefault { get; set; }

    public ServerScanResult? ServerScanResult { get; set; }
}

/// <summary>
/// Selection of KBs to install per server within a project.
/// </summary>
public class ServerSelection
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int ProjectServerId { get; set; }

    public string KbNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Category { get; set; }

    public Project? Project { get; set; }
    public ProjectServer? ProjectServer { get; set; }
}

/// <summary>
/// A single execution (run) of selected updates across servers in a project.
/// </summary>
public class ProjectRun
{
    public int Id { get; set; }
    public int ProjectId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;

    public RunStatus Status { get; set; } = RunStatus.Pending;
    public string? Error { get; set; }

    public int MaxConcurrency { get; set; } = 50;
    public bool AllowReboot { get; set; } = true;
    public bool OnlineOnly { get; set; } = true;
    public string? ErrorPolicy { get; set; }
    public DateTime? ScheduledForUtc { get; set; }

    public string? HangfireJobId { get; set; }

    public Project? Project { get; set; }
    public ICollection<ProjectRunServer> Servers { get; set; } = new List<ProjectRunServer>();
}

/// <summary>
/// Per-server status within a run, including live state and log tail.
/// </summary>
public class ProjectRunServer
{
    public int Id { get; set; }
    public int ProjectRunId { get; set; }
    public int ProjectServerId { get; set; }

    public RunServerStatus Status { get; set; } = RunServerStatus.Queued;
    public DateTime? LastUpdatedUtc { get; set; }

    public string? LastError { get; set; }
    public string? LogPath { get; set; }
    public string? LogTail { get; set; }

    public ProjectRun? ProjectRun { get; set; }
    public ProjectServer? ProjectServer { get; set; }
}

