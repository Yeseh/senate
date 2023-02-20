using Pulumi;
using Pulumi.AzureNative.ContainerRegistry;
using Pulumi.AzureNative.ContainerRegistry.Inputs;
using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.Web;
using Pulumi.AzureNative.Web.Inputs;
using Pulumi.AzureNative.OperationalInsights;
using Pulumi.AzureNative.OperationalInsights.Inputs;
using System.Collections.Generic;
using Pulumi.AzureNative.App;

return await Pulumi.Deployment.RunAsync(() =>
{
    var tags = new Dictionary<string, string>()
    {
        { "Deployment", "Pulumi" },
    };

    var resourceGroup = new ResourceGroup("rg-senate");

    var logAnalytics = new Workspace("laws", new WorkspaceArgs
    {
        ResourceGroupName = resourceGroup.Name,
        Sku = new WorkspaceSkuArgs
        {
            Name = "PerGB2018"
        }
    });

    var workspaceKeys = Output.Tuple(resourceGroup.Name, logAnalytics.Name)
        .Apply(items => GetSharedKeys.InvokeAsync(new GetSharedKeysArgs
        {
            ResourceGroupName = items.Item1,
            WorkspaceName = items.Item2,
        }));

    //var env = new KubeEnvironment("kubeenv", new KubeEnvironmentArgs
    //{
    //    ResourceGroupName = resourceGroup.Name,
    //    InternalLoadBalancerEnabled = false,
         
    //    AppLogsConfiguration = new AppLogsConfigurationArgs
    //    {
    //        Destination = "log-analytics",
    //        LogAnalyticsConfiguration = new LogAnalyticsConfigurationArgs
    //        {
    //            CustomerId = logAnalytics.CustomerId,
    //            SharedKey = workspaceKeys.Apply(k => k.PrimarySharedKey!)
    //        }
    //    }
    //}); 
    
    var registry = new Registry("acr", new RegistryArgs
    {
        ResourceGroupName = resourceGroup.Name,
        Sku = new SkuArgs() {  Name = "Basic" },
        AdminUserEnabled = true,
    });

    var credentials = Output.Tuple(resourceGroup.Name, registry.Name)
        .Apply(items => ListRegistryCredentials.InvokeAsync(new()
        {
            ResourceGroupName = items.Item1,
            RegistryName = items.Item2,
        }));

    var adminUsername = credentials.Apply(c => c.Username);
    var adminPassword = credentials.Apply(c => c.Passwords[0].Value);

    //var portalImg = new Image("senate/portal", new ImageArgs
    //{
    //    ImageName = Output.Format($"{registry.LoginServer}/senate/portal:latest"),
    //    Registry = new ImageRegistry
    //    {
    //        Server = registry.LoginServer, 
    //        Username = adminUsername!,
    //        Password = adminPassword! 
    //    }
    //});
    
    //var apiImg = new Image("senate/api", new ImageArgs
    //{
    //    ImageName = Output.Format($"{registry.LoginServer}/senate/api:latest"),
    //    Registry = new ImageRegistry
    //    {
    //        Server = registry.LoginServer, 
    //        Username = adminUsername!,
    //        Password = adminPassword! 
    //    }
    //});

    var asp = new AppServicePlan("senate-asp", new AppServicePlanArgs()
    {
        Tags = tags,
        Reserved = true,
        Kind = "windows",
        ResourceGroupName = resourceGroup.Name,
        Sku = new SkuDescriptionArgs()
        {
            Name = "S1",
            Tier = "Standard"
        }
    });  

    var app = new WebApp("senate-portal", new WebAppArgs()
    {
        ResourceGroupName = resourceGroup.Name,
        ServerFarmId = asp.Id,
        HttpsOnly = true,
        SiteConfig= new SiteConfigArgs()
        { 
             Cors  = new CorsSettingsArgs()
             {
                AllowedOrigins = "*"
             },
             NetFrameworkVersion = "v7.0",
        }
    });
    
    var api = new WebApp("senate-api", new WebAppArgs()
    {
        ResourceGroupName = resourceGroup.Name,
        ServerFarmId = asp.Id,
        HttpsOnly = true,
        SiteConfig= new SiteConfigArgs()
        {
             Cors  = new CorsSettingsArgs()
             {
                AllowedOrigins = "*"
             },
             NetFrameworkVersion = "v7.0"
        }
    });

    return new Dictionary<string, object?>();
});