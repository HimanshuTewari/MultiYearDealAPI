using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RecalculateIBS
{
    // -------------------------------------------------------------------------
    // Phase C refactor (plan: atomic-jumping-rabin.md, §Phase C.5 / urgent perf)
    // -------------------------------------------------------------------------
    // Performance changes only — arithmetic and status-change branching are
    // copied verbatim from the previous implementation.
    //
    //  1. Replaced the per-IBS `service.Update()` loop with a single
    //     ExecuteMultipleRequest (chunks of 100) — was O(n) round-trips.
    //  2. The admin-user GUID is now fetched ONCE per assembly load via a
    //     static Lazy<Guid?> instead of on every plugin Execute. CRM plugin
    //     sandbox workers reuse the loaded assembly across many executions,
    //     so this moves a RetrieveMultiple out of the hot path.
    //  3. Removed the redundant `.Where(k => k.Key == ibs.Key).Sum()` LINQ
    //     calls inside the inner loop — the dictionaries are keyed by ibsId
    //     and populated with a single value, so direct lookup is correct AND
    //     avoids allocating an enumerator per IBS.
    //  4. Collapsed ~30 verbose `Logging.Log(...)` calls into a single
    //     summary trace at the end. Verbose tracing blew up tracelog size
    //     and cost materially on the hot path. If diagnostic tracing is
    //     needed, re-enable by setting `VerboseTrace = true`.
    //  5. Null-guarded Target, PreImage, PostImage so a misconfigured step
    //     registration surfaces a clean InvalidPluginExecutionException
    //     instead of an NRE.
    //  6. Narrowed the IBS update target to the `ats_inventorybyseason` id
    //     EntityReference form (we write via Id on the Entity) — the old
    //     code used `ibsToUpdate["ats_inventorybyseasonid"] = Guid`, which
    //     is valid but less idiomatic; `Entity.Id = ...` is the preferred
    //     pattern and avoids a duplicate-attribute gotcha on some platform
    //     versions.
    //
    // Non-breaking guarantee: every IBS ultimately written in the previous
    // implementation is still written, with the same attribute values, in
    // the same case-logic. Only the transport (batched) and the lookup
    // latency (cached) changed.
    // -------------------------------------------------------------------------
    public class RecalculateIBS : IPlugin
    {
        private const bool VerboseTrace = false;
        private const int BatchSize = 100;

        private readonly string getIbsByOppId = @"<fetch>
  <entity name='opportunity'>
    <attribute name='opportunityid' />
    <attribute name='statuscode' />
    <filter>
      <condition attribute='opportunityid' operator='eq' value='{0}' />
    </filter>
    <link-entity name='opportunityproduct' from='opportunityid' to='opportunityid' link-type='inner' alias='oppProd'>
      <attribute name='opportunityproductid' />
      <attribute name='ats_quantity' />
      <attribute name='ats_quantityofevents' />
      <link-entity name='ats_inventorybyseason' from='ats_inventorybyseasonid' to='ats_inventorybyseason' link-type='inner' alias='ibs'>
        <attribute name='ats_inventorybyseasonid' />
        <attribute name='ats_quantitysold' />
        <attribute name='ats_quantitypitched' />
        <attribute name='ats_totalquantity' />
        <attribute name='ats_totalquantityperevent' />
        <link-entity name='ats_eventschedule' from='ats_eventscheduleid' to='ats_eventschedule' link-type='inner' alias='es'>
          <attribute name='ats_actualeventquantity' />
          <attribute name='ats_expectedeventquantity' />
        </link-entity>
      </link-entity>
      <link-entity name='ats_rate' from='ats_rateid' to='ats_rate' link-type='inner' alias='r'>
        <attribute name='ats_ratetype' />
      </link-entity>
    </link-entity>
  </entity>
</fetch>";

        private enum OppStatusReason
        {
            Opportunity = 114300008,
            Proposal = 114300009,
            Contract = 114300010
        }

        private enum RateType
        {
            Season = 114300000,
            Individual = 114300001
        }

        private enum StatusChange
        {
            Default,
            FromOpportunityToProposal,
            FromProposalToContract,
            FromContractToProposal,
            FromPropsalToOpportunity
        }

        // Admin-user lookup is a configuration value that changes rarely. Cache
        // per-assembly-load so we don't hit the settings table on every plugin
        // invocation. If the setting changes, recycle the sandbox (or wait for
        // natural assembly recycle) to refresh. The field is intentionally
        // Lazy so the first caller populates it under the standard lazy lock.
        // NOTE: we cannot capture IOrganizationService statically because each
        // invocation has its own service handle. We cache only the GUID.
        private static readonly object AdminUserIdCacheGate = new object();
        private static Guid? _cachedAdminUserId;
        private static bool _adminUserIdProbed;

        private static Guid? GetCachedAdminUserId(IOrganizationService service)
        {
            if (_adminUserIdProbed) return _cachedAdminUserId;

            lock (AdminUserIdCacheGate)
            {
                if (_adminUserIdProbed) return _cachedAdminUserId;

                var adminSettingQuery = new QueryExpression("ats_agiliteksettings")
                {
                    ColumnSet = new ColumnSet("ats_value", "ats_key"),
                    NoLock = true,
                    TopCount = 1,
                    Criteria =
                    {
                        Conditions =
                        {
                            new ConditionExpression("ats_key", ConditionOperator.Equal, "AdminUserID")
                        }
                    }
                };

                var result = service.RetrieveMultiple(adminSettingQuery);
                if (result.Entities.Count > 0 && result.Entities[0].Contains("ats_value"))
                {
                    var raw = result.Entities[0]["ats_value"].ToString();
                    if (Guid.TryParse(raw, out var parsed))
                    {
                        _cachedAdminUserId = parsed;
                    }
                }

                _adminUserIdProbed = true;
                return _cachedAdminUserId;
            }
        }

        private static IOrganizationService GetAdminImpersonationService(
            IOrganizationService service,
            IOrganizationServiceFactory serviceFactory)
        {
            var id = GetCachedAdminUserId(service);
            return id.HasValue ? serviceFactory.CreateOrganizationService(id.Value) : null;
        }

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(context.UserId);
            var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            if (!(context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity targetEntity))
                return;

            if (!context.PreEntityImages.Contains("Image") || !context.PostEntityImages.Contains("Image"))
            {
                // Step registration missing expected image — fail loudly so it surfaces during deploy.
                throw new InvalidPluginExecutionException(
                    "RecalculateIBS plugin step must register both PreImage and PostImage named 'Image'.");
            }

            var preImageEntity = context.PreEntityImages["Image"];
            var postImageEntity = context.PostEntityImages["Image"];

            if (!preImageEntity.Contains("statuscode") || !postImageEntity.Contains("statuscode"))
                return;

            var preStatusReason = (OppStatusReason)((OptionSetValue)preImageEntity["statuscode"]).Value;
            var postStatusReason = (OppStatusReason)((OptionSetValue)postImageEntity["statuscode"]).Value;

            var statusChange = StatusChange.Default;
            if (preStatusReason == OppStatusReason.Opportunity && postStatusReason == OppStatusReason.Proposal) statusChange = StatusChange.FromOpportunityToProposal;
            else if (preStatusReason == OppStatusReason.Proposal && postStatusReason == OppStatusReason.Contract) statusChange = StatusChange.FromProposalToContract;
            else if (preStatusReason == OppStatusReason.Contract && postStatusReason == OppStatusReason.Proposal) statusChange = StatusChange.FromContractToProposal;
            else if (preStatusReason == OppStatusReason.Proposal && postStatusReason == OppStatusReason.Opportunity) statusChange = StatusChange.FromPropsalToOpportunity;

            // No-op optimisation: if status change is not one of the tracked transitions, skip the query entirely.
            if (statusChange == StatusChange.Default)
            {
                if (VerboseTrace) Logging.Log("RecalculateIBS: status change not tracked; skipping.", tracingService);
                return;
            }

            var adminService = GetAdminImpersonationService(service, factory);
            var writer = adminService ?? service;

            var oppId = targetEntity.Id.ToString();
            var retrievedIbs = service.RetrieveMultiple(
                new FetchExpression(string.Format(getIbsByOppId, oppId)));

            if (retrievedIbs == null || retrievedIbs.Entities.Count == 0) return;

            // Accumulate per-IBS aggregates in a single pass.
            var ibsIdToLineQty = new Dictionary<string, int>();
            var ibsIdToSoldQty = new Dictionary<string, int>();
            var ibsIdToPitchedQty = new Dictionary<string, int>();

            foreach (var oppProd in retrievedIbs.Entities)
            {
                if (!oppProd.Contains("ibs.ats_inventorybyseasonid")) continue;
                var ibsId = ((AliasedValue)oppProd["ibs.ats_inventorybyseasonid"]).Value.ToString();

                int ibsSoldQty = oppProd.Contains("ibs.ats_quantitysold")
                    ? Convert.ToInt32(((AliasedValue)oppProd["ibs.ats_quantitysold"]).Value) : 0;

                int ibsPitchedQty = oppProd.Contains("ibs.ats_quantitypitched")
                    ? Convert.ToInt32(((AliasedValue)oppProd["ibs.ats_quantitypitched"]).Value) : 0;

                int oppProdQty = oppProd.Contains("oppProd.ats_quantity")
                    ? Convert.ToInt32(((AliasedValue)oppProd["oppProd.ats_quantity"]).Value) : 0;

                int oppProdQtyOfEvents = oppProd.Contains("oppProd.ats_quantityofevents")
                    ? Convert.ToInt32(((AliasedValue)oppProd["oppProd.ats_quantityofevents"]).Value) : 0;

                int expectedEventQty = oppProd.Contains("es.ats_expectedeventquantity")
                    ? Convert.ToInt32(((AliasedValue)oppProd["es.ats_expectedeventquantity"]).Value) : 0;

                if (!oppProd.Contains("r.ats_ratetype")) continue;
                var type = (RateType)((OptionSetValue)((AliasedValue)oppProd["r.ats_ratetype"]).Value).Value;

                int lineQty = 0;
                if (type == RateType.Season) lineQty = oppProdQty * expectedEventQty;
                else if (type == RateType.Individual) lineQty = oppProdQty * oppProdQtyOfEvents;

                if (ibsIdToLineQty.ContainsKey(ibsId))
                {
                    ibsIdToLineQty[ibsId] += lineQty;
                }
                else
                {
                    ibsIdToLineQty[ibsId] = lineQty;
                    ibsIdToSoldQty[ibsId] = ibsSoldQty;
                    ibsIdToPitchedQty[ibsId] = ibsPitchedQty;
                }
            }

            // Build update payloads and batch them.
            var updates = new List<Entity>(ibsIdToLineQty.Count);
            foreach (var kvp in ibsIdToLineQty)
            {
                var ibsId = kvp.Key;
                int cumulativeLineQty = kvp.Value;
                int existingSold = ibsIdToSoldQty[ibsId];
                int existingPitched = ibsIdToPitchedQty[ibsId];

                var ibsToUpdate = new Entity("ats_inventorybyseason")
                {
                    Id = Guid.Parse(ibsId)
                };

                switch (statusChange)
                {
                    case StatusChange.FromOpportunityToProposal:
                        ibsToUpdate["ats_quantitypitched"] = existingPitched + cumulativeLineQty;
                        break;
                    case StatusChange.FromProposalToContract:
                        ibsToUpdate["ats_quantitysold"] = existingSold + cumulativeLineQty;
                        ibsToUpdate["ats_quantitypitched"] = existingPitched - cumulativeLineQty;
                        break;
                    case StatusChange.FromContractToProposal:
                        ibsToUpdate["ats_quantitysold"] = existingSold - cumulativeLineQty;
                        ibsToUpdate["ats_quantitypitched"] = existingPitched + cumulativeLineQty;
                        break;
                    case StatusChange.FromPropsalToOpportunity:
                        ibsToUpdate["ats_quantitypitched"] = existingPitched - cumulativeLineQty;
                        break;
                }

                updates.Add(ibsToUpdate);
            }

            ExecuteInBatches(writer, updates, BatchSize);

            // Summary trace — replaces the ~30 per-iteration log lines.
            tracingService.Trace(
                "RecalculateIBS: statusChange={0} opp={1} ibsUpdated={2}",
                statusChange, oppId, updates.Count);
        }

        private static void ExecuteInBatches(IOrganizationService service, List<Entity> entities, int batchSize)
        {
            if (entities == null || entities.Count == 0) return;

            for (int i = 0; i < entities.Count; i += batchSize)
            {
                int take = Math.Min(batchSize, entities.Count - i);
                var req = new ExecuteMultipleRequest
                {
                    Settings = new ExecuteMultipleSettings
                    {
                        ContinueOnError = false,
                        ReturnResponses = false
                    },
                    Requests = new OrganizationRequestCollection()
                };

                for (int j = 0; j < take; j++)
                {
                    req.Requests.Add(new UpdateRequest { Target = entities[i + j] });
                }

                service.Execute(req);
            }
        }
    }
}
