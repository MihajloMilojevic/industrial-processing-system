using System.Xml.Serialization;
using IndustrialProcessingSystem.Core.Enums;
using IndustrialProcessingSystem.Core.Models;

namespace IndustrialProcessingSystem.Services.Configuration;

public class JobConfig
{
    [XmlAttribute("Type")]
    public JobType Type { get; set; }

    [XmlAttribute("Payload")]
    public string Payload { get; set; } = string.Empty;

    [XmlAttribute("Priority")]
    public int Priority { get; set; }

    public Job ToJob() => new Job
    {
        Type = Type,
        Payload = Payload,
        Priority = Priority
    };
}
