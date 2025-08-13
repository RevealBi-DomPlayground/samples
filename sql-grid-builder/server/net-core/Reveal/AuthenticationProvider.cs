using Microsoft.Extensions.Configuration;
using Reveal.Sdk;
using Reveal.Sdk.Data;
using Reveal.Sdk.Data.Microsoft.SqlServer;

namespace RevealSdk.Server.Reveal
{
    public class AuthenticationProvider : IRVAuthenticationProvider
    {
        private readonly IConfiguration _configuration;
        
        public AuthenticationProvider(IConfiguration configuration)
        {
            _configuration = configuration;
        }
        
        public Task<IRVDataSourceCredential> ResolveCredentialsAsync(IRVUserContext userContext,
            RVDashboardDataSource dataSource)
        {        
            IRVDataSourceCredential userCredential = new RVUsernamePasswordDataSourceCredential();
            
            if (dataSource is RVSqlServerDataSource)
            {
                var username = _configuration["DatabaseSettings:Username"];
                var password = _configuration["DatabaseSettings:Password"];
                
                // Use configuration values if available, otherwise fallback to default values
                if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                {
                    userCredential = new RVUsernamePasswordDataSourceCredential(username, password);
                }
                else
                {
                    // Fallback to hardcoded values as a safety measure
                    userCredential = new RVUsernamePasswordDataSourceCredential("jasonberes", "=RevealJasonSdk09");
                }
            }
            return Task.FromResult(userCredential);
        }
    }
}

