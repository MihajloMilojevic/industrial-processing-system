using System.Xml.Serialization;
using IndustrialProcessingSystem.Core.Enums;
using IndustrialProcessingSystem.Core.Models;

namespace IndustrialProcessingSystem.Services.Configuration;

[XmlRoot("SystemConfig")]
public class SystemConfig
{
    [XmlElement("WorkerCount")]
    public int WorkerCount { get; set; }

    [XmlElement("MaxQueueSize")]
    public int MaxQueueSize { get; set; }

    [XmlElement("JobTimeoutSeconds")]
    public double JobTimeoutSeconds { get; set; } = 2.0;

    [XmlElement("PrioritySkipThreshold")]
    public double PrioritySkipThreshold { get; set; } = 0.5;

    /// <summary>
    /// If true, the scheduler never skips a higher-priority job to run a lower-priority one —
    /// it blocks until enough slots are freed.
    /// If false, threshold-based skipping logic is used.
    /// Default: false.
    /// </summary>
    [XmlElement("StrictPriority")]
    public bool StrictPriority { get; set; } = false;

    [XmlArray("Jobs")]
    [XmlArrayItem("Job")]
    public List<JobConfig> Jobs { get; set; } = [];

    public TimeSpan JobTimeout => TimeSpan.FromSeconds(JobTimeoutSeconds);
    public TimeSpan SkipThreshold => TimeSpan.FromSeconds(JobTimeoutSeconds * PrioritySkipThreshold);
}
