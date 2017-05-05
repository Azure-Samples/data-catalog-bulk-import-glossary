using System;
using Newtonsoft.Json;

namespace BulkImportGlossary
{
    [JsonObject(MemberSerialization.OptIn)]
    public class SecurityPrincipal
    {
        public SecurityPrincipal(Guid objectId) : this(objectId, null)
        {
        }

        [JsonConstructor]
        public SecurityPrincipal(Guid objectId, string upn)
        {
            if (objectId == Guid.Empty)
            {
                throw new Exception("objectId is empty!");
            }

            ObjectId = objectId;
            Upn = upn;
        }

        [JsonProperty("objectId")]
        public Guid ObjectId { get; private set; }
        
        [JsonProperty("upn")]
        public string Upn { get; private set; }
    }
}
