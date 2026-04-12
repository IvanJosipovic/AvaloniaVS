using System;

namespace AvaloniaVS;

internal static class Constants
{
    public const string PackageGuidString = "06a821f3-7a9f-4668-a6c5-037eba297c4c";
    public static readonly Guid PackageGuid = new (PackageGuidString);
    public const string PackageName = "Avalonia Declarative Preview";

    public const string AvaloviaFactoryEditorGuidString = @"6D5344A2-2FCD-49DE-A09D-6A14FD1B1224";
    public static readonly Guid AvaloviaFactoryEditorGuid = new (AvaloviaFactoryEditorGuidString);

    public const string AvaloniaCapability = nameof(Avalonia);
}
