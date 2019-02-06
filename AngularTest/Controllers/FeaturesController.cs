﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using AngularTest.Models.AODB;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using AngularTest.Models;
using AngularTest.Models.Import;
using System.Diagnostics;

namespace AngularTest.Controllers
{
    [Route("api/[controller]")]
    public class FeaturesController : Controller
    {
        private readonly AODBContext Context;
        private readonly ImportContext ImportContext;
        public IConfiguration Configuration { get; }

        public FeaturesController(AODBContext context, ImportContext importContext, IConfiguration configuration)
        {
            this.Context = context;
            this.ImportContext = importContext;
            this.Configuration = configuration;
        }

        [HttpGet("[action]")]
        public IEnumerable<FeatureModel> GetFeatures(string serverName)
        {
            var features = Context.Feature.OrderBy(f => f.Name).Select(f => new FeatureModel
            {
                FeatureId = f.FeatureId,
                Name = f.Name,
                Active = f.Active
            }).ToList();

            return features;
        }

        [HttpGet("[action]")]
        public IEnumerable<FeatureClientModel> GetFeatureClients(int featureId)
        {
            var featureClients = (from f in Context.FeatureClient
                                  from c in Context.Client
                                  where c.ClientId == f.ClientId 
                                    && f.FeatureId == featureId
                                    && c.DeactivateDateTime == null
                                  select new FeatureClientModel
                                  {
                                      FeatureId = f.FeatureId,
                                      ClientId = f.ClientId,
                                      CompanyName = c.CompanyName,
                                      Active = f.Active
                                  }).ToList().OrderBy(fc => fc.CompanyName);

            return featureClients;
        }

        [HttpGet("[action]")]
        public IEnumerable<ClientModel> GetClientsAvailableForFeature(int featureId)
        {
            var featureClientIds = from fc in Context.FeatureClient
                                   where fc.FeatureId == featureId
                                   select fc.ClientId;

            var clients = (from c in Context.Client
                                  where !featureClientIds.Contains(c.ClientId)
                                    && c.DeactivateDateTime == null
                                  select new ClientModel
                                  {
                                      ClientId = c.ClientId,
                                      CompanyName = c.CompanyName
                                  }).ToList().OrderBy(fc => fc.CompanyName);

            return clients;
        }

        [HttpGet("[action]")]
        public void ActivateFeatureForClient(int featureId, int clientID)
        {
            
            var featureClient = Context.FeatureClient.Where(fc => fc.FeatureId == featureId && fc.ClientId == clientID).FirstOrDefault();

            if (featureClient != null)
            {
                featureClient.Active = true;
                Context.SaveChanges();
            }                    
        }

        [HttpGet("[action]")]
        public void DeactivateFeatureForClient(int featureId, int clientID)
        {
            
            var featureClient = Context.FeatureClient.Where(fc => fc.FeatureId == featureId && fc.ClientId == clientID).FirstOrDefault();

            if (featureClient != null)
            {
                featureClient.Active = false;
                Context.SaveChanges();
            }
        }

        [HttpGet("[action]")]
        public void AddClientToFeature(int featureId, int clientID)
        {

            var newFeatureClient = new FeatureClient()
            {
                FeatureId = featureId,
                ClientId = clientID,
                Active = true,
                CreateDateTime = DateTime.UtcNow,
                CreateUser = ""
            };

            Context.FeatureClient.Add(newFeatureClient);
            Context.SaveChanges();
        }

        [HttpGet("[action]")]
        public void RemoveClientFromFeature(int featureId, int clientID)
        {

            var featureClientToRemove = Context.FeatureClient.Where(fc => fc.FeatureId == featureId && fc.ClientId == clientID).FirstOrDefault();

            if (featureClientToRemove != null)
            {
                Context.FeatureClient.Remove(featureClientToRemove);
                Context.SaveChanges();
            }
        }

        [HttpGet("[action]")]
        public IEnumerable<FeatureFlagModel> GetFeatureFlags(int featureId)
        {
            var featureFlags = from fc in Context.FeatureConfig
                               from ff in Context.FeatureFlag
                               where fc.FeatureId == featureId
                               && fc.FeatureFlagId == ff.FeatureFlagId
                               select new FeatureFlagModel
                               {
                                   FeatureId = fc.FeatureId,
                                   FeatureFlagId = fc.FeatureFlagId,
                                   FlagName = ff.FlagName,
                                   FlagValue = fc.FeatureFlagValue
                               };

            return featureFlags;
        }

        [HttpGet("[action]")]
        public IEnumerable<ClientConfigurationAttributeModel> GetClientConfigurationAttributes(int featureId, int clientId)
        {
            var clientConfigurationAttributes = (from c in Context.ClientConfigurationAttribute
                          join fca in Context.FeatureConfigurationAttribute on c.ConfigurationAttributeId equals fca.ConfigurationAttributeId
                          join f in Context.Feature on fca.FeatureId equals f.FeatureId
                          where f.FeatureId == featureId && c.ClientId == clientId
                          select new ClientConfigurationAttributeModel
                          {
                              ClientConfigurationAttributeId = c.ClientConfigurationAttributeId,
                              ClientId = c.ClientId,
                              FeatureId = f.FeatureId,
                              ConfigurationAttributeId = c.ConfigurationAttributeId,
                              Description = c.ConfigurationAttribute.Description,
                              Value = c.Value
                          });

            return clientConfigurationAttributes;
        }

        [HttpGet("[action]")]
        public IEnumerable<ServerModel> GetServers()
        {
            var serversSetting = Configuration.GetSection("Servers")?.Get<List<ServerModel>>();

            return serversSetting;
        }

        [HttpPost("[action]")]
        public bool SaveFeatureFlags([FromBody] FeatureFlagModel[] featureFlags)
        {
            if (featureFlags != null)
            {
                foreach (var featureFlag in featureFlags)
                {
                    var featureFlagToUpdate = Context.FeatureConfig.FirstOrDefault(fc => fc.FeatureId == featureFlag.FeatureId &&
                    fc.FeatureFlagId == featureFlag.FeatureFlagId);

                    if (featureFlagToUpdate != null)
                    {
                        featureFlagToUpdate.FeatureFlagValue = featureFlag.FlagValue;
                        Context.SaveChanges();
                    }
                }
                return true;
            }
            else
            {
                return false;
            }
        }

        [HttpGet("[action]")]
        public bool ImportZohoRecordsFromAPI()
        {
            try
            {

                var client = new HttpClient();
                List<ExteriorServicesSchedule> schedules = new List<ExteriorServicesSchedule>();

                var continueImport = true;
                var pageSize = 1000;
                var page = 0;

                do
                {
                    var urlFormat = Configuration.GetValue<string>("ZohoAPIUrl");
                    var task = client.GetStringAsync(string.Format(urlFormat, pageSize, pageSize * page));
                    task.Wait();
                    var apiResponse = task.Result;
                    var tempResult = apiResponse.Substring(apiResponse.IndexOf('{'));
                    if (tempResult[tempResult.Length - 1] != '}')
                    {
                        tempResult = tempResult.Remove(tempResult.LastIndexOf('}') + 1);
                    }
                    var parsedResult = JsonConvert.DeserializeObject<ZohoRecordCollection>(tempResult);
                    if (parsedResult != null && parsedResult.Exterior_Services_Schedule.Count > 0)
                    {
                        schedules.AddRange(parsedResult.Exterior_Services_Schedule);
                    }
                    else
                    {
                        continueImport = false;
                    }

                    page++;

                } while (continueImport);


                var existingSchedules = ImportContext.Schedules.ToList();

                if (existingSchedules != null && existingSchedules.Count > 0)
                {
                    foreach (var schedule in existingSchedules)
                    {
                        ImportContext.Schedules.Remove(schedule);
                    }

                    ImportContext.SaveChanges();
                }

                foreach (var schedule in schedules)
                {
                    ImportContext.Schedules.Add(schedule);
                }

                ImportContext.SaveChanges();

                return true;

            }
            catch (Exception e)
            {
                Debug.Write(e.Message);
                return false;
            }
        }

        [HttpGet("[action]")]
        public ZohoRecordCollection GetZohoRecords(int limit, int startindex)
        {
            var result = new ZohoRecordCollection();

            if (limit > 0)
            {
                var existingSchedules = ImportContext.Schedules.Skip(startindex).Take(limit).ToList();
                result.Exterior_Services_Schedule = existingSchedules; 
            }

            return result;
        }
    }

    public class FeatureModel
    {
        public int FeatureId { get; set; }
        public string Name { get; set; }
        public bool Active { get; set; }
    }

    public class FeatureClientModel
    {
        public int FeatureId { get; set; }
        public int ClientId { get; set; }
        public string CompanyName { get; set; }
        public bool Active { get; set; }
    }

    public class FeatureFlagModel
    {
        public int FeatureId { get; set; }
        public int FeatureFlagId { get; set; }
        public string FlagName { get; set; }
        public int FlagValue { get; set; }
    }

    public class ClientModel
    {
        public int ClientId { get; set; }
        public string CompanyName { get; set; }
    }

    public class ClientConfigurationAttributeModel
    {
        public int ClientConfigurationAttributeId { get; set; }
        public int ConfigurationAttributeId { get; set; }
        public int ClientId { get; set; }
        public int FeatureId { get; set; }
        public string Description { get; set; }
        public string Value { get; set; }
    }

    public class ServerModel
    {
        public string Name { get; set; }
        public bool CredsRequiredToUpdate { get; set; }
    }

}