using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using System.Text.Json;
using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Load API settings with reload on change
builder.Configuration.AddJsonFile("api-settings.json", optional: false, reloadOnChange: true);

// Add OpenAPI/Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "Burma Project Ideas API Gateway",
        Version = "v1",
        Description = "YARP Reverse Proxy Gateway for Burma Project Ideas APIs"
    });
});

// Custom proxy config provider that supports runtime enable/disable
builder.Services.AddSingleton<IProxyConfigProvider, DynamicProxyConfigProvider>();

// Add YARP services
builder.Services.AddReverseProxy();

var app = builder.Build();

// Initialize the dynamic config provider
var configProvider = app.Services.GetRequiredService<IProxyConfigProvider>() as DynamicProxyConfigProvider;
configProvider?.Initialize(builder.Configuration);

// Watch for changes in api-settings.json
var fileWatcher = new FileSystemWatcher(Directory.GetCurrentDirectory(), "api-settings.json")
{
    EnableRaisingEvents = true,
    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
};

fileWatcher.Changed += (sender, e) =>
{
    try
    {
        Thread.Sleep(100); // Wait for file to be fully written
        // Reload configuration by rebuilding the configuration root
        var configRoot = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile("api-settings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();
        
        configProvider?.Reload(configRoot);
        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] API configuration reloaded");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Error reloading configuration: {ex.Message}");
    }
};

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();

// Add Swagger
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Burma Project Ideas API Gateway v1");
    c.RoutePrefix = "swagger";
});

// Endpoint to get YARP configuration (routes and clusters)
app.MapGet("/api/gateway/config", () =>
{
    var proxyConfig = configProvider?.GetConfig();
    if (proxyConfig == null)
    {
        return Results.NotFound(new { message = "YARP configuration not available" });
    }

    var routes = proxyConfig.Routes.Select(r => new
    {
        routeId = r.RouteId,
        clusterId = r.ClusterId,
        path = r.Match?.Path,
        order = r.Order,
        metadata = r.Metadata,
        transforms = r.Transforms
    }).ToList();

    var clusters = proxyConfig.Clusters.Select(c => new
    {
        clusterId = c.ClusterId,
        destinations = c.Destinations?.Select(d => new
        {
            name = d.Key,
            address = d.Value.Address,
            health = d.Value.Health,
            metadata = d.Value.Metadata
        }).ToList(),
        loadBalancingPolicy = c.LoadBalancingPolicy,
        sessionAffinity = c.SessionAffinity?.Policy,
        healthCheck = c.HealthCheck,
        httpClient = c.HttpClient,
        metadata = c.Metadata
    }).ToList();

    return Results.Ok(new
    {
        routes = routes,
        clusters = clusters,
        routeCount = routes.Count,
        clusterCount = clusters.Count,
        lastUpdated = DateTime.UtcNow
    });
})
.WithName("GetGatewayConfig")
.WithTags("Gateway")
.Produces(200)
.Produces(404);

// Endpoint to get enabled APIs status
app.MapGet("/api/gateway/status", () =>
{
    var enabledApis = builder.Configuration.GetSection("EnabledApis")
        .GetChildren()
        .ToDictionary(x => x.Key, x => x.Get<bool>());

    var apiConfigMap = new Dictionary<string, string>
    {
        { "burma_calendar", "api-burma-calendar-routes.json" },
        { "burmese_recipes", "api-burmese-recipes-routes.json" },
        { "movie_ticket_online_booking_system", "api-movie-ticket-online-booking-system-routes.json" },
        { "snake", "api-snake-routes.json" }
    };

    var apiStatus = apiConfigMap.Select(kvp => new
    {
        apiKey = kvp.Key,
        apiName = kvp.Key.Replace("_", " ").ToTitleCase(),
        configFile = kvp.Value,
        enabled = enabledApis.TryGetValue(kvp.Key, out var enabled) && enabled,
        fileExists = File.Exists(kvp.Value)
    }).ToList();

    return Results.Ok(new
    {
        apis = apiStatus,
        totalApis = apiStatus.Count,
        enabledCount = apiStatus.Count(a => a.enabled),
        disabledCount = apiStatus.Count(a => !a.enabled)
    });
})
.WithName("GetGatewayStatus")
.WithTags("Gateway")
.Produces(200);

// Endpoint to get routes for a specific API
app.MapGet("/api/gateway/routes/{apiKey}", (string apiKey) =>
{
    var apiConfigMap = new Dictionary<string, string>
    {
        { "burma_calendar", "api-burma-calendar-routes.json" },
        { "burmese_recipes", "api-burmese-recipes-routes.json" },
        { "movie_ticket_online_booking_system", "api-movie-ticket-online-booking-system-routes.json" },
        { "snake", "api-snake-routes.json" }
    };

    if (!apiConfigMap.TryGetValue(apiKey, out var configFile))
    {
        return Results.NotFound(new { message = $"API key '{apiKey}' not found" });
    }

    if (!File.Exists(configFile))
    {
        return Results.NotFound(new { message = $"Configuration file '{configFile}' not found" });
    }

    try
    {
        var json = File.ReadAllText(configFile);
        var config = JsonSerializer.Deserialize<JsonElement>(json);

        return Results.Ok(new
        {
            apiKey = apiKey,
            configFile = configFile,
            configuration = config
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = $"Error reading configuration: {ex.Message}" });
    }
})
.WithName("GetApiRoutes")
.WithTags("Gateway")
.Produces(200)
.Produces(404)
.Produces(400);

app.MapReverseProxy();

app.Run();

// Extension method for title case
public static class StringExtensions
{
    public static string ToTitleCase(this string str)
    {
        if (string.IsNullOrEmpty(str))
            return str;

        return string.Join(" ", str.Split('_')
            .Select(word => char.ToUpper(word[0]) + word.Substring(1).ToLower()));
    }
}

// Dynamic Proxy Config Provider
public class DynamicProxyConfigProvider : IProxyConfigProvider
{
    private volatile IProxyConfig _config = new MemoryConfig(new List<RouteConfig>(), new List<ClusterConfig>());
    private IConfiguration? _configuration;
    private readonly object _lockObject = new object();
    private CancellationTokenSource _changeTokenSource = new CancellationTokenSource();

    // Map of API keys to their config file names
    private static readonly Dictionary<string, string> ApiConfigMap = new()
    {
        { "burma_calendar", "api-burma-calendar-routes.json" },
        { "burmese_recipes", "api-burmese-recipes-routes.json" },
        { "movie_ticket_online_booking_system", "api-movie-ticket-online-booking-system-routes.json" },
        { "snake", "api-snake-routes.json" }
    };

    public void Initialize(IConfiguration configuration)
    {
        _configuration = configuration;
        Reload(configuration);
    }

    public IProxyConfig GetConfig() => _config;

    public void Reload(IConfiguration? configuration = null)
    {
        lock (_lockObject)
        {
            var configToUse = configuration ?? _configuration;
            if (configToUse == null) return;

            var allRoutes = new List<RouteConfig>();
            var allClusters = new List<ClusterConfig>();

            var enabledApis = configToUse.GetSection("EnabledApis")
                .GetChildren()
                .ToDictionary(x => x.Key, x => x.Get<bool>());

            foreach (var apiConfig in ApiConfigMap)
            {
                if (enabledApis.TryGetValue(apiConfig.Key, out var enabled) && enabled)
                {
                    var configFile = apiConfig.Value;
                    if (File.Exists(configFile))
                    {
                        var json = File.ReadAllText(configFile);
                        var config = JsonSerializer.Deserialize<JsonElement>(json);

                        if (config.TryGetProperty("ReverseProxy", out var reverseProxy))
                        {
                            // Load routes
                            if (reverseProxy.TryGetProperty("Routes", out var routes))
                            {
                                foreach (var route in routes.EnumerateObject())
                                {
                                    var routeConfig = LoadRouteConfig(route.Name, route.Value);
                                    if (routeConfig != null)
                                    {
                                        allRoutes.Add(routeConfig);
                                    }
                                }
                            }

                            // Load clusters
                            if (reverseProxy.TryGetProperty("Clusters", out var clusters))
                            {
                                foreach (var cluster in clusters.EnumerateObject())
                                {
                                    var clusterConfig = LoadClusterConfig(cluster.Name, cluster.Value);
                                    if (clusterConfig != null)
                                    {
                                        allClusters.Add(clusterConfig);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Cancel previous token and create new one
            var oldCts = _changeTokenSource;
            _changeTokenSource = new CancellationTokenSource();
            oldCts?.Cancel();
            
            _config = new MemoryConfig(allRoutes, allClusters, _changeTokenSource.Token);
        }
    }

    private RouteConfig? LoadRouteConfig(string routeId, JsonElement routeElement)
    {
        try
        {
            var clusterId = routeElement.GetProperty("ClusterId").GetString() ?? "";
            var match = routeElement.GetProperty("Match");
            var path = match.GetProperty("Path").GetString() ?? "";

            var transforms = new List<IReadOnlyDictionary<string, string>>();
            if (routeElement.TryGetProperty("Transforms", out var transformsElement))
            {
                foreach (var transform in transformsElement.EnumerateArray())
                {
                    var transformDict = new Dictionary<string, string>();
                    foreach (var prop in transform.EnumerateObject())
                    {
                        transformDict[prop.Name] = prop.Value.GetString() ?? "";
                    }
                    transforms.Add(transformDict);
                }
            }

            return new RouteConfig
            {
                RouteId = routeId,
                ClusterId = clusterId,
                Match = new RouteMatch { Path = path },
                Transforms = transforms
            };
        }
        catch
        {
            return null;
        }
    }

    private ClusterConfig? LoadClusterConfig(string clusterId, JsonElement clusterElement)
    {
        try
        {
            var destinations = new Dictionary<string, DestinationConfig>();
            if (clusterElement.TryGetProperty("Destinations", out var destinationsElement))
            {
                foreach (var dest in destinationsElement.EnumerateObject())
                {
                    var address = dest.Value.GetProperty("Address").GetString() ?? "";
                    destinations[dest.Name] = new DestinationConfig { Address = address };
                }
            }

            return new ClusterConfig
            {
                ClusterId = clusterId,
                Destinations = destinations
            };
        }
        catch
        {
            return null;
        }
    }
}

// Simple in-memory config implementation
public class MemoryConfig : IProxyConfig
{
    public IReadOnlyList<RouteConfig> Routes { get; }
    public IReadOnlyList<ClusterConfig> Clusters { get; }
    public IChangeToken ChangeToken { get; }

    private readonly CancellationTokenSource _tokenSource;

    public MemoryConfig(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters, CancellationToken cancellationToken = default)
    {
        Routes = routes;
        Clusters = clusters;
        _tokenSource = cancellationToken != default 
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken) 
            : new CancellationTokenSource();
        ChangeToken = new CancellationChangeToken(_tokenSource.Token);
    }
}
