using System.Xml.Serialization;

namespace IndustrialProcessingSystem.Services.Configuration;

public static class XmlConfigReader
{
    private static readonly XmlSerializer Serializer = new(typeof(SystemConfig));

    public static SystemConfig Read(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Config file not found: {path}");

        using var stream = File.OpenRead(path);

        return Serializer.Deserialize(stream) as SystemConfig
            ?? throw new InvalidDataException("Failed to deserialize SystemConfig.xml.");
    }
}
