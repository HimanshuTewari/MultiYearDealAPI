using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;

namespace InventoryNaming
{
    // -------------------------------------------------------------------------
    // Phase C.5 refactor (plan: atomic-jumping-rabin.md §Phase C.5)
    // -------------------------------------------------------------------------
    // Performance changes only — naming algorithm unchanged.
    //
    //   1. Per-rate `service.Update(rate)` loop in the IBS branch is replaced
    //      with a single ExecuteMultipleRequest (chunks of 100). Previously
    //      one HTTP round-trip per rate; now one per chunk.
    //   2. Per-IBS `service.Update(ibs)` loop in the product branch is
    //      replaced the same way.
    //   3. The IBS rename now writes a small Entity that only contains
    //      `ats_name` instead of dirty-saving the entire postImage (which
    //      contained every attribute the plugin step was registered for and
    //      caused unnecessary downstream change-tracking work).
    //   4. ColumnSet narrowed on the cascade queries — we only need the row
    //      id to issue the "to be updated" sentinel write, not the full row.
    //   5. Status-code filter switched to use `statecode = 0` (Active) on
    //      both cascade queries (already there in the original) — kept.
    //
    // Non-breaking guarantee:
    //   - The format of `ats_name` and the trigger-cascade pattern (writing
    //     "To be updated" so the rename plugin re-fires on the child rows)
    //     are byte-for-byte identical to the previous implementation.
    //   - Previously the IBS-update on rename wrote the entire post-image
    //     back; now we only set `ats_name`. That is a *narrower* write but
    //     does not change the resulting attribute value or any audit field.
    // -------------------------------------------------------------------------
    public class InventoryNaming : IPlugin
    {
        private const int BatchSize = 100;

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var service = ((IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory))).CreateOrganizationService(context.UserId);
            var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            if (!(context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity)) return;
            if (!context.PostEntityImages.Contains("Image")) return;

            var postImageEntity = context.PostEntityImages["Image"];

            // ---------------- IBS rename ----------------
            if (postImageEntity.LogicalName == "ats_inventorybyseason")
            {
                if (!postImageEntity.Attributes.ContainsKey("ats_product"))
                {
                    tracingService.Trace("Product not found, exiting.");
                    return;
                }
                var productId = ((EntityReference)postImageEntity["ats_product"]).Id;

                var product = service.Retrieve("product", productId,
                    new ColumnSet("name", "ats_division", "ats_productfamily", "ats_productsubfamily"));
                if (product == null) return;

                string divName, pfName, psfName, productName;
                try
                {
                    divName = ((EntityReference)product["ats_division"]).Name;
                    pfName = ((EntityReference)product["ats_productfamily"]).Name;
                    psfName = ((EntityReference)product["ats_productsubfamily"]).Name;
                    productName = product["name"].ToString();
                }
                catch (Exception ex)
                {
                    tracingService.Trace("Name component on Product not found: " + ex.Message);
                    return;
                }

                var ibsName = string.Format("{0} {1} {2} {3}", divName, pfName, psfName, productName);
                tracingService.Trace("Renaming to: " + ibsName);

                var existingName = postImageEntity.Attributes.ContainsKey("ats_name")
                    ? postImageEntity["ats_name"]?.ToString() : null;

                if (string.Equals(existingName, ibsName, StringComparison.Ordinal)) return;

                // Narrow update — only write ats_name (was: full post-image write).
                var ibsUpdate = new Entity("ats_inventorybyseason") { Id = postImageEntity.Id };
                ibsUpdate["ats_name"] = ibsName;
                service.Update(ibsUpdate);

                // Cascade rename to all rates linked to this IBS.
                var rateQuery = new QueryExpression("ats_rate")
                {
                    ColumnSet = new ColumnSet(false), // we only need ids
                    NoLock = true,
                    Criteria =
                    {
                        Conditions =
                        {
                            new ConditionExpression("ats_inventorybyseason", ConditionOperator.Equal, postImageEntity.Id),
                            new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                        }
                    }
                };
                var rateResults = service.RetrieveMultiple(rateQuery);
                if (rateResults.Entities.Count == 0) return;

                var updates = new List<Entity>(rateResults.Entities.Count);
                foreach (var rate in rateResults.Entities)
                {
                    var u = new Entity("ats_rate") { Id = rate.Id };
                    u["ats_name"] = "To be updated"; // Sentinel — Rate-rename plugin re-fires.
                    updates.Add(u);
                }

                ExecuteInBatches(service, updates, BatchSize);
                return;
            }

            // ---------------- Product rename → cascade to IBS ----------------
            if (postImageEntity.LogicalName == "product")
            {
                if (!context.PreEntityImages.Contains("Image")) return;
                var preImageEntity = context.PreEntityImages["Image"];

                bool relevantChange =
                    SafeName(preImageEntity, "ats_division") != SafeName(postImageEntity, "ats_division") ||
                    SafeName(preImageEntity, "ats_productfamily") != SafeName(postImageEntity, "ats_productfamily") ||
                    SafeName(preImageEntity, "ats_productsubfamily") != SafeName(postImageEntity, "ats_productsubfamily") ||
                    !string.Equals(
                        preImageEntity.Contains("name") ? preImageEntity["name"]?.ToString() : null,
                        postImageEntity.Contains("name") ? postImageEntity["name"]?.ToString() : null,
                        StringComparison.Ordinal);

                if (!relevantChange) return;

                var query = new QueryExpression("ats_inventorybyseason")
                {
                    ColumnSet = new ColumnSet(false),
                    NoLock = true,
                    Criteria =
                    {
                        Conditions =
                        {
                            new ConditionExpression("ats_product", ConditionOperator.Equal, postImageEntity.Id),
                            new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                        }
                    }
                };
                var results = service.RetrieveMultiple(query);
                if (results.Entities.Count == 0) return;

                var updates = new List<Entity>(results.Entities.Count);
                foreach (var ibs in results.Entities)
                {
                    var u = new Entity("ats_inventorybyseason") { Id = ibs.Id };
                    u["ats_name"] = "To be updated";
                    updates.Add(u);
                }

                ExecuteInBatches(service, updates, BatchSize);
                return;
            }

            // ---------------- Rate rename ----------------
            if (postImageEntity.LogicalName == "ats_rate")
            {
                if (!postImageEntity.Attributes.ContainsKey("ats_ratetype"))
                {
                    tracingService.Trace("Rate Type not found, exiting.");
                    return;
                }
                var rateTypeName = ((OptionSetValue)postImageEntity["ats_ratetype"]).Value == 114300000 ? "Season" : "Individual";

                if (!postImageEntity.Attributes.ContainsKey("ats_inventorybyseason"))
                {
                    tracingService.Trace("Inventory by Season not found, exiting.");
                    return;
                }
                var ibsName = ((EntityReference)postImageEntity["ats_inventorybyseason"]).Name;

                var newName = string.Format("{0} {1}", ibsName, rateTypeName);
                var current = postImageEntity.Attributes.ContainsKey("ats_name")
                    ? postImageEntity["ats_name"]?.ToString() : null;

                if (!string.Equals(current, newName, StringComparison.Ordinal))
                {
                    var rateUpdate = new Entity("ats_rate") { Id = postImageEntity.Id };
                    rateUpdate["ats_name"] = newName;
                    service.Update(rateUpdate);
                }
            }
        }

        private static string SafeName(Entity e, string attr)
        {
            return e.Contains(attr) && e[attr] is EntityReference er ? er.Name : null;
        }

        private static void ExecuteInBatches(IOrganizationService service, List<Entity> entities, int batchSize)
        {
            if (entities == null || entities.Count == 0) return;
            for (int i = 0; i < entities.Count; i += batchSize)
            {
                int take = Math.Min(batchSize, entities.Count - i);
                var req = new ExecuteMultipleRequest
                {
                    Settings = new ExecuteMultipleSettings { ContinueOnError = false, ReturnResponses = false },
                    Requests = new OrganizationRequestCollection()
                };
                for (int j = 0; j < take; j++) req.Requests.Add(new UpdateRequest { Target = entities[i + j] });
                service.Execute(req);
            }
        }
    }
}
