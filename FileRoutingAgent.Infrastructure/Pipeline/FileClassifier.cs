using FileRoutingAgent.Core.Configuration;
using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Core.Interfaces;
using FileRoutingAgent.Infrastructure.Utilities;

namespace FileRoutingAgent.Infrastructure.Pipeline;

public sealed class FileClassifier : IFileClassifier
{
    public ClassifiedFile? Classify(StableFile file, FirmPolicy policy)
    {
        var extension = Path.GetExtension(file.SourcePath).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        if (!policy.ManagedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var matcher = new GlobMatcher(policy.IgnorePatterns.FileGlobs, policy.IgnorePatterns.FolderGlobs);
        if (matcher.IsFileIgnored(Path.GetFileName(file.SourcePath)))
        {
            return null;
        }

        var directoryPath = Path.GetDirectoryName(file.SourcePath) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(directoryPath) && matcher.IsFolderIgnored(directoryPath))
        {
            return null;
        }

        var category = extension switch
        {
            ".pdf" => FileCategory.Pdf,
            ".pset" => FileCategory.PlotSet,
            ".dgn" => FileCategory.Cad,
            ".dwg" => FileCategory.Cad,
            ".dxf" => FileCategory.Cad,
            _ => FileCategory.Unknown
        };

        if (category == FileCategory.Unknown)
        {
            return null;
        }

        return new ClassifiedFile(file, category, extension);
    }
}

