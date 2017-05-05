using System.Collections.Generic;
using Newtonsoft.Json;

namespace BulkImportGlossary
{
    [JsonObject(MemberSerialization.OptIn)]
    public class GlossaryItem
    {
        public GlossaryItem(
            string id,
            string name,
            string parentId,
            string definition,
            string description,
            IEnumerable<SecurityPrincipal> stakeholders)
        {
            Id = id;
            Name = name;
            ParentId = parentId;
            Definition = definition;
            Description = description;
            Stakeholders = stakeholders;
        }

        public GlossaryItem()
        {
        }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("definition")]
        public string Definition { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("parentId")]
        public string ParentId { get; set; }

        [JsonProperty("stakeholders")]
        public IEnumerable<SecurityPrincipal> Stakeholders { get; set; }

        public string Serialize()
        {
             return JsonConvert.SerializeObject(this, Formatting.None);
        }
    }
}
