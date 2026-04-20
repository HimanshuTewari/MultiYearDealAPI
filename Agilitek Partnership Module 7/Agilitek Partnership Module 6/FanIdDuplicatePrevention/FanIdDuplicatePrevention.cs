using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;

namespace FanIdDuplicatePrevention
{
    // -------------------------------------------------------------------------
    // Phase C.6 refactor (plan: atomic-jumping-rabin.md §Phase C.6)
    // -------------------------------------------------------------------------
    // Performance changes only — duplicate-detection logic unchanged.
    //
    //   1. Two sequential `RetrieveMultiple` queries to the settings table
    //      (one for "Fan ID Field Name", one for "Agilitek System User ID")
    //      are collapsed into ONE query with `LogicalOperator.Or`. Saves a
    //      round-trip on every invocation.
    //   2. Settings are cached in `static` fields per assembly load — they
    //      change rarely (config), so we don't hit the DB on every plugin
    //      Execute. CRM plugin sandbox workers reuse the loaded assembly
    //      across many executions, so this moves another RetrieveMultiple
    //      out of the hot path. (Assembly recycle clears the cache.)
    //   3. Duplicate-check query narrowed: `TopCount = 2` (we only need to
    //      know "more than one"), `ColumnSet(false)` (we only need ids),
    //      `NoLock = true` (read-committed not required for an existence
    //      check), and the current record id excluded so the result count
    //      means "another row exists" directly.
    // -------------------------------------------------------------------------
    public class FanIdDuplicatePrevention : IPlugin
    {
        // Per-assembly-load cache. Tuple = (FanIdFieldName, AgilitekSystemUserId).
        // Volatile so other threads see the published values without a fence on .NET 4.6.2+.
        private static readonly object SettingsGate = new object();
        private static volatile bool _settingsProbed;
        private static string _cachedFanIdField;
        private static Guid? _cachedAgilitekSystemUserId;

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var service = ((IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory))).CreateOrganizationService(context.UserId);
            var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            if (!(context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity)) return;
            if (!context.PostEntityImages.Contains("Image")) return;

            var postImageEntity = context.PostEntityImages["Image"];

            string fanIdField;
            Guid systemUserId;
            try
            {
                if (!TryLoadSettings(service, out fanIdField, out systemUserId))
                {
                    tracingService.Trace("Fan ID Field Name / Agilitek System User ID setting missing.");
                    return;
                }
            }
            catch (Exception ex)
            {
                tracingService.Trace("Error retrieving settings during Fan ID Duplicate Prevention: {0}", ex);
                return;
            }

            tracingService.Trace("Initiating User ID: {0}", context.InitiatingUserId);

            if (context.InitiatingUserId != systemUserId || !postImageEntity.Contains(fanIdField)) return;

            var fanid = postImageEntity.GetAttributeValue<string>(fanIdField);
            if (string.IsNullOrWhiteSpace(fanid)) return;

            tracingService.Trace("Fan ID: {0}", fanid);

            var dupQuery = new QueryExpression("contact")
            {
                ColumnSet = new ColumnSet(false), // existence check only
                NoLock = true,
                TopCount = 2,                     // we only need to know "more than one"
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression(fanIdField, ConditionOperator.Equal, fanid),
                        new ConditionExpression("contactid", ConditionOperator.NotEqual, postImageEntity.Id)
                    }
                }
            };

            var dups = service.RetrieveMultiple(dupQuery);
            if (dups.Entities.Count >= 1)
            {
                throw new InvalidPluginExecutionException("The Fan ID already exists.");
            }

            tracingService.Trace("Fan ID does not exist.");
        }

        /// <summary>
        /// Loads both settings in ONE round-trip (Or-filter on ats_key) and caches
        /// them per assembly load. Returns false if either setting is missing
        /// (matches the old behaviour which surfaced the missing setting as an
        /// IndexOutOfRangeException → caught and logged → return).
        /// </summary>
        private static bool TryLoadSettings(IOrganizationService service, out string fanIdField, out Guid systemUserId)
        {
            if (_settingsProbed)
            {
                fanIdField = _cachedFanIdField;
                systemUserId = _cachedAgilitekSystemUserId.GetValueOrDefault();
                return !string.IsNullOrWhiteSpace(fanIdField) && _cachedAgilitekSystemUserId.HasValue;
            }

            lock (SettingsGate)
            {
                if (_settingsProbed)
                {
                    fanIdField = _cachedFanIdField;
                    systemUserId = _cachedAgilitekSystemUserId.GetValueOrDefault();
                    return !string.IsNullOrWhiteSpace(fanIdField) && _cachedAgilitekSystemUserId.HasValue;
                }

                var query = new QueryExpression("ats_agiliteksettings")
                {
                    ColumnSet = new ColumnSet("ats_key", "ats_value"),
                    NoLock = true,
                    Criteria = new FilterExpression(LogicalOperator.Or)
                    {
                        Conditions =
                        {
                            new ConditionExpression("ats_key", ConditionOperator.Equal, "Fan ID Field Name"),
                            new ConditionExpression("ats_key", ConditionOperator.Equal, "Agilitek System User ID")
                        }
                    }
                };

                var result = service.RetrieveMultiple(query);

                string foundField = null;
                Guid? foundUser = null;
                foreach (var row in result.Entities)
                {
                    var key = row.GetAttributeValue<string>("ats_key");
                    var value = row.GetAttributeValue<string>("ats_value");
                    if (string.Equals(key, "Fan ID Field Name", StringComparison.Ordinal))
                    {
                        foundField = value;
                    }
                    else if (string.Equals(key, "Agilitek System User ID", StringComparison.Ordinal))
                    {
                        if (Guid.TryParse(value, out var g)) foundUser = g;
                    }
                }

                _cachedFanIdField = foundField;
                _cachedAgilitekSystemUserId = foundUser;
                _settingsProbed = true;

                fanIdField = foundField;
                systemUserId = foundUser.GetValueOrDefault();
                return !string.IsNullOrWhiteSpace(foundField) && foundUser.HasValue;
            }
        }
    }
}
