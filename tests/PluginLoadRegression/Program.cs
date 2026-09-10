using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

// Exercise the real compiled entry point, stopping before hooks, configuration writes or networking.
if (args.Length != 2) {
    Console.Error.WriteLine("Usage: PluginLoadRegression <XIVChatNext.dll> <Dalamud library directory>");
    return 2;
}
var pluginPath = Path.GetFullPath(args[0]);
var searchDirectories = new[] { Path.GetDirectoryName(pluginPath)!, Path.GetFullPath(args[1]) };
AssemblyLoadContext.Default.Resolving += (context, name) => {
    foreach (var directory in searchDirectories) {
        var dependency = Path.Combine(directory, name.Name + ".dll");
        if (File.Exists(dependency)) return context.LoadFromAssemblyPath(dependency);
    }
    return null;
};

try {
    var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(pluginPath);
    var pluginType = assembly.GetType("XIVChatPlugin.Plugin", throwOnError: true)!;
    var constructor = pluginType.GetConstructors().Single();
    var parameters = constructor.GetParameters();
    var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    var services = pluginType.GetProperties(flags)
        .Where(property => property.PropertyType.IsInterface &&
            (property.PropertyType.Namespace == "Dalamud.Plugin.Services" || property.PropertyType.Name == "IDalamudPluginInterface"))
        .ToArray();
    if (parameters.Length != services.Length || services.Length == 0)
        throw new Exception("Every constructor-used Dalamud service must be supplied as a constructor parameter.");
    var resolved = parameters.Select(parameter => DispatchProxy.Create(parameter.ParameterType, typeof(StopBeforeConfigurationProxy))).ToArray();

    // Dalamud allocates an uninitialized instance and then invokes its constructor on that instance.
    var instance = RuntimeHelpers.GetUninitializedObject(pluginType);
    try {
        ((MethodBase)constructor).Invoke(instance, resolved);
        throw new Exception("Constructor unexpectedly continued beyond configuration loading.");
    } catch (TargetInvocationException ex) when (ex.InnerException is ConfigurationReachedException) {
        Console.WriteLine("PASS Real plugin constructor reaches the injected configuration service without a null reference");
    }
    foreach (var property in services) {
        var parameterIndex = Array.FindIndex(parameters, parameter => parameter.ParameterType == property.PropertyType);
        if (parameterIndex < 0 || !ReferenceEquals(property.GetValue(instance), resolved[parameterIndex]))
            throw new Exception($"Service {property.Name} was not assigned before configuration loading.");
    }
    Console.WriteLine($"PASS All {services.Length} services survive constructor initialization");
    var clientStateType = services.Single(p => p.Name == "ClientState").PropertyType;
    var playerStateType = services.Single(p => p.Name == "PlayerState").PropertyType;
    var clientState = DispatchProxy.Create(clientStateType, typeof(LogoutStateProxy));
    var playerState = DispatchProxy.Create(playerStateType, typeof(LogoutStateProxy));
    pluginType.GetField("<ClientState>k__BackingField", flags)!.SetValue(instance, clientState);
    pluginType.GetField("<PlayerState>k__BackingField", flags)!.SetValue(instance, playerState);
    var serverType = assembly.GetType("XIVChatPlugin.Server", true)!;
    var server = RuntimeHelpers.GetUninitializedObject(serverType);
    serverType.GetField("_plugin", flags)!.SetValue(server, instance);
    var identity = serverType.GetMethod("CurrentIdentity", flags)!;
    var playerData = serverType.GetMethod("GeneratePlayerData", flags)!;
    foreach (var (loggedIn, loaded) in new[] { (false, true), (true, false), (true, true) }) {
        ((LogoutStateProxy)clientState).LoggedIn = loggedIn;
        ((LogoutStateProxy)playerState).Loaded = loaded;
        // Default RowRef has already been cleared, even though both loading flags may remain true.
        for (int frame = 0; frame < 100; frame++) {
            if (identity.Invoke(server, null) != null || playerData.Invoke(server, null) != null)
                throw new Exception("Cleared world reference retained an actionable player identity.");
        }
    }
    Console.WriteLine("PASS Real plugin identity and player-data readers tolerate cleared world rows for 300 logout frames");
    serverType.GetField("_loggedOut", flags)!.SetValue(server, true);
    ((LogoutStateProxy)clientState).ReadsForbidden = ((LogoutStateProxy)playerState).ReadsForbidden = true;
    if (identity.Invoke(server, null) != null || playerData.Invoke(server, null) != null)
        throw new Exception("Logout barrier did not clear identity.");
    Console.WriteLine("PASS Logout barrier avoids native service reads until the next login event");
    if (!File.Exists(Path.ChangeExtension(pluginPath, ".json"))) throw new Exception("Development manifest missing.");
    if (!File.Exists(Path.ChangeExtension(pluginPath, ".pdb"))) throw new Exception("Debug symbols missing.");
    Console.WriteLine("PASS Development manifest and debug symbols accompany the DLL");
    return 0;
} catch (Exception ex) {
    Console.Error.WriteLine(ex);
    return 1;
}

public sealed class ConfigurationReachedException : Exception;
public class StopBeforeConfigurationProxy : DispatchProxy {
    protected override object? Invoke(MethodInfo? method, object?[]? arguments) {
        if (method?.Name == "GetPluginConfig") throw new ConfigurationReachedException();
        throw new Exception($"Unexpected service call before configuration: {method?.Name}");
    }
}

public class LogoutStateProxy : DispatchProxy {
    public bool LoggedIn { get; set; } = true;
    public bool Loaded { get; set; } = true;
    public bool ReadsForbidden { get; set; }
    protected override object? Invoke(MethodInfo? method, object?[]? arguments) => ReadsForbidden
        ? throw new Exception("Native state was read after logout") : method?.Name switch {
        "get_IsLoggedIn" => LoggedIn,
        "get_IsLoaded" => Loaded,
        "get_ContentId" => (ulong)1,
        "get_CharacterName" => "Logout Test",
        "get_HomeWorld" => Activator.CreateInstance(method.ReturnType),
        _ => throw new Exception("Unexpected service read during logout: " + method?.Name),
    };
}
