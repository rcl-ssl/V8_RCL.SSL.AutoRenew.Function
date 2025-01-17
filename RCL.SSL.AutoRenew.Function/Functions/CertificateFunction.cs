#nullable disable

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RCL.SSL.SDK;
using System.Net;

namespace RCL.SSL.AutoRenew.Function
{
    public class CertificateFunction
    {
        private readonly ILogger<CertificateFunction> _logger;
        private readonly IAzureAccessTokenService _azureAccessTokenService;
        private readonly ICertificateService _certificateService;
        private readonly IOptions<CertificateOptions> _certificateOptions;

        public CertificateFunction(ILogger<CertificateFunction> logger,
            IAzureAccessTokenService azureAccessTokenService,
            ICertificateService certificateService,
            IOptions<CertificateOptions> certificateOptions)
        {
            _logger = logger;
            _azureAccessTokenService = azureAccessTokenService;
            _certificateService = certificateService;
            _certificateOptions = certificateOptions;
        }

        [Function("Certificate-Schedule-Renewal-Manual")]
        public async Task<HttpResponseData> RunCertificateRenewManual([HttpTrigger(AuthorizationLevel.Function, "get", Route = "certificate/schedule/renewal/manual/certificatename/{certificatename}")] HttpRequestData req,
            string certificatename)
        {
            if (!string.IsNullOrEmpty(certificatename))
            {
                _logger.LogInformation($"INFO : Scheduling certificate : {certificatename} to manually renew ...");

                try
                {
                    await SheduleCertificateRenewalAsync(certificatename);

                    string message = $"SUCCESS : {certificatename} was scheduled for renewal. Wait a few minutes and verify that the certificate was renewed in the RCL SSL Portal.";

                    _logger.LogInformation(message);

                    var response = req.CreateResponse(HttpStatusCode.OK);
                    response.Headers.Add("Content-Type", "text/plain ; charset=utf-8");
                    response.WriteString(message);

                    return response;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"ERROR : {ex.Message}");

                    var response = req.CreateResponse(HttpStatusCode.BadRequest);
                    response.Headers.Add("Content-Type", "text/plain ; charset=utf-8");
                    response.WriteString($"ERROR : {ex.Message}");

                    return response;
                }
            }
            else
            {
                string messageCertificateNotFound = "ERROR : Certificate name was not found in request parameters";

                _logger.LogInformation(messageCertificateNotFound);

                var responseCertificateNotFound = req.CreateResponse(HttpStatusCode.BadRequest);
                responseCertificateNotFound.Headers.Add("Content-Type", "text/plain ; charset=utf-8");
                responseCertificateNotFound.WriteString(messageCertificateNotFound);

                return responseCertificateNotFound;
            }
        }

        [Function("Certificate-Schedule-Renewal-Automatic")]
        public async Task RunCertificateRenewAutomatic([TimerTrigger("%CRON_EXPRESSION%")] MyInfo myTimer)
        {
            if(string.IsNullOrEmpty(_certificateOptions.Value.CertificatesToRenew))
            {
                _logger.LogError("ERROR: Certificates to renew were not found in the Certificate configuration");
                return;
            }

            List<string> certificateNames = _certificateOptions.Value.CertificatesToRenew.Split(';').ToList();

            if (certificateNames.Count > 0)
            {
                foreach (string certificateName in certificateNames)
                {
                    try
                    {
                        await SheduleCertificateRenewalAsync(certificateName,true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"{ex.Message}");
                    }
                }
            }
        }

        private async Task SheduleCertificateRenewalAsync(string certificateName, bool checkExpiry = false)
        {
            try
            {
                var certificate = await _certificateService.CertificateGetAsync(certificateName);

                if (string.IsNullOrEmpty(certificate?.certificateName))
                {
                    throw new Exception($"ERROR: Certificate : {certificateName} was not found");
                }

                if (certificate.target == RCLSSLAPIConstants.targetStandAlone)
                {
                    throw new Exception("ERROR: Stand Alone Certificates cannot be automatically renewed with the AutoRenew Function");
                }

                if(checkExpiry == true)
                {
                    DateTime.TryParse(certificate.expiryDate, out var expiryDate);

                    if(expiryDate.AddDays(-30) > DateTime.Now)
                    {
                        _logger.LogInformation($"INFO: Certificate : {certificate.certificateName} is uptodate");
                        return;
                    }
                }

                string tokenError = "ERROR: Could not get access token, please ensure that you properly configured the Microsoft Entra App Credentials";

                var token = await _azureAccessTokenService.GetTokenAsync(RCLSSLAPIConstants.azureResource);

                if (string.IsNullOrEmpty(token?.access_token))
                {
                    throw new Exception(tokenError);
                }

                certificate.accessToken = token.access_token;

                if (certificate.target == RCLSSLAPIConstants.targetAzureKeyVaultDNS)
                {
                    var keyVaultToken = await _azureAccessTokenService.GetTokenAsync(RCLSSLAPIConstants.keyVaultResource);

                    if (string.IsNullOrEmpty(keyVaultToken?.access_token))
                    {
                        throw new Exception(tokenError);
                    }

                    certificate.accessTokenKeyVault = keyVaultToken.access_token;
                }

                await _certificateService.CertificateScheduleRenewAsync(certificate);

                _logger.LogInformation($"INFO: Scheduled certificate : {certificateName} for renewal");

            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }
    }

    public class MyInfo
    {
        public MyScheduleStatus ScheduleStatus { get; set; }

        public bool IsPastDue { get; set; }
    }

    public class MyScheduleStatus
    {
        public DateTime Last { get; set; }

        public DateTime Next { get; set; }

        public DateTime LastUpdated { get; set; }
    }
}
