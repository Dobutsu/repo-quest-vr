namespace RepoQuest.Builder.Pipeline;

/// <summary>.meta files of the plugins the pipeline adds, byte for byte as the bash pipeline wrote them ({GUID} is
/// replaced); the GUIDs are MD5 hashes of fixed strings, so the project and its Library stay stable</summary>
internal static class PluginMetas
{
    public const string RepoXRPlugin = """
        fileFormatVersion: 2
        guid: {GUID}
        PluginImporter:
          externalObjects: {}
          serializedVersion: 2
          iconMap: {}
          executionOrder: {}
          defineConstraints: []
          isPreloaded: 0
          isOverridable: 0
          isExplicitlyReferenced: 1
          validateReferences: 0
          platformData:
          - first:
              : Any
            second:
              enabled: 0
              settings:
                Exclude Android: 0
                Exclude Editor: 1
                Exclude Linux64: 1
                Exclude OSXUniversal: 1
                Exclude Win: 1
                Exclude Win64: 1
          - first:
              Android: Android
            second:
              enabled: 1
              settings:
                CPU: ARM64
          - first:
              Any:
            second:
              enabled: 0
              settings: {}
          - first:
              Editor: Editor
            second:
              enabled: 0
              settings:
                CPU: AnyCPU
                DefaultValueInitialized: true
                OS: AnyOS
          userData:
          assetBundleName:
          assetBundleVariant:
        """;

    public const string SteamKitPlugin = """
        fileFormatVersion: 2
        guid: {GUID}
        PluginImporter:
          externalObjects: {}
          serializedVersion: 2
          iconMap: {}
          executionOrder: {}
          defineConstraints: []
          isPreloaded: 0
          isOverridable: 0
          isExplicitlyReferenced: 1
          validateReferences: 0
          platformData:
          - first:
              : Any
            second:
              enabled: 0
              settings:
                Exclude Android: 0
                Exclude Editor: 1
                Exclude Linux64: 1
                Exclude OSXUniversal: 1
                Exclude Win: 1
                Exclude Win64: 1
          - first:
              Android: Android
            second:
              enabled: 1
              settings:
                CPU: ARM64
          - first:
              Editor: Editor
            second:
              enabled: 0
              settings:
                CPU: AnyCPU
                DefaultValueInitialized: true
                OS: AnyOS
          userData:
          assetBundleName:
          assetBundleVariant:
        """;

    public const string OpusPlugin = """
        fileFormatVersion: 2
        guid: {GUID}
        PluginImporter:
          externalObjects: {}
          serializedVersion: 2
          iconMap: {}
          executionOrder: {}
          defineConstraints: []
          isPreloaded: 0
          isOverridable: 0
          isExplicitlyReferenced: 0
          validateReferences: 1
          platformData:
          - first:
              : Any
            second:
              enabled: 0
              settings: {}
          - first:
              Android: Android
            second:
              enabled: 1
              settings:
                CPU: ARM64
          - first:
              Editor: Editor
            second:
              enabled: 0
              settings:
                DefaultValueInitialized: true
          userData:
          assetBundleName:
          assetBundleVariant:
        """;
}
