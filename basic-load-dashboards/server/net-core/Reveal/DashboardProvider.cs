using Reveal.Sdk;

namespace RevealSdk.Server.Reveal
{
    public class DashboardProvider : IRVDashboardProvider
    {
        public Task<Dashboard> GetDashboardAsync(IRVUserContext userContext, string dashboardId)
        {
            if (dashboardId == "HealthcareJson")
            { 
                var filePath = Path.Combine(Environment.CurrentDirectory, $"Dashboards/{dashboardId}.json");
                var json = File.ReadAllText(filePath);
                var dashboard = Dashboard.FromJsonString(json);
                return Task.FromResult(dashboard);
            }
            else
            {
                var filePath = Path.Combine(Environment.CurrentDirectory, $"Dashboards/{dashboardId}.rdash");
                var dashboard = new Dashboard(filePath);
                return Task.FromResult(dashboard);
            }
        }

        public Task SaveDashboardAsync(IRVUserContext userContext, string dashboardId, Dashboard dashboard)
        {
            throw new NotImplementedException();
        }
    }
}