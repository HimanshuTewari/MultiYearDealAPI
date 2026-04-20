using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;

namespace MultiYearDeal.Workflows
{
    // -------------------------------------------------------------------------
    // EnvVarReader — TTL-cached reader for D365 Environment Variables.
    // -------------------------------------------------------------------------
    //
    // Resolves a `environmentvariabledefinition` + its (optional) current
    // `environmentvariablevalue` in one query, prefers the Current Value, and
    // falls back to the Default Value. The result is cached per schema name
    // in a static dictionary for 60 seconds so plugin invocations don't thrash
    // the settings table. Plugin sandbox recycles clear the cache naturally;
    // during sandbox testing the TTL means a value edited in the Power
    // Platform env-var grid takes effect inside a minute without recycling.
    //
    // Safe against missing env vars / access errors: returns the caller's
    // fallback silently.
    //
    // Usage:
    //   int oliBatchSize = EnvVarReader.ReadInt(service, "ats_OliBatchSize", 1);
    // -------------------------------------------------------------------------
    public static class EnvVarReader
    {
        private static readonly object _gate = new object();
        private static readonly Dictionary<string, CacheEntry> _cache =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan _ttl = TimeSpan.FromSeconds(60);

        private class CacheEntry
        {
            public DateTime ExpiresAt;
            public string Value;
        }

        /// <summary>
        /// Returns the string value of an environment variable, or <paramref name="fallback"/>
        /// if the variable is not defined / not set / not accessible.
        /// </summary>
        public static string Read(IOrganizationService service, string schemaName, string fallback)
        {
            if (service == null || string.IsNullOrWhiteSpace(schemaName)) return fallback;

            lock (_gate)
            {
                if (_cache.TryGetValue(schemaName, out var entry) && DateTime.UtcNow < entry.ExpiresAt)
                    return entry.Value ?? fallback;
            }

            string loaded = null;
            try
            {
                loaded = LoadFromCrm(service, schemaName);
            }
            catch
            {
                // Swallow — a missing / inaccessible env var must not break the plugin.
                // Caller gets the fallback.
            }

            lock (_gate)
            {
                _cache[schemaName] = new CacheEntry
                {
                    ExpiresAt = DateTime.UtcNow.Add(_ttl),
                    Value = loaded
                };
            }

            return loaded ?? fallback;
        }

        /// <summary>
        /// Reads the env var as an int. Returns <paramref name="fallback"/> if the value
        /// is missing or not a valid integer.
        /// </summary>
        public static int ReadInt(IOrganizationService service, string schemaName, int fallback)
        {
            var raw = Read(service, schemaName, null);
            return int.TryParse(raw, out var parsed) ? parsed : fallback;
        }

        /// <summary>
        /// Reads the env var as a bool. Accepts "true"/"false" (case-insensitive) and
        /// anything else returns <paramref name="fallback"/>.
        /// </summary>
        public static bool ReadBool(IOrganizationService service, string schemaName, bool fallback)
        {
            var raw = Read(service, schemaName, null);
            return bool.TryParse(raw, out var parsed) ? parsed : fallback;
        }

        private static string LoadFromCrm(IOrganizationService service, string schemaName)
        {
            var qe = new QueryExpression("environmentvariabledefinition")
            {
                ColumnSet = new ColumnSet("schemaname", "defaultvalue"),
                NoLock = true,
                TopCount = 1,
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("schemaname", ConditionOperator.Equal, schemaName)
                    }
                }
            };

            var valueLink = qe.AddLink(
                "environmentvariablevalue",
                "environmentvariabledefinitionid",
                "environmentvariabledefinitionid",
                JoinOperator.LeftOuter);
            valueLink.EntityAlias = "val";
            valueLink.Columns = new ColumnSet("value");

            var result = service.RetrieveMultiple(qe);
            if (result.Entities.Count == 0) return null;

            var def = result.Entities[0];

            // Prefer Current Value.
            if (def.Contains("val.value"))
            {
                var v = ((AliasedValue)def["val.value"]).Value?.ToString();
                if (!string.IsNullOrEmpty(v)) return v;
            }

            // Fall back to the Default Value the admin supplied when defining the variable.
            return def.GetAttributeValue<string>("defaultvalue");
        }

        /// <summary>
        /// Test/diagnostic helper — clears the in-memory cache so the next Read call re-fetches.
        /// </summary>
        public static void ClearCache()
        {
            lock (_gate) { _cache.Clear(); }
        }
    }
}
